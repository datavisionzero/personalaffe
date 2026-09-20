# The codebase

`VISION.md` says what personalaffe is. `docs/mvp-plan.md` says in what order it
is built. This one says where that lives: how the repository is laid out, which
project holds what, which way the dependencies point, and which toolchain builds
what.

**This document is kept accurate as each epic lands.** Every section below
marks what already exists and what is still only planned. A file that lands
somewhere this does not describe means one of the two is wrong.

Status: the four .NET projects, the two test projects, the self-applying
migrator, the health endpoints (PERSONAL-2), the checked-in contract and the
problem document (PERSONAL-3), the web workspace with one screen (PERSONAL-4)
the Go CLI with two verbs (PERSONAL-5), the image with its Compose topology
(PERSONAL-6), the CI gate (PERSONAL-7) and the fresh-checkout verification
(PERSONAL-8) all exist. **PERSONAL-E1 is complete.**

**PERSONAL-E2 is complete**: the owner and the one-time setup (PERSONAL-9), the
door in front of the `/api` group (PERSONAL-10), the second factor and the
recovery codes (PERSONAL-11), agent access with a permission per application
(PERSONAL-12), the credential half of `pea` (PERSONAL-13), the browser's door
and the owner's two screens (PERSONAL-14), the recovery on the server
(PERSONAL-15), and the suite that proves the door holds (PERSONAL-16). What the
epic decided is
[ADR 0002](./adr/0002-one-owner-with-a-browser-and-agents-with-tokens.md).

**PERSONAL-E3 is complete**: the guard on a write (PERSONAL-17), deletion that
sets content aside (PERSONAL-18), one Trash over the four applications
(PERSONAL-19), the sweep that empties it (PERSONAL-20), the rules for restoring
into a tree (PERSONAL-21), the revision convention (PERSONAL-22) and the two
clients that carry the version (PERSONAL-23). What the epic decided is
[ADR 0003](./adr/0003-content-is-guarded-by-what-it-was-read-at-and-deleted-by-being-set-aside.md).

**PERSONAL-E4 is complete**: the application switch and what it refuses
(PERSONAL-25), the owned primitives and the five states (PERSONAL-27), the
Markdown field and its renderer (PERSONAL-29, PERSONAL-30), the frame — sidebar,
palette, keys, routes — with the owner's four switches on screen and the refresh
(PERSONAL-26, PERSONAL-28, PERSONAL-31), and the browser checks in a seventh CI
job (PERSONAL-32). What the epic decided is
[ADR 0004](./adr/0004-one-frame-four-switches-and-a-screen-that-asks-again.md).

**PERSONAL-E5 is complete**: the Scratchpad entry, its table and the clock it
expires by (PERSONAL-33), the five endpoints over it (PERSONAL-34), the sweep
that empties it whether the application is switched on or off (PERSONAL-35),
`pea scratchpad` and the first verb in this product that destroys something
(PERSONAL-36), the screen with its capture box and its one-click copy
(PERSONAL-37), and the suite and the record that close it (PERSONAL-38). What
the epic decided is
[ADR 0005](./adr/0005-the-scratchpad-keeps-nothing-and-its-clock-runs-from-the-last-change.md).

**PERSONAL-E6 is complete**: the file, the folder and the address their bytes
live at (PERSONAL-39), the ten endpoints over them and the reference a rename
cannot break (PERSONAL-40), the first real contributor to the Trash and the
tidy-up behind it (PERSONAL-41), `pea files` and the paths it reaches them by
(PERSONAL-42), the screen with its breadcrumb and its drop target
(PERSONAL-43), and the suite and the record that close it (PERSONAL-44). What
the epic decided is
[ADR 0006](./adr/0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md).

**PERSONAL-E7 is complete**: the page, its title, its tree and the history
behind it (PERSONAL-45), the nine endpoints over them and the export
(PERSONAL-46), `pea knowledge` (PERSONAL-47), the screen with its tree and the
editor that finally has work (PERSONAL-48), and the suite and the record that
close it (PERSONAL-49). What the epic decided is
[ADR 0007](./adr/0007-a-page-is-its-id-and-its-history-only-grows.md).

**PERSONAL-E8 is complete**: the task, its list, its date and its order
(PERSONAL-50), the nine endpoints over them (PERSONAL-51), `pea tasks`
(PERSONAL-52), the screen and the end of `Awaited` (PERSONAL-53), and the suite
and the record that close it (PERSONAL-54). What the epic decided is
[ADR 0008](./adr/0008-a-due-date-is-a-day-and-an-order-is-a-number-between-two-others.md).

**PERSONAL-E9 is complete**: the words and the columns Postgres keeps up to
date from every row (PERSONAL-55), the home page's tiles and the rows behind
them (PERSONAL-56), the weather and the one outbound request in this product
(PERSONAL-57), the six endpoints over the three (PERSONAL-58), `pea search`,
`pea dashboard` and `pea weather` (PERSONAL-59), the home page that is finally a
dashboard and the field that reaches everything (PERSONAL-60), and the suite and
the record that close it (PERSONAL-61). What the epic decided is
[ADR 0009](./adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md).

