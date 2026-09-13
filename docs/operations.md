# Running an instance

One application container and one PostgreSQL, beside two volumes that are the
whole of what has to be backed up. Nothing else: no other affe product has to be
running, no message broker, no object store, no mail server.

> **This foundation has no authentication.** PERSONAL-E2 has not landed, so
> every address this serves is open to whoever can reach the port. Run it on a
> machine you own, published on loopback, and put nothing personal in it. Public
> production use waits for the security epic and the release epic
> ([`docs/mvp-plan.md`](./mvp-plan.md)).

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
open  http://127.0.0.1:8080/
```

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
| `PERSONALAFFE_TRUSTED_PROXY` | unset | Which peers may speak for the caller. See below. |
| `PERSONALAFFE_LOG_LEVEL` | `Information` | `Verbose`, `Debug`, `Information`, `Warning`, `Error` or `Fatal`. |
| `PERSONALAFFE_PORT` | `127.0.0.1:8080` | Compose only: the whole left half of the published port, so an address in front of it binds there and nowhere else. |

A value the instance will not accept stops the start with one line naming the
variable. Overriding any of them is a line in `deploy/.env` followed by
`docker compose up -d`.

## Behind a reverse proxy

The instance terminates no TLS and asks for none: put a proxy you already run in
front of it, give the proxy the certificate, and let it talk to
`127.0.0.1:8080`.

`X-Forwarded-For` is a header any client can write, so **nothing is believed
until you name the proxy**. Unset, every request looks as if it came from
whatever spoke to the socket — honest, if unhelpful, and the safe half of the
trade: the rate limits and the log lines of PERSONAL-E2 are built on that
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
