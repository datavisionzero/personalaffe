import { useCallback, useEffect, useState } from "react";

import { api, codeOf, describe, type Problem } from "@/api/client";

/**
 * What the instance said about itself, as one of the four things a screen has
 * to be able to draw.
 *
 * The three that are not "here it is" are separate on purpose. "The instance
 * refused" and "nothing answered at all" look the same to a `try`/`catch` and
 * are different to the person reading the screen: one means this installation
 * is up and said no, the other means it is not there — a container that has not
 * started, a proxy in front of nothing, a laptop with no network.
 */
export type Instance =
  | { state: "asking" }
  | { state: "answered"; version: string }
  | { state: "refused"; reason: string; code?: string }
  | { state: "unreachable"; reason: string };

/**
 * Asks the instance what version it is — the one operation that answers before
 * anything has authenticated (`docs/api.md`).
 */
export function useInstance(): { instance: Instance; ask: () => void } {
  const [instance, setInstance] = useState<Instance>({ state: "asking" });

  // The request, and nothing else: no state is written on the way in, so that
  // the effect below starts it without a render of its own. What it returns is
  // how the caller says it has stopped listening — an answer that arrives after
  // the screen is gone is dropped rather than written into a state nobody is
  // reading.
  const request = useCallback(() => {
    let abandoned = false;

    api
      .GET("/api/version")
      .then(({ data, error, response }) => {
        if (abandoned) {
          return;
        }

        if (data) {
          setInstance({ state: "answered", version: data.version });
          return;
        }

        const problem = error as Problem | undefined;
        setInstance({
          state: "refused",
          reason: describe(problem, response.status),
          code: codeOf(problem),
        });
      })
      .catch((failure: unknown) => {
        if (!abandoned) {
          setInstance({
            state: "unreachable",
            reason: failure instanceof Error ? failure.message : String(failure),
          });
        }
      });

    return () => {
      abandoned = true;
    };
  }, []);

  useEffect(() => request(), [request]);

  // Asking again is somebody's doing, so this one does say so on the screen
  // first: the state it replaces may have been an answer, and leaving it
  // standing while a new request runs would show something that is no longer
  // being claimed.
  const ask = useCallback(() => {
    setInstance({ state: "asking" });
    request();
  }, [request]);

  return { instance, ask };
}
