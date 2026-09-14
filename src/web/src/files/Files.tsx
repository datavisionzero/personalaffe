import {
  ChevronRightIcon,
  DownloadIcon,
  FolderIcon,
  FolderPlusIcon,
  FileIcon,
  PencilIcon,
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
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { refusal } from "@/session/useSession";
import { Field, Refused, selectClass } from "@/shared/Form";
import { useAsk } from "@/shared/ask";
import { Busy, Denied, Disabled, Empty, Failed } from "@/shell/States";

type Listing = Schemas["FilesResponse"];
type Folder = Schemas["FolderResponse"];
type StoredFile = Schemas["FileResponse"];

/** One thing in the listing, whichever kind it is. */
type Thing =
  | { kind: "folder"; it: Folder }
  | { kind: "file"; it: StoredFile };

/** What an upload in flight looks like on the screen. */
type Arriving = { name: string; refused?: string };

/**
 * Files: a folder you can walk, something to drop into, and a download that is
 * one click (`docs/api.md`, Files).
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
  const [changing, setChanging] = useState<Thing>();

  const picker = useRef<HTMLInputElement>(null);

  // Which refusal the last read made. Two of the five states are decided by the
  // code rather than the status, and this is where it is kept.
  const why = useRef<string>(undefined);

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

      setChanging(undefined);
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

  return (
    <main
      className="mx-auto flex w-full max-w-4xl flex-col gap-5 p-5 md:p-8"
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
      <header className="flex flex-col gap-1">
        <h1 className="text-xl font-semibold tracking-tight">Files</h1>
        <p className="text-muted-foreground text-sm text-balance">
          Your own storage, on this instance's disk. Deleting puts something in the Trash, where it
          stays until its retention runs out. A file's download link keeps working after you rename
          it or move it.
        </p>
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

      <div className="flex flex-wrap items-center gap-2">
        <Button type="button" onClick={() => picker.current?.click()}>
          <UploadIcon aria-hidden />
          Upload files
        </Button>
        <Button type="button" variant="outline" onClick={() => setMaking(true)}>
          <FolderPlusIcon aria-hidden />
          New folder
        </Button>
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
        {listing !== undefined && (
          <span className="text-muted-foreground ml-auto text-xs">
            {size(listing.used_bytes)} of {size(listing.max_total_bytes)} used · at most{" "}
            {size(listing.max_file_bytes)} per file
          </span>
        )}
      </div>

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

      {listing !== undefined && (listing.folders.length > 0 || listing.files.length > 0) && (
        <ul className="flex flex-col divide-y rounded-lg border">
          {listing.folders.map((it) => (
            <li key={it.id} className="flex flex-wrap items-center gap-2 p-2.5">
              <Button
                variant="ghost"
                size="sm"
                className="min-w-0 flex-1 justify-start gap-2"
                onClick={() => void navigate(`/files/${it.id}`)}
              >
                <FolderIcon aria-hidden className="shrink-0" />
                <span className="truncate">{it.name}</span>
              </Button>
              {actions({ kind: "folder", it })}
            </li>
          ))}

          {listing.files.map((it) => (
            <li key={it.id} className="flex flex-wrap items-center gap-2 p-2.5">
              <span className="flex min-w-0 flex-1 items-center gap-2 px-2 text-sm">
                <FileIcon aria-hidden className="text-muted-foreground shrink-0 size-4" />
                <span className="truncate">{it.name}</span>
                <span className="text-muted-foreground shrink-0 text-xs">{size(it.size)}</span>
              </span>
              <Button
                variant="outline"
                size="sm"
                // A plain link to the instance's own address: the browser
                // fetches it with the session it already has, and nothing on
                // this page holds a file the instance can store.
                render={<a href={`/api/files/${it.id}/content`} download={it.name} />}
                aria-label={`Download ${it.name}`}
              >
                <DownloadIcon aria-hidden />
                Download
              </Button>
              {actions({ kind: "file", it })}
            </li>
          ))}
        </ul>
      )}

      <MakeAFolder
        open={making}
        working={working === "folder"}
        refused={refused}
        onClose={() => setMaking(false)}
        onMake={(name) => void makeFolder(name)}
      />

      <ChangeIt
        thing={changing}
        working={changing !== undefined && working === changing.it.id}
        here={listing}
        refused={refused}
        onClose={() => setChanging(undefined)}
        onChange={(name, into) => changing !== undefined && void change(changing, name, into)}
      />
    </main>
  );

  function actions(thing: Thing) {
    return (
      <span className="flex items-center gap-1">
        <Button
          variant="ghost"
          size="sm"
          disabled={working === thing.it.id}
          aria-label={`Rename or move ${thing.it.name}`}
          onClick={() => setChanging(thing)}
        >
          <PencilIcon aria-hidden />
        </Button>
        <Button
          variant="ghost"
          size="sm"
          disabled={working === thing.it.id}
          aria-label={`Delete ${thing.it.name}`}
          onClick={() => void discard(thing)}
        >
          <Trash2Icon aria-hidden />
        </Button>
      </span>
    );
  }

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
function ChangeIt({
  thing,
  working,
  here,
  refused,
  onClose,
  onChange,
}: {
  thing: Thing | undefined;
  working: boolean;
  here: Listing | undefined;
  refused: string | undefined;
  onClose: () => void;
  onChange: (name: string, into: string | null) => void;
}) {
  return (
    <Dialog open={thing !== undefined} onOpenChange={(next) => !next && onClose()}>
      <DialogContent>
        {thing !== undefined && (
          // Keyed by what is being changed, so the form is a new one for each
          // and its fields start out as that thing's rather than as the last
          // one's. A dialog that filled itself in an effect would be a dialog
          // that renders once with somebody else's name in it.
          <ChangeForm
            key={thing.it.id}
            thing={thing}
            working={working}
            here={here}
            refused={refused}
            onClose={onClose}
            onChange={onChange}
          />
        )}
      </DialogContent>
    </Dialog>
  );
}

function ChangeForm({
  thing,
  working,
  here,
  refused,
  onClose,
  onChange,
}: {
  thing: Thing;
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
        <DialogTitle>Rename or move</DialogTitle>
      </DialogHeader>

      <Field label="Name" hint="Names in one folder are one each, whatever their capitals.">
        <Input
          name="name"
          value={name}
          autoFocus
          onChange={(event) => setName(event.target.value)}
        />
      </Field>

      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium" htmlFor={where}>
          Where
        </label>
        <select
          id={where}
          name="where"
          className={selectClass}
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
      </div>

      <Refused>{refused}</Refused>

      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button type="submit" disabled={name.trim() === "" || working}>
          {working ? "Saving…" : "Save"}
        </Button>
      </DialogFooter>
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
