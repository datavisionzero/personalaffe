import {
  ChevronDownIcon,
  ChevronUpIcon,
  ListTodoIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router";

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
import { MarkdownField } from "@/shared/MarkdownField";
import { useAsk } from "@/shared/ask";
import { todayHere } from "@/tasks/today";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Lists = Schemas["TaskListsResponse"];
type List = Schemas["TaskListResponse"];
type Tasks = Schemas["TasksResponse"];
type Task = Schemas["TaskResponse"];

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
 * enough that a step at a time is the whole interaction.
 */
export function Tasks() {
  const params = useParams();

  const at = (params["*"] ?? "").replace(/\/+$/, "");
  const open = at === "" ? undefined : at;

  const [capturing, setCapturing] = useState("");
  const [wrote, setWrote] = useState<{ at: string; refused?: string }>();
  const [working, setWorking] = useState<string>();
  const [making, setMaking] = useState(false);
  const [editing, setEditing] = useState<Task>();

  const address = open === undefined ? "/tasks" : `/tasks/${open}`;
  const refused = wrote?.at === address ? wrote.refused : undefined;

  const [why, setWhy] = useState<string>();

  const lists = useAsk<Lists>("/api/tasks/lists", async (signal) => {
    const answer = await api.GET("/api/tasks/lists", { signal });

    setWhy(answer.error ? refusal(answer.error, answer.response.status).code : undefined);

    return answer;
  });

  const tasks = useAsk<Tasks>(
    address,
    (signal) =>
      api.GET("/api/tasks/lists/{id}/tasks", {
        params: { path: { id: open ?? "" } },
        signal,
      }),
    // Nothing arrives underneath a half-typed capture, and nothing at all is
    // asked when no list is open.
    { hold: capturing !== "", every: open === undefined ? false : undefined },
  );

  const all = lists.asked.at === "known" ? lists.asked.value.items : [];
  const here = open !== undefined && tasks.asked.at === "known" ? tasks.asked.value.items : [];
  const openTasks = here.filter((task) => !task.completed);
  const done = here.filter((task) => task.completed);

  async function write(what: string, sending: () => Promise<{ error?: unknown; response: Response }>) {
    setWorking(what);
    setWrote({ at: address });

    try {
      const answer = await sending();

      if (answer.error) {
        setWrote({ at: address, refused: said(answer.error, answer.response.status) });
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
        body: { title: capturing.trim(), description: "", due_on: null },
      }),
    );

    if (went) {
      // Cleared only once the instance has it: a box emptied optimistically is
      // a box that loses what somebody typed when the write is refused.
      setCapturing("");
      tasks.refresh();
      lists.refresh();
    }
  }

  async function change(task: Task, to: Partial<Task>) {
    const went = await write(task.id, () =>
      api.PUT("/api/tasks/{id}", {
        params: { path: { id: task.id }, ...guardedBy(versionOf(task.updated_at)) },
        // One write carries all of it, because all of it is one row.
        body: {
          list: to.list ?? task.list,
          title: to.title ?? task.title,
          description: to.description ?? task.description,
          due_on: to.due_on === undefined ? task.due_on : to.due_on,
          completed: to.completed ?? task.completed,
          after: to.after === undefined ? task.after : to.after,
        },
      }),
    );

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
      tasks.refresh();
      lists.refresh();
    }
  }

  async function makeList(name: string) {
    const went = await write("list", () => api.POST("/api/tasks/lists", { body: { name } }));

    if (went) {
      setMaking(false);
      lists.again();
    }
  }

  /** Where a task goes when it moves one step up or down among its neighbours. */
  function step(task: Task, by: -1 | 1) {
    const order = here;
    const from = order.findIndex((one) => one.id === task.id);
    const to = from + by;

    if (from < 0 || to < 0 || to >= order.length) {
      return;
    }

    // The neighbour it lands behind: the one before the place it is going,
    // skipping itself. `null` is the top of the list.
    const without = order.filter((one) => one.id !== task.id);
    const after = to === 0 ? null : (without[to - 1]?.id ?? null);

    void change(task, { after });
  }

  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-5 p-5 md:flex-row md:p-8">
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

        {lists.asked.at === "failed" && whyItFailed(lists.asked.why)}

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

      <section className="flex min-w-0 flex-1 flex-col gap-4">
        {open === undefined && lists.asked.at === "known" && (
          <Empty title="Pick a list, or make one.">
            Tasks are small personal commitments in named lists. A task has a title, and whatever else
            it needs: a note, a day it is due, and a place in the order you set.
          </Empty>
        )}

        {open !== undefined && (
          <>
            <form
              className="flex items-center gap-2"
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
                value={capturing}
                placeholder="What has to be done?"
                onChange={(event) => setCapturing(event.target.value)}
              />
              <Button type="submit" disabled={capturing.trim() === "" || working === "capture"}>
                Add
              </Button>
            </form>

            {tasks.asked.at === "asking" && <Busy title="Reading the list…" />}

            {tasks.asked.at === "failed" && whyItFailed(tasks.asked.why)}

            {tasks.asked.at === "known" && here.length === 0 && (
              <Empty title="Nothing on this list.">
                Type what has to be done in the box above. It goes at the end, and you can move it
                afterwards.
              </Empty>
            )}

            {openTasks.length > 0 && <Rows tasks={openTasks} />}

            {done.length > 0 && (
              <>
                <h2 className="text-muted-foreground mt-2 text-xs font-medium">Done</h2>
                <Rows tasks={done} />
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

      <MakeAList
        open={making}
        working={working === "list"}
        refused={refused}
        onClose={() => setMaking(false)}
        onMake={(name) => void makeList(name)}
      />

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

  function Rows({ tasks: rows }: { tasks: Task[] }) {
    return (
      <ul className="flex flex-col divide-y rounded-lg border">
        {rows.map((task) => (
          <li key={task.id} className="flex flex-wrap items-center gap-2 p-2.5">
            <input
              type="checkbox"
              name={`done-${task.id}`}
              checked={task.completed}
              disabled={working === task.id}
              aria-label={`${task.completed ? "Reopen" : "Complete"} ${task.title}`}
              className="size-4 shrink-0 accent-brand"
              onChange={(event) => void change(task, { completed: event.target.checked })}
            />

            <span
              className={`min-w-0 flex-1 text-sm ${
                task.completed ? "text-muted-foreground line-through" : ""
              }`}
            >
              {task.title}
            </span>

            {task.due_on !== null && <Due day={task.due_on} completed={task.completed} />}

            <span className="flex items-center gap-0.5">
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={working === task.id || here[0]?.id === task.id}
                aria-label={`Move ${task.title} up`}
                onClick={() => step(task, -1)}
              >
                <ChevronUpIcon aria-hidden />
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={working === task.id || here[here.length - 1]?.id === task.id}
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
          </li>
        ))}
      </ul>
    );
  }

  function whyItFailed(sentence: string) {
    if (why === "forbidden") {
      return <Denied what="Tasks" />;
    }

    if (why === "disabled") {
      return <Disabled what="Tasks" />;
    }

    return <Failed why={sentence} again={lists.again} />;
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

/** The dialog behind "New list". */
function MakeAList({
  open,
  working,
  refused,
  onClose,
  onMake,
}: {
  open: boolean;
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onMake: (name: string) => void;
}) {
  const [name, setName] = useState("");

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setName("");
          onClose();
        }
      }}
    >
      <DialogContent>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            onMake(name.trim());
          }}
        >
          <DialogHeader>
            <DialogTitle>New list</DialogTitle>
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
              {working ? "Making it…" : "Make it"}
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
              list,
              // Moving it to another list puts it at the end of that one, which
              // is where anything arriving in a list goes.
              after: list === task.list ? task.after : null,
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
