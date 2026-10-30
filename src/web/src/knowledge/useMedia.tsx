import { FilePicker } from "./FilePicker";
import { useEffect, useRef, useState, type ClipboardEvent, type DragEvent } from "react";
import { api, describe, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import type { EditorHandle } from "@/shared/Editor";

type StoredFile = Schemas["FileResponse"];
const imageTypes = new Set(["image/png", "image/jpeg", "image/gif", "image/webp", "image/avif"]);
const escapeLabel = (label: string) => label.replace(/[\\[\]]/g, "\\$&");

export function useMedia(editor: EditorHandle | null, enabled: boolean) {
  const [choosing, setChoosing] = useState(false);
  const [status, setStatus] = useState<string>();
  const [error, setError] = useState<string>();
  const uploading = useRef(false);
  const currentEditor = useRef(editor);
  useEffect(() => { currentEditor.current = editor; }, [editor]);
  function insert(file: StoredFile) {
    const link = `${imageTypes.has(file.media_type) ? "!" : ""}[${escapeLabel(file.name)}](file:${file.id})`;
    currentEditor.current?.apply(at => ({ text: at.text.slice(0, at.from) + link + at.text.slice(at.to), from: at.from + link.length, to: at.from + link.length }));
  }
  async function upload(files: File[]) {
    if (!enabled || uploading.current || files.length === 0) return;
    uploading.current = true;
    setError(undefined);
    try {
      const listing = await api.GET("/api/files");
      if (!listing.data) throw new Error(describe(listing.error, listing.response.status));
      let used = listing.data.used_bytes;
      for (const file of files) {
        if (!imageTypes.has(file.type)) throw new Error("Use PNG, JPEG, GIF, WebP or AVIF images. Other files can be selected from Files.");
        if (file.size > listing.data.max_file_bytes || used + file.size > listing.data.max_total_bytes) throw new Error(`${file.name} exceeds the file or storage limit.`);
        setStatus(`Uploading ${file.name}…`);
        const answer = await api.POST("/api/files/content", {
          params: { query: { name: file.name || `image-${Date.now()}.png` } },
          body: file as unknown as string,
          bodySerializer: body => body as BodyInit,
          headers: { "Content-Type": file.type },
        });
        if (!answer.data) throw new Error(describe(answer.error, answer.response.status));
        insert(answer.data); used += file.size;
      }
      setStatus("Images uploaded to Files. Save the page to keep the references.");
    } catch (failure) { setStatus(undefined); setError(failure instanceof Error ? failure.message : "Upload failed. Try again."); }
    finally { uploading.current = false; }
  }
  const onPaste = (event: ClipboardEvent) => {
    if (!enabled || !event.clipboardData.files.length) return;
    event.preventDefault(); event.stopPropagation();
    void upload(Array.from(event.clipboardData.files));
  };
  const onDrop = (event: DragEvent) => {
    if (!enabled || !event.dataTransfer.files.length) return;
    event.preventDefault(); event.stopPropagation();
    void upload(Array.from(event.dataTransfer.files));
  };
  return {
    onPaste, onDrop,
    onDragOver: (event: DragEvent) => { if (enabled && event.dataTransfer.types.includes("Files")) event.preventDefault(); },
    controls: enabled && <>
      <Button type="button" variant="ghost" size="sm" disabled={!editor} onClick={() => setChoosing(true)}>Insert file</Button>
      <Dialog open={choosing} onOpenChange={setChoosing}><DialogContent>
        <DialogHeader><DialogTitle>Insert a file or image</DialogTitle></DialogHeader>
        {choosing && <FilePicker onPick={file => { insert(file); setChoosing(false); }} />}
      </DialogContent></Dialog>
    </>,
    feedback: enabled && <>
      {status && <p role="status" className="text-xs text-muted-foreground">{status}</p>}
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
    </>,
  };
}