**All four applications are here, and one question reaches all of them.** An
instance captures plain text, lists it, pins it, expires it and destroys it; it
stores files in folders and hands them back byte for byte; it keeps lasting
notes as Markdown in a tree, with a history behind every page and an export
anybody can read; and it keeps personal commitments in named lists, in an order
the owner sets. One search finds across the four, one home page says what is
pending, and a tile says what it is doing outside. All of it is reached in a
browser and from `pea`. And PERSONAL-E10 put the things around it that are not
features: an installation somebody else can follow, one backup carrying both
stores as of one moment, a restore and an upgrade that have been walked rather
than described, a pass over the security surface, and the artifacts a release is
cut from. `docs/mvp-plan.md` is ten epics of ten.

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

### Taken for PERSONAL-E4

The shared editing experience, from the list ADR 0001 named rather than from a
second selection exercise. In both sources under `src/web/src/shared/`, and now
under personalaffe's:

- `MarkdownField.tsx` — what every screen imports to edit Markdown.
- `Editor.tsx` — the only file that knows CodeMirror; a lazy chunk of its own.
- `StandInEditor.tsx` — what the tests write into, because jsdom lays nothing out.
- `markdownCommands.ts` — what the toolbar does to a selection, as pure functions.
- `Markdown.tsx` — the renderer: `react-markdown`, `remark-gfm`, `remark-breaks`.
- `components/ui/` and `index.css` — the shadcn-generated primitives and the
  token layer, owned by the repository that carries them.

The package versions came with them from both sources' `src/web/package.json`.
Adapted: `links.ts` admits `http`, `https` and `mailto`, plus this product's
own `file:` since PERSONAL-E6 gave a file an address a body can name, and the
token layer is hostingaffe's structure with personalaffe's colours. `components/ui/` came over whole, with
`lib/utils.ts`, `hooks/use-mobile.ts`, the theme provider and `components.json`,
so that the next primitive is generated the same way these were.

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
├─ .github/workflows/          the gate: ci on every push and pull request; release on a tag
├─ deploy/                     Dockerfile, Compose (production and development), .env.example
├─ scripts/
│  ├─ smoke.sh                 does this hang together? ten checks against a running instance
│  ├─ restore.sh               a backup put back: stop, restore, start
│  ├─ rehearse-a-restore.sh    the whole circle, against the image, and what CI's `restore` job runs
│  ├─ rehearse-an-upgrade.sh   an earlier build, upgraded and rolled back; CI's `upgrade` job
│  └─ an-instance.sh           what both rehearsals say to an instance, and the life they put in one
├─ docs/
│  ├─ adr/                     the decisions
│  ├─ api/openapi.json         the HTTP contract, captured and checked in
│  ├─ codebase.md              this
│  ├─ api.md                   the HTTP surface: conventions, errors, endpoints
│  ├─ cli.md                   `pea`: configuration, input, exit codes, verbs
│  ├─ install.md               installing what was published, from no checkout at all
│  ├─ operations.md            running it: variables, volumes, upgrade, reset
│  └─ mvp-plan.md              what is built, in what order
├─ src/
│  ├─ Personalaffe.Domain/         the rules
│  ├─ Personalaffe.Application/    the acts and the ports they need
│  ├─ Personalaffe.Infrastructure/ Postgres, the file store
│  ├─ Personalaffe.Api/            HTTP and the composition root
│  ├─ cli/                         the Go CLI — `pea`
│  └─ web/                         the single-page application, and browser/ its checks
├─ tests/
│  ├─ Personalaffe.UnitTests/
│  └─ Personalaffe.IntegrationTests/
├─ SECURITY.md                 how to report something, what is in scope, and what holds the door
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
change arrives as another one on top, only ever forward. `Security/` is the
password hasher: Argon2id in a value that carries the parameters it was made
with, so that raising the cost later does not lock out the owner who exists.
`Files/` is the one thing in this layer that is not the database: the local file
store, and the only class in the product that opens a file the owner stored.

**Two things are not HTTP**, and they are the image's two verbs.
`Hosting/OwnerRecovery.cs` is the one an operator runs on the machine when the
password, the authenticator and the recovery codes are all gone; it goes through
the same act the browser's password change goes through, so the two cannot
drift. `Hosting/Backup.cs` is the one that takes the database and the file
volume as of one moment — it holds the instance still through
`MaintenancePause`, takes the lock the Trash sweep takes so that no purge can
run between the two halves, and writes one tar whose manifest is last so that an
interrupted one is not mistaken for a finished one.

**Neither is an endpoint and neither can become one.** Their authorization is
that somebody is standing at the host, which is the same authorization `pg_dump`
has; an agent's token is not an authorization to reset the owner's password or
to take a copy of everything they have ever written
([`docs/operations.md`](./operations.md)).

