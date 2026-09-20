import { useBookmarkPrivacy } from "@/bookmarks/useBookmarkPrivacy";
import { useState } from "react";

import { api, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Busy, Empty, Failed } from "@/shell/States";
import { applicationNamed } from "@/shell/applications";
import { useAsk } from "@/shared/ask";
import { refusal } from "@/session/useSession";
import { Refused } from "@/shared/Form";

/**
 * One list over the four applications: what was deleted and can still be had
 * back (`docs/api.md`, The Trash).
 *
 * <b>It is empty in this build and that is the shape working</b>, not the shape
 * missing. Deleting is what fills it, and nothing can be deleted until an
 * application has content — so what is proved here today is the screen, the
 * refresh and the guarded restore, against a list the instance really answers.
 *
 * A restore sends back the version the list gave it. Two browsers looking at
 * the same entry is the ordinary case in a workspace one person carries around,
 * and the second restore is refused rather than applied to something that has
 * moved.
 */
export function Trash() {
  const privacy = useBookmarkPrivacy();
  const [refused, setRefused] = useState<string>();
  const [working, setWorking] = useState<string>();

  const { asked, again, refresh, unanswered } = useAsk(`/api/trash:${privacy.epoch}`, (signal) =>
    api.GET("/api/trash", { signal, headers: privacy.headers }),
  );

  async function restore(entry: Schemas["TrashEntryResponse"]) {
    setWorking(entry.id);
    setRefused(undefined);

    try {
      const answer = await api.POST("/api/trash/{application}/{id}/restore", {
        headers: privacy.headers,
        params: {
          path: { application: entry.application, id: entry.id },
          ...guardedBy(versionOf(entry.updated_at)),
        },
      });

      if (answer.error) {
        const { message, code } = refusal(answer.error, answer.response.status);

        setRefused(
          code === "stale"
            ? "That entry changed while this list was open. It is being read again."
            : message,
        );
      }
    } finally {
      refresh();
      setWorking(undefined);
    }
  }

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">Trash</h1>
        <p className="text-muted-foreground text-sm text-balance">
          Deleting a page, a task, a list, a file or a folder sets it aside rather than destroying
          it. An application that is switched off is not shown here, and what is in its Trash goes
          on expiring on the day it was going to.
        </p>
      </header>

      <Refused>{refused}</Refused>

      {/* The list is still the last one the instance gave, and this says so.
          Restoring from a list that is not current is what the guard on the
          write catches; this is so that nobody is surprised by it. */}
      {unanswered && asked.at === "known" && (
        <p role="status" className="text-muted-foreground text-xs text-balance">
          The instance stopped answering. This is what it last said.
        </p>
      )}

      {asked.at === "asking" && <Busy title="Reading the Trash…" />}
      {asked.at === "failed" && <Failed why={asked.why} again={again} />}

      {asked.at === "known" && asked.value.items.length === 0 && (
        <Empty title="Nothing has been deleted.">
          There is no content in this build yet, so there is nothing that could have been. What is
          deleted later appears here and can be put back for as long as its recovery period lasts.
        </Empty>
      )}

      {asked.at === "known" && asked.value.items.length > 0 && (
        <ul className="flex flex-col gap-2">
          {asked.value.items.map((entry) => (
            <li
              key={`${entry.application}:${entry.id}`}
              className="flex flex-wrap items-center justify-between gap-3 rounded-lg border p-3"
            >
              <div className="flex flex-col gap-0.5">
                <span className="font-medium">{entry.name}</span>
                <span className="text-muted-foreground text-xs">
                  {applicationNamed(entry.application)?.label ?? entry.application}
                  {entry.where !== null && entry.where !== undefined && ` · ${entry.where}`} ·
                  deleted by {entry.deleted_by.kind === "owner" ? "you" : entry.deleted_by.name} ·
                  expires {new Date(entry.expires_at).toLocaleDateString()}
                </span>
              </div>

              <Button
                type="button"
                variant="outline"
                disabled={working !== undefined}
                onClick={() => void restore(entry)}
              >
                {working === entry.id ? "…" : "Put it back"}
              </Button>
            </li>
          ))}
        </ul>
      )}

      {asked.at === "known" && asked.value.has_more && (
        <p className="text-muted-foreground text-xs">
          There is more in the Trash than this list shows.
        </p>
      )}

      {/* The one thing this screen cannot do. Emptying it is the owner's alone
          and there is no way back from it, so it is not a button beside a list
          somebody is scanning. */}
      <p className="text-muted-foreground text-xs text-balance">
        Nothing here is removed for good by this screen. What expires, expires on its own; removing
        something before then is the owner's doing over the API.
      </p>
    </main>
  );
}
