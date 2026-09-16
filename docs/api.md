# The HTTP surface

One instance, one address, one API. The web application and `pea` are both
clients of it and there is no second way in
([`docs/codebase.md`](./codebase.md)).

**There is a door now.** Five operations are outside it — the version, the two
health checks and the two setup operations — and everything else needs a
credential. What the door takes and what it answers is *The door* below; the
conventions, the error document and the codes were settled in PERSONAL-3, before
there was a second endpoint to settle them differently. Sections that describe
something not yet implemented say so in their first line.

## The contract is the artifact

[`docs/api/openapi.json`](./api/openapi.json) is checked in. It is captured from
a running instance rather than written by hand, and a test compares the two.
Both clients are generated from it and **neither generated output is
committed**: the document is the artifact, its output is not.

```sh
# rewrite the checked-in document from a running instance, and pass
PERSONALAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests

# fail if the instance serves anything else
dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests
```

**A change to an endpoint is a change to the document, in the same commit.**
Changing a response shape without recapturing turns `ContractTests` red on the
desk and, if it is pushed, the trunk.

The instance serves the same document at `/api/openapi/v1.json`, without a
credential, so that a client can compile against an installation it has not
been let into yet.

### Generating the clients

Both read the checked-in document. Both generators are pinned, and running
either twice produces the same bytes.

| Client | Generator | Command | Output |
| --- | --- | --- | --- |
| Web | `openapi-typescript` 7.13.0 | `cd src/web && npm run generate` | `src/web/src/api/schema.d.ts` |
| CLI | `oapi-codegen` 2.8.0 | `cd src/cli && go generate ./...` | `src/cli/internal/api/client.gen.go` |

*The two workspaces arrive with PERSONAL-4 and PERSONAL-5; the commands above
are what they wire in. Until then the document is checked against both
generators by hand, and both were run against it when it was first captured.*

## Conventions

- **Every endpoint is under `/api`.** Everything outside it is the web
  application's, which is what keeps the two from fighting over `/files`,
  `/tasks` and `/pages`.
- **JSON in, JSON out**, `application/json`, UTF-8.
- **Fields are `snake_case`.** `updated_at`, not `updatedAt`.
- **A closed set travels as its word**, never as a number: `read_write`, not
  `2`. A value outside the set is refused at the door as `validation` rather
  than stored as a row nobody can read.
- **An object takes the fields it defines and no others.** A field it does not
  define is `unknown-field` rather than a value quietly dropped: an agent
  writing `passwrd` has no screen to notice the omission on. The one shape that
  is deliberately open is the problem document, which carries what its code
  needs.
- **Timestamps are RFC 3339 in UTC with microseconds** —
  `2026-09-13T14:03:07.123456Z` — one spelling everywhere, so that the value a
  client reads is the value it can send back.
