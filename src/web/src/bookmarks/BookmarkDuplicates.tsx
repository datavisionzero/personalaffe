import { useState } from "react";
import { Link } from "react-router";
import { api, describe, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Refused } from "@/shared/Form";
import { useAsk } from "@/shared/ask";
import { Busy, Failed } from "@/shell/States";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { folderPath, type Bookmark, type Folder } from "./useBookmarks";

type Selection = { url: string; keep?: Bookmark; remove: Bookmark[] };
export function BookmarkDuplicates({ folders, changed }: { folders: Folder[]; changed: () => void }) {
  const privacy = useBookmarkPrivacy();
  const [open, setOpen] = useState(false);
  const [offset, setOffset] = useState(0);
  const [url, setUrl] = useState("");
  const [memberOffset, setMemberOffset] = useState(0);
  const [selection, setSelection] = useState<Selection>();
  const [confirmed, setConfirmed] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string>();
  const [message, setMessage] = useState<string>();
  const groups = useAsk(`duplicates:${privacy.epoch}:${open}:${offset}:${url}:${memberOffset}`, (signal) => open
    ? api.GET("/api/bookmarks/duplicates", { headers: privacy.headers, params: { query: { offset, limit: 10, url: url || undefined, member_offset: memberOffset } }, signal })
    : Promise.resolve({ data: { groups: [], next_offset: null }, response: new Response() }), { hold: Boolean(selection || working) });
  async function cleanup() {
    if (!confirmed || !selection?.keep || !selection.remove.length) return;
    setWorking(true); setError(undefined);
    try {
      const response = await api.POST("/api/bookmarks/duplicates/cleanup", { headers: privacy.headers,
        params: { ...guardedBy(versionOf(selection.keep.updated_at)) }, body: { keep: selection.keep.id, remove: selection.remove.map((row) => ({ id: row.id, updated_at: row.updated_at })) } });
      if (response.data) { setMessage(`${response.data.removed.length} selected copies moved to Trash. The kept bookmark is unchanged.`); setSelection(undefined); setConfirmed(false); groups.again(); changed(); }
      else setError(describe(response.error, response.response.status) + " Nothing was cleaned up. Refresh and review the group again.");
    } catch { setError("The instance did not answer. Refresh and check Trash before retrying."); } finally { setWorking(false); }
  }
  function choose(group: string, row: Bookmark, keep: boolean, checked = true) {
    setConfirmed(false);
    setSelection((current) => {
      const previous = current?.url === group ? current : { url: group, remove: [] };
      return keep ? { ...previous, keep: row, remove: previous.remove.filter((item) => item.id !== row.id) }
        : { ...previous, remove: checked ? [...previous.remove.filter((item) => item.id !== row.id), row] : previous.remove.filter((item) => item.id !== row.id) };
    });
  }
  return <>
    <Button variant="outline" onClick={() => { setOpen(true); setSelection(undefined); setConfirmed(false); setError(undefined); setMessage(undefined); }}>Review duplicates</Button>
    <Dialog open={open} onOpenChange={(value) => { if (!working) { setOpen(value); if (!value) setSelection(undefined); } }}><DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-3xl">
      <DialogTitle>Review duplicate URLs</DialogTitle><DialogDescription>Only scheme, host and default port are normalized. Different folders may intentionally hold the same URL. Choose what to keep and which copies to move to Trash; metadata and opening statistics are not merged. Separate opening statistics are lost when deleted copies expire or are permanently removed.</DialogDescription>
      <Refused>{error}</Refused>{message && <p role="status">{message} <Link to="/trash" className="underline">Open Trash</Link></p>}
      <Button variant="outline" disabled={working} onClick={() => { setSelection(undefined); setConfirmed(false); groups.again(); }}>Refresh groups</Button>
      {selection && <div className="space-y-3 rounded-lg border p-3 text-sm">
        <p className="break-words">Keep: {selection.keep?.title ?? "choose a bookmark"}. {selection.remove.length} selected for Trash (maximum 100).</p>
        <label className="flex items-start gap-2"><input name="confirm-cleanup" type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /> I reviewed differences and want to remove only the selected copies.</label>
        <Button disabled={working || !confirmed || !selection.keep || !selection.remove.length} onClick={() => void cleanup()}>Move selected copies to Trash</Button>
      </div>}
      {groups.asked.at === "asking" && <Busy title="Finding duplicate URLs…" />}
      {groups.asked.at === "failed" && <Failed why={groups.asked.why} again={groups.again} />}
      {groups.asked.at === "known" && <>
        {!groups.asked.value.groups.length && <p className="text-muted-foreground text-sm">No duplicate URLs in this visible collection.</p>}
        {groups.asked.value.groups.map((group) => <section key={group.url} className="min-w-0 space-y-3 rounded-lg border p-3" aria-label={`Copies of ${group.url}`}>
          <h3 className="break-all text-sm font-medium">{group.url}</h3><p className="text-xs text-muted-foreground">{group.count} visible copies</p>
          <ul className="space-y-3">{group.items.map((row) => {
            const folder = folders.find((item) => item.id === row.folder);
            const current = selection?.url === group.url ? selection : undefined;
            return <li key={row.id} className="min-w-0 space-y-2 rounded-md border p-3">
              <Link className="block break-words font-medium underline" to={`/bookmarks/manage?selected=${row.id}`} onClick={() => setOpen(false)}>{row.title}</Link>
              <p className="break-all text-xs text-muted-foreground">{row.url}</p>
              <p className="break-words text-xs">{folder ? folderPath(folder, folders) : "Unsorted"} · {row.favorite ? "Favorite" : "Not a favorite"} · {row.read_later ? "Read later" : "Not on reading list"}{row.private ? " · Private" : ""}</p>
              {row.description && <p className="max-h-32 overflow-auto whitespace-pre-wrap break-words text-sm">{row.description}</p>}
              {row.tags.length > 0 && <p className="break-words text-xs">Tags: {row.tags.join(", ")}</p>}
              <div className="flex flex-wrap gap-4 text-sm">
                <label className="flex items-center gap-2"><input name={`keep-${group.url}`} type="radio" checked={current?.keep?.id === row.id} onChange={() => choose(group.url, row, true)} /> Keep {row.title}</label>
                <label className="flex items-center gap-2"><input name={`remove-${row.id}`} type="checkbox" checked={current?.remove.some((item) => item.id === row.id) ?? false} disabled={working || current?.keep?.id === row.id || ((current?.remove.length ?? 0) >= 100 && !current?.remove.some((item) => item.id === row.id))} onChange={(event) => choose(group.url, row, false, event.target.checked)} /> Remove {row.title}</label>
              </div>
            </li>;
          })}</ul>
          {group.next_member_offset !== null && <Button variant="outline" onClick={() => { setUrl(group.url); setOffset(0); setMemberOffset(group.next_member_offset!); }}>More copies of this URL</Button>}
          {memberOffset > 0 && <Button variant="outline" onClick={() => setMemberOffset(Math.max(0, memberOffset - 50))}>Previous copies</Button>}
        </section>)}
        <div className="flex flex-wrap gap-2"><Button variant="outline" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - 10))}>Previous groups</Button><Button variant="outline" disabled={groups.asked.value.next_offset === null} onClick={() => setOffset(offset + 10)}>Next groups</Button>
          {url && <Button variant="outline" onClick={() => { setUrl(""); setOffset(0); setMemberOffset(0); }}>All groups</Button>}</div>
      </>}
    </DialogContent></Dialog>
  </>;
}
