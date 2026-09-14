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
depends on the code: `validation` carries `errors`, a field to its messages, and
`unknown-field` carries `field`. A client that does not know an extension member
ignores it.

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
| `stale` | 412 | The object has changed since it was read. |
| `conflict` | 409 | Something else already occupies that name or place. |
| `internal` | 500 | Something went wrong on the server. |

The set is [`RefusalCode`](../src/Personalaffe.Domain/RefusalCode.cs) and it
grows with the epics that need it — `disabled` arrives with the application
switch (PERSONAL-E4). **Each addition is a row in this table in the same
commit.**

**Switching on the status would collapse the distinction `deleted` exists to
make.** Both it and `not-found` are 404; one of them means the owner can have
the thing back.

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
a field in the body because an upload's body is the file
([`Files`](#extension-points-not-yet-implemented) is PERSONAL-E6's), and a guard
that only half the writes in the product can use is not a guard.

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
anybody.

Deletion is a write in both senses the product has: it needs read/write access
to the application, and it carries `If-Match` like any other write, so nothing
can delete a version it never read.

**Who deleted something travels with it as a copy** — the kind, the id, and the
name the access had at the time — and nothing links back to the agent access
row. The question the owner is asking of that list is which of their agents did
this, and a name that disappears when the access is revoked is no answer.

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

**An instance today answers an empty Trash**, because Scratchpad, Knowledge,
Tasks and Files are PERSONAL-E5 to PERSONAL-E8 and none of them exists yet.
That is the shape working rather than missing: a module joins the Trash by
contributing to it and by nothing else.

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

## Extension points, not yet implemented

These are named so that the operations of later epics do not each invent their
own spelling.

- **Application enablement** (PERSONAL-E4) adds `disabled`: an application the
  owner has switched off rejects content operations rather than answering them.

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

### `GET /api/trash`

`application` narrows it to one; `limit` bounds it, 1 to 1000, 200 by default.
Newest deletion first. Only the applications the caller may read are asked.

### `POST /api/trash/{application}/{id}/restore`

Puts one thing back. Needs read/write access to that application and the
entry's `updated_at` in `If-Match`. `name` restores it under another name, for
the case where the place it came from is occupied — without it, an occupied
name is `conflict`. 204, and the object is in the application again.

### `DELETE /api/trash/{application}/{id}`

Removes one thing for good, bytes included. **The owner alone.** Takes
`If-Match` like the restore. 204, and there is no way back.

### `DELETE /api/trash`

Empties it, or one application's part of it with `application`. **The owner
alone.** Answers `{ "removed": 7 }`.

### Anything else under `/api`

An address under the prefix that no endpoint took answers `not-found` as a
problem document, not the web application's `index.html`. A client asking for an
endpoint an older instance does not have gets JSON saying so, rather than a 200
of HTML it has to recognise.
