import { useDestination } from "@/shell/navigation";
import {
  ChevronRightIcon,
  DownloadIcon,
  FolderIcon,
  FolderPlusIcon,
  PencilIcon,
  FolderInputIcon,
  InfoIcon,
  ArrowLeftIcon,
  ArrowDownIcon,
  ArrowUpIcon,
  XIcon,
  Trash2Icon,
  UploadIcon,
} from "lucide-react";
import { useId, useRef, useState } from "react";
import { useNavigate, useParams } from "react-router";

import { api, guardedBy, versionOf, type Schemas } from "@/api/client";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogClose,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { FileGlyph, FileRow, type Thing, type FileAction } from "./FileListing";
import { useIsMobile } from "@/hooks/use-mobile";
import { Input } from "@/components/ui/input";
import { refusal } from "@/session/useSession";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useAsk } from "@/shared/ask";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Listing = Schemas["FilesResponse"];

/** What an upload in flight looks like on the screen. */
type Arriving = { name: string; refused?: string };

/**
 * Files: a folder you can walk, a table to select from, and actions for the
 * selected item (`docs/api.md`, Files).
 *
 * <b>The address is the folder.</b> `/files/<id>` is a real address, so a link
 * into a folder opens that folder — which is what VISION §6 means by navigation
 * belonging to the application: a file manager is a tree and a tree is walked,
 * not filtered.
 *
 * <b>Deleting says what is true, and that is why it asks nothing.</b> The
 * Scratchpad's delete is a dialog because it destroys the row; this one sets
 * content aside and the Trash has it. The sentence afterwards says so.
 */
