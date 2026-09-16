import { useEffect, useState } from "react";

import { api, type Schemas } from "@/api/client";
import { useAsk, type Asked } from "@/shared/ask";

export type Found = Schemas["FoundResponse"];
export type TheFindings = Schemas["FindingsResponse"];

/**
 * The shortest word this application will send. The instance drops single
 * letters and refuses a needle made only of them (`docs/api.md`, The search),
 * so a field that sent one would be making a request whose answer is always the
 * same refusal.
 */
export const shortest = 2;

/**
 * The words the instance will actually look for, worked out the way it works
 * them out: letters and digits, nothing shorter than {@link shortest}.
 *
 * It is here for two reasons — so that a field does not ask a question that can
 * only be refused, and so that a finding can be marked up with the words it was
 * found by rather than with whatever was in the box.
 */
export function usable(typed: string): string[] {
  return typed
    .split(/[^\p{L}\p{N}]+/u)
    .filter((word) => word.length >= shortest)
    .map((word) => word.toLowerCase());
}

/** Whether there is anything here worth asking the instance about. */
export function worthAsking(typed: string): boolean {
  return usable(typed).length > 0;
}

/**
 * One search over the four applications.
 *
 * <b>It asks once and does not ask again</b>, which makes this the one screen in
 * the application with the refresh switched off. A search is a question about a
 * moment; re-running it on a timer would move rows under somebody who is reading
 * them, and every row is a link to something that refreshes itself once it is
 * opened.
 *
 * <b>A query with nothing to look for is answered here</b>, without a request.
 * The empty answer is a real answer — it is what a field that has just been
 * opened shows — and sending it to the instance would be one refusal per
 * keystroke.
 */
export function useFindings(
  query: string,
  limit = 20,
): { asked: Asked<TheFindings>; again: () => void } {
  const asking = worthAsking(query);

  return useAsk(
    // The address is the answer's own label, so a search that is walked away
    // from cannot have its answer arrive under the next one (`shared/ask.ts`).
    asking ? `/api/search?q=${encodeURIComponent(query)}&limit=${limit}` : "/api/search?",
    (signal) =>
      asking
        ? api.GET("/api/search", { params: { query: { q: query, limit } }, signal })
        : Promise.resolve({
            data: { query, items: [], has_more: false },
            response: new Response(),
          }),
    { every: false },
  );
}

/**
 * What was typed, a moment after the typing stopped.
 *
 * A field that answers while somebody types is the point of the search
 * (`Needle`), and a request per keystroke is how that becomes a request per
 * keystroke. A short wait is the whole mechanism; there is nothing to cancel,
 * because a request that is overtaken is aborted by `useAsk` when the address
 * changes.
 */
export function useSettled(typed: string, after = 200): string {
  const [settled, setSettled] = useState(typed);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(typed), after);

    return () => clearTimeout(timer);
  }, [typed, after]);

  return settled;
}

/**
 * Where a finding lives in this application: the address that opens the screen
 * it is on.
 *
 * <b>The instance never says this.</b> It answers what the thing is and what it
 * sits in; which address that is belongs to the application that has the
 * addresses. A page is its own address, a task opens its list, a file opens its
 * folder, and an entry opens the Scratchpad.
 */
export function addressOf(found: Found): string {
  switch (found.application) {
    case "knowledge":
      return `/knowledge/${found.id}`;
    case "tasks":
      return found.within === null ? "/tasks" : `/tasks/${found.within}`;
    case "files":
      return found.within === null ? "/files" : `/files/${found.within}`;
    case "scratchpad":
      return "/scratchpad";
  }
}
