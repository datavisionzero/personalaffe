# personalaffe — Product Vision

> **Status:** MVP direction agreed · **Language:** English
>
> This document describes the product's direction and agreed MVP boundaries,
> not its final specification. See [the MVP plan](docs/mvp-plan.md) for delivery
> order, epic scope, acceptance criteria, and remaining implementation details.

**License:** MIT · **Hosting model:** self-hosted · **Usage model:** one person
per instance

## 1. Elevator Pitch

personalaffe is a modular, self-hosted workspace for the small personal things
that need a reliable home in everyday life: briefly sharing text between
devices, preserving knowledge, managing simple tasks, and storing small files.

The application runs on the owner's infrastructure, is designed for secure
operation under a public subdomain, and belongs to exactly one person. That
person uses a web interface; their AI agents perform nearly the same content
operations through a full-featured CLI and the same HTTP API.

personalaffe does not replace a single large specialist application. It
provides a shared, calm environment for several small personal applications,
each of which would be too small to justify a dedicated service, while a loose
file or chat history would be too unreliable.

## 2. The Problem

Personal information is scattered across messages to oneself, local text
files, browser tabs, note-taking apps, cloud storage, and to-do tools. This
creates several sources of friction:

- **Temporary content has no good home.** A piece of text needs to move from
  a work computer to a laptop or phone without creating a permanent knowledge
  page or using a third-party messenger.
- **Small needs lead to large products.** For a few personal pages, lists, or
  files, many standard applications are too broad, too specialized, or
  unnecessarily difficult to operate.
- **Temporary and permanent content get mixed together.** A scratchpad should
  clean itself up; personal knowledge should remain. A tool that treats both
  alike either becomes cluttered or loses important information.
- **AI agents cannot contribute reliably.** Data lives in interfaces without
  a suitable CLI, in local files on the wrong machine, or in services whose
  generic APIs require unnecessary calls and context.
- **Data ownership and convenient access seem at odds.** Local storage
  preserves control but is not accessible everywhere. Cloud services are
  convenient but place private everyday data on someone else's infrastructure.
- **Many small self-hosted services create work of their own.** Separate
  deployments, accounts, backups, and interfaces are disproportionate to the
  small tasks they are meant to solve.

## 3. The Product Promise

**One person gets a private digital workspace on their own infrastructure,
accessible from any device and equally usable by their agents.**

Each included application solves a clearly defined problem and can offer its
own experience. Together, they share sign-in, navigation, search where useful,
settings, API, CLI, operations, and backup. New areas can be added later without
forcing the existing ones into a single universal data model.

## 4. Target Audience

**Primary audience:** technically experienced individuals who run their own
services with Docker Compose and work with CLI-based AI agents day to day.

Typical users:

- own a small server or VPS and a domain,
- want to keep personal data under their control,
- access the same content from multiple computers or their phone,
- prefer focused tools with good defaults,
- want agents to capture, organize, search, and maintain their content.

**Not the target audience:** teams, families, or organizations that need to
collaborate on content, separate it between users, share it, or manage it with
complex roles and permissions.

## 5. Guiding Principles

1. **One instance belongs to one person.** There is exactly one human account.
   No tenancy model, teams, sharing, or hidden multi-user complexity.
2. **Private, but accessible over the internet.** No content is accessible
   anonymously. The web interface, API, and CLI are designed so that an instance
   can run securely under a public subdomain behind a TLS-terminating reverse
   proxy.
3. **Humans and agents are first-class operators.** Content features are
   available through both the web interface and CLI. Agents receive their own
   revocable credentials and their actions are traceable, without creating a
   second user account.
4. **Several small applications, one shared home.** Each area has its own
   model, navigation, and suitable interface. The shared application shell
   keeps them from becoming a jumble of independent products.
5. **Only enable what is needed.** Each application can be enabled or disabled
   individually. Disabled areas disappear from navigation, the dashboard, and
   normal workflows without silently deleting their data. Their API and CLI
   operations are unavailable too; retention deadlines continue to run.
6. **Temporary and permanent are deliberate properties.** The Scratchpad
   cleans itself up; Knowledge persists. Retention is part of each area's
   domain rather than an accidental side effect.
7. **Simple to operate and back up.** One Compose file, few containers, clear
   configuration, and a documented backup and restore process for the database
   and files.
8. **Opinionated rather than endlessly configurable.** Good default workflows
   matter more than freely definable entities, fields, and workflows.
9. **Extensible without prematurely becoming a plugin system.** The
   architecture and navigation anticipate more applications. The MVP promises
   neither a plugin marketplace nor a stable extension API.