**`Personalaffe.Api` is HTTP and the composition root.** `Http/` maps the
endpoints, one file per object, plus the cross-cutting pieces that arrive in
PERSONAL-3: `Problems` writing every refusal as one document, `VersionHeader`,
`Rfc3339`. `Hosting/` holds what runs before anything is served — the schema
migration, and later the owner bootstrap, in that order. `Program.cs` is the only
file that knows all four layers.

Implemented: `Domain/` with `Refusal`, `RefusalCode`, `Owner`, `Password`,
`Caller`, `BrowserSession`, `Totp`, `Base32`, `RecoveryCode`,
`WorkspaceApplication`, `Permission`, `Permissions`, `TokenSecret`,
`AgentAccess`, PERSONAL-E3's `ContentVersion`, `Actor`, `IRecoverable`,
`Recoverable`, `Restoration` and `Revisions`, and PERSONAL-E4's
`ApplicationState`;
`Application/Ports/` with the settings records the host validates at startup,
`IOwners`, `IPasswordHasher`, `IBrowserSessions`, `IRecoveryCodes`,
`IAgentAccessStore`, `ICallerIdentity`, `ITrash`, `IExclusiveWork` and
`IApplicationSwitch`; `Application/Acts/` with the setup, sign-in, session,
security and agent-access acts, the four Trash acts and the purge, and the two
application acts beside `ReachingAnApplication`; `Persistence/` with the
context, the migrator, five tables and their stores, six migrations,
`GuardedSave`, `ExclusiveWork` and the two configuration helpers
`RecoverableContent` and `ContentRevisions`; `Security/` with the Argon2id hasher;
`Hosting/` including `RetentionService`; and `Http/` with `Routes`, `Problems`,
`Rfc3339`, `VersionHeader`, `OpenApiDocument`, `Authentication`,
`BrowserSecurity`, `EntityTags`, `Applications`, `InstanceEndpoints`,
`HealthEndpoints`, `SetupEndpoints`, `SessionEndpoints`, `MeEndpoints`,
`SecurityEndpoints`, `AgentEndpoints`, `ApplicationEndpoints` and
`TrashEndpoints`.

Implemented for the Scratchpad (PERSONAL-E5): `Domain/Scratchpad/` with
`ScratchpadEntry`, `Application/Ports/IScratchpadEntries`,
`Application/Acts/Scratchpad/` with the five acts and `ExpireTheEntries`,
`Persistence/ScratchpadEntries` with its configuration and migration, and
`Http/ScratchpadEndpoints`.

Implemented for Tasks (PERSONAL-E8): `Domain/Tasks/` with `TaskList`,
`PersonalTask`, `TaskTitle` and `Positions`; `Application/Ports/ITasks` with
`TheList`; `Application/Acts/Tasks/` with the nine acts; `Persistence/Tasks` and
`TasksTrash` with their two configurations and their migration; and
`Http/TaskEndpoints`.

Implemented for Knowledge (PERSONAL-E7): `Domain/Knowledge/` with `Page`,
`PageTitle` and `PageRevision`; `Application/Ports/IPages` with
`PageInTheTree`; `Application/Acts/Knowledge/` with the eight acts and
`ExportTheKnowledge`; `Persistence/Pages` and `KnowledgeTrash` with their two
configurations and their migration; and `Http/KnowledgeEndpoints`.

Implemented for Files (PERSONAL-E6): `Domain/Files/` with `StoredFile`,
`Folder`, `FileName` and `StorageAddress`;
`Application/Ports/IStoredFiles`, `IFileBytes` and `StorageRoot`;
`Application/Acts/Files/` with the ten acts and `TidyTheStorage`;
`Persistence/StoredFiles` and `FilesTrash` with their two configurations and
their migration; `Files/LocalFileBytes`; and `Http/FileEndpoints` with
`FileBodies` beside it.

**Nothing of the MVP's features is planned but unimplemented any more.** An
instance answers the five outside the door, the owner's own, which applications
it has, its Scratchpad, its Files, its Knowledge, its Tasks, a Trash that three
of the four fill and the Scratchpad deliberately never puts anything in, one
search over all four, a home page of tiles, and the weather. PERSONAL-E10 added
no module at all: what it added is the backup and restore verbs beside the
recovery one, the headers in front of every answer, and the workflow that cuts a
release.

PERSONAL-E9 added `Domain/Search/` and `Domain/Dashboard/` and `Domain/Weather/`;
`Application/Ports/ISearch`, `IDashboard`, `IDashboardTiles`, `IWeatherPlace`,
`IWeather` and `WeatherSettings`; `Application/Acts/Search/`, `Acts/Dashboard/`
and `Acts/Weather/`; `Persistence/Search` — the only hand-written SQL in the
product — beside `Persistence/Dashboard`, `DashboardTiles` and `WeatherPlaces`,
the `search_vector` column that `Configurations/SearchIndex.cs` puts on four
existing tables, and `Weather/OpenMeteo`, the one thing here that opens a socket
to somewhere else; and `Http/SearchEndpoints`, `DashboardEndpoints` and
`WeatherEndpoints`.

