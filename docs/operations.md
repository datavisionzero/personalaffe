# Running an instance

One application container and one PostgreSQL, beside two volumes that are the
whole of what has to be backed up. Nothing else: no other affe product has to be
running, no message broker, no object store, no mail server.

What it wants from the machine it runs on is Docker, somewhere to keep two
files, and a name in DNS pointing at it. **It terminates no TLS and asks for
none** — the proxy you already run does that, and
[Behind a reverse proxy](#behind-a-reverse-proxy) is a worked configuration for
one, end to end.

## From nothing to a working instance

Seven steps, in order. None of them is guessed and none of them needs anything
this repository has and you do not; the whole of what you provide is a password,
a certificate and a name.

### 1. The password, and the file it goes in

Everything an operator sets is `deploy/.env`, beside the Compose file. Copy the
example — it lists every variable with its default and what it is for — and set
the one value that has none.

```sh
cp deploy/.env.example deploy/.env
openssl rand -base64 33          # put it after POSTGRES_PASSWORD= in that file
```

**No secret in this product has a default.** An instance whose database password
is missing does not start with a weak one; it stops, before it opens a socket,
with a line saying which variable and why. That is the same answer for every
variable below.

### 2. The image

A release publishes one, and naming the version is the whole step:

```sh
# in deploy/.env
PERSONALAFFE_IMAGE=ghcr.io/datavisionzero/personalaffe:0.1.0
```

[`docs/install.md`](install.md) is that route from beginning to end, and it
assumes no checkout at all. From a checkout, the image is built rather than
pulled, which is what the Compose file's default means:

```sh
docker build -f deploy/Dockerfile -t personalaffe:local .
```

### 3. Up, and waited for

```sh
docker compose -f deploy/docker-compose.yml up -d --wait
```

`--wait` and not `-d` alone. Without it the command returns when the containers
have been *started*, which is before PostgreSQL has accepted a connection and
before the migrations have run — and an operator reading "done" then curls a
port that answers nothing. With it, the command waits for the health check that
is already in the Compose file and comes back non-zero if the instance never
becomes ready, which is what makes this line safe to put in a script.

The application waits for PostgreSQL to be healthy, then checks the file storage
root, then applies any pending migrations, and only then serves. An instance
that could not do one of those does not start and says which in its log.

```sh
curl http://127.0.0.1:8080/api/health/ready     # {"status":"ready"}
curl http://127.0.0.1:8080/api/version
```

**The port is on loopback and that is deliberate.** `PERSONALAFFE_PORT` is the
whole left half of the published port, so the default `127.0.0.1:8080` binds
there and nowhere else: a reverse proxy on the same machine reaches the
instance, and the internet does not reach it except through that proxy. There is
no step below that changes this.

### 4. The proxy in front of it

The proxy is yours, the certificate is yours, and
[Behind a reverse proxy](#behind-a-reverse-proxy) is a configuration that works
for one of them and says what the other would differ in. Put it in place and
confirm that the name resolves and the certificate is valid before going on:

```sh
curl -sS https://workspace.example.com/api/health/ready
```

### 5. Tell the instance about the proxy

`X-Forwarded-For` is a header any client can write, so nothing is believed until
you name the proxy. Two lines in `deploy/.env`, and then the containers are
recreated with them:

```sh
PERSONALAFFE_TRUSTED_PROXY=all                            # see below
PERSONALAFFE_PUBLIC_URL=https://workspace.example.com
```

```sh
docker compose -f deploy/docker-compose.yml up -d --wait
```

**Before claiming it, not after.** The throttle on failed sign-ins is built on
the caller's address, and an instance that believes nothing counts every attempt
against the proxy — which is one address for everybody. `all` is what belongs
here when the port is on loopback: the proxy is then the only thing that can
reach the instance, so whoever connects *is* the proxy.

### 6. Claim it

A fresh instance belongs to nobody, and the first thing done with it is the
one-time setup: an email address, which is the login identifier and nothing to
do with sending mail, and a password of at least twelve characters. Open
`https://workspace.example.com/` and it asks; from a terminal it is one request:

```sh
curl -X POST https://workspace.example.com/api/setup \
  -H 'content-type: application/json' \
  -d '{"email": "owner@example.com", "password": "correct horse battery staple"}'
```

**It works exactly once.** There is one owner, there is no second account, and a
second attempt is refused. An owner who has lost their password gets back in
through the machine this runs on, below; there is no password-reset mail,
because there is no mail.

Sign in, and — if you want one — enrol a second factor on the security screen.
It is optional, the recovery codes it gives you are shown once, and
[When the owner is locked out](#when-the-owner-is-locked-out) is what stands
behind both.

### 7. Switch the applications on, and let an agent in

Four switches in Settings — Scratchpad, Files, Knowledge, Tasks — each of which
takes its application out of the web application, the API, the CLI, the search
and the home page while it is off. Retention keeps running regardless; switching
something off never quietly erases what has not expired.

Agent access is on the same screen: a named, revocable authorization with no
access, read access or read/write access **per application**. It is issued once
and shown once, it is not a second human account, and it cannot issue
credentials, change security settings, empty the Trash or reset the instance.
[`docs/cli.md`](./cli.md) is what to do with one.

### Proving it, from outside

```sh
scripts/smoke.sh https://workspace.example.com
```

Ten checks against the address an owner actually uses, and every one of them
is an operation that answers before anything has authenticated: that the API
answers and says what it is, that liveness and readiness both answer, that the
door in front of everything else is shut, that a body the reader cannot make
sense of is refused as the document the contract promises rather than as a bare
status, that every answer carries the headers that say what a browser may do
with it — which is the check that catches a proxy in front of this instance
stripping them — that the web application is served from the same origin, that an unknown
address under `/api` is still an API error and not the page, that the contract
the instance serves is the one that is checked in, and that `pea` — built there
and then from that document — reports the same version the browser reads. It
writes nothing into the repository and needs no credential; the two malformed
bodies it sends are refused while the reader is still parsing them, so neither
can claim an instance that has no owner.

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

Eleven variables, and the instance reads them in one block before it opens a
socket — so a value it will not accept stops the start with the one line that
names the variable, rather than with a stack trace out of the first request. The
fourth column is that line, minus the ` The instance will not start.` every one
of them ends with.

| Variable | Default | What it is | When it is wrong |
| --- | --- | --- | --- |
| `ConnectionStrings__Postgres` | — | **Required.** The database. Never written to a log: what is printed is the same string with every credential replaced by `***`. | `… is not set. It is the PostgreSQL this instance keeps its data in, for example Host=db;Port=5432;Database=personalaffe;Username=personalaffe;Password=…`, or `… names no host.` — and never the value. |
| `PERSONALAFFE_STORAGE_ROOT` | `/var/lib/personalaffe/files` in the image | Where the owner's files go. Must be writable by the user the container runs as, and must not be under the static web root. | `… is not a usable path.` A path that is fine but unwritable is caught a moment later, by the check at startup, which names the path and the variable. |
| `PERSONALAFFE_MAX_FILE_MIB` | `64` | Whole mebibytes, 1 to 1048576. How large one stored file may be. | `… is a whole number of mebibytes.`, `… is between 1 and 1048576 MiB.`, or `… is larger than PERSONALAFFE_MAX_STORAGE_MIB, so no file could ever be stored.` |
| `PERSONALAFFE_MAX_STORAGE_MIB` | `5120` | Whole mebibytes, 1 to 1048576. How much the Files application may store in all, what is in the Trash included. Must not be smaller than the per-file limit. | The same two lines, about this variable. |
| `PERSONALAFFE_TRASH_RETENTION` | `30` | Whole days, 1 to 3650. How long deleted knowledge pages, tasks, lists, files and folders stay recoverable. See below. | `… is a whole number of days.` or `… is between 1 and 3650 days.` |
| `PERSONALAFFE_SCRATCHPAD_RETENTION` | `7` | Whole days, 1 to 3650. How long an unpinned Scratchpad entry lasts after it was last changed. See below. | The same two lines, about this variable. |
| `PERSONALAFFE_WEATHER` | `on` | `on` or `off`. Whether this instance asks anybody outside it about the weather. Off, and nothing in this product opens a socket to anywhere. See below. | `… is on or off.` |
| `PERSONALAFFE_WEATHER_FRESHNESS` | `15` | Whole minutes, 1 to 1440. How long a reading is held before the provider is asked again. | `… is a whole number of minutes.` or `… is between 1 and 1440 minutes.` |
| `PERSONALAFFE_TRUSTED_PROXY` | unset | Which peers may speak for the caller: an address, a CIDR network, a comma-separated list of either, or `all`. Unset, nobody may. See below. | `…: 10.0.0.999 is neither an IP address nor ``all``.`, or `… neither a CIDR network nor ``all``.` for an entry with a `/` in it. |
| `PERSONALAFFE_PUBLIC_URL` | unset | Where this instance is reached, like `https://workspace.example.com`. Optional: what it buys is a stricter check on writes made from a browser, which without it are checked against the host alone. It is never used to build a link. | `… is workspace.example.com, which is not an address this instance can be reached at: scheme and host, like https://workspace.example.com.` |
| `PERSONALAFFE_LOG_LEVEL` | `Information` | `Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`. | `… is "chatty", which is not a level. It is one of: …` |

Two more are read by the Compose file rather than by the instance, and mean
nothing to a `docker run` that does not use it:

| Variable | Default | What it is |
| --- | --- | --- |
| `PERSONALAFFE_PORT` | `127.0.0.1:8080` | The whole left half of the published port, so an address in front of it binds there and nowhere else. |
| `PERSONALAFFE_IMAGE` | `personalaffe:local` | The image this instance runs. There is no published release yet, so the default is what a local `docker build` produces. |

Overriding any of them is a line in `deploy/.env` followed by
`docker compose -f deploy/docker-compose.yml up -d --wait`.

**This table is checked rather than maintained.** A variable the code reads and
this table does not list, or the other way round, fails
`TheVariablesAreDocumentedTests` in the unit suite — along with the same
comparison against `deploy/.env.example` and against the environment the Compose
file passes through. A document nobody can run is not a document an operator can
trust.

## What the environment does not decide

There is a twelfth variable, and it is not in the table above because setting it
does nothing an operator can observe: `ASPNETCORE_ENVIRONMENT`. The image never
sets it, so it is `Production`.

**That is not free, and it was not always true.** ASP.NET Core decides a handful
of things by that name, and one of them was wrong in every build this product
had shipped before PERSONAL-70. `RouteHandlerOptions.ThrowOnBadRequest` defaults
to on under `Development` and off everywhere else, and off means a request body
the reader cannot bind is answered by the framework itself — an empty `400`,
before the one place that writes a problem document is ever reached.
[`docs/api.md`](./api.md) opens with "every refusal is one document"; two of them
were a bare status. No test could see it, because a test host runs as
`Development`, so five suites asserted a contract that held only in the
configuration nobody installs.

Both defaults that key off the name are pinned in `Program.cs`:

| What the framework decides by the name | Left to it | Here |
| --- | --- | --- |
| `RouteHandlerOptions.ThrowOnBadRequest` | on under `Development`, off elsewhere | **on**, so a body that cannot be bound is `unknown-field` or `validation`, as the document [`docs/api.md`](./api.md) describes |
| `ServiceProviderOptions.ValidateOnBuild` and `ValidateScopes` | on under `Development`, off elsewhere | **on**, so a service graph this instance cannot build refuses the start, and a scoped service captured by a singleton is a refusal rather than a database connection that lives as long as the process |

`TheEnvironmentDecidesNothingTests` starts an instance as `Production` and asks
it — the only suite in this repository that does, and it says out loud which
environment it got, because a lever that silently stopped working would leave a
file of tests that pass by testing `Development` twice. `scripts/smoke.sh` and
the image job in CI ask the same of a running instance, in two HTTP calls.

Four things are still decided by the name, and none of them reaches a running
container:

- **`appsettings.Development.json`** is read under that name only. The image
  does not contain the file: `deploy/Dockerfile.dockerignore` keeps it out of
  the build context and `deploy/Dockerfile` deletes it again after publishing,
  because it carries a local connection string.
- **User secrets** — `UserSecretsId` in `Personalaffe.Api.csproj` — are read
  under that name only, out of the developer's home directory. There is no such
  store in a container.
- **The developer exception page** is put in front of everything under that
  name. It never sees anything: `UseExceptionHandler` sits inside it and answers
  every exception with a problem document, which is what
  `A_bug_is_still_a_bug_and_a_refusal_is_still_a_refusal` holds.
- **The static web assets manifest** is loaded under that name only, which is
  how files a referenced project or package contributes to `wwwroot` are found
  before a publish. Nothing contributes any: the web application is built by its
  own toolchain into `wwwroot` — by `npm run build` locally, by the first stage
  of `deploy/Dockerfile` in the image — and what serves it is the same
  `UseStaticFiles` either way.

And two differences that are not about the name at all, but about where a test
runs. Both are known, and both are covered somewhere the subject is real:

- **The suites reach the instance in-process**, through the test host rather
  than through Kestrel over a socket. What a request line or a header does
  before it reaches routing is Kestrel's, is the same in both, and is nothing
  this product configures — and the image job in CI drives the real container
  over a real port.
- **`pg_dump` and `pg_restore` are in the image and not in the suites.** The
  backup and restore verbs shell out to them, so the integration tests hold
  every refusal and the whole file half without a PostgreSQL client anywhere,
  and `scripts/rehearse-a-restore.sh` runs the real tools against the real image
  on every push. See [Backing it up](#backing-it-up).

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

**Three of the four applications fill the Trash**: Files, Knowledge and Tasks.
The Scratchpad deliberately never puts anything there — what it deletes is
destroyed. A page goes with its history, a list with its tasks, a folder with
everything in it. When the sweep removes a file, it removes the row and then the
bytes — in that order, so that an instance killed between the two leaves bytes
nobody points at rather than a row whose file is missing. The tidy-up below is
what takes those away.

## The storage volume tidies itself up

Storing a file writes the bytes first and the row second, which is what keeps an
instance killed mid-upload from leaving a row whose file is gone. What it can
leave instead is bytes nothing points at: an upload whose connection went away,
or one that landed a moment before the process died. **A third sweep, in the
same hourly loop, removes them.**

```
Tidied the storage volume: removed 2 file(s) nothing pointed at.
```

It removes two things and nothing else: an unfinished upload under `incoming/`,
and a file under `files/` whose row is gone. **Both only when they have not been
written to for an hour**, which is what keeps an upload still arriving safe —
far longer than any upload this instance accepts.

**What it does not recognise, it leaves alone.** A file under the storage root
whose name is not one this product writes — a restore somebody unpacked by hand,
a note beside the volume — is an operator's own and is never removed. A sweep
that deleted what it did not recognise would eventually delete something that
mattered.

**A file in the Trash keeps its bytes**, and they count towards
`PERSONALAFFE_MAX_STORAGE_MIB`. They are still the owner's to restore; a quota
that ignored them would be one an owner could walk past by deleting and
uploading in turn, and then find they could not restore what they had deleted.
Emptying the Trash is what gives that room back straight away.

## The one request that leaves this instance

The weather tile is the only thing in personalaffe that talks to anything but
its own database and its own disk. It asks
[Open-Meteo](https://open-meteo.com), which needs no account, no API key and no
secret in this file — which is why it is in the MVP at all
([ADR 0009](adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).

**What is sent is two coordinates.** The point the owner chose, rounded to four
decimal places, and nothing about who is asking: not the owner's address, not a
session, not the label they gave the place. The label is looked up once, in
Settings, and stored; a tile never asks a geocoder.

**`PERSONALAFFE_WEATHER=off` switches it off entirely**, and then nothing in
this product opens a socket to anywhere. An instance on a machine that is
supposed to talk to nobody but its owner is a reasonable thing to run, and a
tile is not a reason to take that away. The tile stays on the home page and says
that this instance does not ask.

**A provider that is slow, down or gone is not an incident.** A refused
connection, a 500, a timeout or a body that is not what was expected is "no
reading": the tile says it does not know, the rest of the home page is
untouched, and the line in the log is at information rather than warning —
somebody else's bad afternoon is not this instance's health, and an operator
reading a log during a real outage should not have to rule it out first.

**It cannot hold the home page up.** The weather has an address of its own,
`GET /api/weather`, and is never part of the answer to `GET /api/dashboard`.
Readings are held for `PERSONALAFFE_WEATHER_FRESHNESS`, so a browser left open
troubles the provider four times an hour by default rather than once every
fifteen seconds.

**Attribution is not optional.** What a free provider is paid in is the credit
that travels with every answer; both clients show it beside the number.

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
`127.0.0.1:8080`. **The proxy is yours.** This product does not ship one, does
not configure one, and does not need a particular one — what follows is one
worked configuration so that nothing has to be guessed, and the paragraph after
it is what a different proxy has to be told instead.

Nothing here needs WebSocket, streaming or HTTP/2 to the origin. Every screen
that keeps up with the instance does so by asking again; an upload and a
download are one ordinary request each. A proxy that can pass a large body and
a large response through is the whole requirement.

### Caddy, end to end

```caddyfile
workspace.example.com {
	# The certificate. Caddy gets one from Let's Encrypt on first start and
	# renews it; `tls internal` instead would be its own CA, for a name that
	# is not public.
	tls owner@example.com

	# The instance, on the loopback address the Compose file publishes.
	reverse_proxy 127.0.0.1:8080 {
		# What the instance reads back out, once PERSONALAFFE_TRUSTED_PROXY
		# names this proxy. Caddy sets all three by itself; they are written
		# here because a configuration that relies on a default is one
		# nobody can check.
		header_up X-Forwarded-For {remote_host}
		header_up X-Forwarded-Proto {scheme}
		header_up X-Forwarded-Host {host}
	}

	# No request body limit. The instance has one — PERSONALAFFE_MAX_FILE_MIB,
	# 64 MiB by default — and refuses an over-large upload with a
	# `problem+json` document both clients understand, while the body is still
	# arriving. A smaller limit here would replace that with the proxy's own
	# error page, which neither client can read and which says nothing about
	# what the limit is.

	encode zstd gzip
}
```

`caddy reload --config /etc/caddy/Caddyfile`, and that is the whole of it.

### What another proxy has to be told instead

nginx is the other one most operators already run. Three of its defaults are
wrong for this, and the rest is the same two headers:

```nginx
server {
	listen 443 ssl;
	server_name workspace.example.com;

	ssl_certificate     /etc/letsencrypt/live/workspace.example.com/fullchain.pem;
	ssl_certificate_key /etc/letsencrypt/live/workspace.example.com/privkey.pem;

	location / {
		proxy_pass http://127.0.0.1:8080;

		proxy_http_version 1.1;
		proxy_set_header Host              $host;
		proxy_set_header X-Forwarded-For   $remote_addr;
		proxy_set_header X-Forwarded-Proto $scheme;

		# nginx's default is 1 MiB, and it refuses a larger upload itself
		# with an HTML page the instance never sees and neither client can
		# read. 0 hands every body through and lets the instance answer.
		client_max_body_size 0;

		# And hands it through as it arrives rather than spooling 64 MiB to
		# disk first, so that a refusal arrives while the upload is still
		# going rather than after all of it has been accepted.
		proxy_request_buffering off;

		# A download of a large file is one long response. The default is
		# sixty seconds between reads, which is generous for a volume on the
		# same machine and not for one that is not.
		proxy_read_timeout 300s;
	}
}
```

Apache, HAProxy and Traefik need the same four things and nothing more: the
certificate, the two forwarded headers, a request body limit that is not smaller
than this instance's, and a read timeout that survives a large download.

### Naming the proxy to the instance

`X-Forwarded-For` is a header any client can write, so **nothing is believed
until you name the proxy**. Unset, every request looks as if it came from
whatever spoke to the socket — honest, if unhelpful, and the safe half of the
trade: **the throttle on failed sign-ins is built on that address**, and behind
an unnamed proxy every caller in the world shares one.

Five failures for one account and twenty from one address, both counted over
fifteen minutes, and what a throttled attempt answers is exactly what a wrong
one answers — saying "you are being throttled" would tell a guesser they had
found something worth guessing at.

```sh
PERSONALAFFE_TRUSTED_PROXY=10.0.0.7            # one proxy
PERSONALAFFE_TRUSTED_PROXY=10.0.0.0/24         # a network of them
PERSONALAFFE_TRUSTED_PROXY=all                 # whoever connects
```

`all` belongs behind a proxy that is the only thing able to reach the instance —
which is what publishing the port on loopback does. One hop is believed and two
headers are read, the caller's address and the scheme; a longer chain is not
this product's to reason about.

`PERSONALAFFE_PUBLIC_URL` is the second half of telling the instance where it
is. It is optional and it is not a link: what it buys is that a write arriving
from a browser has its origin compared against this address whole, rather than
against the host the request happened to arrive at.

## The two volumes, and which commands destroy them

| Volume | What is in it |
| --- | --- |
| `personalaffe-db` | Everything in the database, the file metadata included. |
| `personalaffe-files` | The owner's files, as files, under `files/` — plus `incoming/`, which holds only uploads still arriving. |

They are not the same thing, and **a backup that takes one without the other is
half a backup**: a file is a row in one and bytes in the other, and neither on
its own is the file. A database restored without its volume is a listing of
files that cannot be downloaded; a volume restored without its database is bytes
under names nobody can read, since a file's address is its id and the name the
owner gave it is in the database. Taking them consistently together is
[Backing it up](#backing-it-up), below. `incoming/` is not in a backup: nothing
points at what is in it and the tidy-up empties it within the hour.

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

```
[09:42:18 INF] HTTP POST /api/knowledge/pages responded 409 in 5.0712 ms to 203.0.113.9
```

Method, path, status, duration, and who asked — and **nothing the owner or an
agent wrote**. This is a private workspace and its log is not a second copy of
its contents: no title, no file name, no word of a note, and nothing out of a
request body.

**The status is the one the caller was given.** A refusal — a conflict, a stale
write, something not found, something not permitted — is an ordinary line at
information, because it is the product deciding something rather than the
instance failing at something. What is logged at error is a fault nobody
decided, and it is logged once, with its exception; the caller gets a title and
a status and nothing out of it.

**The address is the caller's** once `PERSONALAFFE_TRUSTED_PROXY` names the
proxy, and the proxy's until it does — the same address the throttle on failed
sign-ins counts, so what an operator reads in the log and what the throttle acts
on cannot be two different things.

## Backing it up

```sh
docker compose -f deploy/docker-compose.yml exec -T personalaffe \
  personalaffe backup --to - > personalaffe-$(date -u +%Y%m%d-%H%M%S).tar
```

One command, one file, both stores. Everything else on this page is about
keeping an instance running; this is the part that is about the day it stops.

**Take it somewhere else.** A backup on the machine the instance runs on
survives a mistake and not a disk, and it is the only copy of what the owner has
written. Because it comes out on standard output it can go straight wherever you
already put things — `| ssh`, `| rclone rcat`, `| gpg -e` — with no directory to
create on the container's behalf, no ownership to get right, and nothing that
`docker compose down -v` can take away with the volumes.

### What is in it

| | |
| --- | --- |
| `database.sql` | Everything in the database: the owner and their security material, agent access and its permissions, application switches, tile preferences, the weather place, every page and its revisions, tasks, Scratchpad entries, file metadata — and the Trash, with its deadlines still where they were. |
| `files/…` | The owner's files, as files, under the same names the database points at. |
| `manifest.json` | What this backup is, and it is written **last**. |

The manifest carries the instance's version, the moment it was taken, what took
the dump, the migrations the schema carried, and a length and a SHA-256 for
every part — the dump and each file. That is enough to tell two backups apart,
to check one without the instance it came from, and for a restore to refuse the
wrong one.

**It is last on purpose.** An archive that stops early has no manifest, and an
archive with no manifest is not a backup — which is how an interrupted one is
told from a finished one by something other than its size.

### The pause, and what it costs

The two stores have to be taken as of one moment, so for as long as it runs the
instance is **held still**: reads keep working and writes are refused with
`503 paused`, a sentence saying what is happening, and a `Retry-After` saying
how long. Both clients say something a person understands; `pea` exits 11.

```
This instance is being backed up, and writes are held for the moment so that its
database and its files are taken as of one moment. Try again in a few seconds;
nothing has been changed and nothing has been lost. (paused)
```

`Retry-After` is how long before it is worth asking again — a few seconds — and
not how long the pause may last. The deadline behind the pause is a ceiling
measured in minutes that a backup of a personal workspace goes nowhere near, and
a client told to wait five minutes for half a second of stillness has been given
a worse answer than none.

**How long is "a moment"** is the size of the workspace: the dump, then the
files, at disk speed. On a fresh instance it is under a second; on a few
gigabytes of files it is however long those take to read. It is not a window you
have to schedule around at night — but it is a window, so it is worth knowing
that the owner writing a page during it will be told to press save again.

**Two things cannot happen during it.** A second backup is refused rather than
taking half of a different moment. And the hourly sweep does not run: the backup
takes the lock the sweep takes, so a purge that would have destroyed content
between the dump and the files cannot start, and one that had already started
finishes before the backup begins. Neither is timed and neither is hoped for.

**It ends whether or not anything ends it.** The pause is a deadline in the
database and not a flag in a process: a backup killed between two of its steps
leaves a row saying "still until 10:05", and at 10:05 the instance is writable
again with nobody there to say so. There is no row to find and edit by hand, and
no verb to undo it.

**The container's health check is unaffected**, deliberately: an instance being
backed up is serving, and a Compose that restarted it would interrupt the one
operation that must not be interrupted.

### What it needs, and what it refuses

The dump is taken by `pg_dump`, which is in the image, because the thing that
dumps a PostgreSQL is a PostgreSQL. Its major version is pinned to the one this
Compose file runs. **A database newer than that is refused before anything is
held still**, with the line that says so — the backup does not start, no writes
are refused, and the way through is the database container's own `pg_dump`:

```sh
docker compose -f deploy/docker-compose.yml exec -T db \
  pg_dump -U personalaffe --no-owner --no-privileges personalaffe > database.sql
```

That is half a backup and must be paired with the volume by hand; it is here for
the operator who has upgraded PostgreSQL ahead of this image, not as an
alternative to the verb.

It also refuses, and holds nothing still, when the database does not carry the
schema this build knows — a backup that could not be described is not one worth
taking — and when the storage root is not where
`PERSONALAFFE_STORAGE_ROOT` says.

## Restoring

**A backup that has not been restored is not yet known to be a backup.** Read
this before you need it, and better still, run it once against something you do
not mind losing — `scripts/rehearse-a-restore.sh` does exactly that and is what
CI runs on every push.

```sh
scripts/restore.sh personalaffe-20260916-143237.tar
```

Three steps and a stop between them, which is why it is a script and not a line:

1. `docker compose stop personalaffe` — the database stays up, because it is
   about to be written to. The instance must not be serving while its own
   database is replaced underneath it.
2. `docker compose run --rm -T personalaffe restore --from -` — a one-off
   container from the same image, with the same volumes and the same connection
   string, reading the archive on its standard input.
3. `docker compose up -d --wait` — and the instance starts on what the backup
   says it is.

### What it refuses, before it changes anything

Everything slow and fallible happens before anything is replaced. The archive is
unpacked beside the storage root and every part of it is weighed and hashed
against the manifest; only then is the database loaded, in one transaction, and
only then do two renames put the files in place.

| It refuses | Because |
| --- | --- |
| an archive with no `manifest.json` | the manifest is written last, so an archive without one is a backup that was interrupted |
| a file the manifest names and the archive does not carry | half a backup is not a backup, and the failure it would otherwise cause is a download that 404s months later |
| a length or a checksum that does not match | the archive is damaged; better to know now |
| a schema this build does not know | the same refusal an instance makes when it meets a database a newer build wrote — there is no downgrade path |
| an instance that is not empty | you may have pointed it at the wrong one |

**Nothing is changed by any of those.** The last one is the only one with a way
past it, and it is deliberately a mouthful:

```sh
scripts/restore.sh backup.tar --over-a-populated-instance
```

That replaces the database and the files of an instance that has something in
it. There is no undo.

### What comes back, and the one thing that does not

| | |
| --- | --- |
| The owner's email, password, second factor and unused recovery codes | as they were |
| Every page, its history, every task, every list, every Scratchpad entry | as they were |
| Every file, byte for byte, under the name the database points at | as they were |
| The Trash | as it was, **with its deadlines still counting from the original moments** — something deleted twenty-nine days before the backup has one day left |
| Agent access, its permission per application, and every revocation | as it was |
| Application switches, dashboard tiles, the weather place | as they were |
| **Every signed-in browser** | **signed out** |

**The sessions are the deliberate exception.** A session in a dump is a browser
that was signed in when the backup was taken — possibly months ago, possibly
signed out on purpose since — and bringing one back would undo a decision the
owner made. **Agent access is the opposite case** and survives: it is a
credential the owner issued and has not revoked, and a restore that silently
broke every automation would be a restore nobody could use. A revoked one stays
revoked, because the dump says so.

So: sign in again afterwards, and check something you would notice the loss of.

**The pause in the dump is not your pause.** A backup is taken while the
instance is held still, so the row saying so is in the dump. The restore lets go
of it, and the instance takes writes from the moment it starts.

### Restoring into a newer build

A backup from an older build restores into a newer one and the instance migrates
it on start, exactly as it migrates anything else — **which is how an upgrade by
restore works**, and how the way back from a failed upgrade works. The other
direction is refused: a build does not serve a schema it has never heard of.

## Upgrading

Three commands, and the first one is not optional. Migrations apply themselves
on start, **only ever forward**; there is no downgrade path, so the backup taken
before the upgrade is the way back and there is no other one.

```sh
docker compose -f deploy/docker-compose.yml exec -T personalaffe \
  personalaffe backup --to - > before-the-upgrade.tar   # first, and not pg_dump
docker compose -f deploy/docker-compose.yml pull        # or rebuild
docker compose -f deploy/docker-compose.yml up -d --wait
```

**`personalaffe backup` and not `pg_dump`.** A dump is the database and nothing
else; what an upgrade could cost you is the database *and* the files, and only
the archive carries both with a manifest tying them together ("Backing it up"
above). A `pg_dump` taken before an upgrade is a rollback that comes back with
every file missing.

The archive lands where you ran the command, which is the host and not either
volume — a way back stored inside the thing you are about to change is not one.

**How long it is down.** The instance stops answering when the old container
does and answers again when the new one has migrated and reported ready. On the
rehearsal below, on a laptop, that is **seven seconds**, of which the migration
is a fraction: most of it is a container starting. A migration that rewrites a
large table is longer, and `up -d --wait` is what tells you it is over — without
`--wait` the command returns while the migration is still running.

**What the log says.** Two lines, and they are the whole of it:

```
[17:18:24 INF] Applying 1 migration(s): ["20260916141952_TheMaintenancePause"]
[17:18:24 INF] Schema is current.
```

A build with nothing to do says `Schema is current; nothing to migrate.`
instead, which is also what the second of two containers says: **two starting at
once do not migrate against each other.** The migration takes a Postgres
advisory lock, so the second waits for the first and then finds the work done.
A restart policy that fires during an upgrade is the ordinary way to get two,
and the rehearsal performs the upgrade with a second container beside the first
on purpose.

### When the upgrade is the thing that went wrong

Going back a version is restoring the backup you took, and it is one command —
the image an instance runs is a variable, so naming the earlier one is the whole
of it:

```sh
PERSONALAFFE_IMAGE=personalaffe:the-one-you-were-on \
  scripts/restore.sh before-the-upgrade.tar --over-a-populated-instance
```

That stops what is serving, puts the archive back with a one-off container from
that image, and starts it again — eight to twelve seconds on the same rehearsal,
for an archive of three megabytes. A larger one takes longer in proportion:
every byte of it is unpacked and hashed against the manifest **before** anything
is replaced. `--over-a-populated-instance` is **the irreversible step**:
everything before it is refusable and refuses ("What it refuses, before it
changes anything"), and past it what the instance held is gone.

**What it costs is the window.** Everything written between the backup and the
rollback is not in the archive and does not come back. Say what that window is
before you open it, and take the backup as late as you can.

You do not have to roll back to discover the upgrade failed halfway: an instance
started against a schema a newer build wrote **refuses to serve rather than
guessing**, and names what it does not know.

```
[17:18:32 FTL] This database was migrated by a newer personalaffe. It carries 1
migration(s) this version does not know about: 20260916141952_TheMaintenancePause.
Start the version that migrated it, or restore the backup taken before the
upgrade — there is no downgrade path. The instance will not start.
```

So putting the old image back on its own is not a rollback. It is a container
that will not start, which is the honest outcome — the schema in front of it is
one it misunderstands, and serving it would be worse.

### The rehearsal

```sh
scripts/rehearse-an-upgrade.sh
```

Fifteen steps, against the images an operator installs, and CI runs it on every
push. Every claim in this section was run rather than written: a life put into an
earlier build through the API, upgraded with a second container starting beside
it, read back out through the same browser session that was open before it — an
upgrade does not sign anybody out, and that is the one place it differs from a
restore — the earlier image put back in front of the new schema to earn the
refusal above, and then, on a second instance, the whole way back: backup,
upgrade, a page written afterwards, rollback, and that page gone.

It builds the earlier builds out of this repository's own history and it
destroys both volumes twice. Do not run it against an installation you care
about.

## The three verbs this image has

`personalaffe recover-owner`, `personalaffe backup` and `personalaffe restore`,
all three above, and nothing else. They are here for the same reason: **their authorization is that somebody is
standing at the machine**, which is the authorization `pg_dump` has and no token,
permission or header reaches. Migrations apply themselves, so there is no verb
for them; a word this binary does not know stops it with a line saying
where to look, rather than starting a second server on a port that is taken.

## The CLI is not in the image

`pea` is a client of the public API and runs wherever you are: a laptop, a CI
runner, an agent's container. It ships as its own binary and needs nothing from
this stack but an address.

```sh
PERSONALAFFE_URL=http://127.0.0.1:8080 pea version
```

The operational verbs `pea` deliberately does not have — migrations, backups —
belong to the binary that has the connection string. An agent's token is not an
authorization to take a copy of everything the owner has ever written, and a
backup that could be started over HTTP would be exactly that.
[`docs/cli.md`](./cli.md) is the rest.
