# A file is its id, and its bytes go down before its row

PERSONAL-E6 is the second application in this workspace and the first one whose
state is not all in the database. Most of what it does is therefore *not* a
decision: it opens every act on `ReachingAnApplication`, it guards every write
with the version it was read at, it draws the five states of
`shell/States.tsx`, and it is reached the same way in a browser and from `pea`.
It is also the first module to inherit PERSONAL-E3's Trash rather than opt out
of it as the Scratchpad did, and it applies `Restoration` as that epic wrote it
rather than inventing a second set of rules for a tree.

What follows is the part that was decided here.

## The id is the address, and the name is a label

`GET /api/files/{id}/content` is where a file's bytes are, and the id is made at
the first upload and never changes. Renaming a file, moving it into another
folder, and replacing its bytes all leave that address answering.

This is the "authenticated stable file reference" `docs/mvp-plan.md` asks for,
and the alternative — an address built out of the path, `/files/Reisen/2026/
bahn.pdf` — loses three times. It breaks every link the moment the owner
organises anything, which is the one thing a file area is for. It makes a rename
a rewrite of every reference anybody ever wrote down. And it makes the name a
thing the instance has to parse, where the whole point of the design below is
that it never does.

`shared/links.ts` gains the `file:` scheme with nothing behind it yet:
`[the report](file:0199f0c4-…)` renders as that address. Knowledge writes those
in PERSONAL-E7, and **there is no second attachment store** — the epic's fourth
plan item, settled by there being nothing to settle.

## A name never reaches the filesystem

Bytes live at `files/ab/cd/<id>`, derived from the file's id and from nothing
else (`Domain/Files/StorageAddress.cs`). Two levels of hexadecimal fan-out,
because one directory holding every file an instance has ever stored gets slower
to list every year, and listing it is what the tidy-up does.

VISION §8 asks that file and path operations never escape the storage area.
**The way that promise is kept is not a filter over what the owner typed.** It
is an address the owner never touches: there is no input to `IFileBytes` that a
path could be smuggled through, because every one of its methods takes a `Guid`.
A unit test walks a thousand random ids and asserts the shape; `LocalFileBytes`
still checks that the resolved path is under the root, because that check costs
one comparison and is the difference between "no caller can do this" and "no
caller can do this as long as every future caller keeps to the rule".

What a name is, then, is only about what the owner can navigate: at most 255
bytes of UTF-8, never empty, never `.` or `..`, no separator, no control
characters. Colons, quotation marks and asterisks are names, because this
product writes no name to a disk and borrowing Windows's list would be borrowing
somebody else's limitation.

**Names in one folder are one each whatever their capitals**, and files and
folders share the namespace. One folder holding `Notes` and `notes` is a tree
nobody can navigate and two things nobody can tell apart in a sentence — and it
is what `Restoration` already assumed when it said what is in the way. The case
the owner typed is kept and shown; only the comparison ignores it.

A name already taken is `conflict` and **never a silent rename**. A product that
appends "(2)" has made a decision the owner would have made differently, and the
same call takes the name to put something back under, so nobody is stuck with
something they cannot get out of the Trash.

## The bytes go down before the row

Storing a file is two writes to two stores, and one of them can succeed while
the other fails. The order is: bytes to the volume, then the row in Postgres.

- **Crash between them, this way round:** bytes nobody points at. Disk, and the
  tidy-up takes it within the hour.
- **Crash between them, the other way round:** a row whose file is missing. The
  owner's file, gone, and a listing that offers a download that cannot work.

One of those costs disk and the other costs data, so the order is not a
preference. The consequence is that this epic owes a tidy-up, and
`TidyTheStorage` is it: a third sweep in the same hourly loop, after the purge,
removing unfinished uploads under `incoming/` and files under `files/` whose row
is gone — **both only once they have not been written to for an hour**, which is
what keeps an upload still arriving safe. What it does not recognise it leaves
alone: a file under the storage root whose name is not one this product writes
is an operator's own, and a sweep that removed what it did not recognise would
eventually remove something that mattered.

An upload lands in `incoming/` and is **moved** into place, so the move is what
makes "the bytes are there" a moment rather than an interval: an upload killed
while it was still arriving leaves a `.part`, never a short file at the address
a row is about to point at.

Removing works the same way round, for the same reason: every purge, every empty
and every permanent removal deletes the rows, commits, and then deletes the
files. A file whose bytes are already gone is not a failure — the sweep has to
be able to finish a job it half-finished before the instance was killed.

## The body of an upload is the file

`POST /api/files/content?name=…&folder=…`, with the bytes as the body and
`Content-Type` as the media type to store them under. No multipart envelope.

An agent that has bytes should be able to send bytes, `curl --data-binary
@thing` should work, and a CLI should not have to assemble a MIME document to
store a file. What multipart would buy is more than one file per request and
fields beside them, and this API wants neither: the name is a query parameter
because the body is taken, and `Content-Type` is a header that already means
what it would mean.

The endpoints read `HttpRequest.Body` by hand, so the document would otherwise
say they take nothing at all. `Http/FileBodies.cs` puts the shape back, because
**both clients are generated from the document**: without it the Go client would
take no body and the TypeScript one would take a string.

**The instance never guesses a media type from a name.** A `.png` holding a
script is a thing that exists, and the guess would be the instance's word for
what the bytes are — which is exactly the claim a download must not make. What
is stored is what the caller declared, with its parameters dropped, and anything
that is not a media type is stored as `application/octet-stream` rather than
refused: a caller who sent nothing usable has still sent bytes worth keeping.