- **A write says which version it replaces.** It sends back the `ETag` the read
  it is based on answered, in `If-Match`, and a write holding an older one is
  refused as `stale`. See [The guarded write](#the-guarded-write).
- **Every response carries `Personalaffe-Version`**, the refused and the failed
  ones included: a 401 is an answer, and a client reporting version skew has to
  be able to read it off whatever it got. The API itself carries no version in
  its address; migrations only run forward and an instance serves one shape.

## Errors

Every refusal is one document, `application/problem+json` (RFC 9457):

```json
{
  "type": "/problems/validation",
  "title": "A field is missing, malformed or over its limit",
  "status": 400,
  "detail": "title: A title is at most 200 characters.",
  "instance": "/api/knowledge/pages",
  "errors": { "title": ["A title is at most 200 characters."] }
}
```

**`type` is relative and stable, and its last segment is the code a client
switches on.** `/problems/not-found` is `not-found`. Switching on the status
instead would collapse distinctions the product makes — `deleted` and
`not-found` are both 404 once recoverable deletion lands.

A document may carry more than the five members of RFC 9457. What it carries
depends on the code: `validation` carries `errors`, a field to its messages,
`unknown-field` carries `field`, and the two storage refusals carry the limit
they are about. A client that does not know an extension member ignores it.

**A bug is not a refusal.** Anything that is not a deliberate refusal answers
`/problems/internal` with a title, a status and nothing else — no message, no
exception type, no frame of a stack. What the operator needs is in the
instance's log.

### The codes

| Code | Status | What it means |
| --- | --- | --- |
| `validation` | 400 | A field is missing, malformed or over its limit. Carries `errors`. |
| `unknown-field` | 400 | The request names a field the object does not define. |
| `unauthenticated` | 401 | No credential, an unknown one, or a revoked one. |
| `second-factor` | 401 | The password was right and the authenticator's code is wanted as well. |
| `forbidden` | 403 | The caller may not do this. |
| `not-found` | 404 | Nothing at that address. |
| `deleted` | 404 | What was at that address is in the Trash. Carries `deleted_at` and `expires_at`. |
| `disabled` | 409 | The application this belongs to is switched off. Carries `application`. |
| `stale` | 412 | The object has changed since it was read. |
| `conflict` | 409 | Something else already occupies that name or place. |
| `too-large` | 413 | What was sent is over a limit this instance sets on one thing. Carries `limit_bytes`. |
| `out-of-space` | 507 | This instance has no room left. Carries `limit_bytes` and `used_bytes`. |
| `internal` | 500 | Something went wrong on the server. |

The set is [`RefusalCode`](../src/Personalaffe.Domain/RefusalCode.cs) and it
grows with the epics that need it. **Each addition is a row in this table in the
same commit.**

**Switching on the status would collapse the distinction `deleted` exists to
make.** Both it and `not-found` are 404; one of them means the owner can have
the thing back.

`too-large` and `out-of-space` are two codes and not one for the same reason:
both are an upload the instance would not take, and the caller's move is to send
something smaller in one case and to delete something in the other.

### Exit codes

`pea` derives its exit code from the status and the code, so that a script
branches without parsing anything. The table lives in
[`docs/cli.md`](./cli.md) with the CLI that implements it (PERSONAL-5).

## The guarded write

Two people change one thing. The owner has a page open in a browser; an agent
edits the same page over the API. One of those writes was made without knowing
about the other, and the product's answer is that it is **refused**, not that it
silently wins.

**A read of one object answers `ETag`.** The value is that object's `updated_at`
in the one timestamp spelling above, as a strong entity tag:

```http
GET /api/trash
...
ETag: "2026-09-14T08:30:00.123456Z"
```

**A write sends it back in `If-Match`**, exactly as it was answered, quotation
marks included:

```http
POST /api/trash/knowledge/0199.../restore
If-Match: "2026-09-14T08:30:00.123456Z"
```

If the object has changed since, the write is refused `412 stale` and changes
nothing. The document carries the object's current `updated_at`, so a client can
tell "somebody got there first" from "I sent something malformed" without asking
again:

```json
{ "type": "/problems/stale",
  "title": "The object has changed since it was read",
  "status": 412,
  "detail": "The page has changed since it was read. Read it again: …",
  "updated_at": "2026-09-14T08:31:12.004000Z" }
```

**A guarded write with no `If-Match` is refused the same way**, and so is `*`, a
weak tag, more than one tag, and anything that is not a timestamp this instance
wrote. One code for all of them, because the caller's move is the same in every
case: read the object again and decide. An endpoint where forgetting the guard
were cheaper than using it is an endpoint where it will be forgotten.

The value is `updated_at` and not a version counter because every object carries
it already and every client already reads it; a counter beside it would be a
second thing that has to agree with the first. The transport is a header and not
a field in the body because an upload's body is the file ([Files](#files)), and
a guard that only half the writes in the product can use is not a guard.

Both clients do this for the caller. `pea` keeps the tag from the read it made
and sends it on the write that follows ([`docs/cli.md`](./cli.md)); the web
application's client does the same.

## Deleting sets content aside

Deleting a knowledge page, a task, a task list, a file or a folder does not
destroy it. The row stays, leaves every ordinary read, and can be restored until
its retention runs out — [the Trash](#the-trash) of `CONTEXT.md`. Its address
answers `404 deleted` rather than `404 not-found` while it is there:

```json
{ "type": "/problems/deleted",
  "title": "What was at that address is in the Trash",
  "status": 404,
  "detail": "The page was deleted by the agent access `the laptop agent` and is in the Trash. …",
  "deleted_at": "2026-09-12T19:02:11.881000Z",
  "expires_at": "2026-10-12T19:02:11.881000Z" }
```

**A Scratchpad entry is the exception and is destroyed immediately.** It is
temporary by definition (`CONTEXT.md`), an owner who deletes one means it, and a
Trash full of the text somebody pasted between two devices is not a service to
anybody. [The Scratchpad](#the-scratchpad) is where that application says so in
full.

Deletion is a write in both senses the product has: it needs read/write access
to the application, and it carries `If-Match` like any other write, so nothing
can delete a version it never read.

**Who deleted something travels with it as a copy** — the kind, the id, and the
name the access had at the time — and nothing links back to the agent access
row. The question the owner is asking of that list is which of their agents did
this, and a name that disappears when the access is revoked is no answer.

## The Scratchpad

Temporary plain text, put down in seconds on one device and read on another
(`CONTEXT.md`, Scratchpad entry). It is the first of the four applications, and
the only one whose deletion is final.

```json
{ "items": [
    { "id": "0199f0c4-…",
      "text": "the wifi password is hunter2",
      "pinned": false,
      "created_at": "2026-09-14T08:30:00.123456Z",
      "updated_at": "2026-09-14T08:30:00.123456Z",
      "expires_at": "2026-09-21T08:30:00.123456Z" } ],
  "has_more": false }
```

**Plain text, and nothing around it.** No title, no tags, no Markdown, no
folder, and no conversion into a knowledge page or a task. An entry is at most
**64 KiB of UTF-8**; text that is empty or only whitespace is `validation`, and
so is text over the limit, with the number in the message.

**One trailing newline is dropped and never two.** A trailing newline is how a
shell ends a line and not something the person typed; a second one is their
blank line and stays. Everything else survives byte for byte — characters
outside ASCII, emoji, tabs, and the newlines in the middle.

**`expires_at` is when the instance's own sweep will destroy the entry, and it
is the only warning there is.** Nothing is set aside, nothing asks first, and
`deleted` is a code this application never answers. It is `null` while the entry
is pinned.

**The period counts from `updated_at`, not from `created_at`** — seven days by
default, and `PERSONALAFFE_SCRATCHPAD_RETENTION` for an operator who wants
another number ([`docs/operations.md`](./operations.md)). An entry edited this
morning does not disappear tonight because it was pasted a week ago, and an
entry unpinned after a year gets a full period from the moment it was unpinned
rather than going on the next sweep. Retention does not stop for anything: not
for the Scratchpad being switched off, and not for the instance being down.

**Pinning is how an entry is kept**, and it is a field on the entry rather than
an address of its own: `PUT` carries the text and the pin together, because a
pin is a change to the entry and a second address carrying one boolean would be
a second place the guard has to be got right.

**Deleting destroys the row.** A second delete is `404 not-found`, the entry is
never in the Trash, and there is no way back. Write access is enough and an
agent has it: `read_write` has always included deletion, which for the
Scratchpad is permanent and for lasting content is into the Trash
([What an agent may do](#what-an-agent-may-do)).

## Files

The owner's own storage: files with names, in folders, on this instance's
volume (`CONTEXT.md`, File and Folder). It is the second application, and the
first one that puts anything in the Trash.

```json
{ "chain": [ { "id": "0199f0c4-…", "name": "Reisen", "parent": null,
               "created_at": "2026-09-14T08:30:00.123456Z",
               "updated_at": "2026-09-14T08:30:00.123456Z" } ],
  "folders": [],
  "files": [
    { "id": "0199f0c5-…",
      "name": "Reisekosten 2026.pdf",
      "folder": "0199f0c4-…",
      "size": 284119,
      "media_type": "application/pdf",
      "created_at": "2026-09-14T08:31:00.000000Z",
      "updated_at": "2026-09-14T08:31:00.000000Z" } ],
  "used_bytes": 284119,
  "max_file_bytes": 67108864,
  "max_total_bytes": 5368709120 }
```

**A file's id is its address, and its name is a label.** `GET
/api/files/{id}/content` answers the bytes and goes on answering them after the
file has been renamed and moved into another folder — which is what makes it
the reference Knowledge links to with `file:<id>`, and why there is no second
attachment store. The bytes live at a path derived from that id and never from the name,
so escaping the storage area is not something a request can ask for.

**The body of an upload is the file.** `POST /api/files/content?name=…&folder=…`
takes the bytes as the body and `Content-Type` as the media type to store them
under; there is no multipart envelope, because an agent that has bytes should be
able to send bytes. Anything that is not a media type is stored as
`application/octet-stream`; the instance never guesses one from the name.

**A download is always an attachment.** `Content-Disposition: attachment` and
`X-Content-Type-Options: nosniff`, whatever the stored media type says. A stored
HTML page served as a document of this instance's own origin would be script
running with the owner's session, and the MVP has no previews to lose by it
(VISION §11).

**A name is one name and not a path.** At most **255 bytes of UTF-8**, never
empty, never `.` or `..`, no `/`, no `\`, no control characters — and **names in
one folder are one each whatever their capitals**. A name already taken is
`conflict` and never a silent rename: two things with one name in one folder is
a tree nobody can navigate, and a product that quietly appends "(2)" has made a
decision the owner would have made differently. Files and folders share the one
namespace.

**The tree is 32 folders deep and a folder cannot be put inside itself.** Both
are `conflict`: the request is well formed and it is the tree's current shape
that refuses it. Moving a folder moves everything in it without changing a row
of it — the children point at their parent, and nothing stores a path.

**Two limits, and two refusals.** One file is at most
`PERSONALAFFE_MAX_FILE_MIB` (64 MiB by default) and the application at most
`PERSONALAFFE_MAX_STORAGE_MIB` (5 GiB) ([`docs/operations.md`](./operations.md)).
Over the first is `too-large`; with no room for the second is `out-of-space`.
Both are counted against the bytes that actually arrive and never against a
declared `Content-Length`, and the stream is cut off at whichever limit is
nearer. What is in the Trash counts towards the total: those bytes are still on
the volume and still the owner's to restore.

**Deleting sets a file aside**, unlike the Scratchpad's. Deleting a folder takes
everything in it under one moment, so the whole thing comes back together; a
child the owner deleted separately keeps its own expiry and does not
([Putting something back into a tree](#putting-something-back-into-a-tree)).

## Knowledge

The owner's lasting notes: Markdown pages in a tree, each with a history behind
it (`CONTEXT.md`, Knowledge page). It is the third application, and the first
one that keeps any history at all.

```json
{ "id": "0199f0c6-…",
  "title": "Die Architektur",
  "parent": "0199f0c5-…",
  "markdown": "# Die Architektur\n\nDas Wichtigste zuerst.\n",
  "created_at": "2026-09-14T08:30:00.123456Z",
  "updated_at": "2026-09-14T09:02:11.000000Z" }
```

**A page's id is its identity, and its title is a label.** `GET
/api/knowledge/pages/{id}` goes on answering through every rename, every move,
every rewrite and every recovery — which is the whole of what "stable links"
means, and the same decision Files made about a file's bytes
([ADR 0006](adr/0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md)).

**`GET /api/knowledge/pages` is the tree and carries no bodies.** Every page's
title and place, flat, each saying which page it is under; a client draws the
hierarchy in one pass. A knowledge base is navigated far more often than any one
page is read, and a listing that carried every page's Markdown would get slower
the more the owner writes.

**A title is at most 200 characters, one line, and carries no `/` or `\`.**
Nothing server-side parses one, but two things outside do: `pea knowledge`
addresses a page by a path of titles, and the export writes one file per page.
**Titles among siblings are one each whatever their capitals**, and a taken one
is `conflict` and never a silent rename.

**The tree is 8 pages deep** — deliberately shallower than Files' 32. A file
tree mirrors however somebody already filed things; a knowledge base is
something they are building in order to find things in, and eight levels of it
is a base where nothing is findable. Past it, and a page put under itself, are
both `conflict`.

**One `PUT` carries the title, the place and the Markdown**, because all three
are the same row. A write that changes something keeps what it replaced; a write
that asks for what is already stored is still guarded, still checked, and leaves
no revision — a history of moments when nothing happened is a history nobody can
read.

**Recovering writes forward.** Putting an old version back leaves a revision of
what was current until then, so history only grows: "undo" is the one operation
that must never destroy work. It is a guarded write on **the page** and not on
the revision — a revision never changes and has no version worth holding — and
it puts back the title as well as the body, leaving the page where it is.

**A page keeps 50 versions.** A count and not an age: a page edited twice a year
deserves its history as much as one edited twice a day.

**Deleting a page takes everything under it and all of its history**, under one
moment, so the whole thing is one Trash entry and comes back together. A
revision that outlived its page would be content the owner believes they
deleted.

**`GET /api/knowledge/export` is a zip of Markdown files.** One `.md` per page
at the path its titles make, each opening with YAML front matter carrying the
id, the parent's id, the real title and the timestamps, plus a `knowledge.json`
saying the same thing in one place. The test of "an open package" is what
somebody can do with it having never heard of this product: unzip it and read
it.

## Tasks

Personal commitments in named lists, in an order the owner sets
(`CONTEXT.md`, Task list and Task). It is the fourth application, and the last
one VISION.md names.

```json
{ "id": "0199f0c7-…",
  "list": "0199f0c6-…",
  "title": "Milch holen",
  "description": "am Markt, nicht im Supermarkt",
  "due_on": "2026-09-14",
  "completed": false,
  "completed_at": null,
  "after": "0199f0c7-…",
  "created_at": "2026-09-14T08:30:00.123456Z",
  "updated_at": "2026-09-14T08:30:00.123456Z" }
```

**`due_on` is a date and never a moment.** `2026-09-14`, and no hour for a
timezone to move: a task due on the fourteenth is due on the fourteenth wherever
the owner is standing. It is the one value in this product that is deliberately
not an instant, and it is why "due dates appear consistently … without
unintended timezone shifts" is a sentence this application can keep.

**`completed_at` is when, and `completed` is whether.** Ticking a box that is
already ticked does not move the moment — otherwise "what did I finish this
week" would answer with whatever somebody last touched.

**One `PUT` carries everything a task is**: the title, the description, the due
date, the list, whether it is done, and where it sits. A task has more fields
than anything else in this workspace and is exactly where a second address per
field would start to look reasonable — `POST …/complete`, `PUT …/due`,
`POST …/move` — and each would be another place the guard has to be got right,
for a change the owner made once.

**Where it sits is `after`: the task it goes behind, or `null` for the top of
its list.** A neighbour and not a number, because a number is the module's
arithmetic and a neighbour is what a caller can act on. A caller that is not
moving anything sends the neighbour it already has, which both clients do
because they read first. `after` naming a task in another list is `validation`.

**Moving one task changes one row.** A position is a number with room on either
side of it and a move is the midpoint of the two it lands between, so nobody
else's version goes stale — which an order of 1 to n, renumbered on every move,
could not say. About fifty moves into the same gap exhaust the midpoints, and
the list is renumbered then: the one case where a move is a change to more than
one row.

**A new task goes at the end**, because capture is what happens when something
occurs to somebody and the order is what they decide afterwards.

**Open and completed come back in one order.** Separating them is what a client
draws (VISION §6.4); doing it here would mean a caller could not put a task back
where it was after reopening it.

**A list is not in another list.** VISION §6.4 asks for named lists and rules
out project planning; a hierarchy would be the first step towards what it rules
out. So Tasks is the one application with no tree, and the only one whose
restore has no ancestors to think about. **List names are one each whatever
their capitals.** A title is at most 200 characters and one line; a description
is Markdown, optional, at most 64 KiB of UTF-8.

**Deleting a list takes every task in it**, under one moment, so the whole thing
is one Trash entry and comes back together. A task restored on its own comes
back **at the end of its list** — where it used to sit is a number the list may
have reused, and the end is the one place that is always free.

## The applications

The workspace is four applications and each can be switched off
(`CONTEXT.md`, Application). `GET /api/applications` is what a client draws its
navigation from: all four, whether each is on, and what the caller may do in it.

```json
{ "items": [
    { "application": "scratchpad",
      "enabled": true,
      "permission": "read_write",
      "updated_at": "2026-09-14T15:30:08.000000Z" } ] }
```

All four, whoever asks. The set is closed and this document names it, so
leaving out the ones an agent cannot reach would hide nothing — and `permission`
beside each is what tells an agent why an operation was refused without a second
request.

`PUT /api/applications/{application}` switches one, with `{ "enabled": false }`
and the `updated_at` it was read at in `If-Match`. **The owner alone**: an agent
that could switch an application off could hide the owner's content from the
owner's own screens. It answers the application in its new state, version
included, so switching twice in a row needs no read in between. Switching one to
the state it is already in is not a write and does not move `updated_at` — the
guard is still checked.

**Switched off hides an application; it removes nothing.** Every operation
inside one is refused `409 disabled`, it is left out of aggregate views like the
Trash, and it is absent from the web application's navigation. What was written
stays written and the Trash keeps what was deleted — *and the retention sweep
goes on running*. A deadline that stopped while an application was off would be
a way to keep expired content for ever, and content that vanished on switching
one back on would be the same surprise from the other side.

**Access is decided before the switch.** A caller who may not reach an
application is refused `403 forbidden` whether it is switched on or off, so the
refusal says nothing about how the owner has configured their workspace.

## The search

One question over the four applications, at `GET /api/search?q=…`. Knowledge
pages, tasks, Scratchpad text and file names (VISION §6.1) — and never what is
inside a file, which is the line between one search over a workspace and a
document search over a disk.

```json
{ "query": "arch dec",
  "items": [
    { "application": "knowledge",
      "id": "0199f0c4-…",
      "title": "Architecture decisions",
      "snippet": "Where the storage decision lives and",
      "within": null,
      "updated_at": "2026-09-14T08:30:00.123456Z",
      "rank": 0.6079271 } ],
  "has_more": false }
```

**Every word is a beginning, and all of them have to be found.** `arch dec`
finds "Architecture decisions"; it does not find a page that says only
"architecture". Somebody searching their own workspace is remembering rather
than querying, and a field that answers while they are still typing has to
narrow as they type. Single letters are dropped, at most eight words are used,
and at most 200 characters are accepted.

**There is no query language.** No `AND`, no quoted phrase, no `-word`. A stray
`"` or `&` does nothing at all: the text is broken into letters and digits
before it reaches a statement, which is also why nothing a caller types can be
an operator.

**`application` narrows it and nothing refuses.** This is an aggregate view like
the Trash: an application this caller cannot read, or one the owner has switched
off, contributes nothing and produces no refusal. What is in the Trash is not
found either — a search is an ordinary read.

**`snippet` is text and never markup.** Nothing marks the matched words up, so
nothing between here and a browser has to decide whether a snippet is safe to
render; a client that wants them marked has the words it asked with. It is
`null` where there was no body to quote — a file name, or a match in a title on
a page with nothing in it.

**`within` is what the thing sits in**: a page's parent, a task's list, a file's
folder. It is what lets a client open the screen the thing is on rather than
the thing alone. `null` for a Scratchpad entry, which sits in nothing.

**`rank` orders the whole list, and what a thing is called outranks what it
mentions.** The index carries the weights and Postgres reads them; nothing in
this product reorders anything afterwards, and no application is preferred for
being one.

**`limit` is 1 to 100, 20 by default**, with `has_more` beside it. There is no
cursor, for the reason the Trash gives: it is one person's workspace and a
search that needs a second page needs a better word.

## The dashboard

What is useful or pending right now, at `GET /api/dashboard` — one request,
because it is one screen (VISION §6.1).

```json
{ "tiles": [
    { "tile": "tasks", "shown": true, "offered": true,
      "updated_at": "2026-01-01T00:00:00.000000Z" } ],
  "tasks": [
    { "id": "0199f0c7-…", "title": "Milch holen", "list_id": "0199f0c6-…",
      "list": "Einkauf", "due_on": "2026-09-14",
      "updated_at": "2026-09-14T08:30:00.123456Z" } ],
  "knowledge": [],
  "scratchpad": null,
  "files": null }
```

**Five tiles in a fixed order**: `tasks`, `knowledge`, `scratchpad`, `files`,
`weather`. VISION §6.1 asks for a predefined dashboard whose tiles can be shown
or hidden individually and says free arrangement comes later, so this is a
closed set and not a layout somebody builds.

**`shown` is the owner's preference and `offered` is whether it can be drawn at
all** — its application switched on, and readable by this caller. They are two
answers because hiding has to outlive a switched-off application: switching
Tasks back on brings the tile back as it was rather than as the default.

**A section is absent where its tile is not drawn, and empty where it is drawn
and holds nothing.** `null` against `[]` — different answers, and a screen draws
them differently: nothing at all, against "you have no open tasks".

**Each drawn tile carries at most five rows**, which is what "an entry point,
not a reporting system" means in a number. Open tasks come soonest due first and
the undated after them; pages, entries and files come most recently changed
first. What is in the Trash is not on the home page, and neither is anything in
an application this caller cannot read — a tile nobody may see is never asked
for rather than asked for and thrown away.

`PUT /api/dashboard/tiles/{tile}` takes `{ "shown": false }` and the tile's
`updated_at` in `If-Match`. **The owner alone**: an agent has no home page.
A tile that is not offered can still be shown and hidden.

## The weather

The one thing in this product that comes from outside it, at
`GET /api/weather`. It has its own address and is deliberately **not** part of
the dashboard: a home page that could not finish drawing until a provider on the
other side of the internet had answered or timed out is exactly what the weather
must never cost.

```json
{ "place": "Wuppertal",
  "latitude": 51.2563,
  "longitude": 7.1482,
  "units": "metric",
  "temperature_unit": "°C",
  "wind_unit": "km/h",
  "available": true,
  "reading": {
    "temperature": 16.1, "feels_like": 16.5, "high": 20.3, "low": 14.2,
    "wind": 2.2, "code": 3, "description": "Overcast", "day": true,
    "read_at": "2026-09-16T07:15:00.000000Z" },
  "attribution": "Weather data by Open-Meteo.com",
  "updated_at": "2026-09-15T18:02:11.000000Z" }
```

**Nothing here refuses because of the weather.** No place set, this instance not
asking anybody, or a provider that did not answer: all three are `reading: null`
beside fields that say which, and none of them is an error. A 503 would be this
instance claiming somebody else's outage as its own.

**`read_at` is when this instance asked**, not when the observation was made,
so a client can say how old the number it is showing is. Readings are held for
as long as the operator's freshness allows
([`docs/operations.md`](./operations.md)), so a home page refreshing every
fifteen seconds troubles a free provider four times an hour.

**`attribution` is what a free provider is paid in.** Show it beside the number.

**`available` is the operator's switch.** `PERSONALAFFE_WEATHER=off` and nothing
in this product opens a socket to anywhere.

`PUT /api/weather/place` takes `{ "name": "…", "latitude": …, "longitude": …,
"units": "metric" }` with `If-Match`, and all of them empty clears it. **The
owner alone**, because it decides what this instance tells an outside service
about the person who owns it — and so is `GET /api/weather/places?q=Wuppertal`,
which is the geocoder that turns a name into the two numbers, asked once in
Settings and never again by a tile.

## The Trash

One list over the four applications, at `GET /api/trash`. There is no table
under it: `deleted_at` stays in each module's own table and the Trash asks each
of them, because a central index would be a second place that has to agree with
the first, and the generic content entity personalaffe deliberately does not
have ([`docs/codebase.md`](./codebase.md)).

```json
{ "items": [
    { "application": "knowledge",
      "id": "0199f0c4-…",
      "name": "architecture",
      "where": "/notes",
      "deleted_at": "2026-09-12T19:02:11.881000Z",
      "deleted_by": { "kind": "agent", "name": "the laptop agent" },
      "expires_at": "2026-10-12T19:02:11.881000Z",
      "updated_at": "2026-09-12T19:02:11.881000Z" } ],
  "has_more": false }
```

`updated_at` on an entry is what a restore or a permanent removal sends back in
`If-Match`: a list cannot answer an `ETag` per item, so the version travels in
the item.

**There is no cursor.** A limit and `has_more` are what keep a runaway from
becoming an unbounded response; a personal Trash holds one person's deletions
over one retention period, and four cursors and a tie-break rule would be a lot
of machinery for a list that fits on a screen.

**Who may do what, and why:**

| Act | Who |
| --- | --- |
| Read the list | Anyone, filtered to the applications they may read |
| Restore an entry | Read/write access to the application it is in |
| Remove one entry for good | **The owner alone** |
| Empty the Trash | **The owner alone** |

An agent that could permanently remove one entry could bypass the Trash in two
steps instead of one, and what the Trash is for is that an agent acting on the
owner's behalf cannot destroy the owner's content. `pea` therefore has no verb
that destroys anything, the same way it has none that issues a credential
([`docs/cli.md`](./cli.md)).

**Entries leave by themselves.** `expires_at` is when the instance's own sweep
removes one for good — thirty days after the deletion by default, and
`PERSONALAFFE_TRASH_RETENTION` for an operator who wants another number
([`docs/operations.md`](./operations.md)). Retention does not stop for anything:
not for an application being switched off, and not for the instance being down.

**Three of the four applications fill it**: a deleted file, folder, page, list
or task is here — with its bytes, or with its history, or with the tasks that
were in it — until it is restored or its retention runs out. The Scratchpad
deliberately contributes nothing, because what it deletes is destroyed. A module
joins the Trash by contributing to it and by nothing else, and the Scratchpad is
the one that never will.

## Putting something back into a tree

Files and Knowledge have hierarchies, and restoring into one has two rules,
decided once for both.

Tasks is the exception, and it is one because it has no tree: a list is not in
another list, so there is no ancestor for anything to come back with. What is
below applies to Files and Knowledge.

**A folder or a page that is in the Trash comes back with what needs it.**
Restoring a page whose folder is also deleted restores the folder too —
refusing until the folder has been restored first would make the owner walk the
tree by hand, and doing neither would leave the page somewhere they cannot
reach. The folder comes back as itself and not with everything it used to
contain: restoring one page is restoring one page.

**A name already taken is `conflict`, not a silent rename.** Two things with one
name in one place is a tree nobody can navigate, and a product that quietly
appends "(2)" has made a decision the owner would have made differently. The
refusal names what is in the way, and `name` on the same call puts it back under
another one, so nobody is ever stuck with something they cannot get out of the
Trash.

**One deletion is one Trash entry.** Deleting a folder takes everything in it
under one moment, and the whole thing comes back together. Something further
down that the owner deleted separately is an entry of its own with an expiry of
its own: it does not come back when its folder does, and removing the folder for
good does not destroy it.

Which leaves one case: a thing whose folder is gone for good while it is still
recoverable. It is **restored to the root**, and the answer says so with
`moved_to_the_root`. Nothing in this product moves the owner's content without
saying it did.

## Keeping the previous version

Knowledge keeps history, and the conventions it keeps it by were settled an
epic before it existed, so that an application that wants history later does not
invent a second set.

**Fifty previous versions of one thing are kept** — a count and not an age. A
page edited twice a year deserves its history as much as one edited twice a day,
and "ninety days" would quietly throw away the whole history of everything the
owner works on slowly, which is most of what a personal knowledge base is for.

**Recovering an old version writes forward.** Putting one back is a change like
any other: it leaves a revision of what it replaced, so what was current a
moment ago is itself recoverable. History only grows, and the recovery is in it.
Rewinding would make "undo" the one operation in this product that destroys
work.

**A recovery carries `If-Match` for the object, not the revision.** A revision
never changes and has no version worth holding; what the caller has to be
holding is the page's. Otherwise recovering something read ten minutes ago would
discard an edit made five minutes ago — which is the case the guard exists for,
arriving through the one door that looks like it should be exempt.

**Revisions belong to the thing they are of.** Deleting it takes them into the
Trash with it, restoring brings them back, and removing it for good removes
them. A revision that outlived its page would be content the owner believes they
deleted.

## The door

**Everything but the five operations under *Operations* needs a credential**,
and absence is `unauthenticated`, never `not-found`. Which endpoints an instance
has is not a secret — the contract says so to anybody — so an address no
endpoint took is still `not-found` rather than a challenge, and a client can
tell "your credential is wrong" from "this instance is older than you think".

There are two credentials and the instance tells them apart itself:

- **A browser session**, in an `HttpOnly` cookie the instance sets at sign-in.
  It is a row on the server, so revoking one means something; the cookie holds a
  secret and nothing else, and no script on the page can read it.
- **`Authorization: Bearer <token>`**, for agents and for `pea`. A token belongs
  to one **agent access** — a named, revocable authorization the owner hands out
  — and the instance tells whose it is from the row it already holds. A token
  starts with `pea_` so that whoever finds one in a log or a shell history knows
  what they have found.

The cookie's strictness follows the request's own scheme. Over HTTPS it is
`__Host-personalaffe_session`, bound to this host and to `/`; over plain HTTP it
is `personalaffe_session` without the prefix and without `secure`, because a
prefixed or `secure` cookie is not stored at all there and the first sign-in of
a new installation often happens over `http://127.0.0.1:8080/`. A session over
plain HTTP travels in the clear: put TLS in front of anything that is not a
trial ([`docs/operations.md`](./operations.md)).

### A browser write proves where it came from

A request that is authenticated **by the cookie** and is not `GET`, `HEAD` or
`OPTIONS` carries two things, or it is `forbidden`:

```
X-Personalaffe-CSRF: 1
Origin: https://workspace.example.com
```

The header is what no cross-site form can set; the origin is compared against
`PERSONALAFFE_PUBLIC_URL` when the operator has set one, and against the host
otherwise — the scheme is left out there, because behind a proxy that terminates
TLS the request arrives as `http` unless the proxy is trusted to say otherwise.

**A request carrying a bearer token never sees this check.** Nothing attaches
that header but the client that holds the token, so `pea` and an agent are
unaffected.

### Sign-in says nothing about why it failed

An instance with no owner, an address that is not the owner's and a password
that is not theirs are one answer, to the byte. Failed attempts are throttled
per address and per caller, in a fifteen-minute window, and a throttled attempt
answers exactly what a wrong one answers: telling a guesser they are being
throttled tells them they have found something worth guessing at.

**`second-factor` is the one exception**, and it says nothing the caller has not
already proved: they have the password. Without it a client could not tell "that
was wrong" from "now the code".

### The second factor

Optional, and off until the owner turns it on. It is a time-based one-time
password to RFC 6238 — SHA-1, six digits, thirty-second steps — which is what
every authenticator app on a phone already speaks; one step either side of now
is accepted, and **a code that has been used cannot be used again**, so a code
read over somebody's shoulder is not good for the rest of its thirty seconds.

Enrolling takes two operations. The first offers a secret and changes nothing;
the second confirms it with a code made from it and turns it on. A secret that
took effect the moment it was shown would lock the owner out of their own
instance on the day they mistyped it into the app — and the way back would be
the procedure on the server, for a mistake made in ten seconds. An offer nobody
confirms goes stale after fifteen minutes.

Turning it on issues ten **recovery codes**, shown once. They are for the day
the phone is lost: personalaffe has no mail server to send a link through, and
these are what stands between that and the procedure on the server. Each works
once, and `second_factor` at sign-in takes either kind — an authenticator's code
or one of these. Which one somebody has to hand is not the instance's business.

### What an agent may do

Agent access is **not a second human account and never becomes one.** It has no
password, no session and no way to sign in; what it has is a token and one
answer per application — `none`, `read` or `read_write`, for `scratchpad`,
`knowledge`, `tasks` and `files`.

`read` reads and changes nothing; `read_write` includes deletion, which for
Scratchpad is permanent and for lasting content is into the Trash. `none` is
refused as `forbidden`.

**Everything under `/api/agents` and `/api/security`, and the session list, is
the owner's alone** — not by a permission that could be granted, but because no
permission for them exists. An agent that could issue a credential could issue
itself a better one. The refusal happens before the request body is read, so an
agent asking for one of these hears "not yours" rather than a remark about a
field it was never going to be allowed to send.

### Changing how the owner signs in

Every operation under `/api/security` that changes something asks for the
password again, however recently the caller signed in. A browser left open is
enough to take an instance over otherwise, and none of these should be one click
away from a screen somebody walked away from. It refuses with `forbidden` rather
than `unauthenticated`: the caller is signed in and stays signed in, and a
client that treated this as a dead session would sign them out over a typo.

Changing the password and turning the second factor off each **sign every other
browser out**. The point of changing a password is that whoever else was in is
now out.

## Operations

Five outside the door, and the rest behind it.

The five are held to carrying **no owner data, no credential, and nothing about
the host**: an instance on the public internet with nobody signed in answers
exactly these, and a test asserts their answers stay short and say nothing else.
They are also the last five: everything the epics after this add is behind the
door.

### `GET /api/version`

```json
{ "version": "0.0.0-dev" }
```

What this build calls itself: the tag it was cut from, or `0.0.0-dev` for a
build nobody released. It is the same string as the `Personalaffe-Version`
header. It exists outside authentication on purpose — a client asks it before it
knows whether its credential is any good, so that skew is reported as skew and
not as a refusal.

### `GET /api/health/live`

```json
{ "status": "live" }
```

200 while the process is running. **It touches nothing** — not the database, not
the disk. A database that has gone away is a reason to stop sending traffic and
not a reason to kill a healthy container, and a liveness check that is a second
database check turns every outage into a restart loop.

### `GET /api/health/ready`

```json
{ "status": "ready" }
```

200 when the database answers and carries the schema this build knows; 503 with
`{"status": "not-ready"}` otherwise. It asks the database every time rather than
remembering that the start went well.

The answer is a word. **Why** readiness failed goes to the instance's log at
warning, where the operator is — not to whatever can reach the port.

### `GET /api/setup`

```json
{ "required": true }
```

Whether this instance still needs its one-time setup. It is outside the door
because it has to be: a browser arriving at a fresh installation cannot sign in,
and something has to tell it to set up instead.

It is also the whole of what it says. **Who** the owner is, when they were set
up and what address they use are not in the answer — an instance on the public
internet answers this to whoever asks, and `required: false` is the most it will
ever tell them.

### `POST /api/setup`

```json
{ "email": "owner@example.com", "password": "correct horse battery staple" }
```

Claims an instance that has no owner, and answers `204`. **It works exactly
once**: a second attempt is `conflict`, whichever surface it comes from and
however many arrive at the same moment — the unique index on the owner's table
is what decides that, not a read taken a moment earlier.

`conflict` rather than `forbidden`, because nothing about the caller is wrong:
the instance is simply already somebody's. There is one owner, there is no
invitation and no second account, and an owner who has lost their password
recovers on the machine that runs the instance rather than through a second
account ([`docs/operations.md`](./operations.md), When the owner is locked
out).

The email address is the **login identifier** and nothing else: personalaffe
sends no mail, has no SMTP setting and needs none. A password is 12 to 200
characters and has no other rule — length, a slow hash and a throttle on failed
attempts are what protect one owner's workspace, and a character-class rule
mostly buys a short password with a digit stuck on the end.

Nothing comes back, and nothing about the password is ever readable again: what
is stored is an Argon2id value that carries the parameters it was made with, so
raising the cost later does not lock the owner out.

### `POST /api/session`

```json
{ "email": "owner@example.com", "password": "correct horse battery staple" }
```

Signs the owner in and answers `204` with the session cookie set. Wrong in any
way, it is `unauthenticated` and says no more than that.

With a second factor enrolled, the password alone answers `second-factor`, and
the same request is sent again with the code beside it:

```json
{ "email": "…", "password": "…", "second_factor": "123456" }
```

`second_factor` takes an authenticator's code or one of the recovery codes.

### `DELETE /api/session`

Ends the session this request came in on and takes the cookie out of the
browser — both names of it, because an instance that gained a TLS proxy after
somebody signed in over plain HTTP still has the other one in that browser.

A caller holding a token has no session to end and is told so: a token is
revoked where it was issued, and answering `204` would say something had been
taken away that is still working.

### `GET /api/me`

```json
{
  "kind": "owner",
  "email": "owner@example.com",
  "name": null,
  "permissions": { "scratchpad": "read_write", "knowledge": "read_write", "tasks": "read_write", "files": "read_write" },
  "since": "2026-09-13T12:00:00.000000Z"
}
```

Who the presented credential admits, and the cheapest way for a client to find
out that it still works — which is what both clients do with it. An agent
answers with its own name and exactly what it reaches, and `email` is `null`:
the owner's address is not an agent's to know.

### `GET /api/sessions`, `DELETE /api/sessions/{id}`, `DELETE /api/sessions`

Where this instance is signed in — with what each browser called itself, when it
began and when it was last used, and which one is asking — then ending one of
them, or all of them but this one. The list carries only sessions that still
admit somebody; an expired or revoked one is not a thing the owner can do
anything about.

### `GET /api/security`

```json
{ "second_factor_enabled": false, "enrolled_at": null, "recovery_codes_remaining": 0 }
```

### `POST /api/security/second-factor`

`{ "password": "…" }` → the offer, which is not yet in force:

```json
{ "secret": "JBSWY3DPEHPK3PXP", "uri": "otpauth://totp/personalaffe:owner%40example.com?secret=…" }
```

The URI is what a phone usually reads as a QR code; the secret is the same thing
for an app that is being typed into.

### `POST /api/security/second-factor/confirm`

`{ "code": "123456" }` → the ten recovery codes, shown once and never again. A
wrong code leaves the instance exactly as it was, and the offer still standing.

### `POST /api/security/second-factor/off`

`{ "password": "…" }` → `204`. The recovery codes go with it, and every other
browser is signed out.

### `POST /api/security/recovery-codes`

`{ "password": "…" }` → ten fresh codes. The old set stops working, because two
sets in force at once would mean a sheet of paper somebody threw away still
gets in.

### `POST /api/security/password`

`{ "current_password": "…", "password": "…" }` → `204`, and every other browser
signed out.

### `GET /api/agents`

Everything the owner has let in, revoked ones included — a revoked access still
names the agent everywhere it ever acted, and "what did I hand out" wants the
whole answer. Each carries its permissions, the head of its current token
(`pea_` and the characters after it, never the rest), when it was let in, when
its token was issued, and roughly when it was last used.

### `POST /api/agents`

```json
{
  "name": "the deploy agent",
  "permissions": { "scratchpad": "read_write", "knowledge": "read", "tasks": "none", "files": "none" }
}
```

`201`, with the access and **the token, which is in this answer and in no
other.** What the row keeps is a digest and the head; an owner who loses the
token reissues rather than recovers, which is the only honest thing a store of
digests can offer.

Names are unique, case-insensitively: two agents called the same thing are two
things nobody can tell apart at the moment of revoking one.

### `PATCH /api/agents/{id}`

`{ "name": …, "permissions": … }`, either or both. What is not sent is not
changed, and a change takes effect on the agent's next request.

### `POST /api/agents/{id}/token`

A new token, which is also how the old one stops working. There is one token
per access that works, and this is it.

### `DELETE /api/agents/{id}`

Shuts the agent out at once, and answers the access as it now stands. **A
timestamp, not a deletion**: the row stays and the list keeps it. Revoking a
revoked access changes nothing and is not an error.

### `GET /api/applications`

All four, their switches, and the caller's own permission in each. Behind the
door, and that is the whole of its access rule.

### `PUT /api/applications/{application}`

`{ "enabled": false }`, with `If-Match`. **The owner alone.** Answers the
application in its new state.

### `GET /api/scratchpad/entries`

`limit` bounds it, 1 to 1000, 200 by default. Newest capture first, with
`has_more` and **no cursor**, for the reason the Trash gives. Each item carries
its own `updated_at` and `expires_at`, so the list alone is enough to pin or
delete from without a second read.

### `POST /api/scratchpad/entries`

`{ "text": "…", "pinned": false }`. `201` with `Location` and the `ETag` of what
was written, so pinning or deleting what was just captured needs no read in
between.

### `GET /api/scratchpad/entries/{id}`

One entry, with its `ETag`. An entry that was deleted or has expired is
`not-found` — never `deleted`, because nobody can have it back.

### `PUT /api/scratchpad/entries/{id}`

`{ "text": "…", "pinned": true }`, with `If-Match`. The text and the pin
together, and the answer carries the version the write produced. A write that
asks for what is already stored changes nothing and does not move `updated_at`
— the guard is still checked.

### `DELETE /api/scratchpad/entries/{id}`

Destroys it, with `If-Match`. `204`, and there is no way back. Read/write access
to the Scratchpad is the whole of the access rule; this is the one destruction
in this product an agent may make.

### `GET /api/files`

`folder` names one, and nothing names the top. Answers the folders and the files
in it, the `chain` from the top down to it, and how much room is left. **No
limit and no cursor**: this is one folder's contents, and the answer to a folder
with too much in it is another folder.

### `POST /api/files/folders`

`{ "name": "Reisen", "parent": null }`. `201` with the `ETag` of what was made.

### `PUT /api/files/folders/{id}`

`{ "name": "Reisen", "folder": null }`, with `If-Match`. Renames it, moves it,
or both; what is in it goes with it. `conflict` for a name already taken, for a
folder put inside itself, and for a tree that would be too deep.

### `DELETE /api/files/folders/{id}`

Into the Trash, with `If-Match`, and everything in it under one moment. `204`.

### `POST /api/files/content`

`name` is required and `folder` names where it goes. **The body is the file**
and `Content-Type` is the media type. `201` with `Location` and the `ETag` of
what was written, so renaming or deleting what was just stored needs no read in
between. `too-large` and `out-of-space` are the two refusals only this endpoint
and the one below can make.

### `GET /api/files/{id}`

One file's metadata, with its `ETag`. A file in the Trash is `404 deleted` with
`deleted_at` and `expires_at`.

### `PUT /api/files/{id}`

`{ "name": "…", "folder": "…" }`, with `If-Match`. Renames it, moves it, or
both. **Its address does not change**, and neither do the bytes.

### `PUT /api/files/{id}/content`

New bytes for the same file, with `If-Match`. The id, the name and the place
stay, so every link to it now answers with these. Nothing is kept of what it
replaced.

### `GET /api/files/{id}/content`

The bytes, as an attachment, behind the same door as everything else. The
address is the id, so a rename cannot break it.

### `DELETE /api/files/{id}`

Into the Trash, with `If-Match`. `204`. `pea trash restore files {id}` brings it
back, and only the owner can remove it for good.

### `GET /api/knowledge/pages`

The whole tree, flat: every page's title, place and version, and no bodies. **No
limit and no cursor** — a page of a tree is a shape nobody can draw a hierarchy
from, and what keeps it bounded is that it carries no Markdown.

### `POST /api/knowledge/pages`

`{ "title": "…", "parent": null, "markdown": "…" }`. `201` with `Location` and
the `ETag` of what was written. A page with a title and nothing under it yet is
a page.

### `GET /api/knowledge/pages/{id}`

One page with its Markdown, and its `ETag`. A page in the Trash is `404 deleted`
with `deleted_at` and `expires_at`.

### `PUT /api/knowledge/pages/{id}`

`{ "title": "…", "parent": "…", "markdown": "…" }`, with `If-Match`. The title,
the place and the body together. `conflict` for a title already taken among its
siblings, for a page put under itself, and for a tree that would be too deep.

### `DELETE /api/knowledge/pages/{id}`

Into the Trash, with `If-Match`, and everything under it — and its history —
under one moment. `204`.

### `GET /api/knowledge/pages/{id}/revisions`

What the page used to say, newest first: an id, the title it had, when it
stopped being current and who ended it. **Without the bodies**: fifty versions
of a mebibyte each is not a read anybody should make to see when something
changed.

### `GET /api/knowledge/pages/{id}/revisions/{revision}`

One of them, with what it said.

### `POST /api/knowledge/pages/{id}/revisions/{revision}`

Puts it back, with `If-Match` — **the page's version, not the revision's**.
Answers the page as it now is. What was current until now is a revision of its
own.

### `GET /api/knowledge/export`

Every page, as a zip of Markdown files, as an attachment. Read access to
Knowledge is the whole of the access rule.

### `GET /api/tasks/lists`

Every list, by name, with `open` and `all` beside it. The first thing a client
draws is which list has anything in it, and a count is cheaper than every task.

### `POST /api/tasks/lists`

`{ "name": "Einkauf" }`. `201` with the `ETag` of what was made. A name another
list already has is `conflict`.

### `PUT /api/tasks/lists/{id}`

`{ "name": "Einkäufe" }`, with `If-Match`.

### `DELETE /api/tasks/lists/{id}`

Into the Trash, with every task in it, under one moment. `204`.

### `GET /api/tasks/lists/{id}/tasks`

What is in it, **open and completed together**, in the owner's order. No limit
and no cursor: a personal list is one person's, and a page of a manually ordered
list is a page nobody can reorder from.

### `POST /api/tasks/lists/{id}/tasks`

`{ "title": "…", "description": "…", "due_on": "2026-09-14" }`. It goes at the
end. `201` with `Location` and the `ETag`.

### `GET /api/tasks/{id}`

One task, with its `ETag` and the neighbour it sits behind.

### `PUT /api/tasks/{id}`

`{ "list": "…", "title": "…", "description": "…", "due_on": "2026-09-14",
"completed": false, "after": "…" }`, with `If-Match`. All of it, because all of
it is one row.

### `DELETE /api/tasks/{id}`

Into the Trash, with `If-Match`. `204`. It comes back at the end of its list.

### `GET /api/search`

`q` is what to look for; `application` narrows it to one; `limit` bounds it, 1
to 100, 20 by default. Best match first. Only the applications the caller may
read **and the owner has switched on** are asked, and nothing in the Trash is
found.

### `GET /api/dashboard`

The tiles and what is in the ones being drawn. At most five rows each. No
parameters: it is one screen.

### `PUT /api/dashboard/tiles/{tile}`

`{ "shown": false }`, with `If-Match`. **The owner alone.** Answers the tile in
its new state with its new `ETag`.

### `GET /api/weather`

What it is doing where the owner said, with the place's `ETag`. Never refuses
because of the weather.

### `PUT /api/weather/place`

`{ "name": "…", "latitude": …, "longitude": …, "units": "metric" }`, with
`If-Match`; all empty clears it. **The owner alone.** Answers the weather there,
so setting a place and seeing it is one request.

### `GET /api/weather/places`

`q` is a place name; answers what a geocoder thinks it names, best first.
**The owner alone.** Empty where the provider did not answer or this instance is
not asking anybody.

### `GET /api/trash`

`application` narrows it to one; `limit` bounds it, 1 to 1000, 200 by default.
Newest deletion first. Only the applications the caller may read **and the
owner has switched on** are asked.

### `POST /api/trash/{application}/{id}/restore`

Puts one thing back. Needs read/write access to that application, the
application switched on, and the entry's `updated_at` in `If-Match`. `name` restores it under another name, for
the case where the place it came from is occupied — without it, an occupied
name is `conflict`. Answers where it landed:

```json
{ "application": "knowledge", "id": "0199f0c4-…", "name": "architecture",
  "where": "/notes", "moved_to_the_root": false }
```

### `DELETE /api/trash/{application}/{id}`

Removes one thing for good, bytes included. **The owner alone.** Takes
`If-Match` like the restore. 204, and there is no way back.

### `DELETE /api/trash`

Empties it, or one application's part of it with `application`. **The owner
alone.** A switched-off application is left out of an "empty everything" and
refuses an "empty this one". Answers `{ "removed": 7 }`.

### Anything else under `/api`

An address under the prefix that no endpoint took answers `not-found` as a
problem document, not the web application's `index.html`. A client asking for an
endpoint an older instance does not have gets JSON saying so, rather than a 200
of HTML it has to recognise.
