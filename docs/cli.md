# `pea`

personalaffe from the console: the interface for agents and for people who would
rather type than click. It is a client of the public API and nothing else — it
reaches no database, no file volume and no other affe product, and it knows an
instance only through the client generated from
[`docs/api/openapi.json`](./api/openapi.json).

**Two verbs exist today**, `version` and `status`, and they are the foundation's.
Everything else on this page — the ladders, the input rules, the exit codes — is
the shape every later verb is written to, and it is here because it was settled
in PERSONAL-5, before there was a second verb to settle it differently.

```sh
pea version    # what pea is, what the instance is
pea status     # which instance, and where the credential is coming from
```

## What it promises

- **stdout is data, stderr is sentences.** Everything a program would read goes
  to stdout; everything a person would read about what happened goes to stderr.
  `pea version --json | jq` never sees a word of explanation.
- **`--json` is the object as the API answered it**, on every verb that has one.
- **Nothing is ever interactive.** `pea` never prompts, never opens an editor,
  and never needs a terminal. It reads stdin only where a flag says so.
- **Every request has a deadline.** Thirty seconds, so that an agent cannot hang
  on a request; Ctrl-C and SIGINT cancel where the command is.
- **A credential is never an argument.** There is no `--token`: a flag stands in
  the shell history, in `ps`, and in whatever a CI runner logs about the command
  it ran. The two ways in are an environment variable and a file with a mode.
- **The credential is never printed.** `pea status` says where it came from,
  which is the question worth asking, and never what it is.

## Which instance, and as whom

Two questions, each answered by a ladder rather than by one variable. The first
rung that has an answer wins.

**Which instance:**

1. `--url https://workspace.example.com`
2. `PERSONALAFFE_URL`
3. `instance` in the configuration file

**As whom:**

1. `PERSONALAFFE_TOKEN` — the environment wins, because that is how an agent
   receives its own credential and how CI holds one
2. the file named by `token_file` in the configuration, which must not be
   readable by anybody else on the machine (`chmod 600`)

*A third rung — the operating system's keychain, where a browser sign-in leaves
a session — arrives with the sign-in that fills it in PERSONAL-E2. The ladder is
built with two so that adding the third is an insertion rather than a redesign.*

There is no project and no tenant: one instance belongs to one owner
([`CONTEXT.md`](../CONTEXT.md)), and there is nothing else to select.

### The configuration file

`$PERSONALAFFE_CONFIG`, else `$XDG_CONFIG_HOME/personalaffe/config.json`, else
`~/.config/personalaffe/config.json`. It is written `0600` in a `0700`
directory, and **it holds no credential** — only which instance, and where a
credential file is if one was chosen:

```json
{
  "instance": "https://workspace.example.com",
  "token_file": "/run/secrets/personalaffe"
}
```

A file that is not there is an empty one. A machine that has never been pointed
at an instance is not a machine with a broken install.

### Plain HTTP

`pea` refuses `http://` to anything that is not loopback: a token over plain
HTTP is a token in somebody's network log. `localhost`, `127.0.0.1` and `::1`
are the exception, because they cannot be true there. Meaning it is explicit and
never inferred — `--insecure-http`, or `PERSONALAFFE_INSECURE_HTTP=1`.

## Text in

Where a verb takes a piece of a person's text — a scratchpad entry, a knowledge
page, a task's description — it takes it from a file or from stdin, and never
from an editor:

```sh
pea scratchpad add --text-file note.md
cat note.md | pea scratchpad add --text-file -
```

A trailing newline is how a shell ends a line and is dropped; the newlines
inside are the person's and stay. Text that is not ASCII survives unchanged.
Where a verb takes a stored file rather than text, every byte survives, the last
newline included.

*The verbs that use this arrive with their applications. The reading is
implemented and tested now because every one of them will need it, and because
what breaks first — Unicode, and newlines — breaks the same way for all of
them.*

## Exit codes

A script branches on the code; nothing has to be parsed.

| Code | What happened |
| --- | --- |
| 0 | It worked. |
| 1 | A 500, an answer `pea` could not parse, or a bug in `pea`. |
| 2 | A mistake in the arguments, or in `PERSONALAFFE_URL` / `PERSONALAFFE_TOKEN`. |
| 3 | `404` — nothing at that address. |
| 4 | `400 validation` and every `422` — the instance refused what was asked. |
| 5 | `409` — something else already occupies that name or place. |
| 6 | `412 stale` — the object changed since it was read. |
| 7 | `401` and `403` — the door stayed shut. |
| 9 | Version skew: this `pea` does not talk to that instance. |
| 10 | The instance could not be reached at all: DNS, connection refused, timeout, TLS. |

**8 is deliberately free.** It is what "there is nothing" would be — a verb that
looked for work and found none — and no such verb exists yet. Leaving it unused
means no script has to relearn a number when one does.

The codes come from the status and the problem document's code together
([`docs/api.md`](./api.md), Errors), which is why 3 and 7 stay separate however
many refusals share a status.

### Skew

Every answer carries `Personalaffe-Version`, and `pea` reads it off whatever it
got — a refusal included. A released `pea` talks to an instance of the same
major version whose minor is no newer than its own; a `0.0.0-dev` build on
either side is not checked, because it has no number to compare and the check
exists for skew between releases.

`pea` also refuses to believe a `200` that is not JSON. The instance serves the
web application from the same port and falls back to `index.html` for every path
outside `/api`, so an endpoint an older instance does not have would otherwise
answer a page of HTML with a success status.

## The verbs

### `pea version`

```
$ pea version
pea       0.0.0-dev
instance  0.0.0-dev
```

Asks `GET /api/version`, the one operation that answers before anything has
authenticated — so this is the command that works when the credential is the
thing that is wrong. With no instance named it prints `pea`'s own version, says
on stderr what is missing, and exits 0: not knowing which instance to ask is not
a failure of this command. An address that *was* named and is wrong is, and
exits 2.

### `pea status`

```
$ pea status
instance   https://workspace.example.com
version    pea 1.4.0, instance 1.4.0
token      from PERSONALAFFE_TOKEN
```

The two questions worth asking before a write, with the rung that answered each.
Nothing here stops at the first failure: `status` is the command somebody runs
*because* something is wrong, and a missing credential must not take the
instance's version down with it.

## Building it

```sh
cd src/cli
go generate ./...   # the client of the contract, into internal/api; never committed
go vet ./...
go test ./...
go build ./cmd/pea
```

`go generate` is not optional and not a convenience: nothing compiles without
it, which is the point — a working tree is never a state where the client agrees
with a stale contract.
