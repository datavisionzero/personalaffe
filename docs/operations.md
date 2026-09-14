# Running an instance

One application container and one PostgreSQL, beside two volumes that are the
whole of what has to be backed up. Nothing else: no other affe product has to be
running, no message broker, no object store, no mail server.

> **There is a door, and there is nothing behind it yet.** An instance is
> claimed once by its owner and everything but five operations then needs a
> credential — but the second factor, agent access and the four applications are
> still being built. Run it on a machine you own, published on loopback, and put
> nothing personal in it. Public production use waits for the rest of the
> security epic and the release epic ([`docs/mvp-plan.md`](./mvp-plan.md)).

## Starting it

```sh
cp deploy/.env.example deploy/.env      # and set POSTGRES_PASSWORD
docker build -f deploy/Dockerfile -t personalaffe:local .
docker compose -f deploy/docker-compose.yml up -d
```

There is no published image yet, which is why the build is a step of its own;
`PERSONALAFFE_IMAGE` in `deploy/.env` is what names a published one when there
is.

The application waits for PostgreSQL to be healthy, then checks the file storage
root, then applies any pending migrations, and only then serves. An instance
that could not do one of those does not start and says which in its log.

```sh
curl http://127.0.0.1:8080/api/health/ready     # {"status":"ready"}
curl http://127.0.0.1:8080/api/version
curl http://127.0.0.1:8080/api/setup           # {"required":true} until somebody claims it
open  http://127.0.0.1:8080/
```

### Claiming it

A fresh instance belongs to nobody, and the first thing done with it is the
one-time setup: an email address, which is the login identifier and nothing to
do with sending mail, and a password of at least twelve characters.

```sh
curl -X POST http://127.0.0.1:8080/api/setup \
  -H 'content-type: application/json' \
  -d '{"email": "owner@example.com", "password": "correct horse battery staple"}'
```

**It works exactly once.** There is one owner, there is no second account, and a
second attempt is refused. An owner who has lost their password gets back in
through the machine this runs on ([`docs/api.md`](./api.md)); there is no
password-reset mail, because there is no mail.

## When the owner is locked out

The password is gone, the phone with the authenticator on it is gone, and the
recovery codes are on a piece of paper nobody can find. **personalaffe sends no
mail**, so there is no link to click and no address to send one to. What stands
where that would be is a verb on the machine this runs on:

```sh
docker compose -f deploy/docker-compose.yml exec -T personalaffe \
  personalaffe recover-owner --password-file -
```

It reads the new password from standard input, which is what `-` means. Type it,
press Enter, then Ctrl-D. **It is never an argument**: an argument stands in the
shell history of the machine you are standing at, which is the one machine a
locked-out owner is least able to clean up afterwards. A file works too, if the
password is already in one:

```sh
docker compose -f deploy/docker-compose.yml exec -T personalaffe \
  personalaffe recover-owner --password-file /run/secrets/new-password
```

It answers what it did, and what it did is exactly this:

| | |
| --- | --- |
| The password | replaced with the one you gave it |
| The second factor | turned off, and the recovery codes with it |
| Every signed-in browser | signed out |
| Agent access | **untouched** |
| Everything in the workspace | **untouched** |

A new password alone would be no use behind an authenticator that is in a river,
which is why the factor goes too — sign in and enrol one again if you want one.
This is a way back in and not a reset: nothing the owner stored is touched, and
no agent is shut out.

Nothing was changed if it refuses: a password under twelve characters, an
instance nobody has claimed, or a database this build has not migrated each stop
it before it writes anything.

**Its authorization is that you are standing at the machine**, and that is the
whole of it. There is no endpoint, no permission and no token that reaches this —
whoever has the host has the database, which is the same authorization
`pg_dump` has. The owner is shown afterwards, on their security screen, that a
recovery happened and when: a recovery nobody performed is a recovery somebody
else performed.

## The two health checks

`/api/health/live` says the process is running and touches nothing else.
`/api/health/ready` says the database answers and carries the schema this build
knows. Compose's healthcheck uses readiness, not liveness: a container reporting
healthy with no database would be a container nothing is watching.

Neither carries owner data or anything about the host. **Why** a readiness check
failed is in the instance's log at warning, where the operator is.

