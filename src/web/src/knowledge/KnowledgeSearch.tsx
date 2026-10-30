import { useState } from "react";
import { Link } from "react-router";
import { api, type Schemas } from "@/api/client";
import { Input } from "@/components/ui/input";
import { useAsk } from "@/shared/ask";
import { useSettled, usable } from "@/search/useFindings";
import { marked } from "@/search/marked";
import { Failed } from "@/shell/States";

export function KnowledgeSearch({ page, query, setQuery }: { page?: string; query: string; setQuery: (query: string) => void }) {
  const [subtree, setSubtree] = useState(false);
  const settled = useSettled(query);
  const within = subtree ? page : undefined;
  const asking = usable(query).length > 0 && usable(settled).length > 0;
  const results = useAsk<Schemas["FindingsResponse"]>(`knowledge-search:${settled}:${within ?? ""}`, signal => asking
    ? api.GET("/api/search", { params: { query: { q: settled, application: "knowledge", limit: 100, within } }, signal })
    : Promise.resolve({ data: { query: settled, items: [], has_more: false }, response: new Response() }), { every: false });
  return <div className="space-y-2">
    <Input name="knowledge-search" type="search" aria-label="Search Knowledge" placeholder="Search titles and content" value={query} onChange={event => setQuery(event.target.value)} />
    {usable(query).length > 0 && <button type="button" className="text-sm text-brand hover:underline" onClick={() => setQuery("")}>Back to page tree</button>}
    {page && <label className="flex gap-2 text-xs"><input name="knowledge-subtree" type="checkbox" checked={subtree} onChange={event => setSubtree(event.target.checked)} />Only this page and its subpages</label>}
    {asking && results.asked.at === "asking" && <p role="status">Searching…</p>}
    {results.asked.at === "failed" && <Failed why={results.asked.why} again={results.again} />}
    {asking && results.asked.at === "known" && <>
      <ul aria-label="Knowledge search results" className="max-h-80 space-y-2 overflow-auto">
        {results.asked.value.items.map(item => <li key={item.id}>
          <Link className="text-sm text-brand" to={`/knowledge/${item.id}`}>{marked(item.title, usable(settled))}</Link>
          {item.snippet && <p className="text-xs text-muted-foreground">{marked(item.snippet, usable(settled))}</p>}
        </li>)}
      </ul>
      {results.asked.value.items.length === 0 && <p role="status" className="text-sm">No matching pages.</p>}
      {results.asked.value.has_more && <p className="text-xs">More than 100 matches. Add another word to narrow your search.</p>}
    </>}
  </div>;
}
