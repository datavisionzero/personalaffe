import { Link } from "react-router";
import { api } from "@/api/client";
import { useAsk } from "@/shared/ask";
import { useSettled } from "@/search/useFindings";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";

export function DuplicateHint({ url, except }: { url: string; except?: string }) {
  const privacy = useBookmarkPrivacy();
  const settled = useSettled(url);
  let valid = false;
  try { valid = ["http:", "https:"].includes(new URL(settled).protocol); } catch { /* Keep typing. */ }
  const found = useAsk(`duplicate-hint:${privacy.epoch}:${settled}`, (signal) => valid
    ? api.GET("/api/bookmarks/duplicates", { headers: privacy.headers, params: { query: { url: settled, limit: 1 } }, signal })
    : Promise.resolve({ data: { groups: [], next_offset: null }, response: new Response() }), { every: false });
  const rows = found.asked.at === "known" ? found.asked.value.groups.flatMap((group) => group.items).filter((row) => row.id !== except) : [];
  if (!rows.length) return null;
  return <div role="status" className="rounded-lg border p-3 text-sm">
    <p>This URL is already saved. Saving another copy is allowed.</p>
    <ul>{rows.slice(0, 5).map((row) => <li key={row.id}><Link className="break-words underline" to={`/bookmarks/manage?selected=${row.id}`} target="_blank" rel="noopener noreferrer">{row.title}</Link></li>)}</ul>
  </div>;
}
