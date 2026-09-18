import {
  ChevronRightIcon,
  DownloadIcon,
  FilePlusIcon,
  HistoryIcon,
  NotebookTextIcon,
  Trash2Icon,
} from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";

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
import { Markdown } from "@/shared/Markdown";
import { useAsk } from "@/shared/ask";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Tree = Schemas["TreeResponse"];
type Outline = Schemas["OutlineResponse"];
type Page = Schemas["PageResponse"];
type Revision = Schemas["RevisionResponse"];

/** What somebody has typed and not yet saved. */
type Draft = { title: string; markdown: string };

/**
 * Knowledge: the tree beside the page, in the editor PERSONAL-E4 built for
 * exactly this (`docs/api.md`, Knowledge).
 *
 * <b>The address is the page.</b> `/knowledge/<id>` is a real address, so a
 * pasted link opens the page it names — and so does a `page:` link inside
 * somebody's Markdown, which is followed rather than opened
 * (`shared/links.ts`).
 *
 * <b>Nothing arrives underneath what somebody is writing.</b> A page being
 * edited holds its own refresh: what is in the field is the newest version of
 * it, and an answer landing on top is what would take it away. That is the
 * acceptance criterion `shared/ask.ts` grew `hold` for, and this is the screen
 * that needed it.
 */