## What it reads

| Variable | Default | What it is |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | — | Required. The database. The instance refuses to start without it, and never writes it to the log: what is printed is the same string with every credential replaced by `***`. |
| `PERSONALAFFE_STORAGE_ROOT` | `/var/lib/personalaffe/files` in the image | Where the owner's files go. Must be writable by the user the container runs as, and must not be under the static web root. |
| `PERSONALAFFE_TRASH_RETENTION` | `30` | Whole days, 1 to 3650. How long deleted knowledge pages, tasks, lists, files and folders stay recoverable. See below. |
| `PERSONALAFFE_SCRATCHPAD_RETENTION` | `7` | Whole days, 1 to 3650. How long an unpinned Scratchpad entry lasts after it was last changed. See below. |
| `PERSONALAFFE_TRUSTED_PROXY` | unset | Which peers may speak for the caller. See below. |
| `PERSONALAFFE_PUBLIC_URL` | unset | Where this instance is reached, like `https://workspace.example.com`. Optional: what it buys is a stricter check on writes made from a browser, which without it are checked against the host alone. It is never used to build a link. |
| `PERSONALAFFE_LOG_LEVEL` | `Information` | `Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`. |
| `PERSONALAFFE_PORT` | `127.0.0.1:8080` | Compose only: the whole left half of the published port, so an address in front of it binds there and nowhere else. |

A value the instance will not accept stops the start with one line naming the
variable. Overriding any of them is a line in `deploy/.env` followed by
`docker compose up -d`.

## The Trash empties itself

Deleting a knowledge page, a task, a list, a file or a folder puts it in the
Trash rather than destroying it, and `PERSONALAFFE_TRASH_RETENTION` is how long
it stays there. Thirty days by default. A Scratchpad entry is not covered: it is
temporary by definition and its deletion is immediate.

**The sweep runs inside the instance** — no cron, no second container, nothing
for an operator to install. It happens once at start and once an hour after
that, and the interval is not a variable: what an operator might want to change
is how long things are kept, not how often something looks.

**A week of downtime costs nothing.** The sweep works from the deadline and not
from what it last did, so the first one after an outage removes everything that
expired while nothing was running. It is safe to run twice and safe to interrupt.

**Two containers over one database do the work once.** The sweep takes a
Postgres advisory lock, the same way the migrator does; an instance that finds
somebody else sweeping skips that round rather than queueing to redo it.

Changing the variable moves the deadline and nothing else. Shortening it expires
more on the next sweep; lengthening it never brings back what is already gone.
A value the instance will not accept — not a whole number, or outside the two
bounds — stops the start with one line naming the variable.

What a sweep removed is one line in the log per sweep, at `Information`: counts
per application, and never a name or a word of what the owner wrote.

```
Swept the Trash: removed 4 expired item(s), Knowledge: 3, Files: 1.
```

**An instance today has three of the four applications still to come**
(`docs/mvp-plan.md`), so what the Trash sweep removes is whatever the Scratchpad
put there — which is nothing, deliberately, because a Scratchpad entry is never
in the Trash.

## The Scratchpad empties itself too

`PERSONALAFFE_SCRATCHPAD_RETENTION` is how long an unpinned Scratchpad entry
lasts. Seven days by default.

**These are two periods and not one.** Thirty days for what was deleted and can
be had back, seven for what was never meant to last. They answer different
questions — how long a mistake can be undone, and how long a note pasted between
two devices is worth keeping — and one number would have to be wrong for one of
them.

**The period counts from when an entry was last changed**, not from when it was
captured. An entry edited this morning does not disappear tonight because it was
pasted a week ago, and an entry unpinned after a year gets a full period from the
moment it was unpinned rather than going on the next sweep.

**A pinned entry never expires.** Pinning is the owner's answer to "keep this",
and it is the only one: there is no per-entry expiry and no notice before
something goes. The list shows `expires_at` for every entry that has one.

**Deletion here is immediate and final.** A Scratchpad entry is not set aside,
does not appear in the Trash, and cannot be restored — by the owner, by an
agent, or by an operator with a shell. The backup is the only way back, which is
the same thing as saying there is none.