PERSONAL-72 added `Domain/Appearance/` — what an instance is called and what its
mark looks like — with `Application/Ports/IInstanceAppearance`,
`Application/Acts/Appearance/`, `Persistence/InstanceAppearances` and its one
seeded row, and `Http/AppearanceEndpoints`. It is the one module whose read is
outside the door, and the only thing added there since PERSONAL-E2 closed that
list: a browser tab and a sign-in screen are drawn before anybody has signed in,
so what the instance is called has to be readable there
([`docs/api.md`](./api.md), The appearance). In the web application it is
`shell/theMark.ts` and `shell/Mark.tsx`, the seven colours in the token layer of
`index.css`, and `shell/AppearanceProvider.tsx`, which sits above the router
beside the theme because what it sets is the document's rather than a screen's.

## Where an application lives

The four applications of `VISION.md` — Scratchpad, Knowledge, Tasks, Files — are
**folders that own their types, acts, stores and endpoints**, not rows in a
registry and not plugins. An application is a folder in each of the four layers
and one in `src/web/src/`:

**The Scratchpad is the worked example** rather than the illustration it used
to be: PERSONAL-E5 built exactly this shape, and PERSONAL-E6 was written by
reading it. Files is the worked example of the other half — a module with a
tree, a Trash and bytes beside its rows — and Knowledge was written by reading
that one, which is the third module and the first with a history. Tasks is the
fourth and the one with no tree at all, which is what made it worth having
([ADR 0008](./adr/0008-a-due-date-is-a-day-and-an-order-is-a-number-between-two-others.md)):
it applies the Trash without applying `Restoration`, and says why.

```
src/Personalaffe.Domain/Scratchpad/ScratchpadEntry.cs        the rules
src/Personalaffe.Application/Acts/Scratchpad/                the acts
src/Personalaffe.Application/Ports/IScratchpadEntries.cs     what they need answered
src/Personalaffe.Infrastructure/Persistence/ScratchpadEntries.cs             the store
src/Personalaffe.Infrastructure/Persistence/Configurations/ScratchpadEntryConfiguration.cs
src/Personalaffe.Api/Http/ScratchpadEndpoints.cs             one file per object
src/web/src/scratchpad/Scratchpad.tsx                        its screen
```

Files is the same shape with three more pieces, and each is the module's own
rather than a new convention: `Infrastructure/Files/LocalFileBytes.cs` answers a
second port because the bytes are not in the database,
`Persistence/FilesTrash.cs` is the `ITrash` PERSONAL-E3 asked every lasting
module for, and `Application/Acts/Files/TidyTheStorage.cs` is what the order of
its two writes owes the volume
([ADR 0006](./adr/0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md)).

The port and the store are the one thing the shape above did not say out loud:
an application's acts reach persistence through an interface in
`Application/Ports/`, and `Infrastructure/Persistence/` answers it. Nothing in
`Acts/` knows there is a database.

What more than one of them shares sits at the root of the layer it belongs to,
and only once two of them actually need it: the concurrency guard, recoverable
deletion and actor attribution of PERSONAL-E3; the enablement switch of
PERSONAL-E4; the `search_vector` column of PERSONAL-E9, which a module gets by
calling `builder.IsSearchable(…)` in its own configuration and nowhere else.
**No generic content entity.** Four applications that share conventions are not four rows in one
table, and PERSONAL-E3 is explicit that a universal workflow framework is not
what is wanted.

Identity — the owner, agent access and their permissions — is not an
application. It lives at the root of each layer, because every application asks
it the same question: `Owner`, `AgentAccess`, `Permissions` and `Caller` in
Domain, the acts beside the others, and one store each.

*All four applications' folders exist now. PERSONAL-2 created the folders that
had something to put in them and no others, which is still the rule: an empty
folder claiming a future module is a lie the tree tells. PERSONAL-26 gave each
of the four a route, and PERSONAL-27's `shell/States.tsx` said which epic filled
the ones still to come — until PERSONAL-53 took that state away, because there
are none.*

**What the four applications inherit from PERSONAL-E4**, beside PERSONAL-E3's
safeguards:

- **The switch.** `ReachingAnApplication.ToReadAsync` and `ToWriteAsync` are
  what an act calls first. They ask access, then the switch, in that order, and
  throw `forbidden` or `disabled` (ADR 0004). An aggregate view that leaves an
  application out rather than refusing calls `SwitchedOnAsync`, as
  `ReadTheTrash` does.
- **The frame.** A screen is a route in `shell/Shell.tsx` and a folder beside
  `shell/`. It is drawn inside the sidebar, the header and the palette without
  doing anything, and it is offered in the navigation by being in
  `shell/applications.ts`.
- **The five states.** `shell/States.tsx`: `Busy`, `Empty`, `Denied`,
  `Disabled`, `Failed`. A content screen draws one of them rather than inventing
  its own; none of them is a blank page and none is silent to a screen reader.
- **The refresh.** `shared/ask.ts`. A read is `useAsk(address, request)` and
  refreshes itself; a screen with unsaved work passes `hold`, and nothing
  arrives underneath what somebody is typing.