10. **Open without hidden restrictions.** The entire core is MIT-licensed; no
    essential feature is reserved for an enterprise edition under a different
    license.

## 6. Application Concept

personalaffe has a global shell and several domain-specific applications:

```text
personalaffe
├── Home / Dashboard
├── Scratchpad
├── Knowledge
├── Tasks
├── Files
└── Settings
```

A prominent app switcher provides access to the different areas. After
switching, navigation belongs to the selected application: a knowledge base
needs different navigation from a file manager, and the Scratchpad should not
be forced into the same list view.

The global shell remains consistent and includes at least:

- sign-in, sessions, and personal security,
- an app switcher and access to the home page,
- settings and management of agent credentials,
- a consistent responsive layout,
- global search across the four core applications; quick actions may follow
  later.

### 6.1 Home and Dashboard

The home page answers: **What is useful or pending for me right now?** It is
a quick entry point into the personal workspace, not a reporting system.

Widgets can summarize content from active applications, for example:

- open tasks or tasks due soon,
- recently used lists or knowledge pages,
- the latest Scratchpad entries,
- recently uploaded files,
- weather for a location stored in settings.

Only widgets from enabled applications are offered. The MVP has a predefined
layout with individually showable or hideable tiles; free arrangement comes
later. Global search covers knowledge pages, tasks, Scratchpad text, and file
names, but does not search file contents. Changes made by agents or other
devices become visible without manual reload. The responsive web application
requires a network connection; offline editing is outside the MVP.

### 6.2 Scratchpad

The Scratchpad is a private, cross-device clipboard. Content can be pasted in
seconds, found on another device, copied, and immediately removed if needed.

Key properties:

- quick capture and content that is easy to copy,
- a chronological view that makes entries easy to find again,
- manual, permanent deletion of individual entries,
- automatic deletion after a configurable period, seven days by default,
- visibly pinned entries exempt from automatic expiry until unpinned,
- full operation through the CLI.

MVP entries are plain text only, and manual deletion is immediately permanent.
Retention continues while the application is disabled. Direct conversion into
knowledge pages or tasks is deferred.

The Scratchpad is deliberately not a knowledge base, an operating-system
clipboard synchronizer, or a permanent archive. Content worth keeping is moved
or copied into a permanent area.

### 6.3 Knowledge

The Knowledge area is a permanent personal knowledge base: a small, private
Confluence for one person, without its organizational and sharing complexity.

Initially, it should support:

- creating, reading, editing, moving, and deleting pages,
- organizing pages in a clear, bounded structure,
- searching content and opening it directly through stable URLs,
- storing readable structured text and code,
- maintaining knowledge equally well through an agent or a human,
- exporting content without a proprietary format.

Pages store Markdown and use a comfortable source editor with formatting tools
and preview, adopting the existing editor and library choices from planaffe
and hostingaffe. Pages have a manageable hierarchy and stable links that
survive renaming and moving. Pages can link to existing files; there is no
separate attachment store. A simple revision history allows previous page
content to be recovered. Exact hierarchy limits and any additional tagging or
backlink behavior remain implementation details; they are not additional MVP
commitments.

### 6.4 Tasks

The Tasks area provides simple, pleasant to-do lists. It helps one person
capture and complete small personal commitments without turning them into
project management.

Core features include:

- multiple named lists,
- quickly capturing, editing, completing, and deleting tasks,
- a title, optional description, optional due date, and manual task ordering,
- clearly separating open and completed tasks,
- optional due dates where useful for the dashboard and everyday use,
- showing open and upcoming tasks on the home page,
- full day-to-day operation through the CLI.

Sprints, teams, assignments, dependency graphs, time tracking, and extensive
project planning are outside the core scope. Recurring tasks are deferred.

### 6.5 Files

The Files area is simple storage for smaller files that the owner wants to
access from multiple devices or through an agent.

Core features include:

- uploading and downloading files,
- creating folders and navigating a tree structure,
- renaming, moving, and deleting files and folders,
- displaying basic metadata such as name, size, and modification date,
- secure, authenticated file operations through the web interface and CLI.

File content is stored on a local server volume, with metadata in PostgreSQL
and configurable size limits. Object storage is deferred. Deleted files and
folders enter Trash with a recovery period.

The MVP is neither a document editor nor a media library: no PDF or Office
previews, collaborative editing, comments, sharing links, or complex versioning.
The focus is on storage, organization, and reliable downloads.

## 7. CLI, API, and Working with AI Agents

The CLI is a full-featured working interface for agents and terminal-oriented
humans, not a reduced remote control. It uses the same HTTP API as the web
interface.

The CLI makes the following commitments:

