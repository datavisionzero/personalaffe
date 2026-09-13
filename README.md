# personalaffe

A private workspace belonging to one person: a Scratchpad, a Knowledge base,
Tasks and Files, reachable from a browser, from an HTTP API and from a console.
[`VISION.md`](VISION.md) says what it is and what it deliberately is not;
[`CONTEXT.md`](CONTEXT.md) is the language it uses;
[`docs/mvp-plan.md`](docs/mvp-plan.md) says in what order it is built.

> **It is not a workspace yet.** What exists today is the foundation of
> PERSONAL-E1: a .NET host, a PostgreSQL schema that migrates itself, a checked-in
> HTTP contract, three operations — a version and two health checks — and a web
> page and a CLI that read them. There is no authentication, no owner, and no
> content —
> **do not put anything personal in an instance of it.** Authentication is
> PERSONAL-E2's, the content safeguards PERSONAL-E3's.

[`docs/codebase.md`](docs/codebase.md) is where the code lives and which way
its dependencies point; [`docs/api.md`](docs/api.md) is the HTTP surface, its
conventions and its errors. Read them before adding a file or an endpoint.

## Running the backend

Everything below is run from the repository root, and needs the .NET SDK of
[`global.json`](global.json) and a Docker that can run containers.

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

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs the commands above
on every push and every pull request: the two .NET test projects, the web
workspace, the CLI, the contract against a real PostgreSQL, and the image with a
smoke test that starts it through the Compose file and checks that one container
serves both halves. It publishes nothing and holds no credential.
[`docs/codebase.md`](docs/codebase.md) has the job-by-job table.

## Licence

MIT. See [LICENSE](LICENSE).
