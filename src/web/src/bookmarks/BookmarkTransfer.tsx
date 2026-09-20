import { useEffect, useRef, useState } from "react";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";
import { folderPath, type Folder } from "./useBookmarks";

export function BookmarkTransfer({ folders, changed }: { folders: Folder[]; changed: () => void }) {
  const privacy = useBookmarkPrivacy();
  const active = useRef(true);
  const fileRound = useRef(0);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const [mode, setMode] = useState<"import" | "export">();
  const [html, setHtml] = useState("");
  const [folder, setFolder] = useState("");
  const [includePrivate, setIncludePrivate] = useState(false);
  const [preview, setPreview] = useState<Schemas["BookmarkImportPreview"]>();
  const [working, setWorking] = useState(false);
  const [error, setError] = useState<string>();
  const [message, setMessage] = useState<string>();
  function open(next: "import" | "export") { fileRound.current++; setMode(next); setHtml(""); setFolder(""); setIncludePrivate(false); setPreview(undefined); setError(undefined); setMessage(undefined); }
  async function read(file?: File) {
    const round = ++fileRound.current;
    setPreview(undefined); setError(undefined); setHtml("");
    if (!file) return;
    if (file.size > 2 * 1024 * 1024) { setError("Choose an HTML file no larger than 2 MiB."); return; }
    try { const text = await file.text(); if (active.current && fileRound.current === round) setHtml(text); } catch { setError("The file could not be read."); }
  }
  async function inspect() {
    setWorking(true); setError(undefined);
    try {
      const response = await api.POST("/api/bookmarks/import/preview", { headers: privacy.headers, body: { html, folder: folder || null, preview_hash: null } });
      if (response.data) setPreview(response.data); else setError(describe(response.error, response.response.status));
    } catch { setError("The instance did not answer."); } finally { setWorking(false); }
  }
  async function apply() {
    if (!preview) return;
    setWorking(true); setError(undefined);
    try {
      const response = await api.POST("/api/bookmarks/import", { headers: privacy.headers, body: { html, folder: folder || null, preview_hash: preview.preview_hash } });
      if (response.data) { setMessage(`${response.data.imported_bookmarks} bookmarks imported, ${response.data.created_folders} folders created, ${response.data.skipped_duplicates} duplicates skipped, ${response.data.rejected.length} rejected.`); setPreview(undefined); setHtml(""); changed(); }
      else { setError(describe(response.error, response.response.status)); setPreview(undefined); }
    } catch { setError("The instance did not answer. Preview again before retrying."); setPreview(undefined); } finally { setWorking(false); }
  }
  async function download() {
    setWorking(true); setError(undefined);
    try {
      const response = await api.GET("/api/bookmarks/export", { headers: privacy.headers, params: { query: { folder: folder || undefined, include_private: includePrivate } } });
      if (!response.data) { setError(describe(response.error, response.response.status)); return; }
      if (!active.current) return;
      const blob = new Blob([response.data.html], { type: "text/html;charset=utf-8" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a"); anchor.href = url; anchor.download = "bookmarks.html"; anchor.click(); URL.revokeObjectURL(url);
      setMessage(`${response.data.bookmarks} bookmarks and ${response.data.folders} folders exported.`);
    } catch { setError("The export could not be downloaded. Please try again."); } finally { setWorking(false); }
  }
  return <>
    <div className="flex flex-wrap gap-2"><Button variant="outline" onClick={() => open("import")}>Import HTML</Button><Button variant="outline" onClick={() => open("export")}>Export HTML</Button></div>
    <Dialog open={Boolean(mode)} onOpenChange={(opened) => { if (!opened && !working) { setMode(undefined); setHtml(""); setPreview(undefined); } }}>
      <DialogContent className="max-h-[90dvh] overflow-y-auto sm:max-w-lg"><DialogTitle>{mode === "import" ? "Import browser bookmarks" : "Export browser bookmarks"}</DialogTitle>
        <DialogDescription>Browser HTML does not preserve private markings, favorites, tags or reading status and is not a backup. The file is unencrypted. Reimport private exports into a private destination folder.</DialogDescription>
        <p className="text-xs text-muted-foreground">Limits: 2 MiB UTF-8, 5,000 entries and 32 folder levels. Existing links in the same folder are skipped; nothing is overwritten.</p>
        <Field label={mode === "import" ? "Destination folder" : "Export folder"}><select name="transfer-folder" className={selectClass} disabled={working} value={folder} onChange={(event) => { setFolder(event.target.value); setPreview(undefined); }}>
          <option value="">{mode === "import" ? "Root / Unsorted" : "All folders"}</option>{folders.map((row) => <option key={row.id} value={row.id}>{row.effective_private ? "Private · " : ""}{folderPath(row, folders)}</option>)}
        </select></Field>
        {mode === "import" ? <>
          <Field label="Bookmark HTML file"><Input name="bookmark-html" type="file" accept=".html,.htm,text/html" disabled={working} onChange={(event) => void read(event.target.files?.[0])} /></Field>
          <Button variant="outline" disabled={!html || working} onClick={() => void inspect()}>Preview import</Button>
          {preview && <div className="space-y-3 rounded-lg border p-3">
            <p>{preview.valid_bookmarks} valid bookmarks, {preview.folders} folders; {preview.new_bookmarks} new bookmarks, {preview.new_folders} new folders, {preview.skipped_duplicates} duplicates skipped, {preview.rejected.length} rejected.</p>
            {preview.rejected.length > 0 && <ul className="max-h-32 overflow-auto text-sm">{preview.rejected.map((row) => <li key={row.entry}>Entry {row.entry}: {row.reason}</li>)}</ul>}
            <Button disabled={working} onClick={() => void apply()}>Confirm import</Button>
          </div>}
        </> : <>
          <label className="flex items-start gap-2 text-sm"><input name="export-private" type="checkbox" disabled={!privacy.enabled || working} checked={includePrivate} onChange={(event) => setIncludePrivate(event.target.checked)} /> Include private bookmarks in this unencrypted file</label>
          {!privacy.enabled && <p className="text-xs text-muted-foreground">Enable private mode first to include private content.</p>}
          <Button disabled={working} onClick={() => void download()}>Download HTML</Button>
        </>}
        <Refused>{error}</Refused>{message && <p role="status">{message}</p>}
      </DialogContent>
    </Dialog>
  </>;
}
