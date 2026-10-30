import { DownloadIcon, FileIcon, FileImageIcon, FileTextIcon, FolderIcon, FolderInputIcon, InfoIcon, MoreVerticalIcon, PencilIcon, Trash2Icon } from "lucide-react";
import { useState } from "react";
import type { Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";

export type Thing = { kind: "folder"; it: Schemas["FolderResponse"] } | { kind: "file"; it: Schemas["FileResponse"] };
export type FileAction = "actions" | "details" | "rename" | "move" | "trash";

export function FileGlyph({ thing }: { thing: Thing }) {
  const Icon = thing.kind === "folder" ? FolderIcon
    : thing.it.media_type.startsWith("image/") ? FileImageIcon
    : thing.it.media_type.startsWith("text/") || thing.it.media_type === "application/pdf" ? FileTextIcon : FileIcon;
  return <Icon aria-hidden className={`size-5 shrink-0 ${thing.kind === "folder" ? "text-brand fill-brand/15" : "text-muted-foreground"}`} />;
}

export function FileRow({ thing, selected, busy, mobile, size, onSelect, onOpen, onAction }: {
  thing: Thing; selected: boolean; busy: boolean; mobile: boolean;
  size: (bytes: number) => string;
  onSelect: () => void; onOpen: () => void; onAction: (action: FileAction) => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const menuItem = "min-h-10 gap-3 px-3";
  const date = new Date(thing.it.updated_at).toLocaleDateString();
  return <tr className={`group border-b border-border/60 ${selected ? "bg-brand-soft" : "hover:bg-muted/50"}`}
    onClick={onSelect}
    onContextMenu={(event) => {
      event.preventDefault();
      onSelect();
      if (mobile) onAction("actions"); else setMenuOpen(true);
    }}>
    <td className="px-3 py-1 md:px-4">
      <Button variant="ghost" className="h-auto min-h-12 w-full min-w-0 justify-start gap-3 rounded-none px-0 text-left font-normal hover:bg-transparent"
        aria-label={thing.it.name}
        aria-pressed={thing.kind === "file" ? selected : undefined}
        onClick={() => {
          if (thing.kind === "folder") onOpen();
          else if (mobile) onAction("actions");
          else onSelect();
        }}
        onDoubleClick={() => { if (thing.kind === "file" && !mobile) onAction("details"); }}>
        <FileGlyph thing={thing} />
        <span className="min-w-0 flex-1">
          <span className="block whitespace-normal wrap-anywhere md:truncate md:whitespace-nowrap">{thing.it.name}</span>
          <span className="text-muted-foreground mt-0.5 block text-xs md:hidden">
            {thing.kind === "file" ? `${size(thing.it.size)} · ` : ""}{date}
          </span>
        </span>
      </Button>
    </td>
    <td className="text-muted-foreground hidden px-4 text-xs md:table-cell"><time dateTime={thing.it.updated_at}>{date}</time></td>
    <td className="text-muted-foreground hidden px-4 text-right text-xs tabular-nums md:table-cell">{thing.kind === "file" ? size(thing.it.size) : "—"}</td>
    <td className="w-14 px-2 text-right">
      {mobile ? <Button variant="ghost" size="icon" className="size-11" data-file-actions={thing.it.id} aria-label={`Actions for ${thing.it.name}`} aria-haspopup="dialog" onClick={() => onAction("actions")}><MoreVerticalIcon /></Button>
        : <DropdownMenu open={menuOpen} onOpenChange={(open) => { setMenuOpen(open); if (open) onSelect(); }}>
          <DropdownMenuTrigger render={<Button variant="ghost" size="icon" data-file-actions={thing.it.id} aria-label={`Actions for ${thing.it.name}`} />}><MoreVerticalIcon /></DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-60 p-1.5">
            {thing.kind === "file" && <><DropdownMenuItem className={menuItem} render={<a href={`/api/files/${thing.it.id}/content`} download={thing.it.name} />} aria-label={`Download ${thing.it.name}`}><DownloadIcon />Download</DropdownMenuItem><DropdownMenuSeparator /></>}
            <DropdownMenuItem className={menuItem} disabled={busy} onClick={() => onAction("move")}><FolderInputIcon />Move to folder</DropdownMenuItem>
            <DropdownMenuItem className={menuItem} disabled={busy} onClick={() => onAction("rename")}><PencilIcon />Rename</DropdownMenuItem>
            <DropdownMenuItem className={menuItem} onClick={() => onAction("details")}><InfoIcon />Details</DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem className={menuItem} variant="destructive" disabled={busy} aria-label={`Delete ${thing.it.name}`} onClick={() => onAction("trash")}><Trash2Icon />Move to Trash</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>}
    </td>
  </tr>;
}
