import { useState } from "react";

import { api, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Busy, Failed } from "@/shell/States";
import { applications } from "@/shell/applications";
import type { ApplicationState, TheApplications } from "@/shell/useApplications";
import { refusal, type Me } from "@/session/useSession";
import { Refused } from "@/shared/Form";

/**
 * The four switches.
 *
 * <b>A switch is a guarded write like any other</b> (`docs/api.md`, The guarded
 * write): it sends the version the screen read, and a switch made from a stale
 * screen is refused rather than applied. That is not ceremony here — the owner
 * has a phone and a desk, and the workspace is exactly the thing both of them
 * are looking at.
 *
 * Switching is the owner's alone, so an agent is shown the switches as they
 * stand and no buttons. Drawing buttons that can only ever be refused would be
 * offering something that is not on offer.
 */
export function ApplicationSwitches({
  me,
  applications: asked,
}: {
  me: Me;
  applications: TheApplications;
}) {
  const [working, setWorking] = useState<string>();
  const [refused, setRefused] = useState<string>();

  async function flip(state: ApplicationState) {
    setWorking(state.application);
    setRefused(undefined);

    try {
      const answer = await api.PUT("/api/applications/{application}", {
        params: {
          path: { application: state.application },
          ...guardedBy(versionOf(state.updated_at)),
        },
        body: { enabled: !state.enabled },
      });

      if (answer.error) {
        const { message, code } = refusal(answer.error, answer.response.status);

        setRefused(
          code === "stale"
            ? "This workspace changed somewhere else while the screen was open. It is being read again."
            : message,
        );
      }
    } finally {
      // Either way: what is on the screen afterwards is what the instance says,
      // not what this screen hoped for.
      asked.again();
      setWorking(undefined);
    }
  }

  if (asked.asked.at === "asking") {
    return <Busy title="Asking which applications this workspace has…" />;
  }

  if (asked.asked.at === "failed") {
    return <Failed why={asked.asked.why} again={asked.again} />;
  }

  const states = asked.asked.value;

  return (
    <section className="flex flex-col gap-4">
      <p className="text-muted-foreground max-w-prose text-sm text-balance">
        A workspace is four applications and you can switch any of them off. What is in one is kept
        exactly as it is — switching it off hides it from this browser, the API and the CLI, and
        switching it on again brings it back untouched. Retention keeps running while an application
        is off: what is in the Trash still expires on the day it was going to.
      </p>

      <Refused>{refused}</Refused>

      <ul className="flex flex-col gap-3">
        {applications.map((application) => {
          const state = states.find((candidate) => candidate.application === application.name);

          if (state === undefined) {
            return null;
          }

          return (
            <li
              key={application.name}
              className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-4"
            >
              <div className="flex flex-col gap-1">
                <div className="flex items-center gap-2 font-medium">
                  <application.icon aria-hidden className="size-4" />
                  {application.label}
                </div>
                <p className="text-muted-foreground text-sm text-balance">{application.hint}</p>
              </div>

              <div className="flex items-center gap-3">
                <span
                  className={
                    state.enabled ? "text-brand text-sm" : "text-muted-foreground text-sm"
                  }
                >
                  {state.enabled ? "On" : "Off"}
                </span>

                {me.kind === "owner" && (
                  <Button
                    type="button"
                    variant="outline"
                    disabled={working !== undefined}
                    aria-label={`${state.enabled ? "Switch off" : "Switch on"} ${application.label}`}
                    onClick={() => void flip(state)}
                  >
                    {working === state.application
                      ? "…"
                      : state.enabled
                        ? "Switch off"
                        : "Switch on"}
                  </Button>
                )}
              </div>
            </li>
          );
        })}
      </ul>

      {me.kind !== "owner" && (
        <p className="text-muted-foreground text-xs text-balance">
          Switching an application on or off is the owner's alone. Agent access acts on the owner's
          behalf in the applications it was given and nowhere else.
        </p>
      )}
    </section>
  );
}
