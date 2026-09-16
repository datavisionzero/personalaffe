import { SearchIcon } from "lucide-react";
import { useId } from "react";
import { Link, useSearchParams } from "react-router";

import { Input } from "@/components/ui/input";
import { Busy, Empty, Failed } from "@/shell/States";
import { applicationNamed } from "@/shell/applications";
import { marked } from "./marked";
import { addressOf, shortest, useFindings, useSettled, usable, type Found } from "./useFindings";

/**
 * One search over the four applications, as a screen.
 *
 * <b>The query is in the address.</b> `/search?q=architecture` is a real
 * address, so a search can be linked to, kept open in a tab, and reloaded — and
 * the palette hands over to it by walking here rather than by copying its own
 * state somewhere.
 *
 * What it draws is what the instance found, in the order the instance found it.
 * Nothing here reorders or regroups: a page does not outrank a task for being a
 * page, and grouping by application would hide exactly that.
 */
export function Search() {
  const [address, setAddress] = useSearchParams();
  const field = useId();

  // <b>The address is the state.</b> There is no copy of the query in this
  // component, so arriving from a link, from the palette or from the browser's
  // own back button all fill the field the same way, and there is nothing to
  // keep in step.
  const query = address.get("q") ?? "";
  const settled = useSettled(query);
  const { asked, again } = useFindings(settled, 50);

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-5 p-5 md:p-8">
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">Search</h1>
        <p className="text-muted-foreground text-sm text-balance">
          Knowledge pages, tasks, Scratchpad text and file names. What is inside a file is never
          looked at.
        </p>
      </header>

      <div className="flex items-center gap-2">
        <SearchIcon aria-hidden className="text-muted-foreground size-4 shrink-0" />
        <Input
          autoFocus
          id={field}
          name="q"
          type="search"
          aria-label="Search the workspace"
          placeholder="Two letters is enough to start"
          value={query}
          onChange={(event) =>
            // Replace rather than push: typing a word is one search, not eight
            // entries in the browser's history to walk back out of.
            setAddress(event.target.value === "" ? {} : { q: event.target.value }, {
              replace: true,
            })
          }
        />
      </div>

      {asked.at === "asking" && <Busy title="Looking…" />}

      {asked.at === "failed" && <Failed why={asked.why} again={again} />}

      {asked.at === "known" && asked.value.items.length === 0 && (
        <Empty
          title={
            usable(settled).length === 0
              ? "Type something to look for."
              : `Nothing matches “${settled}”.`
          }
        >
          {usable(settled).length === 0
            ? `Every word is matched as a beginning, and a word is at least ${shortest} letters.`
            : "Every word has to be found, so fewer words find more."}
        </Empty>
      )}

      {asked.at === "known" && asked.value.items.length > 0 && (
        <>
          <ul aria-label="What was found" className="flex flex-col gap-1">
            {asked.value.items.map((found) => (
              <Row key={`${found.application}:${found.id}`} found={found} words={usable(settled)} />
            ))}
          </ul>

          {asked.value.has_more && (
            <p className="text-muted-foreground text-xs text-balance">
              There is more than this. Another word narrows it: every word has to be found.
            </p>
          )}
        </>
      )}
    </main>
  );
}

function Row({ found, words }: { found: Found; words: string[] }) {
  const application = applicationNamed(found.application);

  return (
    <li>
      <Link
        to={addressOf(found)}
        className="hover:bg-accent flex flex-col gap-0.5 rounded-md px-2 py-2"
      >
        <div className="flex items-center gap-2">
          {application !== undefined && (
            <application.icon aria-hidden className="text-muted-foreground size-3.5 shrink-0" />
          )}
          <span className="truncate text-sm font-medium">{marked(found.title, words)}</span>
          <span className="text-muted-foreground ml-auto shrink-0 text-[11px] uppercase">
            {application?.label ?? found.application}
          </span>
        </div>

        {found.snippet !== null && found.snippet !== "" && (
          <p className="text-muted-foreground line-clamp-2 text-sm">
            {marked(found.snippet, words)}
          </p>
        )}
      </Link>
    </li>
  );
}