export function Files() {
  const params = useParams();
  const navigate = useNavigate();

  // The route is `/files/*`, so what is under it is the folder — nothing for
  // the top of the tree, which has no id because it is not a row.
  const at = (params["*"] ?? "").replace(/\/+$/, "");
  const folder = at === "" ? undefined : at;

  // What the last write said, and which folder it said it in. It is a pair
  // rather than two pieces of state cleared on a walk: a sentence about a file
  // in another folder has no business on this screen, and `useAsk` keeps its
  // own answer the same way and for the same reason.
  const [wrote, setWrote] = useState<{ at: string; refused?: string; said?: string }>();
  const [working, setWorking] = useState<string>();
  const [arriving, setArriving] = useState<Arriving[]>([]);
  const [over, setOver] = useState(false);
  const [making, setMaking] = useState(false);
  const mobile = useIsMobile();
  const [panel, setPanel] = useState<{ at: string; id: string; view: Exclude<FileAction, "trash"> }>();
  const [sort, setSort] = useState<{ by: "name" | "updated" | "size"; descending: boolean }>({ by: "name", descending: false });
  const actionOrigin = useRef<HTMLElement | null>(null);
  const [selection, setSelection] = useState<{ at: string; id: string }>();

  const picker = useRef<HTMLInputElement>(null);

  // Which refusal the last read made. Two of the five states are decided by the
  // code rather than the status, and this is where it is kept.
  const why = useRef<string>(undefined);
  const [missing, setMissing] = useState(false);

  const address = folder === undefined ? "/api/files" : `/api/files?folder=${folder}`;

  const refused = wrote?.at === address ? wrote.refused : undefined;
  const said = wrote?.at === address ? wrote.said : undefined;

  const setRefused = (message: string | undefined) => setWrote({ at: address, refused: message });
  const setSaid = (message: string | undefined) => setWrote({ at: address, said: message });

  const { asked, again, refresh, unanswered } = useAsk<Listing>(address, async (signal) => {
    const answer = await api.GET("/api/files", {
      params: { query: folder === undefined ? {} : { folder } },
      signal,
    });

    why.current = answer.error ? refusal(answer.error, answer.response.status).code : undefined;
    setMissing(why.current === "not-found");

    return answer;
  });

  async function upload(chosen: FileList | null) {
    if (chosen === null || chosen.length === 0) {
      return;
    }

    setRefused(undefined);
    setSaid(undefined);

    // Each file is its own request and its own refusal: one that is too large
    // must not take the others down with it.
    for (const file of Array.from(chosen)) {
      setArriving((all) => [...all, { name: file.name }]);

      const answer = await api.POST("/api/files/content", {
        params: {
          query: folder === undefined ? { name: file.name } : { name: file.name, folder },
        },
        // The body is the file. `bodySerializer` is what keeps the client from
        // turning it into JSON on the way out.
        body: file as unknown as string,
        bodySerializer: (body: unknown) => body as BodyInit,
        headers: { "Content-Type": file.type === "" ? "application/octet-stream" : file.type },
      });

      setArriving((all) => all.filter((one) => one.name !== file.name));

      if (answer.error) {
        const { message } = refusal(answer.error, answer.response.status);

        // Each upload's own refusal, kept beside the others: one file that is
        // too large must not take the rest of a drop down with it.
        setWrote((before) => ({
          at: address,
          refused:
            before?.at === address && before.refused !== undefined
              ? `${before.refused} ${message}`
              : message,
        }));
      }
    }

    refresh();
  }

  async function makeFolder(name: string) {
    setWorking("folder");
    setRefused(undefined);

    try {
      const answer = await api.POST("/api/files/folders", {
        body: { name, parent: folder ?? null },
      });

      if (answer.error) {
        setRefused(said_(answer.error, answer.response.status));
        return;
      }

      setMaking(false);
      refresh();
    } finally {
      setWorking(undefined);
    }
  }

  async function change(thing: Thing, name: string, into: string | null) {
    setWorking(thing.it.id);
    setRefused(undefined);

    try {
      const body = { name, folder: into };
      const answer =
        thing.kind === "folder"
          ? await api.PUT("/api/files/folders/{id}", {
              params: {
                path: { id: thing.it.id },
                ...guardedBy(versionOf(thing.it.updated_at)),
              },
              body,
            })
          : await api.PUT("/api/files/{id}", {
              params: {
                path: { id: thing.it.id },
                ...guardedBy(versionOf(thing.it.updated_at)),
              },
              body,
            });

      if (answer.error) {
        setRefused(said_(answer.error, answer.response.status));
        return;
      }

      setPanel(undefined);
      refresh();
    } finally {
      setWorking(undefined);
    }
  }

  async function discard(thing: Thing) {
    setWorking(thing.it.id);
    setRefused(undefined);
    setSaid(undefined);

    try {
      const answer =
        thing.kind === "folder"
          ? await api.DELETE("/api/files/folders/{id}", {
              params: {
                path: { id: thing.it.id },
                ...guardedBy(versionOf(thing.it.updated_at)),
              },
            })
          : await api.DELETE("/api/files/{id}", {
              params: {
                path: { id: thing.it.id },
                ...guardedBy(versionOf(thing.it.updated_at)),
              },
            });

      if (answer.error) {
        setRefused(said_(answer.error, answer.response.status));
        return;
      }

      // The opposite of the Scratchpad's dialog: nothing was destroyed, and
      // saying where it went is more use than asking whether to send it there.
      setPanel(undefined);
      setSaid(
        thing.kind === "folder"
          ? `${thing.it.name} and everything in it are in the Trash.`
          : `${thing.it.name} is in the Trash.`,
      );
    } finally {
      refresh();
      setWorking(undefined);
    }
  }

  const listing = asked.at === "known" ? asked.value : undefined;
  useDestination(folder ? `/files/${folder}` : "/files", listing ? listing.chain.at(-1)?.name ?? "Files" : undefined, missing);
  const things: Thing[] = listing ? [
    ...listing.folders.map((it): Thing => ({ kind: "folder", it })),
    ...listing.files.map((it): Thing => ({ kind: "file", it })),
  ] : [];
  // Resolve against the current listing so refreshes, moves and deletes cannot
  // leave actions pointing at an old version or a different folder.
  const selected = selection?.at === address
    ? things.find((thing) => thing.it.id === selection.id)
    : undefined;
  const focused = panel?.at === address ? things.find((thing) => thing.it.id === panel.id) : undefined;
  const ordered = [...things].sort((a, b) => {
    if (a.kind !== b.kind) return a.kind === "folder" ? -1 : 1;
    const comparison = sort.by === "updated" ? Date.parse(a.it.updated_at) - Date.parse(b.it.updated_at)
      : sort.by === "size" ? (a.kind === "file" ? a.it.size : 0) - (b.kind === "file" ? b.it.size : 0)
      : a.it.name.localeCompare(b.it.name, undefined, { numeric: true, sensitivity: "base" });
    return (sort.descending ? -comparison : comparison) || a.it.name.localeCompare(b.it.name);
  });
  function act(thing: Thing, action: FileAction) {
    setRefused(undefined);
    actionOrigin.current = Array.from(document.querySelectorAll<HTMLElement>("[data-file-actions]")).find((button) => button.dataset.fileActions === thing.it.id) ?? null;
    setSelection({ at: address, id: thing.it.id });
    if (action === "trash") void discard(thing);
    else setPanel({ at: address, id: thing.it.id, view: action });
  }
  function sortBy(by: typeof sort.by) {
    setSort({ by, descending: sort.by === by ? !sort.descending : false });
  }
  function sortHeading(by: typeof sort.by, title: string) {
    return <button type="button" className="inline-flex items-center gap-2 py-3 hover:text-foreground" onClick={() => sortBy(by)}>
      {title}{sort.by === by && (sort.descending ? <ArrowDownIcon aria-hidden className="size-3.5" /> : <ArrowUpIcon aria-hidden className="size-3.5" />)}
    </button>;
  }

  return (
    <main
      className="page-frame gap-4"
      onDragOver={(event) => {
        event.preventDefault();
        setOver(true);
      }}
      onDragLeave={() => setOver(false)}
      onDrop={(event) => {
        event.preventDefault();
        setOver(false);
        void upload(event.dataTransfer.files);
      }}
    >
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Files</h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Button className="min-h-11 md:min-h-8" type="button" variant="outline" onClick={() => setMaking(true)}>
            <FolderPlusIcon aria-hidden />New folder
          </Button>
          <Button className="min-h-11 md:min-h-8" type="button" onClick={() => picker.current?.click()}>
            <UploadIcon aria-hidden />Upload files
          </Button>
        </div>
      </header>

      <nav aria-label="Where you are" className="flex flex-wrap items-center gap-1 text-sm">
        <Button variant="ghost" size="sm" onClick={() => void navigate("/files")}>
          <FolderIcon aria-hidden />
          All files
        </Button>
        {(listing?.chain ?? []).map((step, index, all) => (
          <span key={step.id} className="flex items-center gap-1">
            <ChevronRightIcon aria-hidden className="text-muted-foreground size-3.5" />
            {index === all.length - 1 ? (
              <span aria-current="page" className="px-2 font-medium">
                {step.name}
              </span>
            ) : (
              <Button variant="ghost" size="sm" onClick={() => void navigate(`/files/${step.id}`)}>
                {step.name}
              </Button>
            )}
          </span>
        ))}
      </nav>

      <input
        ref={picker}
        type="file"
        name="files"
        multiple
        className="sr-only"
        aria-label="Files to upload"
        onChange={(event) => {
          void upload(event.target.files);
          event.target.value = "";
        }}
      />

      {over && (
        <p role="status" className="border-brand text-brand rounded-lg border border-dashed p-4 text-sm">
          Drop them here.
        </p>
      )}

      {arriving.map((one) => (
        <p key={one.name} role="status" className="text-muted-foreground text-xs">
          Uploading {one.name}…
        </p>
      ))}

      <Refused>{refused}</Refused>

      {said !== undefined && (
        <p role="status" className="text-muted-foreground text-sm text-balance">
          {said} Go to the Trash to put it back.
        </p>
      )}

      {unanswered && asked.at === "known" && (
        <p role="status" className="text-muted-foreground text-xs text-balance">
          The instance stopped answering. This is what it last said.
        </p>
      )}

      {asked.at === "asking" && <Busy title="Opening Files…" />}

      {asked.at === "failed" && whyItFailed(asked.why)}

      {listing !== undefined && listing.folders.length === 0 && listing.files.length === 0 && (
        <Empty title="Nothing in this folder.">
          Drop something onto this screen, or use the button above. Files live on this instance's own
          disk and are reachable from every device you are signed in on.
        </Empty>
      )}

      {things.length > 0 && (
        <div className="overflow-hidden rounded-xl border bg-card">
          <div className="flex min-h-14 flex-wrap items-center justify-between gap-2 border-b px-3 py-2 md:px-4">
            <div className="flex min-w-0 items-center gap-2">
              <span className="text-muted-foreground text-sm">{selected && !mobile ? "1 selected" : `${things.length} items`}</span>
              {selected && !mobile && <Button size="icon-sm" variant="ghost" aria-label="Clear selection" onClick={() => setSelection(undefined)}><XIcon /></Button>}
            </div>
            {selected && !mobile ? <div className="flex items-center gap-1" aria-label="Selected item actions">
              {selected.kind === "file" && <Button variant="ghost" size="sm" render={<a href={`/api/files/${selected.it.id}/content`} download={selected.it.name} />} aria-label={`Download ${selected.it.name}`}><DownloadIcon />Download</Button>}
              <Button variant="ghost" size="icon" aria-label={`Move ${selected.it.name}`} onClick={() => act(selected, "move")} disabled={working !== undefined}><FolderInputIcon /></Button>
              <Button variant="ghost" size="icon" aria-label={`Rename ${selected.it.name}`} onClick={() => act(selected, "rename")} disabled={working !== undefined}><PencilIcon /></Button>
              <Button variant="ghost" size="icon" aria-label={`Details for ${selected.it.name}`} onClick={() => act(selected, "details")}><InfoIcon /></Button>
              <Button variant="ghost" size="icon" aria-label={`Delete ${selected.it.name}`} onClick={() => act(selected, "trash")} disabled={working !== undefined}><Trash2Icon /></Button>
            </div> : <div className="flex items-center gap-1">
              <select name="file-sort" aria-label="Sort files" className="h-11 w-28 rounded-md bg-transparent px-2 py-1 text-base md:h-8 md:w-auto md:text-sm" value={sort.by} onChange={(event) => setSort({ by: event.target.value as typeof sort.by, descending: false })}>
                <option value="name">Name</option><option value="updated">Last modified</option><option value="size">Size</option>
              </select>
              <Button variant="ghost" size="icon" className="size-11 md:size-8" aria-label={sort.descending ? "Sort ascending" : "Sort descending"} onClick={() => setSort({ ...sort, descending: !sort.descending })}>{sort.descending ? <ArrowDownIcon /> : <ArrowUpIcon />}</Button>
            </div>}
          </div>
          <table className="w-full table-fixed text-sm" aria-label="Files and folders">
            <thead className="hidden md:table-header-group">
              <tr className="text-muted-foreground border-b text-left text-xs">
                <th scope="col" className="px-4 font-medium" aria-sort={sort.by === "name" ? sort.descending ? "descending" : "ascending" : "none"}>{sortHeading("name", "Name")}</th>
                <th scope="col" className="w-40 px-4 font-medium" aria-sort={sort.by === "updated" ? sort.descending ? "descending" : "ascending" : "none"}>{sortHeading("updated", "Last modified")}</th>
                <th scope="col" className="w-28 px-4 text-right font-medium" aria-sort={sort.by === "size" ? sort.descending ? "descending" : "ascending" : "none"}>{sortHeading("size", "Size")}</th>
                <th scope="col" className="w-14"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {ordered.map((thing) => <FileRow key={thing.it.id} thing={thing} selected={selected?.it.id === thing.it.id} busy={working !== undefined} mobile={mobile} size={size}
                onSelect={() => setSelection({ at: address, id: thing.it.id })}
                onOpen={() => void navigate(`/files/${thing.it.id}`)}
                onAction={(action) => act(thing, action)} />)}
            </tbody>
          </table>
          <div className="text-muted-foreground flex flex-wrap justify-between gap-2 px-4 py-3 text-xs">
            <span>{listing?.folders.length} folders · {listing?.files.length} files</span>
            <span className="hidden md:inline">Drop files here to upload</span>
          </div>
        </div>
      )}

      <Dialog open={focused !== undefined} onOpenChange={(open) => { if (!open && !working) setPanel(undefined); }}>
        <DialogContent showCloseButton={false}
          finalFocus={() => actionOrigin.current?.isConnected ? actionOrigin.current : picker.current?.parentElement?.querySelector<HTMLButtonElement>('button') ?? null}
          className="bottom-0 left-0 top-auto flex h-[85dvh] max-h-[92dvh] flex-col gap-0 md:h-auto max-w-none translate-x-0 translate-y-0 overflow-y-auto rounded-t-2xl rounded-b-none p-0 md:bottom-auto md:left-1/2 md:top-1/2 md:max-w-md md:-translate-x-1/2 md:-translate-y-1/2 md:rounded-xl">
          {focused && panel && <>
            <div className="sticky top-0 z-10 flex shrink-0 items-start gap-3 border-b bg-background p-5 pr-3">
              {panel.view !== "actions" && mobile && <Button variant="ghost" size="icon" className="size-11 shrink-0" aria-label="Back to file actions" disabled={working !== undefined} onClick={() => setPanel({ ...panel, view: "actions" })}><ArrowLeftIcon /></Button>}
              {(!mobile || panel.view === "actions") && <div className="mt-1"><FileGlyph thing={focused} /></div>}
              <DialogHeader className="min-w-0 flex-1 gap-1">
                <DialogTitle className="pr-2 text-base leading-snug wrap-anywhere">{focused.it.name}</DialogTitle>
                <DialogDescription>{focused.kind === "file" ? size(focused.it.size) : "Folder"} · {listing?.chain.at(-1)?.name ?? "All files"}</DialogDescription>
              </DialogHeader>
              <DialogClose render={<Button size="icon" variant="ghost" className="size-11 shrink-0 md:size-8" disabled={working !== undefined} aria-label="Close file actions" />}><XIcon /></DialogClose>
            </div>
            {panel.view === "rename" || panel.view === "move" ? <div className="flex-1 p-5 pb-[max(1.25rem,env(safe-area-inset-bottom))]"><ChangeForm key={`${focused.it.id}-${panel.view}`} thing={focused} mode={panel.view} working={working !== undefined} here={listing} refused={refused}
              onClose={() => setPanel(undefined)} onChange={(name, into) => void change(focused, name, into)} /></div>
              : panel.view === "details" ? <div className="space-y-6 p-5">
                <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-6 gap-y-4 text-sm">
                  <dt className="text-muted-foreground">Type</dt><dd className="wrap-anywhere">{focused.kind === "file" ? focused.it.media_type : "Folder"}</dd>
                  <dt className="text-muted-foreground">Created</dt><dd>{new Date(focused.it.created_at).toLocaleString()}</dd>
                  <dt className="text-muted-foreground">Modified</dt><dd>{new Date(focused.it.updated_at).toLocaleString()}</dd>
                </dl>
                {focused.kind === "file" && <Button className="min-h-11 w-full md:min-h-8" render={<a href={`/api/files/${focused.it.id}/content`} download={focused.it.name} />} aria-label={`Download ${focused.it.name}`}><DownloadIcon />Download</Button>}
              </div> : <div className="flex flex-col p-2 pb-[max(1.5rem,env(safe-area-inset-bottom))] md:pb-2">
                {focused.kind === "file" && <Button variant="ghost" className="h-14 justify-start gap-4 px-4 font-normal md:h-11" render={<a href={`/api/files/${focused.it.id}/content`} download={focused.it.name} />} aria-label={`Download ${focused.it.name}`}><DownloadIcon />Download</Button>}
                <div className="my-2 border-t" />
                <Button variant="ghost" className="h-14 justify-start gap-4 px-4 font-normal md:h-11" disabled={working !== undefined} onClick={() => setPanel({ ...panel, view: "move" })}><FolderInputIcon />Move to folder</Button>
                <Button variant="ghost" className="h-14 justify-start gap-4 px-4 font-normal md:h-11" disabled={working !== undefined} onClick={() => setPanel({ ...panel, view: "rename" })}><PencilIcon />Rename</Button>
                <Button variant="ghost" className="h-14 justify-start gap-4 px-4 font-normal md:h-11" disabled={working !== undefined} onClick={() => setPanel({ ...panel, view: "details" })}><InfoIcon />Details</Button>
                <div className="my-2 border-t" />
                <Button variant="ghost" className="text-destructive h-14 justify-start gap-4 px-4 font-normal md:h-11" aria-label={`Delete ${focused.it.name}`} disabled={working !== undefined} onClick={() => void discard(focused)}><Trash2Icon />Move to Trash</Button>
                <Refused>{refused}</Refused>
              </div>}
          </>}
        </DialogContent>
      </Dialog>

      {listing && (
        <footer className="text-muted-foreground flex flex-wrap items-start justify-between gap-3 text-xs">
          <span>{size(listing.used_bytes)} of {size(listing.max_total_bytes)} used · at most {size(listing.max_file_bytes)} per file</span>
          <details className="max-w-prose">
            <summary className="cursor-pointer">About file storage</summary>
            <p className="mt-2">Files live on this instance's disk. Deleting puts them in the Trash until its retention runs out. Download links keep working after renaming or moving a file.</p>
          </details>
        </footer>
      )}

      <MakeAFolder
        open={making}
        working={working === "folder"}
        refused={refused}
        onClose={() => setMaking(false)}
        onMake={(name) => void makeFolder(name)}
      />

    </main>
  );

  function whyItFailed(sentence: string) {
    // The switch can be thrown, and access revoked, while this screen is open.
    // The code is what tells the two apart; the status cannot, because
    // `disabled` and `conflict` share one.
    if (why.current === "forbidden") {
      return <Denied what="Files" />;
    }

    if (why.current === "disabled") {
      return <Disabled what="Files" />;
    }

    return <Failed why={sentence} again={again} />;
  }
}