- **The editor.** `shared/MarkdownField.tsx`, with its toolbar, its preview and
  its full-screen dialog. Everything written in this workspace goes through it,
  so what is decided there is decided in all of them at once.

## What is not an application

Three things in this product read the four applications without being one of
them, and they live at the root of each layer for the reason identity does:
every application answers them the same question.

**The search** is a column and not a service. `Configurations/SearchIndex.cs`
puts a stored generated `search_vector` on a module's table — one line in that
module's own configuration — and Postgres keeps it up to date from the row.
`Persistence/Search.cs` is four statements over those four columns, and is the
only hand-written SQL in the product; what that costs is spelled out where
somebody changing it will read it, because hand-written SQL does not get the
query filter that hides the Trash.

**The dashboard** owns no content. `IDashboard` is a reading of the tables the
modules already write, in the one shape a tile wants — five rows, ordered by
what makes them useful now — and `IDashboardTiles` is five seeded rows of the
owner's own preferences, keyed the way the application switch is.

**The weather** is the only thing here that asks something outside this
instance. It is behind a port that answers with nothing rather than throwing, it
has an address of its own so that the home page can never wait on it, and an
operator can switch it off entirely
([ADR 0009](./adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).

*A fifth application would join the first two by doing two things: calling
`builder.IsSearchable(…)` in its configuration, and answering a case in
`DashboardTile`. Neither is a registry and neither is a plugin — both are a line
somebody writes.*

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
internal/keychain   where a machine keeps a token, through the tool it ships
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
token. The keychain is reached through the tool the system already ships, and a
machine with none still has the two rungs above it.

Operational verbs that need the database are **not** here: migrations and
backups belong to the .NET binary that has the connection string.

*PERSONAL-5 built the module, the two ladders, the exit table, the text input
and two verbs — `version` and `status`. PERSONAL-13 added the keychain rung and
`login`, `whoami` and `logout`; the content verbs arrive with their
applications.*

**What `pea` holds is an agent token, and there is no password in it.** A
browser signs the owner in; a console is an agent acting on the owner's behalf,
which is what `CONTEXT.md` calls it. So `pea` cannot manage agents or security
settings, and no token can be given permission to
([`docs/cli.md`](./cli.md)).

## The frontend is built separately and joined once

`src/web/` is a Vite project with its own `package.json`, and nothing in the
.NET build knows it exists. Development runs the two side by side — the Vite dev
server against `dotnet run`, with Vite forwarding `/api` — and the only place
they are joined is `deploy/Dockerfile`, which builds the SPA in a Node stage and
copies it into the published output. A local `npm run build` lands in
`src/Personalaffe.Api/wwwroot/`, which the API serves, so that one `dotnet run`
gives the whole product.

Its layout is one folder per area, as in both sources: `shell` owns the frame,
the navigation, the palette, the keys and the routes; `shared` the Markdown
field, the editor behind it and `ask.ts`, which is how a screen keeps up with
the instance; `api` the generated client and its wrapper; `components/ui` the
owned primitives, with `lib` and `hooks` what they reach for. `session`,
`security` and `agents` are the door and the owner's own settings; `home`,
`settings`, `trash` and `editor` are the screens the frame routes to. The four
applications get a folder each when they arrive.

`browser/` is beside `src/` rather than in it: those are Playwright's checks,
they run against a running instance rather than against a module, and vitest is
told not to pick them up.

*PERSONAL-4 created `api/`, `shell/`, `shared/` and one screen. PERSONAL-14
added `session/`, `security/` and `agents/` — the door and the owner's own
settings — and turned `shell/App.tsx` into what decides between them.
PERSONAL-27, PERSONAL-29 and PERSONAL-30 added `components/ui/`, `lib/`,
`hooks/` and the editing components under `shared/`; PERSONAL-26 added the
frame: `shell/` grew the sidebar, the palette, the keys and the routes, and
`home/`, `settings/`, `trash/` and `editor/` are the screens behind them.
PERSONAL-37 added the first application folder, `scratchpad/`, PERSONAL-43 the
second, `files/`, PERSONAL-48 the third, `knowledge/` — which is also where
`editor/` went, because the field it stood in for now has a screen that writes —
and PERSONAL-53 the fourth, `tasks/`. There are no more.*

**Nothing is drawn until the instance has said whether it has an owner and
whether this browser is signed in.** `session/useSession.ts` asks the two
questions in that order, and the three answers a screen has to be able to draw —
no owner yet, nobody signed in, signed in — are separate states: a sign-in form
at a fresh installation is a door with no lock and no key.

Every write carries `X-Personalaffe-CSRF` (`api/client.ts`), which is half of
what a browser write proves; the other half is `Origin`, which the browser sets
itself on anything that is not a GET.

*PERSONAL-27 installed Base UI, `react-router`, `lucide-react` and the IBM Plex
faces; PERSONAL-29 and PERSONAL-30 added CodeMirror, `react-markdown` and its
two remark plugins; PERSONAL-32 added Playwright. Each arrived with the thing
that uses it.*

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

All three of those are walked rather than asserted. `MigrationTests` applies
them to an empty database, to a populated one, and against a history row a
newer build would have written; `scripts/rehearse-an-upgrade.sh` does the same
three things to the real image, upgrading a build one migration behind this one
with a second container starting beside it, and then rolling it back
([`docs/operations.md`](./operations.md), Upgrading).

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
rules of Domain and the acts of Application against substituted ports, the
layering test, and the parts of Infrastructure that need nothing installed
either — the password hasher is a function, and a function is a unit test.

Two of its files test documents rather than code, because a document nobody can
run is one an operator cannot trust. `LayeringTests` reads the four project
files. `TheVariablesAreDocumentedTests` reads `docs/operations.md`,
`deploy/docker-compose.yml` and `deploy/.env.example` and compares the three
against every `const string …Variable` the settings types declare — so a
variable that is added, renamed or removed fails the unit suite until the table
an operator reads has heard about it. It is the one reason the unit tests
reference `Personalaffe.Api` at all: one of the eleven is read by
`Http/TrustedProxies` rather than by a settings record.

**`Personalaffe.IntegrationTests`** also owns **the proving ground**: a `things`
table with a tree, a history and the deletion columns, created by the test
fixture and living nowhere in `src/`. PERSONAL-E3's conventions landed before
there was any content to apply them to, and a convention nobody has applied is a
convention nobody has tested — so `Thing`, `ThingRevision` and `Things` are a
content module in every respect except that no instance has them. No migration
carries them and the contract does not mention them; what is under test is the
production code they call.

It brings up Postgres with Testcontainers,
because the parts no substitute can vouch for — that the migrations apply to an
empty database, that a second start finds nothing to do, that readiness fails
when the database is gone, that the served contract is the checked-in one — are
precisely the ones worth testing.

**One of its suites starts the instance as `Production`, and it is the only one
that does.** `WebApplicationFactory` starts everything as `Development`, the
image runs as `Production`, and the framework decides a few things by that name
— which is how an empty `400` where the contract promises a document survived
seven epics under five suites that asserted the opposite.
`TheEnvironmentDecidesNothingTests` is what holds the pinning that ended it, and
[`docs/operations.md`](./operations.md#what-the-environment-does-not-decide) is
the list of what the name still decides.

The frontend carries its own tests inside `src/web/src/`, and the CLI its own
inside `src/cli/`, each run by the CI job that builds it. **The one exception is
`src/web/browser/`**, which is Playwright against a real instance in a real
browser and has a job of its own — the subject none of the other six covers.

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

*PERSONAL-6 implemented this and PERSONAL-E6 filled it. `StorageService` checks
the place before the instance serves, because the failure it catches is an
operator's, made once, and otherwise invisible until the day the owner's file
goes missing; `StorageRoot` resolves that place once, in the composition root,
so the directory the check proves writable is the directory the store writes
into. Under it are `files/`, where a stored file lives at an address made from
its id, and `incoming/`, which holds only uploads still arriving.
[`docs/operations.md`](./operations.md) is what an operator reads.*

## The gate

`.github/workflows/ci.yml` runs on every push to `main`, every pull request and
on demand. It is the only thing standing between a mistake and the trunk, and it
runs the same commands a contributor runs — nine jobs: six beside each other,
the image once they are all green, and two more on top of the image:

| Job | What it runs | The same thing locally |
| --- | --- | --- |
| Unit tests | restore, build, `tests/Personalaffe.UnitTests` | `dotnet test tests/Personalaffe.UnitTests -c Release` |
| Integration tests | the same, plus Testcontainers' Postgres | `dotnet test tests/Personalaffe.IntegrationTests -c Release` |
| Web | `npm ci`, typecheck, lint, test, build | the same, in `src/web` |
| CLI | `go generate`, `go vet`, `go test`, `go build` | the same, in `src/cli` |
| OpenAPI contract | starts the instance against a real Postgres, captures the served document, `git diff --exit-code` | `dotnet test tests/Personalaffe.IntegrationTests --filter ContractTests` |
| Browser checks | builds the application into the host's `wwwroot`, starts the instance, drives it in Chromium | `npm run browser`, against an instance you have up (README) |
| Image and smoke test | builds `deploy/Dockerfile`, starts `deploy/docker-compose.yml`, waits for readiness, checks both halves | `docker build -f deploy/Dockerfile …` then `docker compose … up -d` |
| Backup and restore | puts a life into an instance, backs it up, destroys both volumes, puts the backup back, reads every bit of it out again | `scripts/rehearse-a-restore.sh` |
| Upgrade and rollback | puts a life into an earlier build, upgrades it while a second container starts beside it, reads it out, then walks the way back from a failed upgrade | `scripts/rehearse-an-upgrade.sh` |

**No job stands in for a toolchain.** There is no skip condition and no
always-succeeding placeholder: every one of them builds or runs the thing it is
named after, and a subject that stopped existing turns its job red rather than
quiet.

Three things it deliberately does not do: it publishes no image, it deploys
nothing, and it holds no credential — `permissions: contents: read` is the whole
of what it is given. Publishing is `release.yml`'s, below.

### The release, which is the other workflow

`.github/workflows/release.yml` runs on a tag and on nothing else, and it is the
only thing in this repository that writes anywhere outside it: the release under
the tag, and the image in this repository's package registry. What it is given
is `contents: write` and `packages: write`, and the token GitHub hands the run —
there is no other credential, here or anywhere.

| Job | What it does |
| --- | --- |
| What is being released | The version out of the tag, whether it is a pre-release, and the refusal of a tag that is not on `main` |
| `pea` | Four static binaries — `darwin/arm64`, `darwin/amd64`, `linux/amd64`, `linux/arm64` — each with the tag compiled in and the licence beside it, and one `SHA256SUMS` over the archives |
| Image | `linux/amd64` and `linux/arm64`, pushed as `<version>` and, for a release that is not a pre-release, `latest` |
| The two of them agree | The published image started the documented way, the binary from the archive talking to it, and an instance built a major ahead to prove the refusal is real |
| The release | The notes, the archives, the checksums, the Compose file, the example environment and the licence, under the tag |

**The four platforms and the two architectures are decisions**, written down
where they are made: a workspace is reached from the machine its owner sits at
and from whatever a script runs on, and an instance runs on a small server
somebody rents or a box at home. There is no Windows binary because nobody has
asked for one.

A tag with a hyphen in it is a pre-release: published on purpose, marked as one,
and `latest` does not move. That is what makes this workflow rehearsable without
telling anybody they have a new version.

Two details worth keeping when it grows. The generation steps are never cached
away: `npm ci` runs the package's own `pre*` scripts and the Go job runs
`go generate` explicitly, so neither toolchain can be tested against a client
that agrees with a stale contract. And the image job cleans up after itself with
`down -v` under `if: always()`, after dumping the instance's log under
`if: failure()` — `up -d` says nothing about why a container is unhealthy.

**A feature epic extends these jobs rather than adding its own.** A new
application's tests are more tests in the two .NET test projects, in `src/web`
and in `src/cli`, and they are run by the job that already builds that
toolchain. What justifies another job is a subject none of the others covers,
and PERSONAL-32 found the one: `browser` runs Playwright in Chromium against a
real instance serving the built application from its own `wwwroot`. Everything
under `src/web/src` runs in jsdom, which lays nothing out and cannot say whether
the sidebar is a drawer at a phone's width, whether CodeMirror works at all, or
whether a screen brings a change made elsewhere onto itself while nobody touches
it. Its checks are `src/web/browser/`, its configuration is
`src/web/playwright.config.ts`, and `npm run browser` is how it is run against
an instance somebody already has up.

## What each epic plugged into

The foundation was built to be extended in specific places, and this is the list
of where each epic went — kept, now that all ten have landed, because it is
still the shortest description of how this product is put together, and because
the day somebody adds a fifth application it is the list they need.

**PERSONAL-E2, authentication — landed.** The door is
`Http/Authentication.cs`, in front of the `/api` group and nowhere else: the
group asks for an authenticated caller
and only what says `AllowAnonymous` is outside it, which is the way round that
fails safe. What comes through is a `Caller` on the request, answered to the
acts by `ICallerIdentity`; an act asks for it rather than taking one as an
argument, so no endpoint can forget to pass one. A token path is already cut
into `AuthenticateCaller` and admits nobody until agent access fills it. On the
CLI side, `config.Input.ResolveToken` is a two-rung ladder with the third — the
keychain — left to the sign-in that fills it, and
`client.New(address, token, …)` already sends the bearer header when there is a
token to send.

**PERSONAL-E3, content safeguards — landed.** What a content module inherits,
and the whole of it:

- **The guard.** Declare `UpdatedAt` a concurrency token in the module's
  `IEntityTypeConfiguration`, answer `EntityTags.Write` on a read of one object,
  take `EntityTags.Required(request)` on a write, and save through
  `GuardedSave.SaveAsync` rather than `SaveChangesAsync`. `.Guarded()` on the
  endpoint puts `If-Match` in the contract, which is what both generated clients
  read it from.
- **Recoverable deletion.** Implement `IRecoverable` on the entity and call
  `builder.IsRecoverable()` in its configuration: that is the two columns, the
  three of the actor, the query filter that makes forgetting impossible, and the
  index the sweep uses. `content.Delete(caller, now)` and `content.Restore()`
  are the two acts; `content.Gone(...)` is what a deleted address answers.
- **The Trash and the sweep.** Register one `ITrash` for the application. That
  is the whole of appearing in `GET /api/trash`, in restore, in permanent
  removal and in the hourly purge. Permission is settled in the acts, once, so a
  contributor takes no caller.
- **A hierarchy**, where the module has one: `Restoration.Plan` says what comes
  back with what and what is in the way. What the tree *is* stays in the module.
- **History**, where the module keeps any: `IRevision`, `builder.IsARevision()`,
  and `Revisions.Superseded` after every write.

The proving ground under `tests/` is what each of these was developed against,
and `Things` there is what a module with a tree and a history looks like when it
applies all of them.

**PERSONAL-E4, the shell — landed.** What a content module inherits is listed
under [Where an application lives](#where-an-application-lives): the switch
through `ReachingAnApplication`, the frame, the five states, the refresh and the
editor. The application switch deliberately does not reach the retention sweep —
`ITrash.PurgeAsync` takes a deadline and a cancellation token and nothing else,
and `TheSafeguardsHoldTests` asserts that signature by reflection, so adding a
parameter is a red build rather than a decision nobody notices. What the epic
decided is
[ADR 0004](./adr/0004-one-frame-four-switches-and-a-screen-that-asks-again.md).

Two things in it were scaffolding and are gone. `/editor` was the one screen the
Markdown field had while nothing wrote through it; Knowledge is what it was
waiting for, and the browser checks that drove it now drive the real editor
([ADR 0007](./adr/0007-a-page-is-its-id-and-its-history-only-grows.md)).
`shared/links.ts` has both of its schemes: `file:` since PERSONAL-E6 and `page:`
since PERSONAL-E7 — a file link downloads and a page link is followed inside the
frame.

**PERSONAL-E6, files — landed.** What a module with bytes beside its rows
inherits, and what it owes back: the two stores are two ports (`IStoredFiles`
and `IFileBytes`), the order of their writes is bytes first and then the row,
and the tidy-up that order owes the volume is a third sweep in the same hourly
loop. `Domain/Files/StorageAddress` is why no name ever reaches the filesystem.
What Knowledge plugs into is `shared/links.ts`: `file:<id>` already resolves to
a download, so a page that names a file needs no attachment store
([ADR 0006](./adr/0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md)).

**PERSONAL-E7, knowledge — landed.** What a module that keeps history
inherits, and the whole of it: `IRevision`, `builder.IsARevision()`, and a write
that takes a revision of what it replaced before it changes anything.
`Revisions.Superseded` is what drops the fifty-first, and `IPages.KeepAsync`
does both in one call because the second is not optional. Recovering is a
guarded write on the object and never on the revision. What Tasks plugs into, if
it wants any, is exactly this
([ADR 0007](./adr/0007-a-page-is-its-id-and-its-history-only-grows.md)).

**PERSONAL-E8, tasks — landed.** The one content module with no tree, and the
one place a value is a date rather than a moment. What it adds to the list of
things a later module can read is `Positions`: an order that survives a
concurrent workspace, because a move changes one row
([ADR 0008](./adr/0008-a-due-date-is-a-day-and-an-order-is-a-number-between-two-others.md)).

**PERSONAL-E9, search and the dashboard — landed.** It added nothing to the four
applications and took nothing from them: the index is a column Postgres keeps up
to date, so a module is searchable by calling one line in its own configuration,
and the dashboard reads the tables the modules already write. What a fifth
application would have to do to join both is exactly those two things
([ADR 0009](./adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).

**PERSONAL-E10, operations — landed.** No module and no endpoint: what it added
is beside the product rather than in it. `Hosting/Backup.cs` and
`Hosting/Restore.cs` are two more verbs of the image's binary, beside
`Hosting/OwnerRecovery.cs`, and they are verbs rather than endpoints for the
same reason recovery is — their authorization is that somebody is standing at
the machine. `Http/MaintenanceGuard` is what a write meets while a backup holds
the instance still, and `Http/BrowserSecurity` gained `SecurityHeaders`, in
front of everything. `scripts/` holds the two rehearsals CI runs on every push,
`.github/workflows/release.yml` cuts a release from a tag, and
[`docs/install.md`](./install.md) is what somebody with no checkout follows
([ADR 0010](./adr/0010-the-pause-makes-two-stores-agree-and-the-way-back-is-the-backup.md)).
The Knowledge export is **not** a backup and must not be described as one: it
carries no Trash, no revisions and no agent access.

**Every epic.** A new endpoint is a change to `docs/api/openapi.json` in the
same commit, because `ContractTests` compares the two. A new refusal code is a
row in the table in `docs/api.md` and a case in `Problems`, which throws rather
than guesses when a code has no status. New tests are more tests in the projects
CI already runs; another job is only for a subject none of the existing ones
covers, and three have ever qualified — `browser`, which needs an engine that
lays things out, `restore`, which needs a whole installation destroyed and put
back, and `upgrade`, which needs two builds of this product at once.

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

## Saved link storage

`Domain/Bookmarks` holds saved addresses and their independent folder tree.
`BookmarkText` bounds plain text and accepts absolute HTTP(S) addresses without
fetching them. Bookmark and folder rows carry recoverable deletion and guarded
versions; favorites have a separate persisted position. Folder placement checks
bound the complete resulting tree to 32 levels and reject cycles.

The `bookmarks` application is seeded enabled. Its agent permission column
starts at `none`, including for existing credentials; the owner explicitly grants
access through the same controls as the other applications.
