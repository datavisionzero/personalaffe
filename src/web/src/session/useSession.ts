import { useCallback, useEffect, useRef, useState } from "react";

import { api, codeOf, describe, onDoorEvent, type Problem, type Schemas } from "@/api/client";

export type Me = Schemas["MeResponse"];
export type LockStatus = Schemas["InactivityLockStatusResponse"];

export type Session =
  | { state: "asking" }
  | { state: "setup" }
  | { state: "signed-out" }
  | { state: "signed-in"; me: Me; lock: LockStatus }
  | { state: "locked"; me?: Me; lock?: LockStatus; reason?: string }
  | { state: "unreachable"; reason: string };

type DoorMessage = { kind: "locked" | "unlocked" | "activity" | "configuration" | "signed-out"; lock?: LockStatus };
const channelName = "personalaffe-session-lock";

/** Keeps the server-authoritative browser lock in front of the workspace. */
export function useSession(): {
  session: Session;
  ask: () => void;
  signedIn: (me: Me) => void;
  signedOut: () => void;
  unlocked: () => void;
} {
  const [session, setSessionState] = useState<Session>({ state: "asking" });
  const sessionRef = useRef(session);
  const channelRef = useRef<BroadcastChannel | undefined>(undefined);

  const setSession = useCallback((next: Session) => {
    sessionRef.current = next;
    setSessionState(next);
  }, []);
  const publish = useCallback((message: DoorMessage) => channelRef.current?.postMessage(message), []);

  const settleLock = useCallback(async (me: Me, broadcastUnlock = false) => {
    try {
      const answer = await api.GET("/api/session/lock");
      if (answer.data) {
        if (answer.data.locked) {
          setSession({ state: "locked", me, lock: answer.data });
          publish({ kind: "locked", lock: answer.data });
        } else {
          setSession({ state: "signed-in", me, lock: answer.data });
          if (broadcastUnlock) publish({ kind: "unlocked", lock: answer.data });
        }
        return;
      }
      if (answer.response.status === 401) {
        setSession({ state: "signed-out" });
        return;
      }
      setSession({ state: "locked", me, reason: describe(answer.error as Problem | undefined, answer.response.status) });
    } catch (failure: unknown) {
      setSession({ state: "locked", me, reason: failure instanceof Error ? failure.message : String(failure) });
    }
  }, [publish, setSession]);

  const ask = useCallback(() => {
    void (async () => {
      try {
        const setup = await api.GET("/api/setup");
        if (setup.data?.required) {
          setSession({ state: "setup" });
          return;
        }
        if (!setup.data) {
          setSession({ state: "unreachable", reason: describe(setup.error as Problem | undefined, setup.response.status) });
          return;
        }

        const me = await api.GET("/api/me");
        if (me.data) await settleLock(me.data);
        else if (me.response.status === 423) setSession({ state: "locked" });
        else if (me.response.status === 401) setSession({ state: "signed-out" });
        else setSession({ state: "unreachable", reason: describe(me.error as Problem | undefined, me.response.status) });
      } catch (failure: unknown) {
        setSession({ state: "unreachable", reason: failure instanceof Error ? failure.message : String(failure) });
      }
    })();
  }, [setSession, settleLock]);

  useEffect(() => ask(), [ask]);

  useEffect(() => onDoorEvent((event) => {
    const current = sessionRef.current;
    if (event === "configuration" && (current.state === "signed-in" || current.state === "locked") && current.me) {
      void settleLock(current.me);
      publish({ kind: "configuration" });
    } else if (event === "locked" && current.state === "signed-in") {
      const lock = { ...current.lock, locked: true };
      setSession({ state: "locked", me: current.me, lock });
      publish({ kind: "locked", lock });
    }
  }), [publish, setSession, settleLock]);

  useEffect(() => {
    if (typeof BroadcastChannel === "undefined") return;
    const channel = new BroadcastChannel(channelName);
    channelRef.current = channel;
    channel.onmessage = (event: MessageEvent<DoorMessage>) => {
      const message = event.data;
      const current = sessionRef.current;
      if (message.kind === "signed-out") setSession({ state: "signed-out" });
      else if (message.kind === "locked" && current.state === "signed-in") setSession({ state: "locked", me: current.me, lock: message.lock });
      else if (message.kind === "unlocked" && current.state === "locked" && current.me) void settleLock(current.me);
      else if (message.kind === "configuration" && (current.state === "signed-in" || current.state === "locked") && current.me) void settleLock(current.me);
      else if (message.kind === "activity" && current.state === "signed-in" && message.lock) setSession({ ...current, lock: message.lock });
    };
    return () => {
      channel.close();
      channelRef.current = undefined;
    };
  }, [setSession, settleLock]);

  useEffect(() => {
    if (session.state !== "signed-in" || !session.lock.enabled || !session.lock.locks_at) return;
    const delay = Math.min(2_147_483_647, Math.max(0, new Date(session.lock.locks_at).getTime() - Date.now()));
    const timer = window.setTimeout(() => {
      const lock = { ...session.lock, locked: true };
      setSession({ state: "locked", me: session.me, lock });
      publish({ kind: "locked", lock });
    }, delay);
    return () => window.clearTimeout(timer);
  }, [publish, session, setSession]);

  // Status checks detect changes from another browser but never count as activity.
  useEffect(() => {
    if (session.state !== "signed-in" || !session.lock.enabled) return;
    const check = () => void settleLock(session.me);
    const visible = () => { if (document.visibilityState === "visible") check(); };
    window.addEventListener("focus", check);
    document.addEventListener("visibilitychange", visible);
    const interval = window.setInterval(check, 30_000);
    return () => {
      window.removeEventListener("focus", check);
      document.removeEventListener("visibilitychange", visible);
      window.clearInterval(interval);
    };
  }, [session, settleLock]);

  // Only deliberate keyboard, pointer or touch input extends the deadline.
  useEffect(() => {
    if (session.state !== "signed-in" || !session.lock.enabled) return;
    let lastReport = 0;
    const report = () => {
      const now = Date.now();
      if (now - lastReport < 30_000) return;
      lastReport = now;
      void api.POST("/api/session/lock/activity").then((answer) => {
        const current = sessionRef.current;
        if (answer.data && current.state === "signed-in") {
          setSession({ ...current, lock: answer.data });
          publish({ kind: "activity", lock: answer.data });
        } else if (!answer.data && answer.response.status !== 423) {
          setSession({ state: "locked", me: session.me, reason: "The lock status could not be confirmed." });
        }
      }).catch(() => setSession({ state: "locked", me: session.me, reason: "The lock status could not be confirmed." }));
    };
    window.addEventListener("keydown", report);
    window.addEventListener("pointerdown", report);
    window.addEventListener("touchstart", report);
    return () => {
      window.removeEventListener("keydown", report);
      window.removeEventListener("pointerdown", report);
      window.removeEventListener("touchstart", report);
    };
  }, [publish, session, setSession]);

  const signedIn = useCallback((me: Me) => {
    setSession({ state: "asking" });
    void settleLock(me);
  }, [setSession, settleLock]);
  const signedOut = useCallback(() => {
    setSession({ state: "signed-out" });
    publish({ kind: "signed-out" });
  }, [publish, setSession]);
  const unlocked = useCallback(() => {
    const current = sessionRef.current;
    if (current.state === "locked" && current.me) void settleLock(current.me, true);
    else ask();
  }, [ask, settleLock]);

  return { session, ask, signedIn, signedOut, unlocked };
}

export function refusal(error: unknown, status: number): { message: string; code?: string } {
  const problem = error as Problem | undefined;
  return { message: describe(problem, status), code: codeOf(problem) };
}
