import { useCallback, useEffect, useRef, useState } from "react";

import { describe, type Problem } from "@/api/client";

/** What a screen knows about one thing it asked the instance for. */
export type Asked<T> =
  | { at: "asking" }
  | { at: "failed"; why: string }
  | { at: "known"; value: T };

type Answer<T> = { data?: T; error?: Problem; response: Response };

/** How often a screen asks again, in milliseconds, unless it says otherwise. */
export const refreshEvery = 15_000;

const asking = { at: "asking" } as const;

/**
 * One question a screen asks the instance about the address it is on, asked
 * again by itself while the screen is open.
 *
 * <b>The workspace has more than one writer.</b> The owner has a phone and a
 * desk, and agents write over the API at any time, so a screen that only ever
 * showed what was true when it was opened would be wrong most of the time it is
 * looked at. There is no push: one container, no `LISTEN`/`NOTIFY`, no
 * long-lived connection to keep alive through a reverse proxy — a question
 * asked again on a timer is the whole mechanism, and it costs one request every
 * {@link refreshEvery} against a workspace one person is using.
 *
 * Three rules make that bearable and non-destructive:
 *
 * - **A refresh is quiet.** The answer a screen is showing stays on the screen
 *   while the next one is on its way, and a refresh that fails leaves it
 *   standing and says so beside it. Only a change of address, or asking again
 *   on purpose, puts a screen back to "asking" — spinners that flash every
 *   fifteen seconds are how a refreshing screen becomes an unreadable one.
 * - **A hidden tab asks nothing.** Polling stops when the tab is hidden and one
 *   question is asked the moment it comes back, so a workspace left open for a
 *   day costs nothing and is up to date the moment it is looked at.
 * - **`hold` stops it dead.** A screen with unsaved edits holds its refresh:
 *   what somebody is typing is the newest version of it, and an answer arriving
 *   underneath is what would take it away.
 *
 * The address is the first argument and not a dependency of the second, because
 * it is the answer's own label: a screen that walks from one page to the next
 * would otherwise show the previous page's answer under the new address, and a
 * slow answer arriving after the walk would overwrite the right one.
 */
export function useAsk<T>(
  at: string,
  ask: (signal: AbortSignal) => Promise<Answer<T>>,
  options?: {
    /** Milliseconds between questions; `false` asks once and never again. */
    every?: number | false;
    /** While true, nothing is asked: the screen is holding unsaved work. */
    hold?: boolean;
  },
): {
  asked: Asked<T>;
  /** Ask again, showing that it is being asked — after a write, or on purpose. */
  again: () => void;
  /** Ask again quietly, keeping what is on the screen — what the timer does. */
  refresh: () => void;
  /** Whether the last quiet question failed while an older answer is still shown. */
  unanswered: boolean;
} {
  const [state, setState] = useState<{ at: string; asked: Asked<T> }>();
  // The round is what makes asking again an effect rather than a call.
  const [round, setRound] = useState(0);
  const [unanswered, setUnanswered] = useState(false);

  // The caller writes the request inline, so it is a new function on every
  // render; `at` and the round are what actually decide when to ask again.
  const request = useStable(ask);

  const every = options?.every ?? refreshEvery;
  const hold = options?.hold ?? false;

  // What the timer calls. It is a ref rather than a dependency so that the
  // interval below is not torn down and set up again on every render.
  const quiet = useRef<() => void>(undefined);

  const run = useCallback(
    () => {
      const controller = new AbortController();
      let live = true;

      void (async () => {
        try {
          const { data, error, response } = await request(controller.signal);

          if (!live) {
            return;
          }

          if (data === undefined) {
            setUnanswered(true);
            setState((current) =>
              // A refusal replaces an answer only where there was none to keep.
              // A read that started working and stopped is worth saying beside
              // what it last said, not instead of it — and `again` clears the
              // answer first, so asking on purpose does replace it.
              current === undefined || current.at !== at
                ? { at, asked: { at: "failed", why: describe(error, response.status) } }
                : current,
            );
            return;
          }

          setUnanswered(false);
          setState({ at, asked: { at: "known", value: data } });
        } catch {
          // An aborted request is a screen that walked on, not a failure.
          if (live && !controller.signal.aborted) {
            setUnanswered(true);
            setState((current) =>
              current === undefined || current.at !== at
                ? { at, asked: { at: "failed", why: "The instance did not answer." } }
                : current,
            );
          }
        }
      })();

      return () => {
        live = false;
        controller.abort();
      };
    },
    // `request` is the stable handle of `useStable` and never changes, so
    // listing it costs nothing; `at` is what actually decides when to ask
    // again.
    [at, request],
  );

  useEffect(() => run(), [run, round]);

  useEffect(() => {
    quiet.current = run;
  }, [run]);

  useEffect(() => {
    if (every === false || hold) {
      return;
    }

    // A hidden tab asks nothing, and asks once the moment it is looked at
    // again. `document` is consulted on each tick rather than subscribed to
    // alone, because a tab can be hidden and shown between two of them.
    function onVisible() {
      if (document.visibilityState === "visible") {
        quiet.current?.();
      }
    }

    const timer = setInterval(() => {
      if (document.visibilityState === "visible") {
        quiet.current?.();
      }
    }, every);

    document.addEventListener("visibilitychange", onVisible);

    return () => {
      clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [every, hold, at]);

  return {
    asked: state !== undefined && state.at === at ? state.asked : asking,
    again: useCallback(() => {
      // Asking on purpose does say so on the screen: what it replaces may have
      // been an answer, and leaving it standing while a new request runs would
      // show something that is no longer being claimed.
      setState(undefined);
      setRound((current) => current + 1);
    }, []),
    refresh: run,
    unanswered,
  };
}

/**
 * The latest version of a callback behind a handle that never changes, so that
 * a request written inline at the call site is not a reason to ask again on
 * every render.
 */
function useStable<A extends unknown[], R>(callback: (...args: A) => R): (...args: A) => R {
  const latest = useRef(callback);

  useEffect(() => {
    latest.current = callback;
  });

  return useCallback((...args: A) => latest.current(...args), []);
}