- An agent can perform all ordinary content workflows: maintain Scratchpad
  entries, read and write knowledge, manage tasks, and upload, download, or
  organize files.
- Commands follow a predictable form such as
  `personalaffe <area> <action>`; a short executable name will be chosen before
  release.
- Machine-readable JSON and concise human-readable output are equally
  supported.
- Data goes to `stdout`, errors to `stderr`; exit codes distinguish relevant
  failure cases.
- Non-interactive calls must never wait for input, editors, or pagers.
- Text and files can be passed through `stdin` or a file path without
  cumbersome escaping.
- Agent credentials can be named and revoked individually. Actions can be
  attributed to the credentials used.

Each agent access has no access, read access, or read/write access separately
for each application. The owner creates and revokes agent tokens in the web
interface. Write access includes ordinary deletion under the application's
rules: immediately permanent in Scratchpad, recoverable in Trash for permanent
content. Emptying Trash, managing credentials, changing security settings, and
clearing or resetting the instance remain owner-only operations.

Knowledge, Tasks, and Files have a Trash recovery period. Knowledge pages also
have a simple revision history. Conflicting edits based on an outdated state
are rejected rather than silently overwriting newer work; the caller must
re-read the current state before retrying.

MCP access is a natural future interface, but is not part of the MVP. The HTTP
API and CLI must be clear enough for an MCP server to build on them later
without introducing a second set of domain behavior.

## 8. Identity, Privacy, and Security

The instance has exactly one human account: its owner and administrator.
At minimum, the person signs in with an email address and password and can set
up TOTP two-factor authentication with recovery codes. No mail server is
required: a documented recovery procedure using direct server access handles
lost owner access. Agents are technical identities of this owner,
not additional people or separate data spaces.

For a publicly accessible instance, this means:

- All content pages, API endpoints, and file retrievals require authentication;
  the MVP has no public content or sharing links.
- Passwords, sessions, two-factor secrets, and tokens are handled according to
  modern standards and are never stored or logged in plaintext.
- Authentication attempts are rate-limited; browser and API interfaces account
  for their respective typical attack vectors.
- File and path operations must never escape the intended storage area.
  Upload limits protect the instance from accidental or intentional resource
  exhaustion.
- Security-sensitive and destructive actions are traceable.
- The project documents secure default configuration, updates, backup and
  recovery, and a way to report vulnerabilities.

TLS termination, DNS, and certificate provisioning do not belong in the domain
logic. However, the operations documentation shows a secure setup behind a
reverse proxy such as Caddy, because operation on the open internet is an
intended normal case rather than an exception.

**The product does not promise** end-to-end or zero-knowledge encryption against
the server operator, protection against a compromised host, or formal security
certification.

## 9. Operations and Technical Guardrails

- A production instance starts with Docker Compose and needs only a few clearly
  identified persistent data stores.
- The application uses a .NET backend, React with TypeScript, and a Go CLI.
  One application container serves the web interface and HTTP API; PostgreSQL
  runs alongside it. Internal domain modules are clearly separated.
- Uploaded file content lives on a local persistent volume. Background work,
  including expiry and Trash purging, runs inside the application. Additional
  infrastructure such as a queue, separate search cluster, or object storage
  is outside the MVP.
- The database and file storage together form the backup. Backup and restore
  are documented and tested in practice. A short maintenance pause is accepted
  to obtain a consistent backup of both stores.
- Upgrades are reproducible; schema changes are controlled, and recovery using
  a previous backup is documented.
- The application does not provide its own public TLS endpoint. It runs
  properly behind a reverse proxy and trusts forwarding information only under
  a documented configuration.
- The basic structure and interaction conventions of the existing products
  planaffe, vaultaffe, and hostingaffe are reused where appropriate.
  Editor and library choices specifically follow planaffe and hostingaffe;
  existing implementations are inspected and adapted instead of repeating the
  selection process. A shared cross-product library is deferred until needed.
  Differences inherent to this personal, modular product are not hidden for
  the sake of forced uniformity.
- The MIT license, reproducible builds, and a clear local development setup
  make outside contributions practical.

CI checks the backend, web interface, CLI, and shared API contract. Fresh
installation, upgrade, and complete restore are exercised before the first
release.

## 10. MVP Scope

The MVP is achieved when one person can operate the instance themselves and
use all four core applications meaningfully in everyday life.

It includes:

1. Setup of the sole owner account, sign-in with email and password, and
   optional two-factor authentication.
2. A shared responsive application shell with a home page, app switcher,
   area-specific navigation, and the ability to enable or disable applications.
