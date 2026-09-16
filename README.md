# personalaffe

A private workspace belonging to one person: a Scratchpad, a Knowledge base,
Tasks and Files, reachable from a browser, from an HTTP API and from a console.
[`VISION.md`](VISION.md) says what it is and what it deliberately is not;
[`CONTEXT.md`](CONTEXT.md) is the language it uses;
[`docs/mvp-plan.md`](docs/mvp-plan.md) says in what order it is built.

> **All four applications work.** What exists is
> the foundation of PERSONAL-E1 — a .NET host, a PostgreSQL schema that migrates
> itself, a checked-in HTTP contract, and a web application and a CLI that read
> it — and the door of PERSONAL-E2: an instance is claimed once by its one
> owner, who signs in with an email address, a password and, if they want one, a
> code from an authenticator; agents are let in with named tokens and a
> permission per application; and everything but five operations is behind that
> door.
>
> PERSONAL-E3 adds the safeguards over content: a write says which version it
> replaces and a stale one is refused; deleting lasting content sets it aside in
> a Trash that empties itself after thirty days; restoring brings back the
> folders it needs; and removing anything for good is the owner's alone.
>
> PERSONAL-E4 adds the workspace they are drawn in: the four applications, each
> of which the owner can switch off without losing what is in it; the frame
> around them, with its navigation, its command palette and its keys, on a desk
> and on a phone; the Markdown editor every application will write through; and
> screens that bring a change made on another device or by an agent onto
> themselves without a reload, and without taking away what somebody is in the
> middle of typing.
>
> PERSONAL-E5 adds the first content: the **Scratchpad**. Plain text put down in
> seconds on one device and read on another, pinned when it is worth keeping and
> destroyed by the instance a week after it was last changed when it is not — in
> the browser, over the API, and from `pea scratchpad`. Its deletion is the one
> in this product that cannot be taken back: an entry is never in the Trash.
>
> PERSONAL-E6 adds the second: **Files**. The owner's own storage on this
> instance's disk, with folders to organise it, a download that is one link, and
> a reference that survives every rename — a file's address is its id, so a link
> to it keeps working wherever it is moved. It is the first application to put
> anything in the Trash, and the first whose state is not all in the database:
> the bytes are written before the row, so an instance killed mid-upload leaves
> disk to tidy up rather than a file that is gone.
>
> PERSONAL-E7 adds the third: **Knowledge**. Lasting notes as Markdown in a
> tree, written in the editor the frame has had since PERSONAL-E4, with a
> history behind every page that only ever grows — putting an old version back
> keeps the one it replaced — and an export that is a zip of Markdown files
> anybody can read. A page's address is its id, so a link to it survives every
> rename and every move.
>
> PERSONAL-E8 adds the fourth: **Tasks**. Personal commitments in named lists,
> captured in one field and Enter, with an optional note and an optional day —
> a day, and not a moment, so the fourteenth is the fourteenth wherever you are
> standing — and an order you set, in which moving one task leaves every other
> device's version alone.
>
> PERSONAL-E9 is what the four of them add up to. **One search** over knowledge
> pages, tasks, Scratchpad text and file names — every word matched as a
> beginning, so `arch dec` finds "Architecture decisions" while you are still
> typing it — from a field in the palette or from `/search`, and from
> `pea search`. **A home page that is a dashboard**: five tiles you can hide one
> at a time, five rows each, every row a link to the thing itself. And **a
> weather tile** for a place you set once, at an address of its own so that a
> server on the other side of the internet can never hold your home page up.
>
> **What is left is not a feature.** There is no release, and no backup of this
> has been through a restore anybody has proved — both PERSONAL-E10, and the
> Knowledge export is not one — so **anything you would mind losing still
> belongs somewhere else as well.**

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

### The browser checks

