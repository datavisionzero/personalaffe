import { useState } from "react";
import { Link, useLocation, useSearchParams } from "react-router";
import { ArrowLeftIcon, ArrowRightIcon, LockKeyholeIcon, PlusIcon, StarIcon } from "lucide-react";
import { api, describe, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Refused, selectClass } from "@/shared/Form";
import { Busy, Failed } from "@/shell/States";
import { useSettled } from "@/search/useFindings";
import { BookmarkManagement } from "./BookmarkManagement";
import { BookmarkForm } from "./BookmarkForm";
import { BookmarkLink } from "./BookmarkLink";
import { PrivateSwitch } from "./privacy";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { domainOf, folderPath, useBookmarkDashboard, useBookmarkFolders, useBookmarkList, type Bookmark } from "./useBookmarks";

export function Bookmarks() {
  const privacy = useBookmarkPrivacy();
  const location = useLocation();
  return location.pathname.endsWith("/manage") ? <BookmarkManagement key={privacy.epoch} /> : <Dashboard key={privacy.epoch} />;
}

function Dashboard() {
  const privacy = useBookmarkPrivacy();
  const [params, setParams] = useSearchParams();
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string>();
  const [working, setWorking] = useState(false);
  const query = params.get("q") ?? "";
  const folder = params.get("folder") ?? "";
  const offset = Math.max(0, Number(params.get("offset")) || 0);
  const dashboard = useBookmarkDashboard(adding);
  const folders = useBookmarkFolders(adding);
  const list = useBookmarkList(useSettled(query), folder, false, "rank", offset, adding);
  const filtered = Boolean(query || folder);
  const allFolders = folders.asked.at === "known" ? folders.asked.value.items : [];
  function filter(key: string, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== "offset") next.delete("offset");
    void setParams(next, { replace: true });
  }
  function refresh() { dashboard.refresh(); list.refresh(); }
  async function favorite(row: Bookmark, value: boolean, after: string | null = null) {
    setError(undefined); setWorking(true);
    try {
      const answer = await api.PUT("/api/bookmarks/{id}/favorite", {
        params: { path: { id: row.id }, ...guardedBy(versionOf(row.updated_at)) },
        headers: privacy.headers, body: { favorite: value, after },
      });
      if (!answer.data) setError(describe(answer.error, answer.response.status));
      refresh();
    } catch { setError("The instance did not answer. Please try again."); }
    finally { setWorking(false); }
  }
  function section(title: string, rows: Bookmark[], empty: string, reorder = false) {
    return <section className="space-y-3" aria-label={title}>
      <h2 className="text-base font-semibold">{title}</h2>
      {rows.length === 0 ? <p className="text-muted-foreground rounded-xl border border-dashed p-5 text-sm">{empty}</p> :
        <ul className="grid min-w-0 gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {rows.map((row, index) => <li key={row.id} className="group min-w-0 rounded-xl border bg-card p-3"
            draggable={reorder && !working} onDragStart={(event) => event.dataTransfer.setData("text/personalaffe-bookmark", row.id)}
            onDragOver={(event) => { if (reorder) event.preventDefault(); }}
            onDrop={(event) => {
              if (!reorder || working) return;
              event.preventDefault();
              const moved = rows.find((candidate) => candidate.id === event.dataTransfer.getData("text/personalaffe-bookmark"));
              if (moved && moved.id !== row.id) void favorite(moved, true, row.id);
            }}>
            <div className="flex items-start gap-3">
              <span aria-hidden className="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-lg font-semibold">{domainOf(row.url).slice(0, 1).toUpperCase()}</span>
              <BookmarkLink id={row.id} url={row.url} className="min-w-0 flex-1 rounded-sm py-1 outline-none focus-visible:ring-2 focus-visible:ring-ring">
                <span className="block line-clamp-2 break-words font-medium" title={row.title}>{row.title}</span>
                <span className="text-muted-foreground block truncate text-xs">{domainOf(row.url)}</span>
              </BookmarkLink>
              <Button size="icon-sm" variant="ghost" disabled={working} aria-label={`${row.favorite ? "Unpin" : "Pin"} ${row.title}`}
                onClick={() => void favorite(row, !row.favorite)}><StarIcon className={row.favorite ? "fill-current" : ""} /></Button>
            </div>
            <div className="mt-2 flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
              {row.private && <LockKeyholeIcon aria-label="Private" className="size-3 shrink-0" />}
              <Link className="mr-auto rounded-sm underline-offset-4 hover:underline" to={`/bookmarks/manage?selected=${row.id}`}>Edit</Link>
              {reorder && <>
                <Button variant="ghost" size="icon-sm" disabled={working || index === 0} aria-label={`Move ${row.title} earlier`}
                  onClick={() => void favorite(row, true, index > 1 ? rows[index - 2].id : null)}><ArrowLeftIcon /></Button>
                <Button variant="ghost" size="icon-sm" disabled={working || index === rows.length - 1} aria-label={`Move ${row.title} later`}
                  onClick={() => void favorite(row, true, rows[index + 1].id)}><ArrowRightIcon /></Button>
              </>}
            </div>
          </li>)}
        </ul>}
    </section>;
  }
  return <main className="mx-auto flex w-full min-w-0 max-w-6xl flex-col gap-7 p-4 md:p-8">
    <header className="flex flex-wrap items-center justify-between gap-3">
      <div><h1 className="text-2xl font-semibold tracking-tight">Bookmarks</h1><p className="text-muted-foreground text-sm">Your links, close at hand.</p></div>
      <div className="flex flex-wrap gap-2"><PrivateSwitch /><Button variant="outline" render={<Link to={`/bookmarks/manage?${params}`} />}>Manage</Button>
        <Button onClick={() => setAdding(true)}><PlusIcon /> Add bookmark</Button></div>
    </header>
    <div className="grid gap-3 sm:grid-cols-[1fr_16rem]">
      <Input name="query" type="search" aria-label="Search bookmarks" placeholder="Search titles, URLs, descriptions and folders…" value={query} onChange={(event) => filter("q", event.target.value)} />
      <select name="folder" aria-label="Bookmark folder" className={selectClass} value={folder} onChange={(event) => filter("folder", event.target.value)}>
        <option value="">All folders</option><option value="unsorted">Unsorted</option>
        {allFolders.map((row) => <option key={row.id} value={row.id}>{row.effective_private ? "Private · " : ""}{folderPath(row, allFolders)}</option>)}
      </select>
    </div>
    <Refused>{error}</Refused>
    {(dashboard.unanswered || folders.unanswered || list.unanswered) && <p role="status" className="text-muted-foreground text-sm">The latest refresh failed. Displayed links may have changed.</p>}
    {folders.asked.at === "failed" && <Failed why={folders.asked.why} again={folders.again} />}
    {filtered ? <>
      {list.asked.at === "asking" && <Busy title="Finding bookmarks…" />}
      {list.asked.at === "failed" && <Failed why={list.asked.why} again={list.again} />}
      {list.asked.at === "known" && <>{section("Bookmarks", list.asked.value.items, "No matching bookmarks. Try another search or folder.")}
        <div className="flex gap-2"><Button variant="outline" disabled={offset === 0} onClick={() => filter("offset", String(Math.max(0, offset - 100)))}>Previous</Button>
          <Button variant="outline" disabled={list.asked.value.next_offset === null} onClick={() => filter("offset", String(offset + 100))}>Next</Button></div></>}
    </> : <>
      {dashboard.asked.at === "asking" && <Busy title="Opening bookmarks…" />}
      {dashboard.asked.at === "failed" && <Failed why={dashboard.asked.why} again={dashboard.again} />}
      {dashboard.asked.at === "known" && <>
        {section("Favorites", dashboard.asked.value.favorites, "Pin a bookmark to keep it here. Drag to reorder, or use the arrow buttons.", true)}
        {dashboard.asked.value.has_more_favorites && <Link className="text-sm underline" to="/bookmarks/manage?favorites=true">See all favorites</Link>}
        {section("Often used", dashboard.asked.value.frequent, "Links you open here appear here, based on the last 30 days. Favorites stay above.")}
        {section("Recently added", dashboard.asked.value.recent, "Add your first bookmark to start your collection.")}
      </>}
    </>}
    <Dialog open={adding} onOpenChange={setAdding}><DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
      <DialogTitle>Add bookmark</DialogTitle><DialogDescription>Save a link without fetching anything from its website.</DialogDescription>
      <BookmarkForm folders={allFolders} defaultFolder={folder !== "unsorted" ? folder : undefined} cancel={() => setAdding(false)} saved={() => { setAdding(false); refresh(); }} />
    </DialogContent></Dialog>
  </main>;
}
