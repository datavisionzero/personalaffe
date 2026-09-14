import { Link } from "react-router";

import { Busy, Failed } from "@/shell/States";
import { applications, type Application } from "@/shell/applications";
import type { TheApplications } from "@/shell/useApplications";
import type { Me } from "@/session/useSession";

/**
 * The home page.
 *
 * <b>The dashboard is PERSONAL-E9</b> — the tiles of what is pending, what was
 * touched, the weather — and it lands here. What is here today is the one thing
 * the home page can honestly say before there is any content: which
 * applications this workspace has, and which are switched off.
 *
 * It is not a copy of the sidebar. The sidebar offers what can be walked into;
 * this says what the workspace is, switched-off applications included, because
 * "where did my Tasks go" is a question the navigation cannot answer by leaving
 * them out.
 */
export function Home({ me, applications: asked }: { me: Me; applications: TheApplications }) {
  const called = me.email ?? me.name ?? "this workspace";

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-8 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">Your workspace</h1>
        <p className="text-muted-foreground text-sm">{called}</p>
      </header>

      {asked.asked.at === "asking" && <Busy title="Asking what this workspace has…" />}

      {asked.asked.at === "failed" && <Failed why={asked.asked.why} again={asked.again} />}

      {asked.asked.at === "known" && (
        <section aria-labelledby="applications" className="flex flex-col gap-3">
          <h2 id="applications" className="text-muted-foreground text-xs font-medium uppercase">
            Applications
          </h2>

          <ul className="grid gap-3 sm:grid-cols-2">
            {applications.map((application) => (
              <Tile
                key={application.name}
                application={application}
                state={asked.stateOf(application.name)}
              />
            ))}
          </ul>

          <p className="text-muted-foreground text-xs text-balance">
            Switching an application off hides it everywhere — here, in the navigation, over the API
            and in the CLI. Nothing in it is removed, and its retention deadlines keep running.{" "}
            <Link className="text-brand underline-offset-4 hover:underline" to="/settings/applications">
              The switches are in Settings.
            </Link>
          </p>
        </section>
      )}
    </main>
  );
}

function Tile({
  application,
  state,
}: {
  application: Application;
  state: { enabled: boolean; permission: string } | undefined;
}) {
  const reachable = state !== undefined && state.permission !== "none";
  const open = reachable && state.enabled;

  const inside = (
    <>
      <div className="flex items-center gap-2 font-medium">
        <application.icon aria-hidden className="size-4" />
        {application.label}
        {state !== undefined && !state.enabled && (
          <span className="text-muted-foreground bg-secondary rounded-full px-2 py-0.5 text-[11px] font-normal">
            switched off
          </span>
        )}
        {!reachable && state !== undefined && (
          <span className="text-muted-foreground bg-secondary rounded-full px-2 py-0.5 text-[11px] font-normal">
            not yours to see
          </span>
        )}
      </div>
      <p className="text-muted-foreground text-sm text-balance">{application.hint}</p>
    </>
  );

  return (
    <li>
      {open ? (
        <Link
          to={application.path}
          className="hover:border-ring flex h-full flex-col gap-1.5 rounded-lg border p-4 transition-colors"
        >
          {inside}
        </Link>
      ) : (
        <div className="bg-muted/30 flex h-full flex-col gap-1.5 rounded-lg border border-dashed p-4">
          {inside}
        </div>
      )}
    </li>
  );
}
