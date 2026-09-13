# The codebase

`VISION.md` says what personalaffe is. `docs/mvp-plan.md` says in what order it
is built. This one says where that lives: how the repository is laid out, which
project holds what, which way the dependencies point, and which toolchain builds
what.

**This document is a blueprint for PERSONAL-E1 and is kept accurate as the epic
lands.** Every section below marks what already exists and what is still only
planned. A file that lands somewhere this does not describe means one of the two
is wrong.

Status: the four .NET projects, the two test projects, the self-applying
migrator, the health endpoints (PERSONAL-2), the checked-in contract and the
problem document (PERSONAL-3), the web workspace with one screen (PERSONAL-4)
the Go CLI with two verbs (PERSONAL-5), the image with its Compose topology
(PERSONAL-6) and the CI gate (PERSONAL-7) exist. PERSONAL-8 is the fresh-checkout
verification and the handoff.

## Where this comes from

personalaffe is the fourth product in the same family, and the third one built
on the same foundation. The stack was chosen in
[ADR 0001](./adr/0001-adopt-the-existing-affe-stack-and-components.md); this
section records what was inspected to write the blueprint and what is taken
from each.

| Source | Revision inspected | What is taken |
| --- | --- | --- |
| `planaffe` | `e816afa` (2026-09-13) | The four-layer backend, the checked-in contract with two generated clients, the Go CLI shape, the Markdown editing components, the CI gate written whole before its subjects exist. |
| `hostingaffe` | `530b5c2` (2026-09-11) | The same foundation with planaffe's domain cut out, which is the closer starting point: the `/api` prefix, the layered project files, the startup migrator, the problem document, the Compose and image setup. |

Both are public repositories under `datavisionzero`. Nothing is shared at build
time: there is no cross-product library, and there will not be one
(ADR 0001). What is adopted is copied and adapted, and this table is where a
later reader finds out which copy came from where.

**hostingaffe is the primary reference.** It is itself a copy of planaffe with
planaffe's domain removed, so it is the version of this foundation that has
already had a domain cut out of it once.

### Taken, per area

- **Build and layering** — `Directory.Build.props`, `Directory.Packages.props`,
  `global.json`, `.editorconfig`, the four `.csproj` shapes, and the layering
  test that reads them. Adapted: names, and no `Logaffe.Serilog` sink.
- **Persistence** — `Persistence/SchemaMigrator.cs`, `SchemaVersions.cs`,
  `DesignTimeDbContextFactory.cs`, the `.config/dotnet-tools.json` pin of
  `dotnet-ef`. Adopted as they stand, renamed.
- **HTTP** — `Http/Routes.cs`, `Problems.cs`, `Rfc3339.cs`, `VersionHeader.cs`,
  `OpenApiDocument.cs`, `Hosting/InstanceVersion.cs`,
  `Hosting/SchemaMigrationService.cs`. Adapted: the refusal set is
  personalaffe's own and grows with the epics that need it.
- **Contract** — `docs/api/openapi.json` captured from a running instance, and
  `ContractTests` comparing the two. Adopted wholesale.
- **CLI** — `internal/config`, `internal/exit`, `internal/problem`,
  `internal/client`, `internal/version`, `internal/render`, `internal/cmd/input.go`
  and the `oapi-codegen` generation step. Adapted: the executable is `pea`, the
  variables are `PERSONALAFFE_*`, and there is no project or tenant concept to
  resolve.
- **Web** — the Vite/React/TypeScript workspace, `tsconfig` set, the ESLint
  config, `api/client.ts` over `openapi-fetch`, the Tailwind and Base UI setup.
  Adapted: no session or record screens; the foundation screen is one page.
- **Delivery** — `deploy/Dockerfile`, `deploy/Dockerfile.dockerignore`,
  `deploy/docker-compose.yml`, `deploy/docker-compose.dev.yml`,
  `deploy/.env.example`, `.github/workflows/ci.yml`. Adapted: a file-storage
  volume beside the database volume, and no SMTP service.

### Kept for PERSONAL-E4, not implemented here

The shared editing experience is a later epic's work, and this is the list it
starts from rather than a second selection exercise (ADR 0001). In both sources
under `src/web/src/shared/`:

