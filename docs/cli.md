# `pea`

personalaffe from the console: the interface for agents and for people who would
rather type than click. It is a client of the public API and nothing else — it
reaches no database, no file volume and no other affe product, and it knows an
instance only through the client generated from
[`docs/api/openapi.json`](./api/openapi.json).

**Five verbs exist today.** Two are the foundation's and three are the
credential's; the content verbs arrive with their applications. Everything else
on this page — the ladders, the input rules, the exit codes — is the shape every
later verb is written to, and it is here because it was settled in PERSONAL-5,
before there was a second verb to settle it differently.

```sh
pea version         # what pea is, what the instance is
pea status          # which instance, and where the credential is coming from
pea login           # check a token and keep it in this machine's keychain
pea whoami          # who the credential admits, and what it reaches
pea logout          # take it out again
pea trash list      # what was deleted and is still recoverable
pea trash restore   # put one thing back
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
  it ran. The three ways in are an environment variable, a file with a mode, and
  this machine's keychain.
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
3. this machine's keychain, which `pea login` fills and `pea logout` empties

There is no project and no tenant: one instance belongs to one owner
([`CONTEXT.md`](../CONTEXT.md)), and there is nothing else to select.

### The credential is an agent token

`pea` holds **agent access**: a named, revocable authorization the owner hands
out, with no access, read access or read/write access to each of the four
applications ([`CONTEXT.md`](../CONTEXT.md)). The owner creates one in the
browser and the token is shown once.

There is no password here and there will not be. A browser signs the owner in;
a console is an agent acting on the owner's behalf, which is what CONTEXT.md
already calls it. What follows from that is worth saying plainly: **`pea`
cannot manage agents, change security settings or reset the instance**, and no
token can be given permission to — an agent that could issue a credential could
issue itself a better one.

### The keychain

macOS's login keychain, or whatever answers the Secret Service API on Linux —
GNOME Keyring, KWallet — reached through the tool the system already ships
(`security`, `secret-tool`). What that buys over a file is the thing a file
cannot: the secret is held by something with its own access control and screen
lock, and `pea` keeps no copy on disk.

**Neither tool is ever given the token as an argument.** An argument stands in
`ps` for anybody on the machine to read, which is the whole thing this rung
exists to avoid: macOS takes the command on standard input, and `secret-tool`
takes the secret on standard input.

A machine with neither is a machine with no keychain — an answer, not a failure.
The two rungs above still work, and that is what CI and an agent's container
use. A keychain that is there and says something else — locked, or a prompt
somebody denied — is said out loud rather than passed off as "no credential".

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

`deleted` is a 404 and therefore exit 3, like every other 404. The distinction a
script needs — that the thing can still be brought back — is in the problem
document's `type`, and `--json` hands it over. A code of its own would mean
every script that handles "not there" would have to learn two numbers for it.

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
token      from the keychain
```

The two questions worth asking before a write, with the rung that answered each.
Nothing here stops at the first failure: `status` is the command somebody runs
*because* something is wrong, and a missing credential must not take the
instance's version down with it.

### `pea login --token-file FILE`

```sh
pea login --url https://workspace.example.com --token-file token.txt
cat token.txt | pea login --token-file -
```

Reads the token from a file or from stdin, asks `GET /api/me` with it, and — if
the instance admits it — keeps it in this machine's keychain and writes the
instance into the configuration, so that nothing afterwards needs the flag or
the variable.

**Checked before it is stored.** A token that does not work is exit 7 now rather
than a puzzle at the next command. Nothing is written when the instance refuses.

There is no `--token`: a credential is never an argument.

### `pea whoami`

```
$ pea whoami
kind         agent
name         the deploy agent
scratchpad   read_write
knowledge    read
tasks        none
files        none
```

Who the credential admits, and exactly what it reaches. It is the cheapest way
to find out that a credential still works; a revoked token and one that never
existed answer the same way, which is exit 7.

### `pea logout`

Takes this machine's token out of the keychain, and does nothing else.
**The token itself keeps working** — revoking agent access is the owner's doing,
in the browser, and an agent that could revoke its own credential would be
deciding something about the instance. A machine with nothing stored is left as
it is, because that is the state that was asked for.

### `pea trash list`

```sh
pea trash list
pea trash list --application knowledge --limit 20
```

```
knowledge	0199f0c4-…	architecture	/notes	by agent "the laptop agent"	expires 2026-10-12
```

One line per entry, tab separated, newest deletion first. Only the applications
this credential may read are asked, so an agent sees its own half of the
workspace and nothing beyond it. `--json` is the object the API answered.

An empty Trash says so on stderr and writes nothing to stdout, so a pipeline
reading it gets nothing rather than a sentence.

### `pea trash restore APPLICATION ID`

```sh
pea trash restore knowledge 0199f0c4-0000-7000-8000-000000000001
pea trash restore knowledge 0199f0c4-… --name "architecture (2026)"
```

Puts one thing back. Needs read/write access to the application it is in.

**A write says which version it replaces** ([`docs/api.md`](./api.md), The
guarded write), and `pea` does that part itself: it reads the entry out of the
Trash and sends the version it found. Nothing has to hold a timestamp by hand,
which is the whole reason the guard is bearable for an agent. `--if-match` skips
the read for a caller that already has one. A version that is no longer the
object's is exit 6.

If the place it came from now holds something of that name, the restore is
refused as a conflict (exit 5) and `--name` puts it back under another one. If
the folder it came from is gone for good, it goes to the root and `pea` says so
on stderr — nothing here moves the owner's content quietly.

### There is no verb that destroys anything

`pea trash purge` does not exist, and neither does emptying the Trash or
removing one entry for good. Those are the owner's, and `pea` holds agent
access: the same reason it cannot issue a credential or change a security
setting. An agent that could permanently remove one entry could bypass the Trash
in two steps instead of one, and the Trash exists precisely so that an agent
acting on the owner's behalf cannot destroy the owner's content.

The browser is where the owner does it.

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
