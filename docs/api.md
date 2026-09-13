# The HTTP surface

One instance, one address, one API. The web application and `pea` are both
clients of it and there is no second way in
([`docs/codebase.md`](./codebase.md)).

**Three operations exist today.** They are the foundation's, they carry no
personal content, and they are listed under *Operations* below. Everything else
on this page is the shape every later operation is written to — the conventions,
the error document, the codes — and it is here because it was settled in
PERSONAL-3, before there was a second endpoint to settle it differently.
Sections that describe something not yet implemented say so in their first
line.

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
- **Timestamps are RFC 3339 in UTC with microseconds** —
  `2026-09-13T14:03:07.123456Z` — one spelling everywhere, so that the value a
  client reads is the value it can send back.
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
depends on the code: `validation` carries `errors`, a field to its messages. A
client that does not know an extension member ignores it.

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
| `forbidden` | 403 | The caller may not do this. |
| `not-found` | 404 | Nothing at that address. |
| `stale` | 412 | The object has changed since it was read. |
| `conflict` | 409 | Something else already occupies that name or place. |
| `internal` | 500 | Something went wrong on the server. |

The set is [`RefusalCode`](../src/Personalaffe.Domain/RefusalCode.cs) and it
grows with the epics that need it — `deleted` with recoverable deletion
(PERSONAL-E3), `disabled` with the application switch (PERSONAL-E4). **Each
addition is a row in this table in the same commit.**

Of the eight, the foundation can raise `not-found` and `internal`. The other
six are the shape their epics are written to; `unauthenticated`, `forbidden` and
`stale` in particular are here because the shape of every later write depends on
them being decided already.

### Exit codes

`pea` derives its exit code from the status and the code, so that a script
branches without parsing anything. The table lives in
[`docs/cli.md`](./cli.md) with the CLI that implements it (PERSONAL-5).

## Extension points, not yet implemented

These are named so that the operations of later epics do not each invent their
own spelling. **None of them is implemented.**

- **Authentication** (PERSONAL-E2) is `Authorization: Bearer <token>` for the
  CLI and for agents, and an opaque session cookie for the browser. Everything
  but the three operations below requires one, and absence is
  `unauthenticated`, never `not-found`.
- **Stale-update handling** (PERSONAL-E3): a write that replaces something says
  which version it read, and a write based on an older one is refused as
  `stale` rather than silently winning. The value is the object's `updated_at`
  in the spelling above.
- **Recoverable deletion** (PERSONAL-E3) adds `deleted`, which is a 404 that
  says the object can still be restored.
- **Application enablement** (PERSONAL-E4) adds `disabled`: an application the
  owner has switched off rejects content operations rather than answering them.

## Operations

Three, and they are the whole of what an instance answers today. All three are
outside the door — there is no door yet — and all three are held to carrying
**no owner data, no credential, and nothing about the host**: an instance on the
public internet with nobody signed in answers exactly these, and a test asserts
their answers stay short and say nothing else.

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

### Anything else under `/api`

An address under the prefix that no endpoint took answers `not-found` as a
problem document, not the web application's `index.html`. A client asking for an
endpoint an older instance does not have gets JSON saying so, rather than a 200
of HTML it has to recognise.
