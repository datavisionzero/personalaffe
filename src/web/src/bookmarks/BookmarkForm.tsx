import { useState, type FormEvent } from "react";
import { api, describe, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { TagEditor } from "./TagEditor";
import { domainOf, folderPath, type Bookmark, type Folder } from "./useBookmarks";

export function BookmarkForm({ initial, folders, defaultFolder, saved, cancel }: {
  initial?: Bookmark; folders: Folder[]; defaultFolder?: string; saved: (row: Bookmark) => void; cancel: () => void;
}) {
  const privacy = useBookmarkPrivacy();
  const [url, setUrl] = useState(initial?.url ?? "");
  const [title, setTitle] = useState(initial?.title ?? "");
  const [suggestTitle, setSuggestTitle] = useState(!initial?.title);
  const [description, setDescription] = useState(initial?.description ?? "");
  const [folder, setFolder] = useState(initial?.folder ?? defaultFolder ?? "");
  const [tags, setTags] = useState(initial?.tags ?? []);
  const [confirmed, setConfirmed] = useState(false);
  const exposing = initial?.private && !folders.find((row) => row.id === folder)?.effective_private;
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string>();

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (exposing && !confirmed) return;
    setWorking(true); setError(undefined);
    const body = { title: title.trim() || domainOf(url), url, description, folder: folder || null, tags };
    try {
      const answer = initial
        ? await api.PUT("/api/bookmarks/{id}", { params: { path: { id: initial.id }, ...guardedBy(versionOf(initial.updated_at)) }, body, headers: privacy.headers })
        : await api.POST("/api/bookmarks", { body, headers: privacy.headers });
      if (answer.data) saved(answer.data);
      else setError(describe(answer.error, answer.response.status));
    } catch { setError("The instance did not answer. Your input is still here."); }
    finally { setWorking(false); }
  }

  return <form onSubmit={(event) => void submit(event)} className="flex min-w-0 flex-col gap-4">
    <Field label="URL"><Input name="url" type="url" required autoFocus value={url} onChange={(event) => {
        const value = event.target.value; setUrl(value);
        if (suggestTitle) { try { setTitle(new URL(value).hostname.replace(/^www\./, "")); } catch { setTitle(""); } }
      }} placeholder="https://example.com" maxLength={8192} /></Field>
    <Field label="Title" hint="The domain is suggested when the title is empty; you can change it.">
      <Input name="title" value={title} onChange={(event) => { setSuggestTitle(false); setTitle(event.target.value); }} maxLength={200} />
    </Field>
    <Field label="Description"><textarea name="description" className="min-h-24 w-full rounded-lg border border-input bg-transparent p-2 outline-none focus-visible:ring-2 focus-visible:ring-ring" value={description} onChange={(event) => setDescription(event.target.value)} rows={3} /></Field>
    <Field label="Folder"><select name="folder" className={selectClass} value={folder} onChange={(event) => { setFolder(event.target.value); setConfirmed(false); }}>
      <option value="">Unsorted</option>
      {folders.map((row) => <option key={row.id} value={row.id}>{row.effective_private ? "Private · " : ""}{folderPath(row, folders)}</option>)}
    </select></Field>
    <TagEditor value={tags} onChange={setTags} />
    {exposing && <label className="flex items-start gap-2 rounded-lg border p-3 text-sm"><input name="confirm-public" type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /> I understand that moving this bookmark may make it visible outside private mode.</label>}
    <Refused>{error}</Refused>
    <div className="flex justify-end gap-2">
      <Button type="button" variant="outline" onClick={cancel}>Cancel</Button>
      <Button type="submit" disabled={working || Boolean(exposing && !confirmed)}>{working ? "Saving…" : initial ? "Save changes" : "Add bookmark"}</Button>
    </div>
  </form>;
}
