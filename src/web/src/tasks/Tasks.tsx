import { AreaNavigation } from "@/shell/AreaNavigation";
import { useDestination } from "@/shell/navigation";
import {
  ChevronDownIcon,
  ChevronUpIcon,
  ListTodoIcon,
  PencilIcon,
  PlusIcon,
  StickyNoteIcon,
  Trash2Icon,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";

import { api, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { refusal } from "@/session/useSession";
import { Field, Refused } from "@/shared/Form";
import { Markdown } from "@/shared/Markdown";
import { MarkdownField } from "@/shared/MarkdownField";
import { useAsk } from "@/shared/ask";
import { todayHere } from "@/tasks/today";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Lists = Schemas["TaskListsResponse"];
type List = Schemas["TaskListResponse"];
type Tasks = Schemas["TasksResponse"];
type Task = Schemas["TaskResponse"];

/** What a write answers, as far as this screen reads it. */
type Sent = { error?: unknown; response: Response };

/**
 * What was just put in the Trash from the address it was put there from, and
 * what bringing it back takes.
 */
type Gone = {
  at: string;
  sentence: string;
  /** The Trash entries: one list, or one or more tasks. */
  back: string[];
  /** The list to walk to once it is back, when what went was a list. */
  list?: string;
};

/**
 * Tasks: the lists beside them, a box to tick, and an order the owner sets
 * (`docs/api.md`, Tasks).
 *
 * <b>A due date is drawn from the date and never from a `Date`.</b> The
 * instance answers `2026-09-14`; parsing that into a JavaScript `Date` and
 * formatting it is exactly how it becomes the thirteenth for somebody west of
 * the instance. So the string is split and reassembled, and today is compared
 * as a string too.
 *
 * <b>Moving is two buttons and not a drag.</b> A drag needs a pointer, and this
 * screen has to work with a keyboard alone at 400px; a personal list is short
 * enough that a step at a time is the whole interaction. <b>A step is a step
 * among what is drawn</b>: open and completed tasks share one order on the
 * instance and are two groups here, so a task moves past its neighbour in its
 * own group — and past whatever of the other group sits between them, which
 * nobody can see and nobody meant.
 *
 * <b>Deleting is not asked about and can be taken back where it was done.</b>
 * Nothing is destroyed, so there is no dialog in the way; the sentence that
 * says where it went carries the button that brings it back, which is the
 * Trash's own restore and nothing this screen keeps for itself.
 */
export function Tasks() {
  const params = useParams();
  const navigate = useNavigate();
  const [search] = useSearchParams();

  const at = (params["*"] ?? "").replace(/\/+$/, "");
  const open = at === "" ? undefined : at;

  // `?task=` is how the home page and a search point at one task: there is no
  // screen for a single task, so its row is marked and its notes are open.
  const marked = search.get("task") ?? undefined;

  const [capturing, setCapturing] = useState("");
  const [due, setDue] = useState("");
  const [wrote, setWrote] = useState<{ at: string; refused?: string }>();
  const [working, setWorking] = useState<string>();
  const [making, setMaking] = useState(false);
  const [editing, setEditing] = useState<Task>();
  const [renaming, setRenaming] = useState<List>();
  const [gone, setGone] = useState<Gone>();
  // Whose notes are the other way round from how they start: closed, except on
  // the task the address points at.
  const [flipped, setFlipped] = useState<ReadonlySet<string>>(new Set());

  // The control the keyboard was on when it changed something, in the order to
  // look for it afterwards. A tick moves a row to the other group and a step
  // moves it past another, and either takes the focus away with the element.
  const refocus = useRef<string[]>(undefined);

  const address = open === undefined ? "/tasks" : `/tasks/${open}`;
  const refused = wrote?.at === address ? wrote.refused : undefined;

  const [why, setWhy] = useState<string>();

  const lists = useAsk<Lists>("/api/tasks/lists", async (signal) => {
    const answer = await api.GET("/api/tasks/lists", { signal });

    setWhy(answer.error ? refusal(answer.error, answer.response.status).code : undefined);

    return answer;
  }, { hold: renaming !== undefined });

  const tasks = useAsk<Tasks>(
    address,
    (signal) =>
      api.GET("/api/tasks/lists/{id}/tasks", {
        params: { path: { id: open ?? "" } },
        signal,
      }),
    // Nothing arrives underneath a half-typed capture or an open dialog — what
    // the dialog holds is the version the owner is looking at, and a write from
    // it is refused rather than applied over something they never saw. Nothing
    // at all is asked when no list is open.
    {
      hold: capturing !== "" || due !== "" || editing !== undefined,
      every: open === undefined ? false : undefined,
    },
  );

  const all = lists.asked.at === "known" ? lists.asked.value.items : [];
  useDestination(address, open ? all.find(item => item.id === open)?.name : "Tasks", !!open && lists.asked.at === "known" && !all.some(item => item.id === open));
  const current = open === undefined ? undefined : all.find((list) => list.id === open);
  const here = open !== undefined && tasks.asked.at === "known" ? tasks.asked.value.items : [];
  const openTasks = here.filter((task) => !task.completed);
  const done = here.filter((task) => task.completed);

  const order = here.map((task) => `${task.id}${task.completed ? "+" : "-"}`).join(" ");

  useEffect(() => {
    if (working !== undefined) {
      return;
    }

    for (const control of refocus.current ?? []) {
      const found = document.querySelector<HTMLElement>(`[data-control="${control}"]:not(:disabled)`);

      if (found !== null) {
        found.focus();
        return;
      }
    }
  }, [order, working]);

  const pointedAt = marked !== undefined && here.some((task) => task.id === marked);

  useEffect(() => {
    if (!pointedAt || marked === undefined) {
      return;
    }

    const row = document.getElementById(`task-${marked}`);

    // Asked for rather than assumed: not everything that draws this has a
    // viewport to scroll.
    if (row !== null && "scrollIntoView" in row) {
      row.scrollIntoView({ block: "center" });
    }
  }, [pointedAt, marked]);

  async function write(what: string, sending: () => Promise<Sent>) {
    setWorking(what);
    setWrote({ at: address });

    try {
      const answer = await sending();

      if (answer.error) {
        setWrote({ at: address, refused: said(answer.error, answer.response.status) });
        if (refusal(answer.error, answer.response.status).code === "stale") {
          // Both, because the sentence says it is being read again and either
          // may be what was stale.
          lists.refresh();
          if (open !== undefined) {
            tasks.refresh();
          }
        }
        return false;
      }

      return true;
    } finally {
      setWorking(undefined);
    }
  }

  async function capture() {
    if (capturing.trim() === "" || open === undefined) {
      return;
    }

    const went = await write("capture", () =>
      api.POST("/api/tasks/lists/{id}/tasks", {
        params: { path: { id: open } },
        body: { title: capturing.trim(), description: "", due_on: due === "" ? null : due },
      }),
    );

    if (went) {
      // Cleared only once the instance has it: a box emptied optimistically is
      // a box that loses what somebody typed when the write is refused.
      setCapturing("");
      setDue("");
      tasks.refresh();
      lists.refresh();
    }
  }

  async function change(task: Task, to: Partial<Task>) {
    // What is on the screen now, which after a stale refusal is newer than
    // what a dialog was opened with: a deliberate retry uses that version.
    const latest = here.find((one) => one.id === task.id) ?? task;
    const list = to.list ?? latest.list;

    const went = await write(task.id, async () => {
      let after = to.after === undefined ? latest.after : to.after;

      if (list !== latest.list) {
        // Arriving in another list is arriving at the end of it, which is
        // where anything arriving in a list goes — and `null` would be the top.
        const there = await api.GET("/api/tasks/lists/{id}/tasks", {
          params: { path: { id: list } },
        });

        if (there.data === undefined) {
          return there;
        }

        after = there.data.items[there.data.items.length - 1]?.id ?? null;
      }

      return api.PUT("/api/tasks/{id}", {
        params: { path: { id: task.id }, ...guardedBy(versionOf(latest.updated_at)) },
        // One write carries all of it, because all of it is one row.
        body: {
          list,
          title: to.title ?? latest.title,
          description: to.description ?? latest.description,
          due_on: to.due_on === undefined ? latest.due_on : to.due_on,
          completed: to.completed ?? latest.completed,
          after,
        },
      });
    });

    if (went) {
      setEditing(undefined);
      tasks.refresh();
      lists.refresh();
    }
  }

  async function discard(task: Task) {
    const went = await write(task.id, () =>
      api.DELETE("/api/tasks/{id}", {
        params: { path: { id: task.id }, ...guardedBy(versionOf(task.updated_at)) },
      }),
    );

    if (went) {
      setGone({ at: address, sentence: `${task.title} is in the Trash.`, back: [task.id] });
      tasks.refresh();
      lists.refresh();
    }
  }

  /**
   * Puts every completed task on the open list in the Trash, one write each:
   * each is its own entry there, as it would be had they been deleted one at a
   * time, and what a refusal half-way leaves behind is exactly what went.
   */
  async function discardDone() {
    const back: string[] = [];

    await write("discard-done", async () => {
      let sent: Sent = { response: new Response(null, { status: 204 }) };

      for (const task of done) {
        sent = await api.DELETE("/api/tasks/{id}", {
          params: { path: { id: task.id }, ...guardedBy(versionOf(task.updated_at)) },
        });

        if (sent.error) {
          break;
        }

        back.push(task.id);
      }

      return sent;
    });

    if (back.length > 0) {
      setGone({
        at: address,
        sentence:
          back.length === 1
            ? "1 completed task is in the Trash."
            : `${back.length} completed tasks are in the Trash.`,
        back,
      });
    }

    tasks.refresh();
    lists.refresh();
  }

  /**
   * Puts the open list in the Trash with every task on it. Nothing is
   * destroyed, so — like a folder in Files — it is not asked about first; the
   * sentence afterwards says where it went.
   */
  async function discardList(list: List) {
    const went = await write("discard-list", () =>
      api.DELETE("/api/tasks/lists/{id}", {
        params: { path: { id: list.id }, ...guardedBy(versionOf(list.updated_at)) },
      }),
    );

    if (went) {
      setGone({
        at: "/tasks",
        sentence: `${list.name} and everything on it are in the Trash.`,
        back: [list.id],
        list: list.id,
      });
      lists.again();
      void navigate("/tasks");
    }
  }

  /**
   * Brings back what was just deleted here. It is the Trash's restore and
   * needs the version the Trash holds, so the Trash is read first; an entry
   * that is no longer there was put back, or removed, somewhere else.
   */
  async function putBack(what: Gone) {
    let restored = 0;

    const went = await write("put-back", async () => {
      const trash = await api.GET("/api/trash");

      if (trash.data === undefined) {
        return trash;
      }

      let sent: Sent = trash;

      for (const id of what.back) {
        const entry = trash.data.items.find((one) => one.application === "tasks" && one.id === id);

        if (entry === undefined) {
          continue;
        }

        sent = await api.POST("/api/trash/{application}/{id}/restore", {
          params: {
            path: { application: "tasks", id },
            ...guardedBy(versionOf(entry.updated_at)),
          },
        });

        if (sent.error) {
          break;
        }

        restored += 1;
      }

      return sent;
    });

    if (went) {
      setGone(undefined);

      if (restored === 0) {
        setWrote({ at: address, refused: "That is no longer in the Trash." });
      } else if (what.list !== undefined) {
        void navigate(`/tasks/${what.list}`);
      }
    }

    lists.refresh();
    if (open !== undefined) {
      tasks.refresh();
    }
  }

  async function makeList(name: string) {
    const went = await write("list", () => api.POST("/api/tasks/lists", { body: { name } }));

    if (went) {
      setMaking(false);
      lists.again();
    }
  }

  async function renameList(list: List, name: string) {
    // A stale refusal refreshes the lists; a deliberate retry uses that version.
    const latest = all.find((one) => one.id === list.id) ?? list;
    const went = await write("rename-list", () =>
      api.PUT("/api/tasks/lists/{id}", {
        params: { path: { id: list.id }, ...guardedBy(versionOf(latest.updated_at)) },
        body: { name },
      }),
    );

    if (went) {
      setRenaming(undefined);
      lists.refresh();
    }
  }

  /**
   * Moves a task one step among the ones drawn with it — the open ones, or the
   * completed ones.
   */
  function step(task: Task, by: -1 | 1) {
    const group = task.completed ? done : openTasks;
    const neighbour = group[group.findIndex((one) => one.id === task.id) + by];

    if (neighbour === undefined || !group.includes(task)) {
      return;
    }

    // Down is behind the neighbour. Up is where the neighbour is, which is
    // behind whatever the neighbour sits behind — in the whole order, because
    // that is the order the instance keeps. `null` is the top of the list.
    const without = here.filter((one) => one.id !== task.id);
    const after =
      by === 1 ? neighbour.id : (without[without.indexOf(neighbour) - 1]?.id ?? null);

    refocus.current =
      by === 1 ? [`${task.id}:down`, `${task.id}:up`] : [`${task.id}:up`, `${task.id}:down`];

    void change(task, { after });
  }

  function flip(task: Task) {
    setFlipped((before) => {
      const next = new Set(before);

      if (!next.delete(task.id)) {
        next.add(task.id);
      }

      return next;
    });
  }

  const wentAway = gone !== undefined && gone.at === address && (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
      <p role="status" className="text-muted-foreground text-sm text-balance">
        {gone.sentence}
      </p>
      <Button
        variant="outline"
        size="sm"
        disabled={working !== undefined}
        onClick={() => void putBack(gone)}
      >
        {gone.back.length === 1 ? "Put it back" : "Put them back"}
      </Button>
    </div>
  );

  return (
    <main
      className="page-frame md:flex-row"
      // Whatever the owner does next is where the focus belongs, so the place
      // remembered for after a write is forgotten the moment they do anything.
      // A step or a tick sets it again, in the click that follows.
      onKeyDownCapture={() => {
        refocus.current = undefined;
      }}
      onPointerDownCapture={() => {
        refocus.current = undefined;
      }}
    >
      <AreaNavigation label="Lists">
      <nav
        aria-label="The lists"
        className="flex shrink-0 flex-col gap-2 md:w-56 md:border-r md:pr-4"
      >
        <div className="flex items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Tasks</h1>
          <Button variant="outline" size="sm" aria-label="New list" onClick={() => setMaking(true)}>
            <PlusIcon aria-hidden />
          </Button>
        </div>

        {lists.asked.at === "asking" && <Busy title="Reading the lists…" />}

        {lists.asked.at === "failed" && whyItFailed(lists.asked.why, lists.again)}

        {lists.asked.at === "known" && all.length === 0 && (
          <p className="text-muted-foreground text-sm text-balance">
            No lists yet. The button above makes one.
          </p>
        )}

        <ul className="flex flex-col gap-0.5">
          {all.map((list) => (
            <li key={list.id}>
              <Link
                to={`/tasks/${list.id}`}
                aria-current={list.id === open ? "page" : undefined}
                className={`flex items-center gap-2 rounded-lg px-2 py-1 text-sm hover:bg-muted ${
                  list.id === open ? "bg-muted font-medium" : ""
                }`}
              >
                <ListTodoIcon aria-hidden className="text-muted-foreground size-3.5 shrink-0" />
                <span className="truncate">{list.name}</span>
                <span className="text-muted-foreground ml-auto shrink-0 text-xs">{list.open}</span>
              </Link>
            </li>
          ))}
        </ul>
      </nav>
      </AreaNavigation>

      <section className="flex min-w-0 flex-1 flex-col gap-4">
        {open === undefined && lists.asked.at === "known" && (
          <Empty title="Pick a list, or make one.">
            Tasks are small personal commitments in named lists. A task has a title, and whatever else
            it needs: a note, a day it is due, and a place in the order you set.
          </Empty>
        )}

        {open === undefined && wentAway}

        {open !== undefined && (
          <>
            {current !== undefined && (
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="min-w-0 truncate text-lg font-semibold tracking-tight">{current.name}</h2>
                <div className="flex shrink-0 items-center gap-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={working !== undefined}
                    onClick={() => {
                      setWrote(undefined);
                      setRenaming(current);
                    }}
                  >
                    <PencilIcon aria-hidden />
                    Rename list
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={working !== undefined}
                    onClick={() => void discardList(current)}
                  >
                    <Trash2Icon aria-hidden />
                    Delete list
                  </Button>
                </div>
              </div>
            )}

            <form
              className="flex flex-wrap items-center gap-2"
              onSubmit={(event) => {
                event.preventDefault();
                void capture();
              }}
            >
              <label className="sr-only" htmlFor="capture">
                What has to be done
              </label>
              <Input
                id="capture"
                name="title"
                className="min-w-40 flex-1"
                value={capturing}
                placeholder="What has to be done?"
                onChange={(event) => setCapturing(event.target.value)}
              />
              {/* Beside the title and never in its way: Enter in the title is
                  still the whole capture, and the day is there for the task
                  that already has one when it occurs to somebody. */}
              <label className="sr-only" htmlFor="capture-due">
                When it is due
              </label>
              <Input
                id="capture-due"
                type="date"
                name="due"
                className="w-auto"
                value={due}
                onChange={(event) => setDue(event.target.value)}
              />
              <Button type="submit" disabled={capturing.trim() === "" || working === "capture"}>
                Add
              </Button>
            </form>

            {wentAway}

            {tasks.asked.at === "asking" && <Busy title="Reading the list…" />}

            {tasks.asked.at === "failed" &&
              whyItFailed(tasks.asked.why, () => {
                lists.again();
                tasks.again();
              })}

            {tasks.asked.at === "known" && here.length === 0 && (
              <Empty title="Nothing on this list.">
                Type what has to be done in the box above. It goes at the end, and you can move it
                afterwards.
              </Empty>
            )}

            {openTasks.length > 0 && rows(openTasks)}

            {done.length > 0 && (
              <>
                <div className="mt-2 flex items-center justify-between gap-2">
                  <h2 className="text-muted-foreground text-xs font-medium">Done</h2>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={working !== undefined}
                    onClick={() => void discardDone()}
                  >
                    <Trash2Icon aria-hidden />
                    Delete completed
                  </Button>
                </div>
                {rows(done)}
              </>
            )}
          </>
        )}

        <Refused>{refused}</Refused>

        {tasks.unanswered && tasks.asked.at === "known" && (
          <p role="status" className="text-muted-foreground text-xs text-balance">
            The instance stopped answering. This is what it last said.
          </p>
        )}
      </section>

      {making && <ListNameDialog
        title="New list"
        working={working === "list"}
        refused={refused}
        onClose={() => setMaking(false)}
        onSave={(name) => void makeList(name)}
      />}

      {renaming !== undefined && <ListNameDialog
        title="Rename list"
        initialName={renaming.name}
        working={working === "rename-list"}
        refused={refused}
        onClose={() => setRenaming(undefined)}
        onSave={(name) => void renameList(renaming, name)}
      />}

      {editing !== undefined && (
        <EditATask
          task={editing}
          lists={all}
          working={working === editing.id}
          refused={refused}
          onClose={() => setEditing(undefined)}
          onSave={(to) => void change(editing, to)}
        />
      )}
    </main>
  );

  /**
   * One group of rows. <b>A function that is called and not a component that
   * is rendered</b>: declared in here it would be a new component on every
   * render, and every row — with whatever had the focus — would be taken down
   * and put up again each time anything on the screen changed.
   */
  function rows(group: Task[]) {
    return (
      <ul className="flex flex-col divide-y rounded-lg border">
        {group.map((task, place) => {
          const noted = task.description.trim() !== "";
          const shown = noted && flipped.has(task.id) !== (task.id === marked);

          return (
            <li
              key={task.id}
              id={`task-${task.id}`}
              aria-current={task.id === marked ? "true" : undefined}
              className={`flex flex-wrap items-center gap-2 p-2.5 ${
                task.id === marked ? "bg-muted/60" : ""
              }`}
            >
              <input
                type="checkbox"
                name={`done-${task.id}`}
                data-control={`${task.id}:box`}
                checked={task.completed}
                disabled={working === task.id}
                aria-label={`${task.completed ? "Reopen" : "Complete"} ${task.title}`}
                className="size-4 shrink-0 accent-brand"
                onChange={(event) => {
                  refocus.current = [`${task.id}:box`];
                  void change(task, { completed: event.target.checked });
                }}
              />

              <span
                className={`min-w-0 flex-1 text-sm ${
                  task.completed ? "text-muted-foreground line-through" : ""
                }`}
              >
                {task.title}
              </span>

              {/* Only where there is something to read: a button on every row
                  would say nothing about which tasks have more to them. */}
              {noted && (
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-expanded={shown}
                  aria-label={`${shown ? "Hide" : "Show"} the notes on ${task.title}`}
                  onClick={() => flip(task)}
                >
                  <StickyNoteIcon aria-hidden />
                </Button>
              )}

              {task.due_on !== null && <Due day={task.due_on} completed={task.completed} />}

              <span className="flex items-center gap-0.5">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  data-control={`${task.id}:up`}
                  disabled={working === task.id || place === 0}
                  aria-label={`Move ${task.title} up`}
                  onClick={() => step(task, -1)}
                >
                  <ChevronUpIcon aria-hidden />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  data-control={`${task.id}:down`}
                  disabled={working === task.id || place === group.length - 1}
                  aria-label={`Move ${task.title} down`}
                  onClick={() => step(task, 1)}
                >
                  <ChevronDownIcon aria-hidden />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  disabled={working === task.id}
                  aria-label={`Edit ${task.title}`}
                  onClick={() => setEditing(task)}
                >
                  <PencilIcon aria-hidden />
                </Button>
                <Button
                  variant="ghost"
                  size="icon-sm"
                  disabled={working === task.id}
                  aria-label={`Delete ${task.title}`}
                  onClick={() => void discard(task)}
                >
                  <Trash2Icon aria-hidden />
                </Button>
              </span>

              {shown && (
                <Markdown className="text-muted-foreground basis-full pl-6">
                  {task.description}
                </Markdown>
              )}
            </li>
          );
        })}
      </ul>
    );
  }

  function whyItFailed(sentence: string, again: () => void) {
    if (why === "forbidden") {
      return <Denied what="Tasks" />;
    }

    if (why === "disabled") {
      return <Disabled what="Tasks" />;
    }

    return <Failed why={sentence} again={again} />;
  }
}

/**
 * A due date, as the day it is.
 *
 * <b>Nothing here makes a `Date`.</b> The instance answers `2026-09-14`, and
 * that string is a calendar day rather than a moment (`docs/api.md`, Tasks);
 * `new Date("2026-09-14")` is midnight UTC, which is the thirteenth for
 * everybody west of it. So the day is read out of the string and compared to
 * today's, which is taken from the browser's own calendar.
 */
function Due({ day, completed }: { day: string; completed: boolean }) {
  const today = todayHere();
  const overdue = !completed && day < today;
  const now = day === today;

  return (
    <span
      className={`shrink-0 text-xs ${
        overdue ? "text-destructive font-medium" : now ? "text-brand font-medium" : "text-muted-foreground"
      }`}
    >
      {overdue ? "overdue " : now ? "today " : ""}
      {day}
    </span>
  );
}

/** The name form shared by creating and renaming a list. */
function ListNameDialog({
  title,
  initialName = "",
  working,
  refused,
  onClose,
  onSave,
}: {
  title: "New list" | "Rename list";
  initialName?: string;
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onSave: (name: string) => void;
}) {
  const [name, setName] = useState(initialName);

  return (
    <Dialog
      open
      onOpenChange={(next) => {
        if (!next) {
          onClose();
        }
      }}
    >
      <DialogContent>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (name.trim() !== "" && !working) onSave(name.trim());
          }}
        >
          <DialogHeader>
            <DialogTitle>{title}</DialogTitle>
          </DialogHeader>

          <Field label="Name" hint="Names are one each, whatever their capitals.">
            <Input
              name="name"
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </Field>

          <Refused>{refused}</Refused>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={name.trim() === "" || working}>
              {title === "New list" ? (working ? "Making it…" : "Make it") : (working ? "Saving…" : "Save")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Everything about a task that is not its title or its box: the note, the day,
 * and which list it belongs in.
 */
function EditATask({
  task,
  lists,
  working,
  refused,
  onClose,
  onSave,
}: {
  task: Task;
  lists: List[];
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onSave: (to: Partial<Task>) => void;
}) {
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState(task.description);
  const [due, setDue] = useState(task.due_on ?? "");
  const [list, setList] = useState(task.list);

  return (
    <Dialog open onOpenChange={(next) => !next && onClose()}>
      <DialogContent className="sm:max-w-xl">
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            onSave({
              title: title.trim(),
              description,
              due_on: due === "" ? null : due,
              // Where it sits is not this form's to say: it stays where it is,
              // or arrives at the end of the list it is moved to.
              list,
            });
          }}
        >
          <DialogHeader>
            <DialogTitle>Edit</DialogTitle>
          </DialogHeader>

          <Field label="What has to be done">
            <Input
              name="title"
              value={title}
              autoFocus
              onChange={(event) => setTitle(event.target.value)}
            />
          </Field>

          <div className="flex flex-wrap gap-4">
            <Field label="Due">
              {/* A native date field: it takes and answers `2026-09-14`, which
                  is the same string the instance stores, so nothing here ever
                  makes a `Date` out of a day. */}
              <Input
                type="date"
                name="due"
                value={due}
                onChange={(event) => setDue(event.target.value)}
              />
            </Field>

            <Field label="List">
              <select
                name="list"
                className="h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2.5 py-1 text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
                value={list}
                onChange={(event) => setList(event.target.value)}
              >
                {lists.map((one) => (
                  <option key={one.id} value={one.id}>
                    {one.name}
                  </option>
                ))}
              </select>
            </Field>
          </div>

          <MarkdownField
            label="Notes"
            value={description}
            onChange={setDescription}
            size="compact"
            hint="Whatever else there is to say about it."
          />

          <Refused>{refused}</Refused>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={title.trim() === "" || working}>
              {working ? "Saving…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/** What a refusal says on this screen. */
function said(error: unknown, status: number): string {
  const { message, code } = refusal(error, status);

  return code === "stale"
    ? "That changed somewhere else while this list was open. It is being read again."
    : message;
}