3. A useful dashboard showing at least tasks and the most relevant current
   content; a simple configurable weather widget is part of the intended MVP
   provided it does not require a disproportionate external service.
4. A Scratchpad with quick text entry, copying, manual deletion, and
   configurable automatic retention.
5. A Markdown knowledge base with a page hierarchy, stable links, editing and
   preview, simple revision history, search, and an open export format.
6. Simple to-do lists with task status and optional due dates.
7. File storage with folders, upload, download, move, rename, and delete.
8. An HTTP API and CLI for ordinary content workflows, plus separate,
   revocable agent credentials with per-application access levels.
   Permanent content has Trash recovery, and stale edits are rejected.
9. Docker Compose deployment, secure configuration for operation behind a
   reverse proxy, and a proven backup and restore process.
10. Global search over knowledge pages, tasks, Scratchpad text, and file names,
    plus automatic refresh of changes made by other devices and agents.

The ideas in IDEAS.md remain outside the MVP, including the notification center
and standardized data submission. Module boundaries should allow future
applications without implementing these ideas in advance.

## 11. Deliberate Non-Goals

- No multiple human users, tenants, teams, or family accounts.
- No sharing content between people or public links in the MVP.
- No native desktop or mobile app in the MVP; the web application is responsive.
- No complete replacement for Notion, Confluence, Nextcloud, Dropbox, or a
  project management tool.
- No real-time collaboration, comments, mentions, or notifications for other
  people.
- No office suite, PDF viewer, image editing, or media streaming in the Files
  area.
- No universally definable data types, forms, automation rules, or workflows.
- No plugin marketplace or third-party plugins in the MVP.
- No MCP server or native app-store app in the MVP.
- No SSO, LDAP, SCIM, or complex system of roles and permissions.
- No guarantees for very large files or use as a comprehensive cloud backup.

These boundaries are product decisions. Individual points may be deliberately
revisited later; they are not an implicit list of unfinished MVP work.

## 12. Development After the MVP

Natural but deliberately deferred directions include:

- an MCP server built on the same HTTP API,
- a native mobile app that connects to the owner's instance,
- additional focused personal applications,
- richer cross-application search and quick actions,
- more flexible dashboard layouts and additional widgets,
- import and export paths for common note, task, and file formats,
- optional permissions beyond the MVP's per-application access levels,
- links between areas, such as turning a Scratchpad entry into a knowledge page
  or task.

New applications are added only if they clearly address a recurring personal
use case, fit the shared environment, and do not add disproportionate
operational overhead.

## 13. How We Measure Success

The first release fulfills the vision when:

- a technically experienced person can go from a fresh machine to a running
  instance behind their own HTTPS subdomain in a short time,
- that same person can put text in the Scratchpad on one device and copy it on
  another without detours,
- knowledge remains clearly structured and easy to find after several weeks,
- open tasks are visible on the home page and can be completed without
  administrative overhead,
- a small file can be uploaded, moved into a folder, and downloaded again on
  another device,
- an agent with its own token reliably performs the same everyday content
  operations without a browser,
- disabling an unused application actually simplifies the interface without
  destroying its data,
- a complete backup has been successfully restored on a new instance,
- the web application is secure enough for its intended operation on the open
  internet without requiring a private network in front of it.

## 14. Remaining Implementation Details

The major product and architecture decisions above are agreed. The following
details can be settled within their owning epics without reopening the scope:

1. **Knowledge:** exact hierarchy limit, stable address syntax, revision
   retention, and export packaging. Use the existing affe Markdown components.
2. **Scratchpad:** configuration ranges and the precise expiry behavior after
   unpinning an entry.
3. **Tasks:** due-date display conventions, description presentation, and
   ordering interactions.
4. **Files:** default size limits and total quota, filename collision handling,
   and folder restoration behavior.
5. **Dashboard and search:** initial tile selection, ranking, pagination, and
   freshness targets.
6. **Recovery and concurrency:** Trash duration, restore collision handling,
   concurrency token representation, and treatment of concurrent structural
   changes. Ordinary stale updates must be rejected.
7. **Authentication:** setup and server-side recovery mechanics, session and
   token lifetimes, and TOTP enrollment details.
8. **Weather:** provider, caching, attribution, and behavior when unavailable;
   retain the vision's proportional-effort condition.
9. **Operations:** backup commands, maintenance coordination, purge scheduling,
   supported release platforms, and exact CI checks.
10. **CLI:** short executable name, configuration details, and command spelling
    following the established affe conventions.

Module deactivation blocks content access through web, API, and CLI, while
retention deadlines and cleanup continue. Re-enabling restores access to the
remaining data. These are settled rules, not open questions.