export function Knowledge() {
  const params = useParams();
  const navigate = useNavigate();

  const at = (params["*"] ?? "").replace(/\/+$/, "");
  const open = at === "" ? undefined : at;

  const [draft, setDraft] = useState<Draft>();
  const [wrote, setWrote] = useState<{ at: string; refused?: string; said?: string }>();
  const [working, setWorking] = useState(false);
  const [writing, setWriting] = useState(false);
  const [history, setHistory] = useState(false);

  const address = open === undefined ? "/knowledge" : `/knowledge/${open}`;
  const refused = wrote?.at === address ? wrote.refused : undefined;
  const said = wrote?.at === address ? wrote.said : undefined;

  const tree = useAsk<Tree>("/api/knowledge/pages", (signal) =>
    api.GET("/api/knowledge/pages", { signal }),
  );

  // Which refusal the page read made. Two of the five states are decided by the
  // code rather than the status, and this is where it is kept.
  const [why, setWhy] = useState<string>();

  const page = useAsk<Page>(
    address,
    async (signal) => {
      const answer = await api.GET("/api/knowledge/pages/{id}", {
        params: { path: { id: open ?? "" } },
        signal,
      });

      setWhy(answer.error ? refusal(answer.error, answer.response.status).code : undefined);

      return answer;
    },
    // Nothing is asked while there is unsaved work, and nothing at all is asked
    // when no page is open.
    { hold: draft !== undefined, every: open === undefined ? false : undefined },
  );

  const it = open !== undefined && page.asked.at === "known" ? page.asked.value : undefined;
  const title = draft?.title ?? it?.title ?? "";
  const markdown = draft?.markdown ?? it?.markdown ?? "";

  // What this screen last read, kept across the moment it is reading it again.
  // `it` is undefined then, and a change made in that moment still has to know
  // what it is a change to (see `change`).
  const read = useRef<Page>(undefined);

  useEffect(() => {
    if (it !== undefined) {
      read.current = it;
    }
  }, [it]);

  function change(next: Partial<Draft>) {
    // <b>The half nobody touched comes from the page, and the page is the last
    // one this screen actually read.</b> A draft holds both halves because one
    // write carries both — and while the page is being read again there is no
    // `it` to take the other half from. The empty string that stood in for it
    // was a rename saving an empty body over what had just been written, and
    // dropping the change instead would be a rename nobody made: both are what
    // a keystroke landing on the node being replaced used to cost
    // (PERSONAL-66).
    const base = it ?? read.current;

    if (base === undefined) {
      return;
    }

    setDraft((before) => ({
      title: next.title ?? before?.title ?? base.title,
      markdown: next.markdown ?? before?.markdown ?? base.markdown,
    }));
  }

  async function save() {
    if (it === undefined || draft === undefined) {
      return;
    }

    setWorking(true);
    setWrote({ at: address });

    try {
      const answer = await api.PUT("/api/knowledge/pages/{id}", {
        params: { path: { id: it.id }, ...guardedBy(versionOf(it.updated_at)) },
        // One write carries the title, the place and the body, because all
        // three are the same row. The place is not this screen's to change.
        body: { title: draft.title, parent: it.parent, markdown: draft.markdown },
      });

      if (answer.error) {
        setWrote({ at: address, refused: said_(answer.error, answer.response.status) });
        return;
      }

      // Cleared only once the instance has it. A draft dropped optimistically
      // is a draft that loses what somebody wrote when the write is refused.
      setDraft(undefined);
      setWrote({ at: address, said: "Saved." });
      page.again();
      tree.refresh();
    } finally {
      setWorking(false);
    }
  }

  async function write(named: string) {
    setWorking(true);
    setWrote({ at: address });

    try {
      const answer = await api.POST("/api/knowledge/pages", {
        body: { title: named, parent: open ?? null, markdown: "" },
      });

      if (answer.error) {
        setWrote({ at: address, refused: said_(answer.error, answer.response.status) });
        return;
      }

      setWriting(false);
      tree.refresh();
      void navigate(`/knowledge/${answer.data.id}`);
    } finally {
      setWorking(false);
    }
  }

  async function discard() {
    if (it === undefined) {
      return;
    }

    setWorking(true);
    setWrote({ at: address });

    try {
      const answer = await api.DELETE("/api/knowledge/pages/{id}", {
        params: { path: { id: it.id }, ...guardedBy(versionOf(it.updated_at)) },
      });

      if (answer.error) {
        setWrote({ at: address, refused: said_(answer.error, answer.response.status) });
        return;
      }

      setDraft(undefined);
      tree.refresh();
      void navigate("/knowledge");
    } finally {
      setWorking(false);
    }
  }

  async function recover(revision: Revision) {
    if (it === undefined) {
      return;
    }

    setWorking(true);
    setWrote({ at: address });

    try {
      const answer = await api.POST("/api/knowledge/pages/{id}/revisions/{revision}", {
        params: {
          path: { id: it.id, revision: revision.id },
          ...guardedBy(versionOf(it.updated_at)),
        },
      });

      if (answer.error) {
        setWrote({ at: address, refused: said_(answer.error, answer.response.status) });
        return;
      }

      setHistory(false);
      setDraft(undefined);
      setWrote({
        at: address,
        said: "Put back. What the page said until now is a version of its own.",
      });
      page.again();
      tree.refresh();
    } finally {
      setWorking(false);
    }
  }

  const pages = tree.asked.at === "known" ? tree.asked.value.pages : [];

  return (
    <main className="mx-auto flex w-full max-w-6xl flex-col gap-5 p-5 md:flex-row md:p-8">
      <nav
        aria-label="The pages"
        className="flex shrink-0 flex-col gap-2 md:w-64 md:border-r md:pr-4"
      >
        <div className="flex items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Knowledge</h1>
          <Button
            variant="outline"
            size="sm"
            aria-label={open === undefined ? "New page" : "New page under this one"}
            onClick={() => setWriting(true)}
          >
            <FilePlusIcon aria-hidden />
          </Button>
        </div>

        {tree.asked.at === "asking" && <Busy title="Reading the pages…" />}

        {tree.asked.at === "known" && pages.length === 0 && (
          <p className="text-muted-foreground text-sm text-balance">
            Nothing is written yet. The button above starts a page.
          </p>
        )}

        <Branch pages={pages} under={null} open={open} depth={0} />

        <Button
          variant="ghost"
          size="sm"
          className="mt-2 justify-start"
          // A plain link to the instance's own address: the browser fetches it
          // with the session it already has, and nothing on this page holds a
          // zip of everything the owner has written.
          // An anchor wearing the button's clothes, as every other link in
          // this workspace that looks like one does. It stays a link to a
          // screen reader, which is what it is.
          render={<a href="/api/knowledge/export" download />}
        >
          <DownloadIcon aria-hidden />
          Export all of it
        </Button>
      </nav>

      <section className="flex min-w-0 flex-1 flex-col gap-4">
        {open === undefined && (
          <Empty title="Pick a page, or write one.">
            Knowledge is Markdown in a tree. A page keeps its address when you rename it or move it,
            so a link to it goes on working — and what it used to say is kept behind it.
          </Empty>
        )}

        {open !== undefined && page.asked.at === "asking" && <Busy title="Opening the page…" />}

        {open !== undefined && page.asked.at === "failed" && whyItFailed(page.asked.why)}

        {it !== undefined && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <Field label="Title">
                <Input
                  name="title"
                  value={title}
                  className="md:w-80"
                  onChange={(event) => change({ title: event.target.value })}
                />
              </Field>

              <div className="flex items-center gap-1 self-end">
                <Button variant="outline" size="sm" onClick={() => setHistory(true)}>
                  <HistoryIcon aria-hidden />
                  History
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={working}
                  aria-label={`Delete ${it.title}`}
                  onClick={() => void discard()}
                >
                  <Trash2Icon aria-hidden />
                  Delete
                </Button>
              </div>
            </div>

            <MarkdownField
              label="The page"
              value={markdown}
              onChange={(next) => change({ markdown: next })}
              onSubmit={() => void save()}
              hint="⌘/Ctrl + Enter saves. Nothing is read from the instance while there is unsaved work."
            />

            <div className="flex flex-wrap items-center gap-3">
              <Button type="button" disabled={draft === undefined || working} onClick={() => void save()}>
                {working ? "Saving…" : "Save"}
              </Button>
              {draft !== undefined && (
                <Button
                  type="button"
                  variant="ghost"
                  onClick={() => {
                    setDraft(undefined);
                    page.again();
                  }}
                >
                  Throw it away
                </Button>
              )}
              <span className="text-muted-foreground text-xs">
                {draft === undefined
                  ? `Last changed ${new Date(it.updated_at).toLocaleString()}`
                  : "Unsaved."}
              </span>
            </div>
          </>
        )}

        <Refused>{refused}</Refused>

        {said !== undefined && (
          <p role="status" className="text-muted-foreground text-sm text-balance">
            {said}
          </p>
        )}

        {page.unanswered && page.asked.at === "known" && (
          <p role="status" className="text-muted-foreground text-xs text-balance">
            The instance stopped answering. This is what it last said.
          </p>
        )}
      </section>

      <WriteAPage
        open={writing}
        under={pages.find((one) => one.id === open)}
        working={working}
        refused={refused}
        onClose={() => setWriting(false)}
        onWrite={(named) => void write(named)}
      />

      {it !== undefined && (
        <TheHistory
          open={history}
          page={it}
          working={working}
          refused={refused}
          onClose={() => setHistory(false)}
          onRecover={(revision) => void recover(revision)}
        />
      )}
    </main>
  );

  function whyItFailed(sentence: string) {
    if (why === "forbidden") {
      return <Denied what="Knowledge" />;
    }

    if (why === "disabled") {
      return <Disabled what="Knowledge" />;
    }

    return <Failed why={sentence} again={page.again} />;
  }
}

