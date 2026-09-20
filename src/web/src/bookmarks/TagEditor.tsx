import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Field, Refused } from "@/shared/Form";
import { api } from "@/api/client";
import { useAsk } from "@/shared/ask";
import { useBookmarkPrivacy } from "./useBookmarkPrivacy";

export function TagEditor({ value, onChange, label = "Tags" }: { value: string[]; onChange: (tags: string[]) => void; label?: string }) {
  const privacy = useBookmarkPrivacy();
  const suggestions = useAsk(`/api/bookmarks/tags:${privacy.epoch}`, (signal) => api.GET("/api/bookmarks/tags", { headers: privacy.headers, signal }));
  const id = useId();
  const [typed, setTyped] = useState("");
  const [error, setError] = useState<string>();
  function add() {
    const name = typed.trim().toLowerCase();
    if (!name || name.length > 64 || Array.from(name).some((character) => { const code = character.codePointAt(0)!; return code < 32 || (code >= 127 && code <= 159); })) { setError("Use a tag of 1 to 64 characters on one line."); return; }
    if (!value.includes(name) && value.length >= 32) { setError("Use at most 32 tags."); return; }
    onChange([...new Set([...value, name])].sort()); setTyped(""); setError(undefined);
  }
  return <div className="min-w-0 space-y-2">
    <Field label={label}><Input name={`tag-${id}`} list={id} value={typed} placeholder="Type a tag, then Enter" onChange={(event) => setTyped(event.target.value)} onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); add(); } }} /></Field>
    <datalist id={id}>{suggestions.asked.at === "known" && suggestions.asked.value.map((tag) => <option key={tag.name} value={tag.name}>{tag.count} visible bookmarks</option>)}</datalist>
    <div className="flex flex-wrap items-center gap-2"><Button type="button" size="sm" variant="outline" disabled={!typed.trim()} onClick={add}>Add tag</Button>
      {value.map((tag) => <Button key={tag} type="button" variant="secondary" size="sm" className="h-auto max-w-full whitespace-normal break-all" aria-label={`Remove tag ${tag}`} onClick={() => onChange(value.filter((item) => item !== tag))}>{tag} ×</Button>)}
      {value.length > 0 && <Button type="button" size="sm" variant="ghost" onClick={() => onChange([])}>Clear tags</Button>}
    </div><Refused>{error}</Refused>
  </div>;
}