It runs in the same loop as the Trash's sweep, on the same clock, at start and
once an hour. The two are independent: one that fails is logged and the other
still runs. A week of downtime costs nothing here either — the sweep works from
the deadline — and switching the Scratchpad off changes nothing about it, which
is the same promise the Trash makes.

```
The Scratchpad keeps an unpinned entry for 7 days after it was last changed.
Swept the Scratchpad: removed 3 expired entry/entries.
```

## Behind a reverse proxy

The instance terminates no TLS and asks for none: put a proxy you already run in
front of it, give the proxy the certificate, and let it talk to
`127.0.0.1:8080`.

`X-Forwarded-For` is a header any client can write, so **nothing is believed
until you name the proxy**. Unset, every request looks as if it came from
whatever spoke to the socket — honest, if unhelpful, and the safe half of the
trade: the throttle on failed sign-ins and the log lines are built on that
address.

```sh
PERSONALAFFE_TRUSTED_PROXY=10.0.0.7            # one proxy
PERSONALAFFE_TRUSTED_PROXY=10.0.0.0/24         # a network of them
PERSONALAFFE_TRUSTED_PROXY=all                 # whoever connects
```

`all` belongs behind a proxy that is the only thing able to reach the instance —
which is what publishing the port on loopback does. One hop is believed and two
headers are read, the caller's address and the scheme; a longer chain is not
this product's to reason about.

## The two volumes, and which commands destroy them

| Volume | What is in it |
| --- | --- |
| `personalaffe-db` | Everything in the database. |
| `personalaffe-files` | The owner's files, as files. |

They are not the same thing, and a backup that takes one without the other is
half a backup. Taking them consistently together is PERSONAL-E10's.

```sh
docker compose -f deploy/docker-compose.yml restart      # harmless
docker compose -f deploy/docker-compose.yml up -d        # harmless; recreates containers
docker compose -f deploy/docker-compose.yml down         # harmless; keeps both volumes
docker compose -f deploy/docker-compose.yml down -v      # DESTROYS both volumes
```

The first three are ordinary operations: a container is a process, and
recreating it keeps every byte. **`down -v` is the one that is not.** It removes
the volumes with the containers, and there is no undo — the owner's files and
the whole database go with it. So does `docker volume rm`.

### Volume ownership

The container runs as a non-root user, and the storage volume is created owned
by that user. A volume you mount from the host instead — a bind mount — is owned
by whoever owns the directory on the host, and the instance will refuse to start
if it cannot write there. The log says so, naming the path and the variable;
`chown` the directory to the container's user, or mount somewhere it can write.

## The log

Structured, to the console, and to nothing else: `docker compose logs -f
personalaffe`. personalaffe does not depend on another running affe product to
have a log.

The request log carries method, path, status and duration and **nothing the
owner or an agent wrote** — this is a private workspace and its log is not a
second copy of its contents.

## Upgrading

```sh
docker compose -f deploy/docker-compose.yml exec db \
  pg_dump -U personalaffe personalaffe > backup.sql     # first
docker compose -f deploy/docker-compose.yml pull        # or rebuild
docker compose -f deploy/docker-compose.yml up -d
```

Migrations apply themselves on start, **only ever forward**. There is no
downgrade path: going back a version means restoring the backup taken before the
upgrade. An instance started against a schema a newer build wrote refuses to
serve rather than guessing, and says which migrations it has never heard of.

Two containers starting at once do not migrate against each other — the
migration takes a Postgres advisory lock, and the second waits and then finds
nothing to do.

## The one verb this image has

`personalaffe recover-owner` above, and nothing else. Migrations apply
themselves and backups are `pg_dump` beside the container; a word this binary
does not know stops it with a line saying where to look, rather than starting a
second server on a port that is taken.

## The CLI is not in the image

`pea` is a client of the public API and runs wherever you are: a laptop, a CI
runner, an agent's container. It ships as its own binary and needs nothing from
this stack but an address.

```sh
PERSONALAFFE_URL=http://127.0.0.1:8080 pea version
```

The operational verbs `pea` deliberately does not have — migrations, backups —
belong to the binary that has the connection string, and to `pg_dump` beside it.
[`docs/cli.md`](./cli.md) is the rest.
