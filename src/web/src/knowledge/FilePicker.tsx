import { useState } from "react";
import { api, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAsk } from "@/shared/ask";
type StoredFile = Schemas["FileResponse"];

export function FilePicker({ onPick }: { onPick: (file: StoredFile) => void }) {
  const [folder, setFolder] = useState<string>();
  const [query, setQuery] = useState("");
  const listing = useAsk<Schemas["FilesResponse"]>(`media:${folder ?? ""}`, signal => api.GET("/api/files", { params: { query: { folder } }, signal }));
  return <div className="space-y-2">
    <Button type="button" variant="outline" onClick={() => setFolder(undefined)}>Files root</Button>
    <Input name="media-filter" aria-label="Filter files in this folder" value={query} onChange={event => setQuery(event.target.value)} />
    {listing.asked.at === "asking" && <p>Loading files…</p>}
    {listing.asked.at === "failed" && <p role="alert">{listing.asked.why}</p>}
    {listing.asked.at === "known" && <ul className="max-h-72 overflow-auto" aria-label="Available files">
      {listing.asked.value.folders.map(item => <li key={item.id}><Button type="button" variant="ghost" onClick={() => { setFolder(item.id); setQuery(""); }}>Open folder: {item.name}</Button></li>)}
      {listing.asked.value.files.filter(file => file.name.toLocaleLowerCase().includes(query.toLocaleLowerCase())).map(file => <li key={file.id}><Button type="button" variant="ghost" onClick={() => onPick(file)}>{file.name}</Button></li>)}
      {listing.asked.value.files.length === 0 && listing.asked.value.folders.length === 0 && <li>This folder is empty.</li>}
    </ul>}
    <p className="text-xs text-muted-foreground">Images appear inline. Other files become download links. You can also paste or drop images into the editor.</p>
  </div>;
}
