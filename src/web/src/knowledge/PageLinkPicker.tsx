import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";

export type LinkablePage = { id: string; title: string };
export function PageLinkPicker({ pages, onPick, disabled }: { pages: LinkablePage[]; onPick: (page: LinkablePage) => void; disabled: boolean }) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const matches = pages.filter(page => page.title.toLocaleLowerCase().includes(query.toLocaleLowerCase()));
  return <>
    <Button type="button" variant="ghost" size="sm" disabled={disabled} onClick={() => setOpen(true)}>Link page</Button>
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent><DialogHeader><DialogTitle>Link to a page</DialogTitle></DialogHeader>
        <Input name="page-link-query" aria-label="Find a page by title" value={query} onChange={event => setQuery(event.target.value)} autoFocus />
        <ul className="max-h-72 overflow-auto" aria-label="Pages to link">
          {matches.map(page => <li key={page.id}><Button type="button" variant="ghost" onClick={() => { onPick(page); setOpen(false); setQuery(""); }}>{page.title}</Button></li>)}
        </ul>
        {matches.length === 0 && <p>No matching pages.</p>}
      </DialogContent>
    </Dialog>
  </>;
}
