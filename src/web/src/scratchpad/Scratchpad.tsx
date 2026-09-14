import { CopyIcon, PinIcon, PinOffIcon, Trash2Icon } from "lucide-react";
import { useId, useRef, useState } from "react";

import { api, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { refusal } from "@/session/useSession";
import { Refused } from "@/shared/Form";
import { useAsk } from "@/shared/ask";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Entry = Schemas["ScratchpadEntryResponse"];

/** How much of an entry a row shows before it offers the rest. */
const clamped = 400;

/**
 * The Scratchpad: a box at the top that takes text in seconds, the list under
 * it, and one click that puts an entry on the clipboard of whichever device is
 * looking at it (`docs/api.md`, The Scratchpad).
 *
 * <b>The capture box is a plain `<textarea>` and not `MarkdownField`.</b> The
 * Scratchpad is plain text (VISION §6.2) — no title, no tags, no Markdown — so a
 * browser that opens this screen downloads no editor. That is what the lazy
 * chunk of PERSONAL-E4 was for, and this is the screen that proves it.
 *
 * <b>Deleting asks first</b>, which nothing else in this workspace does. Every
 * other deletion sets content aside and can be taken back; this one destroys the
 * row, so the dialog says that in those words rather than asking "are you sure".
 */
export function Scratchpad() {
  const [text, setText] = useState("");
  const [refused, setRefused] = useState<string>();
  const [working, setWorking] = useState<string>();
  const [copied, setCopied] = useState<string>();
  const [selected, setSelected] = useState<string>();
  const [opened, setOpened] = useState<string[]>([]);
  const [removing, setRemoving] = useState<Entry>();

  const texts = useRef(new Map<string, HTMLParagraphElement | null>());

  // The box is one field on a screen that has a list of its own under it,
  // so it is named rather than left to the label alone.
  const box = useId();

  // Which refusal the last read made, where it made one. `useAsk` keeps the
  // sentence, which is what a failure is drawn from; two of the five states are
  // decided by the code instead, and this is where it is kept.
  const why = useRef<string>(undefined);

  // Nothing arrives underneath what somebody is typing: the capture box holding
  // text is unsaved work, and it holds the refresh until it is empty again.
  const { asked, again, refresh, unanswered } = useAsk(
    "/api/scratchpad/entries",
    async (signal) => {
      const answer = await api.GET("/api/scratchpad/entries", { signal });

      why.current = answer.error
        ? refusal(answer.error, answer.response.status).code
        : undefined;

      return answer;
    },
    { hold: text !== "" },
  );

  async function capture() {
    if (text.trim() === "") {
      return;
    }

    setWorking("capture");
    setRefused(undefined);

    try {
      const answer = await api.POST("/api/scratchpad/entries", {
        body: { text, pinned: false },
      });

      if (answer.error) {
        setRefused(refusal(answer.error, answer.response.status).message);
        return;
      }

      // Cleared only once the instance has it. A box emptied optimistically is
      // a box that loses what somebody typed when the write is refused.
      setText("");
      refresh();
    } finally {
      setWorking(undefined);
    }
  }

  async function pin(entry: Entry, pinned: boolean) {
    setWorking(entry.id);
    setRefused(undefined);

    try {
      // One write carries the text and the pin together, so the text goes back
      // with it — the same shape `pea` sends (`docs/api.md`, The Scratchpad).
      const answer = await api.PUT("/api/scratchpad/entries/{id}", {
        params: { path: { id: entry.id }, ...guardedBy(versionOf(entry.updated_at)) },
        body: { text: entry.text, pinned },
      });

      if (answer.error) {
        setRefused(said(answer.error, answer.response.status));
      }
    } finally {
      refresh();
      setWorking(undefined);
    }
  }

  async function destroy(entry: Entry) {
    setWorking(entry.id);
    setRefused(undefined);

    try {
      const answer = await api.DELETE("/api/scratchpad/entries/{id}", {
        params: { path: { id: entry.id }, ...guardedBy(versionOf(entry.updated_at)) },
      });

      if (answer.error) {
        setRefused(said(answer.error, answer.response.status));
      }
    } finally {
      setRemoving(undefined);
      refresh();
      setWorking(undefined);
    }
  }

  /**
   * Puts an entry on the clipboard, or — where the browser will not let a page
   * do that — selects its text and says why.
   *
   * The clipboard is unavailable outside a secure context, which is an instance
   * reached over plain HTTP at a LAN address (`docs/operations.md`). A button
   * that quietly did nothing there would be the worst of the three answers.
   */
  async function copy(entry: Entry) {
    setCopied(undefined);
    setSelected(undefined);

    try {
      await navigator.clipboard.writeText(entry.text);
      setCopied(entry.id);
    } catch {
      setSelected(entry.id);
      select(texts.current.get(entry.id));
    }
  }

  const showing = asked.at === "known" ? asked.value.items : [];

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">Scratchpad</h1>
        <p className="text-muted-foreground text-sm text-balance">
          Plain text, put down here and read on another device. An entry that is not pinned is
          destroyed by this instance a while after it was last changed; pinning is how one is kept.
          Deleting is immediate and cannot be taken back.
        </p>
      </header>

      <form
        className="flex flex-col gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          void capture();
        }}
      >
        <label className="flex flex-col gap-1.5 text-sm">
          <span className="font-medium">New entry</span>
          <textarea
            id={box}
            name="text"
            value={text}
            rows={3}
            placeholder="Anything you want on your other device in ten seconds."
            className="min-h-20 w-full resize-y rounded-lg border border-input bg-transparent px-2.5 py-2 font-mono text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
            onChange={(event) => setText(event.target.value)}
            onKeyDown={(event) => {
              // Enter is a newline here, because this box is for text with
              // newlines in it. The modifier is what says "that is all of it".
              if (event.key === "Enter" && (event.metaKey || event.ctrlKey)) {
                event.preventDefault();
                void capture();
              }
            }}
          />
        </label>

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={text.trim() === "" || working === "capture"}>
            {working === "capture" ? "Putting it down…" : "Put it down"}
          </Button>
          <span className="text-muted-foreground text-xs">
            ⌘/Ctrl + Enter. This list stops refreshing while the box has anything in it.
          </span>
        </div>
      </form>

      <Refused>{refused}</Refused>

      {unanswered && asked.at === "known" && (
        <p role="status" className="text-muted-foreground text-xs text-balance">
          The instance stopped answering. This is what it last said.
        </p>
      )}

      {asked.at === "asking" && <Busy title="Reading the Scratchpad…" />}

      {asked.at === "failed" && whyItFailed(asked.why)}

      {asked.at === "known" && showing.length === 0 && (
        <Empty title="Nothing is in your Scratchpad.">
          Put something in the box above and it is here, on every device you are signed in on. It
          stays until you delete it or it expires, and pinning it stops the clock.
        </Empty>
      )}

      {showing.length > 0 && (
        <ul className="flex flex-col gap-3">
          {showing.map((entry) => {
            const long = entry.text.length > clamped;
            const open = opened.includes(entry.id);

            return (
              <li key={entry.id} className="flex flex-col gap-2 rounded-lg border p-3">
                <p
                  ref={(node) => {
                    texts.current.set(entry.id, node);
                  }}
                  // Whitespace and newlines are shown as they were typed: this
                  // is the text somebody will paste somewhere else, and a
                  // collapsed indent is a changed text.
                  className={`font-mono text-sm break-words whitespace-pre-wrap ${
                    long && !open ? "line-clamp-6" : ""
                  }`}
                >
                  {entry.text}
                </p>

                {long && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    className="self-start px-0"
                    onClick={() =>
                      setOpened((all) =>
                        open ? all.filter((id) => id !== entry.id) : [...all, entry.id],
                      )
                    }
                  >
                    {open ? "Show less" : "Show all of it"}
                  </Button>
                )}

                <div className="flex flex-wrap items-center justify-between gap-3">
                  <span className="text-muted-foreground text-xs">
                    {new Date(entry.created_at).toLocaleString()}
                    {" · "}
                    {entry.expires_at === null
                      ? "pinned, so it never expires"
                      : `expires ${new Date(entry.expires_at).toLocaleString()}`}
                  </span>

                  <div className="flex items-center gap-1">
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      aria-label={`Copy ${excerpt(entry)}`}
                      onClick={() => void copy(entry)}
                    >
                      <CopyIcon aria-hidden />
                      {copied === entry.id ? "Copied" : "Copy"}
                    </Button>

                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      disabled={working === entry.id}
                      aria-label={`${entry.pinned ? "Unpin" : "Pin"} ${excerpt(entry)}`}
                      onClick={() => void pin(entry, !entry.pinned)}
                    >
                      {entry.pinned ? <PinOffIcon aria-hidden /> : <PinIcon aria-hidden />}
                      {entry.pinned ? "Unpin" : "Pin"}
                    </Button>

                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      disabled={working === entry.id}
                      aria-label={`Delete ${excerpt(entry)}`}
                      onClick={() => setRemoving(entry)}
                    >
                      <Trash2Icon aria-hidden />
                      Delete
                    </Button>
                  </div>
                </div>

                {copied === entry.id && (
                  <p role="status" className="text-muted-foreground text-xs">
                    Copied to this device's clipboard.
                  </p>
                )}

                {selected === entry.id && (
                  <p role="status" className="text-muted-foreground text-xs text-balance">
                    This browser only lets a page use the clipboard over HTTPS, and this instance is
                    not reached over it. The text is selected — copy it with ⌘C or Ctrl+C.
                  </p>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {asked.at === "known" && asked.value.has_more && (
        <p className="text-muted-foreground text-xs">
          There is more in the Scratchpad than this list shows.
        </p>
      )}

      {/* The only deletion in this product that cannot be taken back, so it is
          the only one that asks — and what it asks is not "are you sure" but
          what is about to be true. */}
      <Dialog open={removing !== undefined} onOpenChange={(open) => !open && setRemoving(undefined)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete this entry for good?</DialogTitle>
            <DialogDescription>
              A Scratchpad entry is destroyed when it is deleted. It does not go to the Trash, and
              there is no way to bring it back — not from this screen, not from the API, and not by
              whoever runs the server.
            </DialogDescription>
          </DialogHeader>

          {removing !== undefined && (
            <p className="bg-muted/50 max-h-40 overflow-y-auto rounded-lg p-2 font-mono text-xs break-words whitespace-pre-wrap">
              {removing.text}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setRemoving(undefined)}>
              Keep it
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={removing !== undefined && working === removing.id}
              onClick={() => removing !== undefined && void destroy(removing)}
            >
              Delete for good
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </main>
  );

  function whyItFailed(sentence: string) {
    // The switch can be thrown, and access revoked, while this screen is open.
    // Those are two of the five states rather than "something went wrong", and
    // the code is what tells them apart — the status cannot, because `disabled`
    // and `conflict` share one.
    if (why.current === "forbidden") {
      return <Denied what="The Scratchpad" />;
    }

    if (why.current === "disabled") {
      return <Disabled what="The Scratchpad" />;
    }

    return <Failed why={sentence} again={again} />;
  }
}

/**
 * What a refusal says on this screen. `stale` is the one a screen acts on
 * rather than prints: the list is read again, and the sentence says so instead
 * of showing the caller a version they never held.
 */
function said(error: unknown, status: number): string {
  const { message, code } = refusal(error, status);

  return code === "stale"
    ? "That entry changed while this list was open. It is being read again."
    : message;
}

/** How a row's acts name themselves to a screen reader. */
function excerpt(entry: Entry): string {
  const line = entry.text.split("\n")[0]?.trim() ?? "";

  return line.length > 40 ? `“${line.slice(0, 40)}…”` : `“${line}”`;
}

/**
 * Selects an element's text, for the browsers that will not let a page reach
 * the clipboard. It is wrapped because a selection is not something every
 * environment has — a preview, or a test's DOM — and failing to select is not
 * a reason to fail the screen.
 */
function select(node: HTMLElement | null | undefined) {
  if (!node) {
    return;
  }

  try {
    const selection = window.getSelection();
    const range = document.createRange();

    range.selectNodeContents(node);
    selection?.removeAllRanges();
    selection?.addRange(range);
  } catch {
    // A browser that will neither copy nor select has been told about in the
    // sentence beside the button; there is nothing further to do about it.
  }
}
