import { useCallback, useEffect, useState } from "react";

import { api, codeOf, describe, type Problem, type Schemas } from "@/api/client";

/** Who the instance says this browser is. */
export type Me = Schemas["MeResponse"];

/**
 * The four things the application has to be able to draw before it draws
 * anything else.
 *
 * "Nobody has claimed this instance" and "nobody is signed in" are separate
 * states on purpose: a browser arriving at a fresh installation cannot sign in,
 * and offering it a sign-in form would be offering it a door with no lock and
 * no key. "Nothing answered at all" is separate from both, because a container
 * that has not started is not a credential problem and telling somebody to sign
 * in again would be a lie.
 */
export type Session =
  | { state: "asking" }
  | { state: "setup" }
  | { state: "signed-out" }
  | { state: "signed-in"; me: Me }
  | { state: "unreachable"; reason: string };

/**
 * Asks the instance who this browser is, and keeps the answer.
 *
 * Two questions in order, and the first one is outside the door: whether there
 * is an owner at all, and then who the cookie admits. Asking them the other way
 * round would show a fresh installation a sign-in form and a refusal, which is
 * the one screen a person cannot act on.
 */
export function useSession(): {
  session: Session;
  ask: () => void;
  signedIn: (me: Me) => void;
  signedOut: () => void;
} {
  const [session, setSession] = useState<Session>({ state: "asking" });

  const ask = useCallback(() => {
    let abandoned = false;

    void (async () => {
      try {
        const setup = await api.GET("/api/setup");

        if (abandoned) {
          return;
        }

        if (setup.data?.required) {
          setSession({ state: "setup" });
          return;
        }

        if (!setup.data) {
          setSession({
            state: "unreachable",
            reason: describe(setup.error as Problem | undefined, setup.response.status),
          });
          return;
        }

        const me = await api.GET("/api/me");

        if (abandoned) {
          return;
        }

        // Anything but an answer is "nobody is signed in": a session that
        // expired, one the owner revoked from another device, and one that was
        // never there all mean the same thing to this screen.
        setSession(me.data ? { state: "signed-in", me: me.data } : { state: "signed-out" });
      } catch (failure: unknown) {
        if (!abandoned) {
          setSession({
            state: "unreachable",
            reason: failure instanceof Error ? failure.message : String(failure),
          });
        }
      }
    })();

    return () => {
      abandoned = true;
    };
  }, []);

  useEffect(() => ask(), [ask]);

  const signedIn = useCallback((me: Me) => setSession({ state: "signed-in", me }), []);
  const signedOut = useCallback(() => setSession({ state: "signed-out" }), []);

  return { session, ask, signedIn, signedOut };
}

/**
 * What the instance refused with, in a sentence — and the code beside it, for
 * the one refusal a screen acts on rather than prints.
 */
export function refusal(error: unknown, status: number): { message: string; code?: string } {
  const problem = error as Problem | undefined;

  return { message: describe(problem, status), code: codeOf(problem) };
}