## A download is always an attachment

`Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`,
whatever the stored media type says, behind the same door as everything else.

A stored HTML page served inline would be script running with the owner's
session on this instance's own origin. The MVP has no previews (VISION §11), so
nothing is lost by refusing to be a viewer — and VISION §8 is explicit that every
file retrieval requires authentication and that there are no public content or
sharing links, which is why the download is not the one endpoint outside the
door.

## Two limits, and two refusals

`PERSONALAFFE_MAX_FILE_MIB` at 64 and `PERSONALAFFE_MAX_STORAGE_MIB` at 5120 —
VISION §14.4 left both numbers to this epic. Mebibytes and not bytes, because
5 GiB spelled in bytes is a Compose value nobody can read back, and a whole
number of a human-sized unit is the shape `RetentionSettings` established for a
whole number of days.

Both are counted **against the bytes that actually arrive**, never against a
declared `Content-Length` — which a caller writes and nothing checks — and the
stream is cut off at whichever limit is nearer, so a body that claims nothing
cannot fill the volume.

They are two refusal codes and not one. `too-large` (413) means send something
smaller; `out-of-space` (507) means delete something. The script's branch is the
same and the owner's move is not, which is exactly what a code is for. `pea`
gives both exit 4 — a write the instance would not take — and `--json` tells
them apart.

**What is in the Trash counts towards the total.** Those bytes are still on the
volume and still the owner's to restore; a quota that ignored them would be one
an owner could walk past by deleting and uploading in turn, and then find they
could not restore what they had deleted. Emptying the Trash is what gives the
room back at once.

## The tree is a parent, not a path

A folder stores which folder it is in. Nothing stores a path.

A stored path would have to be rewritten for every descendant of a folder
somebody renamed — the one operation this product promises is cheap — and the
first time that rewrite half-failed the tree would disagree with itself. Moving
a folder is therefore one row, and what is in it moves without being touched.

Two consequences follow, and both are `conflict` rather than `validation`,
because the request is well formed and it is the tree's current shape that
refuses it:

- **A folder cannot be put inside itself**, or inside anything already in it.
  That is the one thing a tree can be asked to do that would leave part of it
  unreachable from the root and every walk of it endless.
- **The tree is 32 folders deep.** Deeper than anybody organises, shallow enough
  that every walk — a restore working out its chain, a listing working out a
  breadcrumb, `pea` resolving a path — is bounded by something other than hope.

There is **no unique index on `(parent_id, name)`**, and no foreign key on the
parent. The rule is that two *live* things in one folder cannot share a name,
and a deleted one keeps its name until somebody restores it; an index that
cannot say "live" would refuse a deletion the owner is entitled to undo. The
check is in the acts, where "live" is a word that means something.

## A Trash entry is a deletion, not a row

Deleting a folder sets its whole subtree aside under **one moment**, and what
the owner sees in the Trash is the folder: one thing they deleted, one thing
they bring back. Something further down that they had deleted separately keeps
its own moment, its own expiry and its own entry — it does not come back when
its folder does, and removing the folder for good does not destroy it.

That rule was written in PERSONAL-E3 against a proving ground nobody could
reach. Applying it to a real tree changed nothing about it, which is the result
that epic was hoping for.

## A path is the CLI's convenience and never the API's

`pea files ls /Reisen/2026` walks the path a segment at a time against the
listing, and the wire carries ids. The API has no concept of a path, nothing
server-side keeps a second name for anything, and there is no address to keep in
step with a rename.

An id is accepted wherever a path is — it is what every verb prints — and it is
tried first, so a folder somebody called `0199f0c4-…` shadows nothing. Capitals
do not matter, because the instance's own rule is that they do not.

`pea files get ID --out -` and `pea files put --file -` are the two halves of
one round trip, and `get` without `--out` refuses rather than overwrite
something already in the working directory: a download that silently replaced a
file would be the one destructive thing a read can do.

## What the screen decided

**Deleting asks nothing**, which is the exact opposite of the Scratchpad's
dialog and the reason that one is a dialog
([ADR 0005](./0005-the-scratchpad-keeps-nothing-and-its-clock-runs-from-the-last-change.md)).
Nothing is destroyed here, so the screen says where it went instead of asking
whether to send it there.

**The folder is the address.** `/files/<id>` is real, so a link into a folder
opens that folder — VISION §6's "navigation belongs to the selected
application", applied to the one application that is a tree.

**A download is a link and never a blob.** The browser fetches
`/api/files/{id}/content` with the session it already has, so nothing on the
page ever holds a file the instance was willing to store.

**Where something can be moved is what is on the screen**: the top of the tree,
the folders between here and it, and the folders in this one. A picker over the
whole tree would be a second navigation to build and to make work on a phone,
and moving something two branches away is two moves.

## What PERSONAL-E7 and PERSONAL-E10 will read

- **Knowledge (PERSONAL-E7)** links to a file with `file:<id>` and gets the
  download address. Nothing else is needed and no attachment store is coming.
- **Operations (PERSONAL-E10)** inherits the sentence this epic put in
  `docs/operations.md`: a file is a row in one volume and bytes in the other,
  and neither on its own is the file. A database restored without its volume is
  a listing of downloads that fail; a volume restored without its database is
  bytes under names nobody can read. `incoming/` need not be backed up.
