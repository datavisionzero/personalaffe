# personalaffe — Product Vision

> **Status:** First draft · **Language:** English
>
> This document describes the product's direction, not its final specification.

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
   normal workflows without silently deleting their data.
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
- potentially cross-application search and quick actions later.

### 6.1 Home and Dashboard

The home page answers: **What is useful or pending for me right now?** It is
a quick entry point into the personal workspace, not a reporting system.

Widgets can summarize content from active applications, for example:

- open tasks or tasks due soon,
- recently used lists or knowledge pages,
- the latest Scratchpad entries,
- recently uploaded files,
- weather for a location stored in settings.

Only widgets from enabled applications are offered. How freely their selection
and arrangement can be configured in the MVP remains a detail to decide; the
home page must already be useful with good defaults.

### 6.2 Scratchpad

The Scratchpad is a private, cross-device clipboard. Content can be pasted in
seconds, found on another device, copied, and immediately removed if needed.

Key properties:

- quick capture and content that is easy to copy,
- a chronological view that makes entries easy to find again,
- manual, permanent deletion of individual entries,
- automatic deletion after a configurable period, such as seven days,
- a visible exemption for entries that should be kept longer,
- full operation through the CLI.

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

The exact page hierarchy, editing experience, linking, and role of tags will be
decided separately before implementation. The vision calls for a good personal
knowledge base, not a clone of Confluence or Notion.

### 6.4 Tasks

The Tasks area provides simple, pleasant to-do lists. It helps one person
capture and complete small personal commitments without turning them into
project management.

Core features include:

- multiple named lists,
- quickly capturing, editing, completing, and deleting tasks,
- clearly separating open and completed tasks,
- optional due dates where useful for the dashboard and everyday use,
- showing open and upcoming tasks on the home page,
- full day-to-day operation through the CLI.

Sprints, teams, assignments, dependency graphs, time tracking, and extensive
project planning are outside the core scope.

### 6.5 Files

The Files area is simple storage for smaller files that the owner wants to
access from multiple devices or through an agent.

Core features include:

- uploading and downloading files,
- creating folders and navigating a tree structure,
- renaming, moving, and deleting files and folders,
- displaying basic metadata such as name, size, and modification date,
- secure, authenticated file operations through the web interface and CLI.

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

Agents do not automatically receive access to administrative or particularly
destructive functions. These include changing human sign-in credentials,
managing two-factor authentication, issuing additional credentials, and
clearing or resetting the entire instance. Which content deletion operations
agents may perform and which need additional safeguards will be decided
deliberately for each application.

MCP access is a natural future interface, but is not part of the MVP. The HTTP
API and CLI must be clear enough for an MCP server to build on them later
without introducing a second set of domain behavior.

## 8. Identity, Privacy, and Security

The instance has exactly one human account: its owner and administrator.
At minimum, the person signs in with an email address and password and can set
up two-factor authentication. Agents are technical identities of this owner,
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
- The preferred basic setup is one application with PostgreSQL and persistent
  storage for uploaded files. Additional infrastructure such as a queue,
  separate search cluster, or mandatory object storage is outside the MVP.
- The database and file storage together form the backup. Backup and restore
  are documented and tested in practice.
- Upgrades are reproducible; schema changes are controlled, and recovery using
  a previous backup is documented.
- The application does not provide its own public TLS endpoint. It runs
  properly behind a reverse proxy and trusts forwarding information only under
  a documented configuration.
- The basic structure and interaction conventions of the existing products
  planaffe, vaultaffe, and hostingaffe are reused where appropriate.
  Differences inherent to this personal, modular product are not hidden for
  the sake of forced uniformity.
- The MIT license, reproducible builds, and a clear local development setup
  make outside contributions practical.

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
5. A knowledge base with pages, basic structure, editing, search, and an open
   export format.
6. Simple to-do lists with task status and optional due dates.
7. File storage with folders, upload, download, move, rename, and delete.
8. An HTTP API and CLI for ordinary content workflows, plus separate,
   revocable agent credentials.
9. Docker Compose deployment, secure configuration for operation behind a
   reverse proxy, and a proven backup and restore process.

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
- stronger cross-application search and quick actions,
- more flexible dashboard layouts and additional widgets,
- import and export paths for common note, task, and file formats,
- optional finer-grained permissions per agent credential,
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

## 14. Open Product Questions

These questions do not change the core vision but must be answered before or
during specification of the MVP:

1. **Knowledge model:** How deep can the page hierarchy go? Do pages need tags,
   internal links, backlinks, or attachments? Which editing model combines
   ease of use with an open export format?
2. **Scratchpad:** Are MVP entries text-only? What is the default retention
   period, and how is an entry exempted from automatic deletion or transferred
   into Knowledge?
3. **Tasks:** Which fields are really needed beyond text, list, status, and an
   optional due date? Do ordering or recurring tasks already belong in the MVP?
4. **Files:** What are the maximum individual file size and total quota? Must
   files live on a local volume, or should compatible object storage be an
   option from the start?
5. **Dashboard:** Which widgets are fixed, which are selectable, and how much
   freedom to arrange them is justified in the MVP?
6. **Agent permissions:** Which content deletion operations are allowed by
   default, and which need additional approval or a recovery period?
7. **Search:** Is search within each application sufficient initially, or is
   global search necessary for the first release?
8. **Modularity:** Are applications built in at build time and merely
   disableable, or are stronger internal module boundaries already needed?
9. **Weather:** Which data provider fits self-hosting, privacy, and simple
   configuration, and how does the widget behave without network or API access?
10. **Deletion and recovery:** Which permanent content gets a trash bin, which
    is deleted immediately, and how clearly does this differ from the
    deliberately temporary Scratchpad?
