# personalaffe

A private workspace belonging to one person: a Scratchpad, a Knowledge base,
Tasks and Files, reachable from a browser, from an HTTP API and from a console.
[`VISION.md`](VISION.md) says what it is and what it deliberately is not;
[`CONTEXT.md`](CONTEXT.md) is the language it uses;
[`docs/mvp-plan.md`](docs/mvp-plan.md) says in what order it is built.

> **It is not a workspace yet.** What exists today is the foundation of
> PERSONAL-E1: a .NET host, a PostgreSQL schema that migrates itself, a checked-in
> HTTP contract, and three operations — a version and two health checks. There
> is no authentication, no owner, and no content —
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

## Licence

MIT. See [LICENSE](LICENSE).