/**
 * One level of the tree and everything under it.
 *
 * The instance answers the pages flat, each saying which one it is under, so
 * this is where the hierarchy is drawn — in one pass, without the instance
 * having to have a shape for it.
 */
function Branch({
  pages,
  under,
  open,
  depth,
}: {
  pages: Outline[];
  under: string | null;
  open: string | undefined;
  depth: number;
}) {
  const here = pages.filter((page) => (page.parent ?? null) === under);

  if (here.length === 0) {
    return null;
  }

  return (
    <ul className="flex flex-col gap-0.5">
      {here.map((page) => (
        <li key={page.id}>
          <Link
            to={`/knowledge/${page.id}`}
            aria-current={page.id === open ? "page" : undefined}
            className={`flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm hover:bg-muted ${
              page.id === open ? "bg-muted font-medium" : ""
            }`}
            style={{ paddingInlineStart: `${0.5 + depth * 0.75}rem` }}
          >
            {depth === 0 ? (
              <NotebookTextIcon aria-hidden className="text-muted-foreground size-3.5 shrink-0" />
            ) : (
              <ChevronRightIcon aria-hidden className="text-muted-foreground size-3.5 shrink-0" />
            )}
            <span className="truncate">{page.title}</span>
          </Link>
          <Branch pages={pages} under={page.id} open={open} depth={depth + 1} />
        </li>
      ))}
    </ul>
  );
}

