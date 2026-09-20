# `pea`

personalaffe from the console: the interface for agents and for people who would
rather type than click. It is a client of the public API and nothing else — it
reaches no database, no file volume and no other affe product, and it knows an
instance only through the client generated from
[`docs/api/openapi.json`](./api/openapi.json).

**All four applications are here, and so is the question that reaches all of
them.** Two verbs are the foundation's, three are the credential's, two are the
workspace's, seven are the Scratchpad's (PERSONAL-E5), six are Files'
(PERSONAL-E6), nine are Knowledge's (PERSONAL-E7), ten are Tasks' (PERSONAL-E8)
and three are the workspace's own — one search, the home page, and the weather
(PERSONAL-E9).
Everything else on this page — the ladders, the input rules, the exit codes — is
the shape every later verb is written to, and it is here because it was settled
in PERSONAL-5, before there was a second verb to settle it differently.

```sh
pea version           # what pea is, what the instance is
pea status            # which instance, and where the credential is coming from
pea login             # check a token and keep it in this machine's keychain
pea whoami            # who the credential admits, and what it reaches
pea logout            # take it out again
pea applications      # which applications are switched on, and what this credential reaches
pea scratchpad list   # what is in the Scratchpad, newest first
pea scratchpad add    # put a piece of text down, from a file or stdin
pea scratchpad show   # the text of one entry, and nothing else
pea scratchpad edit   # replace its text, its pin, or both
pea scratchpad pin    # keep one: a pinned entry never expires
pea scratchpad unpin  # put one back under the clock
pea scratchpad rm     # destroy one. Permanent: there is no way back
pea files ls          # what is in a folder, or in the top of the tree
pea files put         # store a file, from a file or stdin
pea files get         # its bytes back, byte for byte
pea files mkdir       # make a folder
pea files mv          # rename something, move it, or both
pea files rm          # into the Trash. `pea trash restore files ID` brings it back
pea knowledge tree    # every page's title and place
pea knowledge show    # the Markdown of one page, and nothing else
pea knowledge new     # write a page
pea knowledge edit    # rewrite it, rename it, or both
pea knowledge mv      # move it, rename it, or both
pea knowledge rm      # into the Trash, with everything under it
pea knowledge history # what it used to say, newest first
pea knowledge recover # put a previous version back; history only grows
pea knowledge export  # the whole base, as a zip of Markdown files
pea tasks lists       # every list, with how much is open in each
pea tasks new-list    # make one
pea tasks ls          # what is in a list, in your order
pea tasks add         # capture one at the end of a list
pea tasks show        # one task; its description to stdout
pea tasks edit        # its title, its date, its description, its list
pea tasks done        # complete it
pea tasks undone      # reopen it
pea tasks mv          # where it sits: --after ID or --top
pea tasks rm          # into the Trash
pea trash list        # what was deleted and is still recoverable
pea trash restore     # put one thing back
pea search            # find something in all four at once
pea dashboard         # what is useful or pending, as the home page has it
pea weather           # what it is doing where the owner said
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

`pea scratchpad add` and `pea scratchpad edit` are the first callers. The rest
arrive with their applications, and they read their text through the same code:
what breaks first — Unicode, and newlines — breaks the same way for all of them.

The instance drops a trailing newline too, so text written straight to the API
is normalised the same way. It drops **one**, never two: a second trailing
newline is somebody's blank line ([`docs/api.md`](./api.md), The Scratchpad).

## Exit codes

A script branches on the code; nothing has to be parsed.

| Code | What happened |
| --- | --- |
| 0 | It worked. |
| 1 | A 500, an answer `pea` could not parse, or a bug in `pea`. |
| 2 | A mistake in the arguments, or in `PERSONALAFFE_URL` / `PERSONALAFFE_TOKEN`. |
| 3 | `404` — nothing at that address. |
| 4 | `400 validation`, every `422`, and the two storage refusals `413 too-large` and `507 out-of-space` — the instance would not take what was sent. |
| 5 | `409` — `conflict`, something else already occupies that name or place, or `disabled`, the application is switched off. |
| 6 | `412 stale` — the object changed since it was read. |
| 7 | `401` and `403` — the door stayed shut. |
| 9 | Version skew: this `pea` does not talk to that instance. |
| 10 | The instance could not be reached at all: DNS, connection refused, timeout, TLS. |
| 11 | `503 paused` — the instance is being backed up and is not taking writes for a moment. The request was fine; send the same one again after the `Retry-After` it came with. |

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

`too-large` and `out-of-space` share exit 4 and are two codes in the document,
because the script's branch is the same — this write did not go through — while
what to do about it is not: send something smaller, or delete something.
`--json` is what tells them apart.

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
instance   Haus (https://workspace.example.com)
version    pea 1.4.0, instance 1.4.0
token      from the keychain
```

