import { useState } from "react";
import { Link, useSearchParams } from "react-router";
import { api, describe, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useAsk } from "@/shared/ask";
import { Busy, Failed } from "@/shell/States";
import { useSettled } from "@/search/useFindings";
import { BookmarkForm } from "./BookmarkForm";
import { FolderForm } from "./FolderForm";
import { BookmarkLink } from "./BookmarkLink";
import { PrivateSwitch } from "./privacy";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { domainOf, folderPath, useBookmarkFolders, useBookmarkList, type Bookmark, type Folder } from "./useBookmarks";

type Action = "move" | "delete";
type Editor = { kind: "bookmark"; row?: Bookmark } | { kind: "folder"; row?: Folder };

export function BookmarkManagement() {
  const privacy = useBookmarkPrivacy();
  const [params, setParams] = useSearchParams();
  const [editor, setEditor] = useState<Editor>();
  const [selected, setSelected] = useState<Bookmark[]>([]);
  const [action, setAction] = useState<Action>();
  const [target, setTarget] = useState("");
  const [confirmPublic, setConfirmPublic] = useState(false);
  const [working, setWorking] = useState(false);
  const [message, setMessage] = useState<string>();
  const [error, setError] = useState<string>();
  const [undo, setUndo] = useState<string[]>([]);
  const [deleteFolder, setDeleteFolder] = useState<Folder>();
  const folder = params.get("folder") ?? "";
  const query = params.get("q") ?? "";
  const sort = params.get("sort") ?? "updated";
  const favorites = params.get("favorites") === "true";
  const offset = Math.max(0, Number(params.get("offset")) || 0);
  const selectedId = params.get("selected");
  const hold = Boolean(editor || action || deleteFolder || selectedId || selected.length || working);
  const folders = useBookmarkFolders(hold);
  const list = useBookmarkList(useSettled(query), folder, favorites, sort, offset, hold);
  const detail = useAsk<Bookmark | null>(`bookmark-detail:${privacy.epoch}:${selectedId}`, (signal) => selectedId
    ? api.GET("/api/bookmarks/{id}", { params: { path: { id: selectedId } }, headers: privacy.headers, signal })
    : Promise.resolve({ data: null, response: new Response() }), { every: false });
  const allFolders = folders.asked.at === "known" ? folders.asked.value.items : [];
  const rows = list.asked.at === "known" ? list.asked.value.items : [];
  const currentFolder = allFolders.find((row) => row.id === folder);
  const exposing = action === "move" && selected.some((row) => row.private) && !allFolders.find((row) => row.id === target)?.effective_private;
  function filter(key: string, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    if (key !== "offset") next.delete("offset");
    setSelected([]); void setParams(next, { replace: true });
  }
  function refresh() { list.refresh(); folders.refresh(); }
  function closeEditor() { setEditor(undefined); if (selectedId) filter("selected", ""); }
  const bookmarkEditor = editor?.kind === "bookmark" ? editor : selectedId && detail.asked.at === "known" && detail.asked.value ? { kind: "bookmark" as const, row: detail.asked.value } : undefined;
  async function favorite(row: Bookmark) {
    setWorking(true); setError(undefined);
    try {
      const result = await api.PUT("/api/bookmarks/{id}/favorite", { params: { path: { id: row.id }, ...guardedBy(versionOf(row.updated_at)) }, headers: privacy.headers, body: { favorite: !row.favorite, after: null } });
      if (!result.data) setError(describe(result.error, result.response.status));
      refresh();
    } catch { setError("The instance did not answer."); } finally { setWorking(false); }
  }
  async function apply() {
    if (!action || (exposing && !confirmPublic)) return;
    setWorking(true); setError(undefined); setMessage(undefined);
    const succeeded: string[] = []; const failed: Bookmark[] = []; const reasons: string[] = [];
    for (const row of selected) {
      try {
        const options = { headers: privacy.headers, params: { path: { id: row.id }, ...guardedBy(versionOf(row.updated_at)) } };
        const result = action === "delete" ? await api.DELETE("/api/bookmarks/{id}", options)
          : await api.PUT("/api/bookmarks/{id}", { ...options, body: { title: row.title, url: row.url, description: row.description, folder: target || null } });
        if (result.response.ok) succeeded.push(row.id);
        else { failed.push(row); reasons.push(`${row.title}: ${describe(result.error, result.response.status)}`); }
      } catch { failed.push(row); reasons.push(`${row.title}: The instance did not answer.`); }
    }
    if (action === "delete") setUndo(succeeded);
    setMessage(`${succeeded.length} ${action === "delete" ? "moved to Trash" : "moved"}; ${failed.length} failed.`);
    setError(reasons.length ? reasons.join(" ") + " Refresh the selection before retrying a changed item." : undefined);
    setSelected(failed); setAction(undefined); setWorking(false); refresh();
  }
  async function removeFolder() {
    if (!deleteFolder) return;
    setWorking(true); setError(undefined);
    try {
      const result = await api.DELETE("/api/bookmarks/folders/{id}", { headers: privacy.headers, params: { path: { id: deleteFolder.id }, ...guardedBy(versionOf(deleteFolder.updated_at)) } });
      if (result.response.ok) { setUndo([deleteFolder.id]); setMessage("Folder and its contents moved to Trash."); setDeleteFolder(undefined); filter("folder", ""); refresh(); }
      else setError(describe(result.error, result.response.status));
    } catch { setError("The instance did not answer."); } finally { setWorking(false); }
  }
  async function restore() {
    setWorking(true); setError(undefined);
    try {
      const trash = await api.GET("/api/trash", { headers: privacy.headers });
      if (!trash.data) { setError(describe(trash.error, trash.response.status)); return; }
      const failed: string[] = []; const reasons: string[] = [];
      for (const id of undo) {
        const row = trash.data.items.find((entry: Schemas["TrashEntryResponse"]) => entry.application === "bookmarks" && entry.id === id);
        if (!row) { failed.push(id); reasons.push("An entry is no longer in this Trash page; open Trash to check it."); continue; }
        const result = await api.POST("/api/trash/{application}/{id}/restore", { headers: privacy.headers, params: { path: { application: "bookmarks", id }, ...guardedBy(versionOf(row.updated_at)) } });
        if (!result.response.ok) { failed.push(id); reasons.push(describe(result.error, result.response.status)); }
      }
      setMessage(`${undo.length - failed.length} restored; ${failed.length} could not be restored.`); setUndo(failed); setError(reasons.join(" ")); refresh();
    } catch { setError("The instance did not answer. Check Trash before retrying."); } finally { setWorking(false); }
  }
  return <main className="mx-auto flex w-full min-w-0 max-w-7xl flex-col gap-5 p-4 md:p-8">
    <header className="flex flex-wrap items-center justify-between gap-3"><h1 className="text-xl font-semibold">Manage bookmarks</h1>
      <div className="flex flex-wrap gap-2"><PrivateSwitch /><Button variant="outline" render={<Link to={`/bookmarks?${params}`} />}>Dashboard</Button>
        <Button variant="outline" onClick={() => setEditor({ kind: "folder" })}>New folder</Button><Button onClick={() => setEditor({ kind: "bookmark" })}>Add bookmark</Button></div></header>
    <div className="grid min-w-0 gap-3 sm:grid-cols-2 lg:grid-cols-4">
      <Input name="query" type="search" aria-label="Search bookmarks" placeholder="Search bookmarks…" value={query} onChange={(event) => filter("q", event.target.value)} />
      <select name="folder" aria-label="Bookmark folder" className={selectClass} value={folder} onChange={(event) => filter("folder", event.target.value)}>
        <option value="">All folders</option><option value="unsorted">Unsorted</option>{allFolders.map((row) => <option key={row.id} value={row.id}>{row.effective_private ? "Private · " : ""}{folderPath(row, allFolders)}</option>)}
      </select>
      <select name="sort" aria-label="Sort bookmarks" className={selectClass} value={sort} onChange={(event) => filter("sort", event.target.value)}><option value="updated">Recently changed</option><option value="title">Title</option><option value="created">Recently added</option><option value="rank">Relevance</option></select>
      <label className="flex items-center gap-2 text-sm"><input name="favorites" type="checkbox" checked={favorites} onChange={(event) => filter("favorites", event.target.checked ? "true" : "")} /> Favorites only</label>
    </div>
    {currentFolder && <div className="flex flex-wrap items-center gap-2 text-sm"><span className="min-w-0 break-words">{folderPath(currentFolder, allFolders)}{currentFolder.effective_private ? currentFolder.private ? " · Private" : " · Private (inherited)" : ""}</span>
      <Button size="sm" variant="outline" onClick={() => setEditor({ kind: "folder", row: currentFolder })}>Edit folder</Button><Button size="sm" variant="outline" onClick={() => setDeleteFolder(currentFolder)}>Delete folder</Button></div>}
    <Refused>{error}</Refused>{message && <p role="status" className="text-sm">{message} {undo.length > 0 && <Button variant="outline" size="sm" disabled={working} onClick={() => void restore()}>Undo deletion</Button>} <Link className="underline" to="/trash">Open Trash</Link></p>}
    {(list.unanswered || folders.unanswered) && <p role="status" className="text-sm text-muted-foreground">Refresh failed. Displayed information may have changed.</p>}
    {folders.asked.at === "failed" && <Failed why={folders.asked.why} again={folders.again} />}
    {selectedId && detail.asked.at === "failed" && <Failed why={detail.asked.why} again={detail.again} />}
    <div className="flex flex-wrap items-center gap-2"><span className="text-sm" role="status">{selected.length} selected</span>
      <Button variant="outline" disabled={!selected.length || working} onClick={() => { setTarget(""); setConfirmPublic(false); setAction("move"); }}>Move selected</Button>
      <Button variant="outline" disabled={!selected.length || working} onClick={() => setAction("delete")}>Delete selected</Button>
      {selected.length > 0 && <Button variant="ghost" onClick={() => setSelected([])}>Clear selection</Button>}</div>
    {list.asked.at === "asking" && <Busy title="Reading bookmarks…" />}
    {list.asked.at === "failed" && <Failed why={list.asked.why} again={list.again} />}
    {list.asked.at === "known" && rows.length === 0 && <p className="text-muted-foreground rounded-lg border border-dashed p-6">No bookmarks match this view.</p>}
    {rows.length > 0 && <div role="table" aria-label="Saved bookmarks" className="min-w-0 rounded-xl border">
      <div role="row" className="hidden grid-cols-[2rem_minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_5rem_8rem_4rem] gap-3 border-b p-3 text-xs font-medium md:grid">
        <div role="columnheader"><input name="select-all" type="checkbox" aria-label="Select this page" checked={rows.every((row) => selected.some((item) => item.id === row.id))} onChange={(event) => setSelected(event.target.checked ? rows : [])} /></div>
        {["Title", "Domain / URL", "Folder", "Favorite", "Changed", "Edit"].map((label) => <span role="columnheader" key={label}>{label}</span>)}
      </div>
      {rows.map((row) => <div key={row.id} role="row" className="grid min-w-0 grid-cols-[2rem_minmax(0,1fr)_auto] items-center gap-3 border-b p-3 last:border-b-0 md:grid-cols-[2rem_minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_5rem_8rem_4rem]">
        <div role="cell"><input name={`select-${row.id}`} type="checkbox" aria-label={`Select ${row.title}`} checked={selected.some((item) => item.id === row.id)} onChange={(event) => setSelected((current) => event.target.checked ? [...current.filter((item) => item.id !== row.id), row] : current.filter((item) => item.id !== row.id))} /></div>
        <div role="cell" className="min-w-0"><BookmarkLink id={row.id} url={row.url} className="block break-words font-medium hover:underline">{row.title}</BookmarkLink><span className="block truncate text-xs text-muted-foreground md:hidden">{domainOf(row.url)}{row.private && " · Private"}</span></div>
        <div role="cell" className="hidden min-w-0 md:block"><span title={row.url} className="block truncate text-sm">{domainOf(row.url)}</span><span className="block truncate text-xs text-muted-foreground">{row.url}</span></div>
        <div role="cell" className="hidden min-w-0 break-words text-xs md:block">{allFolders.find((item) => item.id === row.folder)?.name ?? "Unsorted"}{row.private && " · Private"}</div>
        <div role="cell" className="hidden md:block"><Button variant="ghost" size="sm" disabled={working} aria-label={`${row.favorite ? "Unpin" : "Pin"} ${row.title}`} onClick={() => void favorite(row)}>{row.favorite ? "★" : "☆"}</Button></div>
        <div role="cell" className="hidden text-xs text-muted-foreground md:block">{new Date(row.updated_at).toLocaleString()}</div>
        <div role="cell" className="flex flex-wrap justify-end gap-1"><Button className="md:hidden" variant="ghost" size="sm" disabled={working} aria-label={`${row.favorite ? "Unpin" : "Pin"} ${row.title}`} onClick={() => void favorite(row)}>{row.favorite ? "★" : "☆"}</Button><Button variant="outline" size="sm" onClick={() => setEditor({ kind: "bookmark", row })} aria-label={`Edit ${row.title}`}>Edit</Button></div>
      </div>)}
    </div>}
    {list.asked.at === "known" && <div className="flex gap-2"><Button variant="outline" disabled={offset === 0} onClick={() => filter("offset", String(Math.max(0, offset - 100)))}>Previous</Button><Button variant="outline" disabled={list.asked.value.next_offset === null} onClick={() => filter("offset", String(offset + 100))}>Next</Button></div>}
    <Dialog open={Boolean(bookmarkEditor || editor?.kind === "folder")} onOpenChange={(open) => { if (!open) closeEditor(); }}><DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-lg">
      <DialogTitle>{bookmarkEditor ? bookmarkEditor.row ? "Edit bookmark" : "Add bookmark" : "Edit folder"}</DialogTitle><DialogDescription>Changes are checked against the version you opened.</DialogDescription>
      {bookmarkEditor && <BookmarkForm key={bookmarkEditor.row?.id ?? "new"} initial={bookmarkEditor.row} folders={allFolders} defaultFolder={folder && folder !== "unsorted" ? folder : undefined} saved={() => { closeEditor(); refresh(); }} cancel={closeEditor} />}
      {editor?.kind === "folder" && <FolderForm initial={editor.row} folders={allFolders} saved={() => { closeEditor(); refresh(); }} cancel={closeEditor} />}
    </DialogContent></Dialog>
    <Dialog open={Boolean(action || deleteFolder)} onOpenChange={(open) => { if (!open && !working) { setAction(undefined); setDeleteFolder(undefined); } }}><DialogContent>
      <DialogTitle>{deleteFolder ? "Delete folder and contents?" : action === "delete" ? `Delete ${selected.length} bookmarks?` : `Move ${selected.length} bookmarks`}</DialogTitle>
      <DialogDescription>{action === "move" ? "Each change uses the selected version. Changed items will be left alone." : "Deleted contents go to Trash and can be restored during the recovery period."}</DialogDescription>
      {action === "move" && <Field label="Destination folder"><select name="destination" className={selectClass} value={target} onChange={(event) => { setTarget(event.target.value); setConfirmPublic(false); }}><option value="">Unsorted</option>{allFolders.map((row) => <option key={row.id} value={row.id}>{row.effective_private ? "Private · " : ""}{folderPath(row, allFolders)}</option>)}</select></Field>}
      {exposing && <label className="flex items-start gap-2 text-sm"><input name="confirm-public" type="checkbox" checked={confirmPublic} onChange={(event) => setConfirmPublic(event.target.checked)} /> I understand that these bookmarks may become visible outside private mode.</label>}
      <Refused>{error}</Refused><Button disabled={working || Boolean(exposing && !confirmPublic)} onClick={() => void (deleteFolder ? removeFolder() : apply())}>{working ? "Working…" : "Confirm"}</Button>
    </DialogContent></Dialog>
  </main>;
}