- `MarkdownField.tsx` — what every screen imports to edit Markdown.
- `Editor.tsx` — the only file that knows CodeMirror; a lazy chunk of its own.
- `StandInEditor.tsx` — what the tests write into, because jsdom lays nothing out.
- `markdownCommands.ts` — what the toolbar does to a selection, as pure functions.
- `Markdown.tsx` — the renderer: `react-markdown`, `remark-gfm`, `remark-breaks`.
- `components/ui/` and `index.css` — the shadcn-generated primitives and the
  token layer, owned by the repository that carries them.

The package versions those need are in both sources' `src/web/package.json` and
are adopted with them. PERSONAL-E4 does not reopen the choice.

### Deliberately not taken

- **planaffe's and hostingaffe's domains.** No projects, issues, epics,
  releases, machines, software, installations or deployments. personalaffe's
  words are in `CONTEXT.md` and nowhere else.
- **The multi-user model.** One owner, and agent access beside them
  (`CONTEXT.md`). No users table with roles, no invitations, no team.
- **SMTP.** Owner recovery is a documented server-side procedure
  (PERSONAL-E2); nothing in personalaffe requires a mail service, and
  `MailKit` is not a dependency.
- **The logaffe sink.** `Logaffe.Serilog` makes an instance depend on another
  running affe product. personalaffe logs to the console, structured, and
  nothing else is required.
- **Idempotency keys, `LISTEN`/`NOTIFY`, the device-login flow, per-project
  scope doors.** Each answers a problem this product does not have yet. If one
  turns out to be needed, it arrives with the epic that needs it.
- **A plugin system or a universal entity framework.** Applications are folders
  with an owner, not a registry.

## The shape of the repository

```
personalaffe/
├─ .github/workflows/          the gate: ci on every push and pull request
├─ deploy/                     Dockerfile, Compose (production and development), .env.example
├─ docs/
│  ├─ adr/                     the decisions
│  ├─ api/openapi.json         the HTTP contract, captured and checked in
│  ├─ codebase.md              this
│  ├─ api.md                   the HTTP surface: conventions, errors, endpoints
│  ├─ cli.md                   `pea`: configuration, input, exit codes, verbs
│  ├─ operations.md            running it: variables, volumes, upgrade, reset
│  └─ mvp-plan.md              what is built, in what order
├─ src/
│  ├─ Personalaffe.Domain/         the rules
│  ├─ Personalaffe.Application/    the acts and the ports they need
│  ├─ Personalaffe.Infrastructure/ Postgres, the file store
│  ├─ Personalaffe.Api/            HTTP and the composition root
│  ├─ cli/                         the Go CLI — `pea`
│  └─ web/                         the single-page application
├─ tests/
│  ├─ Personalaffe.UnitTests/
│  └─ Personalaffe.IntegrationTests/
└─ Personalaffe.slnx           plus global.json and the Directory.* properties
```

`src/` and `tests/` is what a .NET contributor arrives expecting, and both
sources use it. Nothing is gained by inventing something more descriptive.

## The four layers

Dependencies point inward and only inward:

```
Api ──────► Application ──────► Domain
 └────────► Infrastructure ──►
```

**`Personalaffe.Domain` holds the rules and carries no references at all** —
neither project nor package. That emptiness is the cheapest check that nothing
has leaked into it, and `LayeringTests` in the unit tests reads the four project
files and fails the build when a reference points outward. Every type here is a
term in [`CONTEXT.md`](../CONTEXT.md), spelled the way that file spells it: a
type named after an `_Avoid_` word is a naming bug rather than a preference.

**`Personalaffe.Application` holds the acts and the ports.** `Acts/` is one
class per thing a caller does; `Ports/` is one interface per thing the acts need
answered. The clock is `TimeProvider` from the base class libraries rather than
a port of ours.

**`Personalaffe.Infrastructure` answers those ports.** `Persistence/` is EF
Core: the context, one `IEntityTypeConfiguration` per table under
`Configurations/`, one store per port beside it, and `Migrations/` — every schema
change arrives as another one on top, only ever forward. Later epics add
`Files/`, the local file store, and `Security/`, the password hasher.

**`Personalaffe.Api` is HTTP and the composition root.** `Http/` maps the
endpoints, one file per object, plus the cross-cutting pieces that arrive in
PERSONAL-3: `Problems` writing every refusal as one document, `VersionHeader`,
`Rfc3339`. `Hosting/` holds what runs before anything is served — the schema
migration, and later the owner bootstrap, in that order. `Program.cs` is the only
file that knows all four layers.