/** The dialog behind "New page". */
function WriteAPage({
  open,
  under,
  working,
  refused,
  onClose,
  onWrite,
}: {
  open: boolean;
  under: Outline | undefined;
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onWrite: (title: string) => void;
}) {
  const [title, setTitle] = useState("");

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setTitle("");
          onClose();
        }
      }}
    >
      <DialogContent>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            onWrite(title.trim());
          }}
        >
          <DialogHeader>
            <DialogTitle>
              {under === undefined ? "New page" : `New page under ${under.title}`}
            </DialogTitle>
          </DialogHeader>

          <Field label="Title" hint="Titles in one place are one each, whatever their capitals.">
            <Input
              name="title"
              value={title}
              autoFocus
              onChange={(event) => setTitle(event.target.value)}
            />
          </Field>

          <Refused>{refused}</Refused>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={title.trim() === "" || working}>
              {working ? "Writing it…" : "Write it"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * What a page used to say, and the way back to one of them.
 *
 * <b>It says what putting one back does</b>, because that is the fact that
 * makes it safe: what the page says now becomes a version of its own, so
 * nothing is lost by undoing something (`docs/api.md`, Keeping the previous
 * version).
 */
function TheHistory({
  open,
  page,
  working,
  refused,
  onClose,
  onRecover,
}: {
  open: boolean;
  page: Page;
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onRecover: (revision: Revision) => void;
}) {
  const [showing, setShowing] = useState<Revision>();
  const [markdown, setMarkdown] = useState<string>();

  const history = useAsk<Schemas["HistoryResponse"]>(
    `/api/knowledge/pages/${page.id}/revisions`,
    (signal) =>
      api.GET("/api/knowledge/pages/{id}/revisions", {
        params: { path: { id: page.id } },
        signal,
      }),
    // Only while it is being looked at, and asked again each time it is opened.
    { every: false },
  );

  async function look(revision: Revision) {
    setShowing(revision);
    setMarkdown(undefined);

    const answer = await api.GET("/api/knowledge/pages/{id}/revisions/{revision}", {
      params: { path: { id: page.id, revision: revision.id } },
    });

    if (!answer.error) {
      setMarkdown(answer.data.markdown);
    }
  }

  const items = history.asked.at === "known" ? history.asked.value.items : [];

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setShowing(undefined);
          setMarkdown(undefined);
          onClose();
        }
      }}
    >
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>What this page used to say</DialogTitle>
        </DialogHeader>

        {history.asked.at === "asking" && <Busy title="Reading the history…" />}

        {history.asked.at === "known" && items.length === 0 && (
          <p className="text-muted-foreground text-sm text-balance">
            This page has never been changed, so there is nothing behind it yet.
          </p>
        )}

        {items.length > 0 && (
          <ul className="flex max-h-48 flex-col gap-1 overflow-y-auto">
            {items.map((revision) => (
              <li key={revision.id}>
                <Button
                  variant={showing?.id === revision.id ? "outline" : "ghost"}
                  size="sm"
                  className="w-full justify-start gap-2"
                  onClick={() => void look(revision)}
                >
                  <span className="truncate">{revision.title}</span>
                  <span className="text-muted-foreground ml-auto shrink-0 text-xs">
                    {new Date(revision.at).toLocaleString()} ·{" "}
                    {revision.by.name ?? "you"}
                  </span>
                </Button>
              </li>
            ))}
          </ul>
        )}

        {showing !== undefined && (
          <div className="bg-muted/50 max-h-56 overflow-y-auto rounded-lg p-3 text-sm">
            {markdown === undefined ? (
              <Busy title="Reading it…" />
            ) : (
              <Markdown>{markdown}</Markdown>
            )}
          </div>
        )}

        <Refused>{refused}</Refused>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={onClose}>
            Close
          </Button>
          <Button
            type="button"
            disabled={showing === undefined || working}
            onClick={() => showing !== undefined && onRecover(showing)}
          >
            Put this one back
          </Button>
        </DialogFooter>

        <p className="text-muted-foreground text-xs text-balance">
          Putting a version back keeps what the page says now: it becomes a version of its own.
          Nothing here ever throws work away.
        </p>
      </DialogContent>
    </Dialog>
  );
}

/**
 * What a refusal says on this screen. `stale` is the one a screen acts on
 * rather than prints.
 */
function said_(error: unknown, status: number): string {
  const { message, code } = refusal(error, status);

  return code === "stale"
    ? "This page changed somewhere else while you were writing. Your text is still here — open the page again in another tab to see what it says now."
    : message;
}