/** The dialog behind "New folder". */
function MakeAFolder({
  open,
  working,
  refused,
  onClose,
  onMake,
}: {
  open: boolean;
  working: boolean;
  refused: string | undefined;
  onClose: () => void;
  onMake: (name: string) => void;
}) {
  const [name, setName] = useState("");

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setName("");
          onClose();
        }
      }}
    >
      <DialogContent>
        <form
          className="flex flex-col gap-4"
          onSubmit={(event) => {
            event.preventDefault();
            onMake(name.trim());
          }}
        >
          <DialogHeader>
            <DialogTitle>New folder</DialogTitle>
          </DialogHeader>

          <Field label="Name">
            <Input
              name="name"
              value={name}
              autoFocus
              onChange={(event) => setName(event.target.value)}
            />
          </Field>

          {/* Inside the dialog, because that is where the caller is looking and
              because a modal one hides the screen behind it from a screen
              reader — an alert out there would be an alert nobody is told. */}
          <Refused>{refused}</Refused>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={name.trim() === "" || working}>
              {working ? "Making it…" : "Make it"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * The dialog behind renaming and moving, which are one write because they are
 * one row (`docs/api.md`, Files).
 *
 * <b>Where it can go is what is on the screen</b>: the top of the tree, the
 * folders between here and it, and the folders in this one. A picker over the
 * whole tree would be a second navigation to build and to make work on a phone,
 * and moving something two branches away is two moves.
 */
function ChangeForm({
  thing,
  mode,
  working,
  here,
  refused,
  onClose,
  onChange,
}: {
  thing: Thing;
  mode: "rename" | "move";
  working: boolean;
  here: Listing | undefined;
  refused: string | undefined;
  onClose: () => void;
  onChange: (name: string, into: string | null) => void;
}) {
  const [name, setName] = useState(thing.it.name);
  const [into, setInto] = useState(folderOf(thing) ?? "");
  const where = useId();

  const chain = here?.chain ?? [];
  const inside = (here?.folders ?? []).filter((one) => one.id !== thing.it.id);

  return (
    <form
      className="flex flex-col gap-4"
      onSubmit={(event) => {
        event.preventDefault();
        onChange(name.trim(), into === "" ? null : into);
      }}
    >
      <DialogHeader>
        <h2 className="font-medium">{mode === "rename" ? "Rename" : "Move to folder"}</h2>
      </DialogHeader>

      {mode === "rename" && <Field label="Name" hint="Names in one folder are one each, whatever their capitals.">
        <Input
          className="h-11 md:h-8"
          name="name"
          value={name}
          autoFocus
          onChange={(event) => setName(event.target.value)}
        />
      </Field>}

      {mode === "move" && <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium" htmlFor={where}>
          Where
        </label>
        <select
          id={where}
          name="where"
          autoFocus
          className={`${selectClass} h-11 text-base md:h-8 md:text-sm`}
          value={into}
          onChange={(event) => setInto(event.target.value)}
        >
          <option value="">All files</option>
          {chain.map((step) => (
            <option key={step.id} value={step.id}>
              {step.name}
            </option>
          ))}
          {inside.map((one) => (
            <option key={one.id} value={one.id}>
              {chain.length === 0 ? one.name : `${chain[chain.length - 1]?.name} / ${one.name}`}
            </option>
          ))}
        </select>
      </div>}

      <Refused>{refused}</Refused>

      <div className="mt-2 flex justify-end gap-2">
        <Button className="min-h-11 min-w-24 md:min-h-8 md:min-w-0" type="button" variant="outline" disabled={working} onClick={onClose}>
          Cancel
        </Button>
        <Button className="min-h-11 min-w-24 md:min-h-8 md:min-w-0" type="submit" disabled={name.trim() === "" || working}>
          {working ? "Saving…" : "Save"}
        </Button>
      </div>
    </form>
  );
}

/** Where something is now: the folder it is in, or nothing at the top. */
function folderOf(thing: Thing): string | null {
  return thing.kind === "folder" ? thing.it.parent : thing.it.folder;
}

/**
 * What a refusal says on this screen. `stale` is the one a screen acts on
 * rather than prints: the listing is read again, and the sentence says so
 * instead of showing a version nobody held.
 */
function said_(error: unknown, status: number): string {
  const { message, code } = refusal(error, status);

  return code === "stale"
    ? "That changed while this folder was open. It is being read again."
    : message;
}

/** A size a person reads, in the units an operator's limits are set in. */
function size(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  const units = ["KiB", "MiB", "GiB", "TiB"];
  let value = bytes / 1024;
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[unit]}`;
}