Implemented: `Domain/` with `Refusal` and `RefusalCode`; `Application/Ports/`
with the two settings records the host validates at startup; `Persistence/` with
the context, the migrator and the first migration; `Hosting/`; and `Http/` with
`Routes`, `Problems`, `Rfc3339`, `VersionHeader`, `OpenApiDocument`,
`InstanceEndpoints` and `HealthEndpoints`.

Planned, not implemented: `Acts/`, `Configurations/`, `Files/`, `Security/`, and
every endpoint but the three the foundation answers.

## Where an application lives

The four applications of `VISION.md` — Scratchpad, Knowledge, Tasks, Files — are
**folders that own their types, acts, stores and endpoints**, not rows in a
registry and not plugins. An application is a folder in each of the four layers
and one in `src/web/src/`:

```
src/Personalaffe.Domain/Scratchpad/         the rules of a scratchpad entry
src/Personalaffe.Application/Acts/Scratchpad/
src/Personalaffe.Infrastructure/Persistence/Configurations/  its table
src/Personalaffe.Api/Http/ScratchpadEndpoints.cs
src/web/src/scratchpad/                     its screens
```

What more than one of them shares sits at the root of the layer it belongs to,
and only once two of them actually need it: the concurrency guard, recoverable
deletion and actor attribution of PERSONAL-E3; the enablement switch of
PERSONAL-E4; the search contribution of PERSONAL-E9. **No generic content
entity.** Four applications that share conventions are not four rows in one
table, and PERSONAL-E3 is explicit that a universal workflow framework is not
what is wanted.

Identity — the owner, agent access and their permissions — is not an
application. It lives at the root of each layer, because every application asks
it the same question.

*Planned. PERSONAL-2 created the folders that had something to put in them and
no others — which is none of these: an empty folder claiming a future module is
a lie the tree tells.*

## The CLI is a client, not a layer

`src/cli/` is an ordinary Go module — `cmd/pea` the binary, `internal/` the
packages. It references nothing in `src/` and knows the instance only through
the client generated from `docs/api/openapi.json`.

```
cmd/pea             the binary
internal/cmd        the command tree, one file per object
internal/client     the HTTP client, the version header, the checks
internal/config     which instance, and as whom
internal/exit       the exit codes
internal/problem    the problem document as `pea` reads it
internal/render     how it prints for a person, and as JSON
internal/version    what this build calls itself
internal/api        the generated client — not committed
```

**The executable is `pea`.** `pa` is planaffe's and `ha` is hostingaffe's; the
three are installed on the same machines, so a third two-letter name that
collides with either would be a daily tax. `pea` is short enough to type all day
and is nobody else's.

**Its configuration namespace is `PERSONALAFFE_`**, and its file is
`~/.config/personalaffe/config.json` — `$PERSONALAFFE_CONFIG` first,
`$XDG_CONFIG_HOME/personalaffe/config.json` next. There is no project or tenant
concept to resolve: one instance belongs to one owner.

The instance is `--url`, then `PERSONALAFFE_URL`, then the instance on disk. The
credential is `PERSONALAFFE_TOKEN`, then a token file the owner named, then the
keychain — the environment first, because that is how an agent receives its own
token. *The credential half is PERSONAL-E2's; PERSONAL-5 builds the ladder and
the precedence, with nothing in the keychain yet.*

Operational verbs that need the database are **not** here: migrations and
backups belong to the .NET binary that has the connection string.

*PERSONAL-5 built the module, the two ladders, the exit table, the text input
and two verbs — `version` and `status`. `internal/keychain` and the sign-in that
fills it are PERSONAL-E2's; the content verbs arrive with their applications.*

## The frontend is built separately and joined once

`src/web/` is a Vite project with its own `package.json`, and nothing in the
.NET build knows it exists. Development runs the two side by side — the Vite dev
server against `dotnet run`, with Vite forwarding `/api` — and the only place
they are joined is `deploy/Dockerfile`, which builds the SPA in a Node stage and
copies it into the published output. A local `npm run build` lands in
`src/Personalaffe.Api/wwwroot/`, which the API serves, so that one `dotnet run`
gives the whole product.