The two questions worth asking before a write, with the rung that answered each.
Nothing here stops at the first failure: `status` is the command somebody runs
*because* something is wrong, and a missing credential must not take the
instance's version down with it.

**The instance is named where the owner has named it.** `Haus` is what
`GET /api/appearance` answers ([`docs/api.md`](./api.md), The appearance), which
is outside the door — so this needs no credential for it and works when the
credential is the thing that is wrong. An instance nobody has named prints the
address alone, exactly as before, and a name that could not be read is dropped
rather than reported: it is a convenience, and a `status` that failed over one
would be useless precisely when it is wanted. In `--json` it is `name`, beside
`instance`, and `null` where there is none.

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

### `pea applications`

```sh
pea applications
```

```
scratchpad	on	read_write
knowledge	off	read
tasks	on	none
files	on	read_write
```

One line per application, tab separated: which of the four this workspace has
switched on, and what this credential may do in each. All four are listed,
including the ones this credential cannot reach — that column is the answer to
why an operation was refused, without a second request.

An application that is switched off refuses every operation in it as `disabled`,
which is exit 5 ([`docs/api.md`](./api.md), The applications). What is in it is
kept and the retention sweep goes on running; switching it back on is the
owner's doing, in the browser.

### `pea scratchpad list`

```sh
pea scratchpad list
pea scratchpad list --limit 20
```

```
0199f0c4-…	2026-09-14T08:30:00Z	-	2026-09-21T08:30:00Z	the wifi password is hunter2 …
0199f0c3-…	2026-09-01T08:30:00Z	pinned	never	ssh key fingerprint
```

One line per entry, tab separated, newest capture first: the id, when it was
captured, whether it is pinned, when it expires, and the first line of the text,
shortened. A pinned entry expires `never`, which is a column rather than a
blank. The whole text is what `show` is for.

An empty Scratchpad writes nothing to stdout and says so on stderr, so a
pipeline reading it gets nothing rather than a sentence.

### `pea scratchpad add --text-file FILE|-`

```sh
pea scratchpad add --text-file note.txt
cat note.txt | pea scratchpad add --text-file -
pea scratchpad add --text-file - --pinned < note.txt
```

