# personalaffe

A private workspace belonging to one person: a Scratchpad, a Knowledge base,
Tasks and Files, reachable from a browser, from an HTTP API and from a console.
[`VISION.md`](VISION.md) says what it is and what it deliberately is not;
[`CONTEXT.md`](CONTEXT.md) is the language it uses;
[`docs/mvp-plan.md`](docs/mvp-plan.md) says in what order it is built.

> **It is not a workspace yet.** What exists today is the foundation of
> PERSONAL-E1 — a .NET host, a PostgreSQL schema that migrates itself, a
> checked-in HTTP contract, and a web page and a CLI that read it — and the
> first half of PERSONAL-E2: an instance is claimed once by its one owner, who
> signs in with an email address and a password and, if they want one, a code
> from an authenticator, and everything but five operations is behind that door.
> An agent can be let in with a named token and a permission per application,
> and there is still no content of any kind for it to reach —
> **do not put anything personal in an instance of it.** The rest of the door is
> PERSONAL-E2's, the content safeguards PERSONAL-E3's.

[`docs/codebase.md`](docs/codebase.md) is where the code lives and which way
its dependencies point; [`docs/api.md`](docs/api.md) is the HTTP surface, its
conventions and its errors. Read them before adding a file or an endpoint.

## Prerequisites

Three toolchains and a Docker, and each is pinned in exactly one place so that
this list cannot drift from what actually builds:

| | Version | Pinned by |
| --- | --- | --- |
| .NET SDK | 10.0.100 or a later feature band | [`global.json`](global.json) |
| Node | 24 | the CI job and the Dockerfile's build stage |
| Go | 1.27 | [`src/cli/go.mod`](src/cli/go.mod) |
| Docker | any that runs containers | — |

PostgreSQL is not in the list: it arrives as a container, both for development
and for the tests, which bring up their own with Testcontainers.

Nothing else is needed. No other affe product has to be running, and there is no
mail server, message broker or object store anywhere in this.

## Running the backend

Everything below is run from the repository root.

```sh
# the development database — postgres:18 on port 55732, this product's own
docker compose -f deploy/docker-compose.dev.yml up -d

dotnet restore Personalaffe.slnx
dotnet build Personalaffe.slnx -c Release

# the instance, against the connection string in appsettings.Development.json
dotnet run --project src/Personalaffe.Api

curl http://localhost:5000/api/version         # what this build calls itself
curl http://localhost:5000/api/health/live     # the process is up
curl http://localhost:5000/api/health/ready    # …and the schema is current
curl http://localhost:5000/api/openapi/v1.json # the contract it serves
```

The migrations apply themselves on start, so a fresh database needs no step of
its own. Stopping the database container keeps its volume; removing the volume
is what throws the data away:

```sh
docker compose -f deploy/docker-compose.dev.yml down     # keeps the data
docker compose -f deploy/docker-compose.dev.yml down -v  # destroys it
```

## Testing

```sh
dotnet test tests/Personalaffe.UnitTests         # seconds, nothing installed
dotnet test tests/Personalaffe.IntegrationTests  # Testcontainers brings up Postgres
```

The integration tests bring up their own PostgreSQL and do not use the
development database, so the two never interfere.

## Running the web application

The two toolchains run side by side in development: the .NET host answers the
API, and Vite serves the application and forwards `/api` to it.

```sh
dotnet run --project src/Personalaffe.Api   # in one terminal, on :5000

cd src/web
npm ci
npm run dev                                 # in another, on :5173
```

`npm run build` writes the built application into
`src/Personalaffe.Api/wwwroot/`, which the host serves — so after one build a
single `dotnet run` gives the whole product on `:5000`. The image does the same
in two stages.

```sh
cd src/web
npm run typecheck
npm run lint
npm run test
npm run build
```

Each of those generates the API layer from the contract first, which is why
`src/web/src/api/schema.d.ts` is not committed.

## Running the CLI

`pea` is a client of the public API and nothing else. It is built from its own
module and needs no part of the .NET build.

```sh
cd src/cli
go generate ./...        # the client of the contract; not committed
go vet ./... && go test ./...
go build -o pea ./cmd/pea

PERSONALAFFE_URL=http://localhost:5000 ./pea version
```