Its layout is one folder per area, as in both sources: `shell` owns the frame
and the routes, `shared` the Markdown field and the editor behind it, `api` the
generated client and its wrapper, `components/ui` the owned primitives. The four
applications get a folder each when they arrive.

*PERSONAL-4 created `api/`, `shell/`, `shared/` and one screen. The four
application folders, the navigation, the settings and the editor are
PERSONAL-E4's and later.*

Two libraries the blueprint names are **not installed yet**: Base UI and
CodeMirror, with `react-markdown` and its two remark plugins. Nothing on the
foundation screen is a dialog, a popup or a Markdown field, and a dependency
added before something uses it is a decision with no reason attached. The
choice is not reopened when they arrive — the versions are in both sources'
`src/web/package.json` and the components are listed above.

## The HTTP contract is an artifact, not an intention

**Every endpoint is under `/api`.** Everything else is the web application's.
This is hostingaffe's separation rather than planaffe's prefix-free one, and the
reason is this product: `/files`, `/tasks` and `/pages` are addresses the SPA
wants for its own screens, and a prefix is what keeps the two from fighting over
them. An endpoint outside the group is a decision, not an oversight.

`docs/api/openapi.json` is checked in, captured from a running instance at
`/api/openapi/v1.json`, and compared by a test against what the instance serves.
Both clients are generated from it before every build, typecheck and test, and
**neither generated output is committed**: the document is the artifact, its
output is not.

```sh
# regenerate the contract from the running instance
PERSONALAFFE_CAPTURE_CONTRACT=1 dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests

cd src/web && npm run generate      # src/web/src/api/schema.d.ts
cd src/cli && go generate ./...     # internal/api/client.gen.go
```

A change to an endpoint is a change to the document, in the same commit.

Conventions the contract carries, written up in [`docs/api.md`](./api.md): JSON in and JSON out, `snake_case` fields,
enums as the names the document spells, timestamps as RFC 3339 in UTC with
microseconds, and every refusal as one `application/problem+json` document whose
relative `type` ends in the code a client switches on. The instance's version is
on every response in `Personalaffe-Version`, and `GET /api/version` answers it
without a credential — the one operation outside the door, and the only
pre-authentication operation there will be.

## Migrations run on start, and only forward

EF Core owns every table and the migrations that apply them. The Api runs the
migrator as a hosted service before it serves anything, under a Postgres
advisory lock so that two containers starting at once do not migrate against
each other. A database carrying migrations this binary does not know about stops
the start rather than being served against — there is no downgrade path, and the
way back from a bad upgrade is the backup taken before it.

A migration is added with the pinned tool and no running instance anywhere:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/Personalaffe.Infrastructure
```

Readiness reflects that this has happened. `GET /api/health/live` says the
process is up; `GET /api/health/ready` says the database answers and the
migration ran. Neither carries owner data, a connection string or a diagnostic
a stranger should not have.

## Tests are split by what they need

**`Personalaffe.UnitTests`** runs in seconds and needs nothing installed: the
rules of Domain and the acts of Application against substituted ports, plus the
layering test.

**`Personalaffe.IntegrationTests`** brings up Postgres with Testcontainers,
because the parts no substitute can vouch for — that the migrations apply to an
empty database, that a second start finds nothing to do, that readiness fails
when the database is gone, that the served contract is the checked-in one — are
precisely the ones worth testing.

The frontend carries its own tests inside `src/web/`, and the CLI its own inside
`src/cli/`, each run by the CI job that builds it.

## The toolchains, and what pins them

| Toolchain | Pinned by | Version |
| --- | --- | --- |
| .NET SDK | `global.json`, `rollForward: latestFeature` | 10.0.100 |
| Node | the CI job and the Dockerfile's build stage | 24 |
| Go | `src/cli/go.mod` | 1.27 |
| PostgreSQL | Compose, and the Testcontainers image | 18 |
| `dotnet-ef` | `.config/dotnet-tools.json`, `rollForward: false` | matches EF Core |

Each is named in exactly one place, so that CI and a laptop cannot disagree. The
Go and Node versions are read from the files that carry them
(`go-version-file`, and the image tag) rather than repeated in the workflow.

```sh
dotnet build Personalaffe.slnx                  # the four projects
dotnet test tests/Personalaffe.UnitTests        # seconds, nothing installed
dotnet test tests/Personalaffe.IntegrationTests # Testcontainers brings up Postgres