`npm run test` is jsdom, which lays nothing out. What it cannot answer — whether
the navigation is a drawer at a phone's width, whether the Markdown editor works
at all, whether a screen picks up a change made somewhere else without a
reload — is `src/web/browser/`, run in Chromium against a real instance.

```sh
docker compose -f deploy/docker-compose.dev.yml up -d
cd src/web
npm run build                                # into the host's wwwroot
npx playwright install chromium              # once per machine

# In another terminal, on :5142 — the address the checks expect.
ASPNETCORE_HTTP_PORTS=5142 dotnet run --project src/Personalaffe.Api

npm run browser
```

They claim the instance they are pointed at and switch its applications on and
off, so point them at one you are willing to lose — `PERSONALAFFE_URL` says
which. CI gives them a database that lives for two minutes
([`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

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

The Scratchpad is the first application it reaches. Text comes from a file or
from stdin and never from an editor, and `show` writes the text and nothing
else, so the two are a round trip:

```sh
export PERSONALAFFE_URL=http://localhost:5000 PERSONALAFFE_TOKEN=pea_…

id=$(echo "the wifi password is hunter2" | ./pea scratchpad add --text-file -)
./pea scratchpad list
./pea scratchpad show "$id" > note.txt
./pea scratchpad pin "$id"      # a pinned entry never expires
./pea scratchpad rm "$id"       # permanent: it is never in the Trash
```

Files are the second, and the one that moves bytes. A path is `pea`'s
convenience — it walks it a segment at a time and the wire carries ids — and
`--file -` and `--out -` are stdin and stdout, so the two are a round trip:

```sh
./pea files mkdir --parents /Reisen/2026
file=$(./pea files put --file "Reisekosten 2026.pdf" --to /Reisen/2026)
./pea files ls /Reisen/2026
./pea files get "$file" --out - | sha256sum
./pea files mv "$file" /Reisen/2026/Bahn.pdf   # its address does not change
./pea files rm "$file"                         # into the Trash, not gone
./pea trash restore files "$file"
```

Knowledge is the third, and the one that writes prose. A path is titles, and
`show` writes the Markdown and nothing else, so the two are a round trip:

```sh
./pea knowledge new /Architektur --text-file notes.md
./pea knowledge tree
./pea knowledge show /Architektur > page.md
./pea knowledge edit /Architektur --title "Die Architektur"
./pea knowledge history /Architektur        # what it used to say
./pea knowledge export --out knowledge.zip  # Markdown anybody can read
```

Tasks is the fourth, and the one that has to be quick:

```sh
./pea tasks new-list Einkauf
./pea tasks add Einkauf "Milch holen" --due 2026-09-14
./pea tasks ls Einkauf
./pea tasks done ID
```

And one question over all four of them, with the home page beside it:

```sh
./pea search arch dec            # every word a beginning, all of them required
./pea search budget --application files
./pea dashboard                  # what is open, what was written, what arrived
./pea weather                    # its own request, on purpose
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
| `PERSONALAFFE_TRASH_RETENTION` | Whole days, 1 to 3650; 30 by default. How long deleted lasting content stays recoverable. |
| `PERSONALAFFE_SCRATCHPAD_RETENTION` | Whole days, 1 to 3650; 7 by default. How long an unpinned Scratchpad entry lasts after it was last changed. |

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

With an instance running, one script asks the eight questions nothing else
answers from the outside — and it takes the address, so the one it is asked
about can be the proxied one an owner actually uses:

```sh
scripts/smoke.sh                          # or scripts/smoke.sh https://workspace.example.com
```

And one rehearses the thing nobody wants to rehearse for the first time in
anger — a life put into an instance, a backup taken, both volumes destroyed, the
backup put back, and then every bit of it read out again through the API:

```sh
scripts/rehearse-a-restore.sh             # destroys the volumes it uses, twice
```

And one rehearses the other thing that happens to an instance with everything in
it — a life put into an earlier build, upgraded while a second container starts
beside it, read back out, and then the whole way back from an upgrade that went
wrong: the pre-upgrade backup put back, and the page written after it gone:

```sh
scripts/rehearse-an-upgrade.sh            # builds two earlier builds out of the history
```

It checks that the API answers and says what it is, that liveness and readiness
both answer, that the door in front of everything else is shut and says which
refusal it is, that the instance says whether it has an owner and nothing else,
that the web application is served from the same origin, that an unknown address
under `/api` is still an API error and not the page, that the contract the
instance serves is the one checked in, and that both clients generate from that
document — with `pea`, built there and then, reporting the same version the
browser would read. It writes nothing into the repository and needs no
credential.

## Known limits

Everything here is PERSONAL-E1 to PERSONAL-E9, and nothing more.

- **All four applications work, and none of what is left is content.** What the
  database carries is an owner, their sessions, their recovery codes, the agents
  they let in, which of the four applications is switched on, the Scratchpad's
  entries, the metadata of the owner's files, their knowledge pages with the
  history behind them, and their task lists.
- **The Trash has three contributors.** The guard on a write, recoverable
  deletion, the hourly sweep and the restore rules all work, and Files,
  Knowledge and Tasks are what fill them. The Scratchpad deliberately does not —
  an entry is destroyed when it is deleted
  ([`docs/codebase.md`](docs/codebase.md)).
- **Tasks is a list and not a planner.** No recurring tasks, no assignments, no
  sprints, no dependencies and no time tracking (VISION §6.4). A due date is a
  day rather than a moment, which is why it is the same day in every timezone.
- **Knowledge is Markdown and a tree, and nothing more.** No tags, no automatic
  backlinks, and no turning a Scratchpad entry into a page. A page keeps fifty
  previous versions and the tree is eight deep.
- **Files stores small files and nothing looks inside them.** No previews, no
  sharing links, no versioning of the bytes, and no search of their contents —
  one search finds a file by its *name* and never by what is in it (VISION §6.1).
  A file is at most 64 MiB and the application at most 5 GiB by default, and both
  are an operator's variable ([`docs/operations.md`](docs/operations.md)).
- **The search has no query language.** No `AND`, no quoted phrase, no `-word`:
  every word is a beginning and all of them have to be found. A limit with
  "there is more" rather than a cursor, and nothing from the Trash — a search is
  an ordinary read.
- **The dashboard is five tiles in a fixed order.** Hide them one at a time;
  there is no free arrangement, no widget store and no custom data (VISION §6.1).
- **The weather is one request and can be switched off.** Open-Meteo, which
  needs no account and no key; what is sent is two coordinates and nothing about
  who is asking. `PERSONALAFFE_WEATHER=off` and nothing in this product opens a
  socket to anywhere.
- **There is no release.** No image is published anywhere, and CI deliberately
  has no credential to publish one with. Release artifacts are PERSONAL-E10's.
- **A backup is one command, and putting it back is one script.**
  `personalaffe backup --to -` holds the instance still — reads keep working —
  and writes one tar carrying the database, the owner's files and a manifest of
  both; `scripts/restore.sh` puts it back, refusing an archive that is damaged,
  interrupted, from a newer build, or pointed at an instance that has something
  in it ([`docs/operations.md`](docs/operations.md)). The whole circle — a life,
  a backup, both volumes destroyed, the backup put back, everything read out
  again — is `scripts/rehearse-a-restore.sh`, and CI runs it on every push.
- **An upgrade is a pull and an up, and the way back is that backup.** Migrations
  apply themselves on start, only ever forward; two containers starting at once
  take an advisory lock rather than migrating against each other; and a build
  put in front of a schema a newer one wrote refuses to serve and names what it
  does not know. Rolling back is `scripts/restore.sh` with the earlier image
  named. All of it is walked rather than asserted, by
  `scripts/rehearse-an-upgrade.sh`, which builds an earlier build out of this
  repository's history — and CI runs that on every push too.

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
