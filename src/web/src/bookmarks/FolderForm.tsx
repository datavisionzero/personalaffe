import { useState, type FormEvent } from "react";
import { api, describe, guardedBy, versionOf } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { folderPath, type Folder } from "./useBookmarks";

export function FolderForm({ initial, folders, saved, cancel }: { initial?: Folder; folders: Folder[]; saved: () => void; cancel: () => void }) {
  const privacy = useBookmarkPrivacy();
  const [name, setName] = useState(initial?.name ?? "");
  const [parent, setParent] = useState(initial?.parent ?? "");
  const [isPrivate, setPrivate] = useState(initial?.private ?? false);
  const [confirmed, setConfirmed] = useState(false);
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string>();
  const inherited = folders.find((row) => row.id === parent)?.effective_private ?? false;
  const exposing = initial?.effective_private && !isPrivate && !inherited;
  const unavailable = new Set(initial ? [initial.id] : []);
  for (let round = 0; round < folders.length; round++) for (const row of folders) if (row.parent && unavailable.has(row.parent)) unavailable.add(row.id);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (exposing && !confirmed) return;
    setWorking(true); setError(undefined);
    const body = { name, parent: parent || null, private: isPrivate };
    try {
      const answer = initial
        ? await api.PUT("/api/bookmarks/folders/{id}", { headers: privacy.headers, params: { path: { id: initial.id }, ...guardedBy(versionOf(initial.updated_at)) }, body })
        : await api.POST("/api/bookmarks/folders", { headers: privacy.headers, body });
      if (answer.data) saved(); else setError(describe(answer.error, answer.response.status));
    } catch { setError("The instance did not answer. Your input is still here."); }
    finally { setWorking(false); }
  }
  return <form className="flex flex-col gap-4" onSubmit={(event) => void submit(event)}>
    <Field label="Folder name"><Input name="name" required maxLength={200} autoFocus value={name} onChange={(event) => setName(event.target.value)} /></Field>
    <Field label="Parent folder"><select name="parent" className={selectClass} value={parent} onChange={(event) => { setParent(event.target.value); setConfirmed(false); }}>
      <option value="">Root</option>{folders.filter((row) => !unavailable.has(row.id)).map((row) => <option key={row.id} value={row.id}>{folderPath(row, folders)}</option>)}
    </select></Field>
    <label className="flex items-center gap-2"><input name="private" type="checkbox" disabled={!privacy.enabled} checked={isPrivate} onChange={(event) => { setPrivate(event.target.checked); setConfirmed(false); }} /> Private folder</label>
    <p className="text-muted-foreground text-sm">{inherited ? "Private visibility is inherited from the parent and cannot be switched off here." : "Private folders and their contents are hidden unless private mode is on."}</p>
    {exposing && <label className="flex items-start gap-2 rounded-lg border p-3 text-sm"><input name="confirm-public" type="checkbox" checked={confirmed} onChange={(event) => setConfirmed(event.target.checked)} /> I understand that this folder and contents without their own private setting may become visible outside private mode.</label>}
    <Refused>{error}</Refused><div className="flex justify-end gap-2"><Button type="button" variant="outline" onClick={cancel}>Cancel</Button><Button disabled={working || Boolean(exposing && !confirmed)}>{working ? "Saving…" : "Save folder"}</Button></div>
  </form>;
}