cd src/cli && go generate ./... && go vet ./... && go test ./... && go build ./...

cd src/web && npm ci && npm run typecheck && npm run lint && npm run test && npm run build
```

CI runs the same commands, each toolchain in a job of its own, and builds the
image last.

## One image, two volumes

`deploy/Dockerfile` is a multi-stage build: a Node stage builds the SPA, a .NET
SDK stage publishes the backend, and the runtime stage carries neither
toolchain. One container serves both the web application and the API.

Two volumes, and they are not the same thing: the database's, and the file
storage root. The storage root is **outside the static web root**, so that no
stored file is reachable as a web asset, and the application refuses to start
when it cannot write there. `PERSONALAFFE_STORAGE_ROOT` names it;
`docs/operations.md` says what a restart preserves and what a volume deletion
destroys.

The instance runs behind a TLS-terminating reverse proxy the operator brings,
and trusts forwarded headers only from a proxy the operator named
(`PERSONALAFFE_TRUSTED_PROXY`). Unset, every request looks as if it came from
whatever spoke to the socket, which is the safe default.

*PERSONAL-6 implemented this. Nothing writes to the storage volume yet — the
Files API that fills it is PERSONAL-E6's — but `StorageService` checks the place
before the instance serves, because the failure it catches is an operator's,
made once, and otherwise invisible until the day the owner's file goes missing.
[`docs/operations.md`](./operations.md) is what an operator reads.*

## The gate

`.github/workflows/ci.yml` runs on every push to `main`, every pull request and
on demand. It is the only thing standing between a mistake and the trunk, and it
runs the same commands a contributor runs — six jobs, five of them beside each
other and the sixth after all of them:

| Job | What it runs | The same thing locally |
| --- | --- | --- |
| Unit tests | restore, build, `tests/Personalaffe.UnitTests` | `dotnet test tests/Personalaffe.UnitTests -c Release` |
| Integration tests | the same, plus Testcontainers' Postgres | `dotnet test tests/Personalaffe.IntegrationTests -c Release` |
| Web | `npm ci`, typecheck, lint, test, build | the same, in `src/web` |
| CLI | `go generate`, `go vet`, `go test`, `go build` | the same, in `src/cli` |
| OpenAPI contract | starts the instance against a real Postgres, captures the served document, `git diff --exit-code` | `dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests` |
| Image and smoke test | builds `deploy/Dockerfile`, starts `deploy/docker-compose.yml`, waits for readiness, checks both halves | `docker build -f deploy/Dockerfile …` then `docker compose … up -d` |

**No job stands in for a toolchain.** There is no skip condition and no
always-succeeding placeholder: every one of them builds or runs the thing it is
named after, and a subject that stopped existing turns its job red rather than
quiet.

Three things it deliberately does not do: it publishes no image, it deploys
nothing, and it holds no credential — `permissions: contents: read` is the whole
of what it is given. Publishing is the release epic's.

Two details worth keeping when it grows. The generation steps are never cached
away: `npm ci` runs the package's own `pre*` scripts and the Go job runs
`go generate` explicitly, so neither toolchain can be tested against a client
that agrees with a stale contract. And the image job cleans up after itself with
`down -v` under `if: always()`, after dumping the instance's log under
`if: failure()` — `up -d` says nothing about why a container is unhealthy.

**A feature epic extends these jobs rather than adding its own.** A new
application's tests are more tests in the two .NET test projects, in `src/web`
and in `src/cli`, and they are run by the job that already builds that
toolchain. What would justify a seventh job is a subject none of the six covers
— a browser check with its own runtime, say, which is what PERSONAL-E4's
acceptance criteria will need.

## What is deliberately not here

- **No second read path and no second write path.** Web and CLI are both
  clients of the same HTTP API, and an act is called from one place.
- **No shared types across the three languages.** The contract is
  `docs/api/openapi.json` and it is checked.
- **No generated code checked in.** Both API clients are generated at build
  time.
- **No dependency on another running affe product.** Not for logging, not for
  identity, not for anything.
- **No second human account, ever.** The instance has one owner; agent access
  is not a user.
