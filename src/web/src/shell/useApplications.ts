import { useCallback, useMemo } from "react";

import { api, type Schemas } from "@/api/client";
import { useAsk, type Asked } from "@/shared/ask";
import { applications, type Application, type ApplicationName } from "./applications";

export type ApplicationState = Schemas["ApplicationResponse"];

/**
 * Which applications this workspace has switched on, and what this credential
 * may do in each (`docs/api.md`, The applications).
 *
 * The frame asks this once and everything else reads the answer: the sidebar
 * draws it, the palette offers it, a route consults it before it draws a
 * screen. Asking it in each of those places would be four requests and four
 * moments at which they could disagree.
 *
 * It refreshes like everything else, which is what makes switching an
 * application off on the phone take it out of the navigation on the desk
 * without a reload.
 */
export type TheApplications = {
  asked: Asked<ApplicationState[]>;
  /** The applications to offer: switched on, and readable by this caller. */
  offered: Application[];
  /** What this caller may do in one of them, whatever the switch says. */
  stateOf: (name: ApplicationName) => ApplicationState | undefined;
  /** Ask again — after switching one. */
  again: () => void;
  unanswered: boolean;
};

export function useApplications(): TheApplications {
  const { asked, again, unanswered } = useAsk("/api/applications", (signal) =>
    api.GET("/api/applications", { signal }),
  );

  const states = useMemo(
    () => (asked.at === "known" ? asked.value.items : []),
    [asked],
  );

  const stateOf = useCallback(
    (name: ApplicationName) => states.find((state) => state.application === name),
    [states],
  );

  const offered = useMemo(
    () =>
      applications.filter((application) => {
        const state = states.find((candidate) => candidate.application === application.name);

        // Nothing is offered until the instance has said. Drawing all four and
        // taking one away a moment later is how a person clicks the thing that
        // was there when they started moving.
        return state !== undefined && state.enabled && state.permission !== "none";
      }),
    [states],
  );

  return {
    asked: asked.at === "known" ? { at: "known", value: asked.value.items } : asked,
    offered,
    stateOf,
    again,
    unanswered,
  };
}
