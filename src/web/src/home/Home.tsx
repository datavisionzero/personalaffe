import type { LucideIcon } from "lucide-react";
import { Link } from "react-router";

import { Busy, Empty, Failed } from "@/shell/States";
import { applicationNamed, applications } from "@/shell/applications";
import type { TheApplications } from "@/shell/useApplications";
import type { Me } from "@/session/useSession";
import { todayHere } from "@/tasks/today";
import { drawn, useDashboard, type TheHome, type TileName } from "./useDashboard";
import { Weather } from "./Weather";

/**
 * The home page, which is the dashboard (VISION §6.1).
 *
 * <b>It answers one question: what is useful or pending right now.</b> Not what
 * this workspace contains — the sidebar says that — and not a report. Five
 * tiles in a fixed order, each with at most five rows, each row a link to the
 * thing itself.
 *
 * <b>A tile that is not being drawn is not here at all, and one that is drawn
 * and holds nothing says so.</b> Those are the instance's two different answers
 * (`null` against `[]`), and collapsing them would mean the owner could not tell
 * "I hid that" from "there is nothing in it".
 *
 * The weather draws itself, from its own address, so that the four tiles above
 * it never wait on a server somewhere else.
 */
export function Home({ me, applications: asked }: { me: Me; applications: TheApplications }) {
  const { asked: home, again } = useDashboard();
  const called = me.email ?? me.name ?? "this workspace";

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">What is useful or pending</h1>
        <p className="text-muted-foreground text-sm">{called}</p>
      </header>

      {home.at === "asking" && <Busy title="Asking what is pending…" />}

      {home.at === "failed" && <Failed why={home.why} again={again} />}

      {home.at === "known" && <Tiles home={home.value} applications={asked} />}
    </main>
  );
}

function Tiles({ home, applications: asked }: { home: TheHome; applications: TheApplications }) {
  const nothing =
    home.tasks === null &&
    home.knowledge === null &&
    home.scratchpad === null &&
    home.files === null &&
    !drawn(home.tiles, "weather");

  if (nothing) {
    return (
      <Empty title="Nothing is on this home page.">
        Every tile is hidden, or the applications behind them are switched off.{" "}
        <Link className="text-brand underline-offset-4 hover:underline" to="/settings/home">
          The tiles are in Settings.
        </Link>
      </Empty>
    );
  }

  const today = todayHere();

  return (
    <div className="flex flex-col gap-4">
      {home.tasks !== null && (
        <Panel tile="tasks" empty="Nothing is open.">
          {home.tasks.map((task) => (
            <Row key={task.id} to={`/tasks/${task.list_id}`} title={task.title}>
              <span className="text-muted-foreground text-xs">{task.list}</span>
              {task.due_on !== null && (
                <span
                  className={
                    task.due_on <= today
                      ? "text-destructive text-xs tabular-nums"
                      : "text-muted-foreground text-xs tabular-nums"
                  }
                >
                  {/* Two strings compared, never two dates: a day is a day
                      wherever the browser is standing (`tasks/today.ts`). */}
                  {task.due_on}
                  {task.due_on <= today && " ·  due"}
                </span>
              )}
            </Row>
          ))}
        </Panel>
      )}

      {home.knowledge !== null && (
        <Panel tile="knowledge" empty="No pages yet.">
          {home.knowledge.map((page) => (
            <Row key={page.id} to={`/knowledge/${page.id}`} title={page.title}>
              <Changed at={page.updated_at} />
            </Row>
          ))}
        </Panel>
      )}

      {home.scratchpad !== null && (
        <Panel tile="scratchpad" empty="Nothing is in the Scratchpad.">
          {home.scratchpad.map((entry) => (
            <Row key={entry.id} to="/scratchpad" title={firstLine(entry.preview)}>
              {entry.pinned && <span className="text-muted-foreground text-xs">pinned</span>}
              <Changed at={entry.updated_at} />
            </Row>
          ))}
        </Panel>
      )}

      {home.files !== null && (
        <Panel tile="files" empty="No files yet.">
          {home.files.map((file) => (
            <Row
              key={file.id}
              to={file.folder_id === null ? "/files" : `/files/${file.folder_id}`}
              title={file.name}
            >
              <Changed at={file.updated_at} />
            </Row>
          ))}
        </Panel>
      )}

      {drawn(home.tiles, "weather") && (
        <section aria-labelledby="tile-weather" className="rounded-lg border p-4">
          <h2 id="tile-weather" className="text-muted-foreground mb-3 text-xs font-medium uppercase">
            Weather
          </h2>
          <Weather />
        </section>
      )}

      <p className="text-muted-foreground text-xs text-balance">
        Every tile can be hidden.{" "}
        <Link className="text-brand underline-offset-4 hover:underline" to="/settings/home">
          The tiles are in Settings.
        </Link>{" "}
        {asked.offered.length < applications.length &&
          "An application that is switched off, or out of this credential's reach, offers no tile."}
      </p>
    </div>
  );
}

/**
 * One tile: a heading that walks into the application it is about, and its rows.
 *
 * The heading is a link because that is what a tile is for — five rows and then
 * "all of them", rather than five rows and a dead end.
 */
function Panel({
  tile,
  empty,
  children,
}: {
  tile: Exclude<TileName, "weather">;
  empty: string;
  children: React.ReactNode[];
}) {
  const application = applicationNamed(tile);
  const label = application?.label ?? tile;
  const Icon: LucideIcon | undefined = application?.icon;

  return (
    <section aria-labelledby={`tile-${tile}`} className="rounded-lg border p-4">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2
          id={`tile-${tile}`}
          className="text-muted-foreground flex items-center gap-2 text-xs font-medium uppercase"
        >
          {Icon !== undefined && <Icon aria-hidden className="size-3.5" />}
          {label}
        </h2>
        {application !== undefined && (
          <Link
            className="text-muted-foreground hover:text-foreground text-xs underline-offset-4 hover:underline"
            to={application.path}
          >
            All of it
          </Link>
        )}
      </div>

      {children.length === 0 ? (
        <p className="text-muted-foreground text-sm">{empty}</p>
      ) : (
        <ul className="flex flex-col">{children}</ul>
      )}
    </section>
  );
}

function Row({
  to,
  title,
  children,
}: {
  to: string;
  title: string;
  children?: React.ReactNode;
}) {
  return (
    <li>
      <Link
        to={to}
        className="hover:bg-accent -mx-2 flex items-center justify-between gap-3 rounded-md px-2 py-1.5 text-sm"
      >
        <span className="truncate">{title}</span>
        <span className="flex shrink-0 items-center gap-2">{children}</span>
      </Link>
    </li>
  );
}

/** When something last changed, as a day. A tile is not a log. */
function Changed({ at }: { at: string }) {
  return <span className="text-muted-foreground text-xs tabular-nums">{at.slice(0, 10)}</span>;
}

/**
 * A Scratchpad entry has no title, so the first line of it stands in — which is
 * what the instance already does for the search, and what a person recognises
 * an entry by.
 */
function firstLine(preview: string): string {
  const line = preview.split("\n", 1)[0]?.trim() ?? "";

  return line === "" ? "(blank)" : line;
}