[`docs/cli.md`](docs/cli.md) has the configuration ladders, the input rules and
the exit codes.

## The contract

`docs/api/openapi.json` is checked in and captured from a running instance, and
both API clients are generated from it. A change to an endpoint is a change to
that document, in the same commit:

```sh
# rewrite it from a running instance, and pass
PERSONALAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests
```

Without the variable the same test fails when the instance serves anything else.
[`docs/api.md`](docs/api.md) has the conventions, the error document and the
generation commands.

## Adding a migration

No running instance anywhere: `dotnet ef` reads the model, not a database.

```sh
dotnet tool restore
dotnet ef migrations add TheThingItAdds \
  --project src/Personalaffe.Infrastructure \
  --output-dir Persistence/Migrations
```

Migrations only run forward. Going back a version means restoring the backup
taken before the upgrade, and an instance started against a schema a newer
build wrote refuses to serve rather than guessing.

## Configuration

| Variable | What it is |
| --- | --- |
| `ConnectionStrings__Postgres` | The PostgreSQL this instance keeps its data in. Required; the instance refuses to start without it. |
| `PERSONALAFFE_LOG_LEVEL` | `Verbose`, `Debug`, `Information` (the default), `Warning`, `Error` or `Fatal`. |

A value the instance will not accept stops the start with one line naming the
variable. The connection string is never written to the log: what is printed is
the same string with every credential replaced by `***`.

## Running it as an installation

One application container and one PostgreSQL:

```sh
cp deploy/.env.example deploy/.env      # and set POSTGRES_PASSWORD
docker build -f deploy/Dockerfile -t personalaffe:local .
docker compose -f deploy/docker-compose.yml up -d
```

[`docs/operations.md`](docs/operations.md) has the variables, the reverse-proxy
boundary, the two volumes and which commands destroy them.

## Checking that it hangs together

With an instance running, one script asks the six questions nothing else answers
from the outside:

```sh
scripts/smoke.sh                          # or scripts/smoke.sh http://127.0.0.1:8080
```

It checks that the API answers, that liveness and readiness both do, that the
web application is served from the same origin, that an unknown address under
`/api` is still an API error, that the contract the instance serves is the one
checked in, and that both clients generate from that document — with `pea`,
built there and then, reporting the same version the browser would read. It
writes nothing into the repository and needs no credential.

## Known limits

Everything here is the foundation of PERSONAL-E1 and nothing more.

- **There is no authentication and no owner.** Every address is open to whoever
  can reach the port. This is PERSONAL-E2's, and until it lands an instance
  belongs on a machine you own, published on loopback.
- **There is no content.** No scratchpad, no knowledge pages, no tasks, no
  files: the database carries one migration and it creates no table. The four
  applications are PERSONAL-E5 through PERSONAL-E8.
- **Nothing is recoverable, because nothing is stored.** Trash, revision
  history and the guard against stale writes are PERSONAL-E3's; the codes they
  will use are already in the contract.
- **The file storage volume is checked but never written to.** The Files
  application is PERSONAL-E6's.
- **There is no release.** No image is published anywhere, and CI deliberately
  has no credential to publish one with. Release artifacts are PERSONAL-E10's.
- **Backups are two volumes and no procedure.** Taking them consistently
  together is PERSONAL-E10's.

Where each of those plugs in is written down in
[`docs/codebase.md`](docs/codebase.md), under *What the next epics plug into*.

## Where this came from

The stack, and most of the conventions in it, are adopted from the two sibling
products rather than chosen again
([ADR 0001](docs/adr/0001-adopt-the-existing-affe-stack-and-components.md)).
Which parts came from where — with the revisions inspected, what was adapted and
what was deliberately left behind — is the table at the top of
[`docs/codebase.md`](docs/codebase.md). Nothing is shared at build time: there is
no cross-product library and no runtime dependency on another affe product.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs the commands above
on every push and every pull request: the two .NET test projects, the web
workspace, the CLI, the contract against a real PostgreSQL, and the image with a
smoke test that starts it through the Compose file and checks that one container
serves both halves. It publishes nothing and holds no credential.
[`docs/codebase.md`](docs/codebase.md) has the job-by-job table.

## Licence

MIT. See [LICENSE](LICENSE).
