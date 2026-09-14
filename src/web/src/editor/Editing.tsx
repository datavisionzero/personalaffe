import { useState } from "react";

import { Button } from "@/components/ui/button";
import { MarkdownField } from "@/shared/MarkdownField";

const opening = `# The shared editor

This is the field every application in this workspace writes through: a
**Knowledge** page, a task's description, anything else that turns out to be
prose. It edits Markdown as _source_ — what you type is what is stored — and
shows you what it will look like beside it.

- The toolbar acts on the selection. So do the usual keys.
- \`Escape\` leaves the field; ⌘/Ctrl+Enter saves, where there is something to
  save to.
- A single newline is a line break, because you pressed Enter and meant it.

> Nothing typed here is stored anywhere. It is gone when you leave.
`;

/**
 * Somewhere to write, before there is anything to write in.
 *
 * <b>This route is a scaffold and says so.</b> The Markdown field is
 * PERSONAL-E4's to deliver and PERSONAL-E5 to PERSONAL-E8's to use, which
 * leaves the field finished for a while with no screen on it: untried by
 * anybody, unreachable by a browser check, and impossible to tell apart from
 * one that does not work. So it has one screen, and the screen is honest about
 * being one — it writes nowhere, it reads nothing, and it goes away when
 * Knowledge arrives and the editor has real work.
 */
export function Editing() {
  const [text, setText] = useState(opening);

  return (
    <main className="mx-auto flex w-full max-w-4xl flex-col gap-5 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">The editor</h1>
        <p className="text-muted-foreground max-w-prose text-sm text-balance">
          The Markdown field the applications will write through, with nothing behind it yet.
          Nothing you type here is sent anywhere or kept: this workspace has no content in it, and
          this is how writing in it will feel when it does.
        </p>
      </header>

      <MarkdownField
        label="Something to write"
        value={text}
        onChange={setText}
        hint="Markdown. The toolbar acts on the selection."
      />

      <div>
        <Button type="button" variant="outline" onClick={() => setText("")}>
          Clear it
        </Button>
      </div>
    </main>
  );
}