The text comes from a file or from stdin and never from an editor
([Text in](#text-in)). `--pinned` pins it at capture, so that it never expires.

**What goes to stdout is the id**, so that `id=$(pea scratchpad add --text-file -)`
is the whole of what a script has to do to hold on to what it wrote. When it
goes is said on stderr. `--json` is the entry as the API answered it.

### `pea scratchpad show ID`

```sh
pea scratchpad show 0199f0c4-0000-7000-8000-000000000001 > note.txt
```

**The text and nothing else**, byte for byte, so that this is the round trip of
`add --text-file`. Whatever a person needs told goes to stderr. `--json` is the
entry as the API answered it.

### `pea scratchpad edit ID`

```sh
cat note.txt | pea scratchpad edit 0199f0c4-… --text-file -
pea scratchpad edit 0199f0c4-… --text-file note.txt --pinned
```

Replaces the text, the pin, or both. **The pin is carried forward unless
`--pinned` says otherwise**: one write carries both, so an edit that said
nothing about the pin would otherwise unpin whatever it touched.

A write says which version it replaces ([`docs/api.md`](./api.md), The guarded
write), and `pea` does that part itself: it reads the entry and sends the
version it found. `--if-match` skips that read where `pea` does not otherwise
need the entry. A version that is no longer the entry's is exit 6.

### `pea scratchpad pin ID`, `pea scratchpad unpin ID`

A pinned entry never expires. **Unpinning gives an entry a full period from that
moment** rather than from its capture, so nothing disappears the instant it is
let go.

Both read the entry first, because one write carries the text and the pin
together and the text has to go back with it.

### `pea scratchpad rm ID`

```sh
pea scratchpad rm 0199f0c4-0000-7000-8000-000000000001
```

Destroys it. **Permanent**: a Scratchpad entry is not set aside, never appears
in the Trash, and neither the owner nor an operator can bring it back.

Nothing prompts, because nothing in `pea` ever does, and there is no `--force`:
a flag that made this one feel dangerous would make every other verb feel safe.
It is guarded like any other write, and `--if-match` skips the read.

**There is no verb that empties the Scratchpad.** A loop over `list` is a script
anybody can write; a single verb that destroys everything is one typo away from
being the thing this product is sorry about.

### `pea files ls [PATH|ID]`

```sh
pea files ls
pea files ls /Reisen/2026
```

```
0199f0c4-…	folder	-	2026-09-14T08:00:00Z	Belege
0199f0c5-…	file	284119	2026-09-14T08:31:00Z	Reisekosten 2026.pdf
```

One line per entry, tab separated: the id, whether it is a folder or a file, its
size in bytes, when it last changed, and its name. Folders come first and a
folder's size column is `-`. `--json` is the object the API answered, quota
included.

An empty folder writes nothing to stdout and says so on stderr.

**A path is `pea`'s convenience and never the API's.** `/Reisen/2026` is walked
a segment at a time and the wire carries ids, so nothing server-side has to keep
a second name for anything. Capitals do not matter, because names in a folder
are one each whatever their capitals
([`docs/api.md`](./api.md)). **An id is accepted wherever a path is**, because
that is what the other verbs print; an id is tried first, so a folder somebody
called `0199f0c4-…` shadows nothing. A segment that names nothing is exit 3 and
says which one.

### `pea files put --file FILE|- [--to PATH] [--name NAME]`

```sh
pea files put --file "Reisekosten 2026.pdf" --to /Reisen/2026
tar c notes | pea files put --file - --name notes.tar
```

The bytes come from a file or from stdin, byte for byte — nothing is trimmed and
nothing added, so the last byte of an archive survives. `--name` is what it is
called on the instance; without it the base name of `--file` is used, which is
why `--file -` needs one rather than being given a name `pea` invented. `--to`
names the folder, and the top of the tree is where it goes without one.

What goes to stdout is the id, so that `id=$(pea files put --file - --name x)`
is the whole of what a script does to hold on to what it wrote. **That id is the
file's address for good**: renaming it and moving it do not change where it is
downloaded from.

Over `PERSONALAFFE_MAX_FILE_MIB` is exit 4 naming the limit, and so is an upload
with no room left — `--json` says which of the two it was.

### `pea files get PATH|ID [--out FILE|-]`

```sh
pea files get /Reisen/2026/bahn.pdf --out bahn.pdf
pea files get 0199f0c5-… --out - | sha256sum
```

The bytes go where `--out` says, and `-` is stdout: that is the round trip of
`put --file -`. Without `--out` they go to the file's own name in the working
directory, and **`pea` refuses rather than overwrite something already there** —
a download that silently replaced a file would be the one destructive thing a
read can do.

`--json` is the file's metadata instead of its bytes.

### `pea files mkdir PATH`

```sh
pea files mkdir /Reisen
pea files mkdir --parents /Reisen/2026/Belege
```

Without `--parents`, every folder above the last one has to be there already and
a missing one is exit 3 naming the segment — and nothing is made. The id goes to
stdout.

### `pea files mv PATH|ID DEST`

```sh
pea files mv /plan.md /Reisen              # into the folder, keeping its name
pea files mv /plan.md /Reisen/der-plan.md  # and renamed
pea files mv /plan.md /der-plan.md         # renamed where it is
```

`DEST` that names an existing folder means "into it", which is what `mv` means
everywhere else; anything else is the place and the name together. The place has
to exist — `mv` has never made directories. A name already taken there is exit 5
and never a silent rename.

One write carries the name and the place together, because both are changes to
the same row. `pea` reads the thing first and sends the version it found;
`--if-match` skips that read.

### `pea files rm PATH|ID`

```sh
pea files rm /Reisen/2026/bahn.pdf
```

**Nothing is destroyed here.** It goes to the Trash, leaves every ordinary read,
and comes back with `pea trash restore files ID` — which the sentence on stderr
says, with the id in it. A folder takes everything in it, under one moment, so
the whole thing comes back together.

Guarded like any other write; `--if-match` skips the read.

### `pea knowledge tree [PATH|ID]`

```sh
pea knowledge tree
pea knowledge tree /Reisen
```

```
0199f0c6-…	2026-09-14T08:00:00Z	Reisen
0199f0c6-…	2026-09-14T08:10:00Z	  Bahn
```

One line per page, tab separated: the id, when it last changed, and the title
indented by how deep it sits. **No body is read** — the tree is titles and
places, which is what makes it one request however much the owner has written.
A page named as an argument is the top of what is printed, and is printed
itself.

**A path is titles and is `pea`'s convenience**, resolved against that one read;
the wire carries ids. Capitals do not matter, because titles under one page are
one each whatever their capitals. An id is accepted wherever a path is.

### `pea knowledge show PATH|ID`

```sh
pea knowledge show /Reisen/Bahn > page.md
```

The Markdown to stdout, byte for byte, and nothing else — which is what makes it
the other half of `edit --text-file`. `--json` is the page as the instance
answered it.

### `pea knowledge new PATH [--text-file FILE|-]`

```sh
pea knowledge new /Reisen
cat notes.md | pea knowledge new /Reisen/2026 --text-file -
```

The last segment of `PATH` is the title; every page above it has to be there
already, and a missing one is exit 3 naming the segment. `--text-file` is
optional: a page with a title and nothing under it yet is a page. The id goes to
stdout.

### `pea knowledge edit PATH|ID [--text-file FILE|-] [--title TITLE]`

One write carries the title, the place and the body together, because all three
are the same row. **What is not given is carried forward**, so `--title` alone
renames and `--text-file` alone rewrites. `pea` reads the page first and sends
the version it found; `--if-match` skips that read.

What it replaced is kept — `pea knowledge history` lists it.

### `pea knowledge mv PATH|ID DEST`

```sh
pea knowledge mv /Architektur /Notizen             # under it, keeping its title
pea knowledge mv /Architektur /Notizen/Aufbau      # and renamed
pea knowledge mv /Architektur /Aufbau              # renamed where it is
```

A `DEST` that names an existing page means "under it"; anything else is the
place and the title together, and the place has to exist. A title already taken
there is exit 5, and so is a page put under itself or a tree that would be too
deep.

### `pea knowledge rm PATH|ID`

**Nothing is destroyed here.** The page goes to the Trash with everything under
it and all of its history, and `pea trash restore knowledge ID` brings it back —
which the sentence on stderr says, with the id in it.

### `pea knowledge history PATH|ID [--revision ID]`

```
0199f0c6-…	2026-09-14T09:00:00Z	agent the writing agent	Bahn
0199f0c6-…	2026-09-14T08:30:00Z	the owner	Die Bahn
```

One line per version, newest first: the id, when it stopped being current, who
ended it, and the title it had. **Bodies are not read** — fifty versions of a
page is not something to print by accident.

`--revision ID` writes that one version's Markdown to stdout instead, so a diff
is one line:

```sh
diff <(pea knowledge history ID --revision R) <(pea knowledge show ID)
```

### `pea knowledge recover PATH|ID REVISION`

Puts a previous version back. **It writes forward**: what the page said until now
becomes a version of its own, so nothing is lost by undoing something. It puts
back the title as well as the body and leaves the page where it is.

The version it is guarded by is **the page's** and not the revision's: a revision
never changes, and recovering something read ten minutes ago must not discard an
edit made five minutes ago.

### `pea knowledge export [--out FILE|-]`

```sh
pea knowledge export --out knowledge.zip
pea knowledge export --out - | unzip -l -
```

The whole base as a zip: one `.md` per page at the path its titles make, each
opening with YAML front matter carrying the id, the parent, the real title and
the timestamps, plus a `knowledge.json` saying the same in one place. Without
`--out` it goes to the name the instance gave it, and `pea` refuses rather than
overwrite something already there.

### `pea tasks lists`

```
0199f0c7-…	1	2	Einkauf
```

One line per list, tab separated: the id, how many are open, how many there are,
and the name. An instance with no lists writes nothing to stdout and says so on
stderr.

### `pea tasks new-list NAME`

```sh
pea tasks new-list Einkauf
```

Names are one each whatever their capitals; one already taken is exit 5. The id
goes to stdout.

### `pea tasks ls LIST`

```
0199f0c7-…	open	2026-09-14!	Milch holen
0199f0c7-…	done	-	Brot holen
```

**Open first and completed after**, because those are two different things to be
looking at (VISION §6.4). A due date that has passed carries a `!`; a task with
no date shows `-`.

`LIST` is a name or an id. **A name is `pea`'s convenience**, resolved against
one read of the lists the way `pea files` resolves a path — the wire carries
ids, and capitals do not matter because the instance's own rule is that they do
not.

### `pea tasks add LIST TITLE [--due 2026-09-14]`

```sh
pea tasks add Einkauf "Milch holen"
pea tasks add Einkauf "Brot holen" --due 2026-09-14
```

The title is an argument because this is the one capture that has to be quick;
a description is `edit --description-file`, like every other body in `pea`. It
goes at the end of the list: capture is what happens when something occurs to
you, and the order is what you decide afterwards. The id goes to stdout.

`--due` takes a day and nothing else. Anything that is not one is exit 2 with
the shape spelled out — a date is never guessed at from a word.

### `pea tasks show ID`

The description to stdout byte for byte, so `show > note.md` is the round trip
of `edit --description-file`. The title, the state and the date go to stderr.

### `pea tasks edit ID`

```sh
pea tasks edit ID --title "Milch und Brot"
pea tasks edit ID --due 2026-09-20
pea tasks edit ID --due none
cat note.md | pea tasks edit ID --description-file -
pea tasks edit ID --list Arbeit
```

One write carries the title, the description, the date, the list, the state and
the place, because all of it is one row. **What is not given is carried
forward**, so nothing changes by accident. `--due none` is how a date is taken
away: an empty string cannot say it, because not giving the flag already means
"leave it alone".

`--list` moves it, and it lands at the end of the list it goes to.

### `pea tasks done ID`, `pea tasks undone ID`

Completing records when; reopening forgets it. Ticking something already ticked
changes nothing and does not move the moment it happened. Both are the same one
write over the whole task.

### `pea tasks mv ID --after ID|--top`

**A place is a neighbour and not a number**: the task it goes behind, or the top
of the list. The other task has to be in the same list. Moving one task changes
one row, so nobody else's version goes stale.

### `pea tasks rm ID`

Into the Trash. `pea trash restore tasks ID` brings it back, **at the end of its
list** — where it used to sit is a number the list may have reused, and the end
is the one place that is always free.

There is no verb here that deletes a whole list: deleting one is not something
to do by accident from a console, and the browser is where the owner does it.

### `pea search WORDS...`

```sh
pea search arch dec
pea search budget 2026 --application files
```

```
knowledge	0199f0c4-…	Architecture decisions	Where the storage decision lives and
files	0199f0c4-…	architecture.pdf	
```

One question over knowledge pages, tasks, Scratchpad text and file names. What
is inside a file is never looked at ([`docs/api.md`](./api.md), The search).

**The words are arguments and not one quoted string**, because that is how
somebody types a search. Every one of them is matched as a beginning and all of
them have to be found, so `arch dec` finds "Architecture decisions" and does not
find a page that only says "architecture". There is no query language: a stray
quote or ampersand does nothing at all.

One line per finding, tab separated: application, id, title, and the snippet
where there is one. A snippet is a person's own text, so it is folded onto one
line here — a newline in a column would be a row `cut` cannot read. The column
is still there and empty for a file name, which has no body to quote.

`--application` narrows it to one; a word that is not one of the four is exit 2
without a request. `--limit` bounds it, 1 to 100, 20 by default, and `pea` says
on stderr when there was more. An application this credential cannot read, or
one the owner has switched off, contributes nothing and is not an error — the
same as `pea trash list`.

### `pea dashboard`

```sh
pea dashboard
```

```
# tasks (2)
0199f0c7-…	2026-09-14	Einkauf	Milch holen
0199f0c7-…	—	Einkauf	Irgendwann

# knowledge (0)
```

What is useful or pending: the tiles the owner keeps on their home page, and
what is in the ones this credential can read. At most five rows each — it is an
entry point, not a report.

**A tile that is not being drawn has no section at all; one that is drawn and
holds nothing has an empty one.** Those are the two different answers the API
gives (`null` against `[]`), and this keeps them apart rather than flattening
them. A task with no due date shows `—`.

The weather is a separate request and a separate verb, and `pea` says so on
stderr when the tile is on the page.

### `pea weather`

```sh
pea weather
```

```
place        Wuppertal
temperature  16.1°C
sky          Overcast
wind         2.2 km/h
today        14.2°C to 20.3°C
read_at      2026-09-16T07:15:00Z
```

The one thing in this product that comes from outside it. It is a separate
request from `pea dashboard` on purpose: a home page must never wait on a server
somewhere else ([`docs/api.md`](./api.md), The weather).

`read_at` is when the instance asked, not when the observation was made. The
attribution goes to stderr with the other sentences — it is what a free provider
is paid in, so show it wherever the number is shown.

**Three ways there is nothing to say, and none of them is a failure**: the owner
has not said where, the operator has told this instance not to ask anybody, or
the provider did not answer. Each is exit 0 with a sentence on stderr saying
which. A non-zero exit would be this instance claiming somebody else's outage as
its own.

### `pea trash list`

```sh
pea trash list
pea trash list --application knowledge --limit 20
```

```
knowledge	0199f0c4-…	architecture	/notes	by agent "the laptop agent"	expires 2026-10-12
```

One line per entry, tab separated, newest deletion first. Only the applications
this credential may read — and the owner has switched on — are asked, so an
agent sees its own half of the workspace and nothing beyond it. `--json` is the
object the API answered.

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

### What `pea` cannot do

`pea trash purge` does not exist, and neither does emptying the Trash, removing
a piece of lasting content for good, issuing a credential, changing a security
setting, switching an application off, showing or hiding a dashboard tile, or
saying where the weather is for. Those are the owner's, and `pea` holds
agent access. An agent that could permanently remove one Trash entry could
bypass the Trash in two steps instead of one, and the Trash exists precisely so
that an agent acting on the owner's behalf cannot destroy the owner's *lasting*
content.

**`pea scratchpad rm` is the exception, and it is not one.** A Scratchpad entry
is destroyed immediately by design ([`docs/api.md`](./api.md), Deleting sets
content aside), and agent write access has included that since PERSONAL-E2:
`read_write` means deletion, which for the Scratchpad is permanent and for
lasting content is into the Trash. Nothing about that verb is a hole in the
rule above; it is the rule, applied to the one application that keeps nothing.

The browser is where the owner does the rest.

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

### Saved links

`pea bookmarks ls` lists a bounded page; `--json` includes `next_offset`, and
`--offset` continues it. Use `--folder ID` (including descendants),
`--folder unsorted`, `--favorites`, and `--sort title|updated|created|rank`.
`pea bookmarks search WORDS...` searches saved titles, URLs, descriptions and
folder paths with the same word-prefix/all-words rules as global search.

```sh
pea bookmarks add https://example.com/guide --title Guide
pea bookmarks get ID --json
pea bookmarks edit ID --description 'A useful reference' --link https://example.com/new
pea bookmarks move ID --folder FOLDER_ID
pea bookmarks favorite ID --after ANOTHER_FAVORITE_ID
pea bookmarks unfavorite ID
pea bookmarks rm ID
pea trash restore bookmarks ID
pea bookmarks folders ls
pea bookmarks folders add Research --parent PARENT_ID
pea bookmarks folders edit FOLDER_ID --name References
pea bookmarks folders move FOLDER_ID --parent root
pea bookmarks folders rm FOLDER_ID
```

Writes read the current ETag first; `--if-match` can require a version held by a
script. Editing still reads fields that were not supplied. A stale write uses
the existing stale-version exit code and is never retried silently.
New links default their title to the domain and print their stable ID.

Private folders inherit visibility to every descendant. Add `--include-private`
explicitly to each invocation that needs them, including `search`, `dashboard`,
`trash list` and `trash restore`. Creating a private folder uses both
`--private` and `--include-private`. This flag is never persisted, does not grant
application permissions, and describes a visibility filter rather than separate
encryption. Moving an entry into a public folder can make it visible to ordinary
calls. Reading, listing and searching never record an opening or fetch a website.

Preview browser HTML with `pea bookmarks import --file bookmarks.html --json`.
Then confirm the reviewed plan with the same file and optional `--folder ID`,
adding `--confirm --preview-hash HASH`. `--file -` reads UTF-8 from stdin.
Changed files or visible collections require a fresh preview.

`pea bookmarks export --out bookmarks.html` creates a new private-permission
file; existing files are not overwritten. `--out -` writes HTML to stdout,
while `--json` returns the structured export document. Exporting private links
requires both `--include-private` and `--export-private`; otherwise the export
contains public entries only. The file remains unencrypted and does not retain
private markings, favorites, tags or reading status. Transfer limits are listed
in `pea bookmarks import --help` and `docs/api.md`.

Tags use repeated options: `pea bookmarks add URL --tag work --tag research`.
On `edit`, supplied `--tag` values replace the set; `--clear-tags` removes it.
Leaving both options out preserves existing tags. On `ls` and `search`, repeated
`--tag` options require all names and combine with other filters.
`pea bookmarks tags` prints names and counts from visible bookmarks only;
`--include-private` follows the same request-local rules as other commands.

`pea bookmarks add URL --read-later` adds to the reading list.
`pea bookmarks reading` lists it newest first and accepts the same tags/search/
folder filters as `ls`. `pea bookmarks read-later ID` marks an existing link;
`pea bookmarks read ID` marks it read without opening or deleting it.
Both accept `--if-match`. To undo while retaining its old queue position, use
`read-later ID --queued-at OLD_READ_LATER_AT --if-match VERSION_AFTER_MARK_READ`.
`edit --read-later=false` can also clear the status. Private entries always need
`--include-private`.

`pea bookmarks duplicates --json` shows visible groups with complete metadata
and reviewed versions. Page with `--offset`; for groups larger than 50 copies,
use `--url URL --member-offset OFFSET`. To clean up, write an explicit selection
file with `{"keep":"ID","remove":[{"id":"ID","updated_at":"TIMESTAMP"}]}`
using those reviewed versions, then run
`pea bookmarks cleanup --selection selection.json --if-match KEEPER_UPDATED_AT --confirm`.
Use `--selection -` for stdin and `--include-private` when reviewing private
copies. Cleanup does not reread or retry versions: any changed or inaccessible
selection refuses the entire action. The keeper stays unchanged and selected
copies remain individually recoverable through Trash; statistics are not merged.
