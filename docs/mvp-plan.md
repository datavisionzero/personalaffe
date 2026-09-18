# personalaffe — MVP Implementation Plan

Status: **delivered — ten epics of ten.** PERSONAL-E1 to PERSONAL-E9 are — the application, API, CLI and delivery foundation; the door in
front of them, with one owner, an optional second factor, agent access with a
permission per application and a recovery that needs the machine rather than a
mail server; the safeguards over content: the guard on a write, deletion that
can be taken back, one Trash over the four applications, the sweep that empties
it, and the rules for restoring into a tree; and the workspace they are drawn
in: the four switches, the frame with its navigation, palette and keys, the
shared Markdown editor, and screens that keep up with the instance without a
reload; and the first application, the Scratchpad — plain text captured in
seconds, copied on another device, pinned when it is worth keeping and destroyed
when it is not; and the second application, Files — bytes on a volume at an
address made from an id, folders to put them in, a Trash that finally has a
contributor, and a tidy-up for what an interrupted upload leaves behind; and the
third, Knowledge — Markdown in a tree, an address a rename cannot break, a
history that only ever grows, and an export that is a zip somebody can read
without ever having heard of this product; and the fourth, Tasks — named lists,
a due date that is a day rather than a moment, and an order in which moving one
task changes one row; and what the four of them add up to: one search over all
of them, kept up to date by a column Postgres computes rather than by anything
this product runs, a home page of five tiles the owner can hide one at a time,
and a weather tile at an address of its own so that a server somewhere else can
never hold the home page up ([`docs/codebase.md`](codebase.md),
[ADR 0002](adr/0002-one-owner-with-a-browser-and-agents-with-tokens.md),
[ADR 0003](adr/0003-content-is-guarded-by-what-it-was-read-at-and-deleted-by-being-set-aside.md),
[ADR 0004](adr/0004-one-frame-four-switches-and-a-screen-that-asks-again.md),
[ADR 0005](adr/0005-the-scratchpad-keeps-nothing-and-its-clock-runs-from-the-last-change.md),
[ADR 0006](adr/0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md),
[ADR 0007](adr/0007-a-page-is-its-id-and-its-history-only-grows.md),
[ADR 0008](adr/0008-a-due-date-is-a-day-and-an-order-is-a-number-between-two-others.md),
[ADR 0009](adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).

And PERSONAL-E10 is everything a person needs before they put something they
would mind losing into it: an installation somebody else can follow from no
checkout at all, behind their own proxy; one backup carrying the database and
the files as of one moment, and a restore that has been done rather than
described; an upgrade that has been walked, and the way back from one that went
wrong; a pass over the security surface now that all of it exists, with the
headers every answer carries and the report address that goes with them; and the
artifacts a release is made of, cut from a tag
([ADR 0010](adr/0010-the-pause-makes-two-stores-agree-and-the-way-back-is-the-backup.md),
[`docs/install.md`](install.md), [`SECURITY.md`](../SECURITY.md)).

**Every feature VISION.md names for the MVP exists, and every promise around it
has a test, a CI job or a walked rehearsal behind it.** What is left is not work:
publishing the first release is a tag, and cutting it is the owner's decision.

One scope decision came back to the owner during the plan and was answered
without them: VISION.md admits a weather tile only if a proportionate
integration exists. Open-Meteo needs no account, no key and no secret, so the
condition is met and the tile is in the MVP
([ADR 0009](adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).
`PERSONALAFFE_WEATHER=off` is the other half of that answer: an instance that is
not supposed to talk to anybody but its owner opens no socket at all.

The plan implements the four core applications in VISION.md. IDEAS.md remains non-binding and outside the MVP. CONTEXT.md defines domain vocabulary. Epic descriptions are maintained in the PERSONAL project in planaffe; this document is the repository copy of their initial plan.

## Agreed guardrails

- One owner, one instance, responsive online web UI, and full ordinary content workflows through HTTP API and Go CLI.
- .NET, React/TypeScript, PostgreSQL, local file storage, one application container, and in-process background work.
- Adopt the editor, rendering, UI, and other suitable library choices from planaffe and hostingaffe. Their existing CodeMirror, react-markdown, remark-gfm, remark-breaks, Tailwind, and Base UI components are the starting point. Do not spend time re-selecting solved dependencies. See docs/adr/0001-adopt-the-existing-affe-stack-and-components.md.
- Per-application agent permissions; no second human account, runtime dependency on other affe products, or shared-library extraction required.
- Disabled applications are unavailable through web/API/CLI and aggregate views; retention keeps running.
- Immediate Scratchpad deletion, recoverable deletion for lasting content, simple Knowledge revision history, and rejected stale updates.
- Markdown pages with stable links, text-only Scratchpad, simple manually ordered tasks, and files with folders.
- Predefined dashboard with hide/show tiles, global search excluding file contents, and automatic refresh without clobbering unsaved edits.
- Optional TOTP with recovery codes, server-side owner recovery without SMTP, and tested backup/restore with a short maintenance pause permitted.

## Delivery sequence

1. Establish the runnable foundation.
2. Add owner/agent access; then build content safeguards and the shared app shell.
3. Deliver Scratchpad, Files, and Tasks as complete API/CLI/web slices. Their implementation can proceed independently once their foundations exist.
4. Deliver Knowledge using the shared editor, content safeguards, and stable file references.
5. Integrate dashboard, global search, and weather.
6. Complete operational acceptance and first-release readiness. Deployment, security, and backup design start in the earlier epics.

The dependencies below express implementation prerequisites. They are recorded in epic descriptions; they are not tracker-enforced blockers between epics. When detailed issues are created later, translate the relevant dependencies into issue blockers. Do not mark unfinished or ambiguous implementation tickets ready automatically.

## Epic plans

### PERSONAL-E1: MVP: Establish the application, API, CLI, and delivery foundation

Prerequisites: none. **Delivered.**

#### Outcome

A runnable personalaffe foundation supports a .NET backend, React/TypeScript web application, Go CLI, and PostgreSQL, with clear internal application boundaries and a shared HTTP contract.

#### Implementation plan

1. Inspect planaffe and hostingaffe before scaffolding. Adopt suitable build, API, client-generation, error-handling, persistence, and delivery conventions. Record which source components were adapted.
2. Establish repository layout, domain/module boundaries, migrations, local development commands, and a minimal deployable application. Keep business rules shared behind the HTTP API.
3. Establish the API contract and client generation for web and CLI. Define configuration, JSON/human output, stdin/file input, stderr, non-interactive behavior, and exit-code conventions.
4. Add the application image, Compose setup, local file volume, health checks, and initial CI for backend, web, CLI, and contract consistency.
5. Document the foundation and extend its tests and contract checks as the feature epics land.

#### Acceptance criteria

- A fresh development checkout builds all three toolchains and starts the application with PostgreSQL.
- One application container serves web and API; file storage persists on a local volume.
- Web and CLI use the same HTTP API; the checked contract and generated clients agree.
- CI exercises real build/contract paths and fails on relevant drift.
- No project tracker, infrastructure management domain, multi-user model, plugin system, or dependency on another running affe product is imported.

#### Constraints and later details

Use the agreed stack and existing library choices; do not repeat library comparisons without a concrete gap. Exact layout, API route spelling, and CLI executable name can be settled here. A shared cross-product library is not required. Production recovery and release validation are completed in the operations epic.

### PERSONAL-E2: MVP: Secure owner access and scoped agent credentials

Prerequisites: PERSONAL-E1. **Delivered.**

#### Outcome

Exactly one human owner can securely access and recover the instance. Each agent acts with individually revocable, per-application permissions.

#### Implementation plan

1. Adapt existing affe authentication foundations to a single owner and one data space. Implement safe one-time setup and email/password browser sign-in.
2. Add optional TOTP enrollment, verification, recovery codes, and session management. Document owner recovery through direct server access without requiring SMTP.
3. Add owner-only web management of named agent tokens. Each application supports no access, read, or read/write.
4. Enforce permissions centrally and in content operations, including direct IDs, downloads, revisions, Trash, searches, and dashboard summaries as those features arrive.
5. Make security-sensitive and destructive actions attributable to the owner or agent access; protect tokens and session material in storage and logs.

#### Acceptance criteria

- The instance cannot acquire a second human account through setup, UI, CLI, or API.
- Unauthenticated content access is denied; browser and token authentication have appropriate protections.
- TOTP and recovery codes work, and lost access can be recovered using the documented server procedure.
- Revoked tokens stop working; read access cannot mutate, and absent access cannot reveal application content through any surface.
- Agent write access permits normal deletion: permanent in Scratchpad and into Trash for lasting content.
- Agents cannot issue credentials, change security settings, empty Trash, or reset the instance.
- Integration tests cover permission bypass attempts and credential revocation.

#### Constraints and later details

Email is the login identifier, not a requirement for a mail delivery service. Token/session lifetimes and recovery mechanics can be chosen here using the existing implementations. Recovery and privileged operations must remain outside ordinary agent authority.

### PERSONAL-E3: MVP: Protect content with recoverable deletion and guarded updates

Prerequisites: PERSONAL-E1, PERSONAL-E2. **Delivered.**

#### Outcome

Permanent content can be recovered after deletion, stale edits cannot overwrite newer work, and retention remains effective even when an application is disabled.

#### Implementation plan

1. Define shared conventions for concurrency guards, recoverable deletion, actor attribution, and application enablement checks without forcing application data into one generic entity.
2. Provide Trash lifecycle support for Knowledge, Tasks, and Files; leave Scratchpad deletion immediately permanent.
3. Run expiry and Trash purging inside the application. Ensure deactivation does not suspend retention deadlines and cleanup resumes safely after downtime.
4. Define recovery behavior for hierarchies and naming collisions, plus owner-only permanent removal. Apply the scoped agent permissions to recoverable content operations.
5. Provide the revision and restore conventions needed by Knowledge; integrate and test them against each real content module.

#### Acceptance criteria

- Two updates based on the same prior state cannot silently replace each other; the second stale write fails through API, CLI, and web.
- Deleted lasting content is excluded from normal reads, search, and dashboard and can be restored within its recovery period.
- Agents cannot bypass Trash by invoking a permanent deletion path.
- Cleanup continues for disabled applications; disabling never silently erases unexpired data.
- Trash expiry and restore conflicts have explicit outcomes, including file bytes and hierarchical content.
- Application-specific tests verify the conventions against real persisted content as modules land.

#### Constraints and later details

The exact Trash duration, concurrency token, hierarchy collision policy, and purge scheduling remain implementation details. Knowledge revision history is completed in the Knowledge epic. Do not build a universal workflow or entity framework.

### PERSONAL-E4: MVP: Deliver the responsive app shell and shared editing experience

Prerequisites: PERSONAL-E1, PERSONAL-E2. **Delivered.**

#### Outcome

The owner moves between independent applications in one coherent responsive workspace. Disabled applications are unavailable consistently, and Markdown editing uses the existing affe experience.

#### Implementation plan

1. Build home, app switcher, settings, and area-owned navigation with desktop and phone layouts.
2. Add owner controls to enable or disable applications. Apply the shared application state to routes, API/CLI access, search contributors, and available tiles.
3. Adapt the existing CodeMirror-based MarkdownField/Editor, Markdown renderer, formatting commands, and Tailwind/Base UI primitives from planaffe and hostingaffe.
4. Establish automatic refresh for agent and cross-device changes, with bounded background activity and connection/error states.
5. Ensure refresh preserves unsaved edits and lets conflict handling resolve stale saves instead of overwriting the editor.
6. Provide shared empty, loading, denied, disabled, and failure states for the content epics.

#### Acceptance criteria

- Navigation works on desktop and phone, including direct links into an application.
- Disabled applications disappear from ordinary workflows and reject direct web/API/CLI content operations.
- Re-enabling reveals retained data; the UI explains that retention continues while disabled.
- The shared editor edits Markdown source with formatting tools and preview using the established libraries, with safe rendering.
- Changes from another device or an agent appear without a manual reload; unsaved human edits are preserved.
- Browser checks cover navigation, responsive layouts, keyboard use, editing, and refresh behavior.

#### Constraints and later details

Use the source components and library choices named in docs/adr/0001-adopt-the-existing-affe-stack-and-components.md. Both products currently use CodeMirror, react-markdown, remark-gfm, remark-breaks, Tailwind, and Base UI. Do not start a fresh editor/library selection exercise. Transport and refresh interval are implementation decisions. Offline editing, a native app, and a plugin system are excluded.

### PERSONAL-E5: MVP: Deliver the cross-device text Scratchpad

Prerequisites: PERSONAL-E2, PERSONAL-E3, PERSONAL-E4. **Delivered.**

#### Outcome

The owner or an agent can capture plain text in seconds, copy it on another device, and rely on predictable automatic cleanup.

#### Implementation plan

1. Add plain-text entries, chronological listing, quick capture/edit, and convenient copying.
2. Implement configurable retention with a seven-day default and visible pinning that exempts an entry from automatic expiry.
3. Implement immediate permanent manual deletion and expiry, including while Scratchpad is disabled.
4. Deliver equivalent ordinary operations in HTTP API, CLI, and web, with concurrency and scoped access enforced.
5. Expose permission-filtered contributions for dashboard, search, and refresh.

#### Acceptance criteria

- Text entered on one device is available and easily copied on another; Unicode and multiline text survive intact.
- CLI accepts text through stdin or a file and returns clean JSON or human-readable output.
- Unpinned entries expire according to the configured policy; pinned entries survive expiry.
- Manual deletion is final, including when performed by an agent with write access.
- Disabled Scratchpad rejects access while retention continues.
- Tests cover pinning, expiry boundaries, authorization, stale edits, and cross-device refresh.

#### Constraints and later details

Plain text only. No operating-system clipboard synchronization, permanent archive, or direct conversion to other applications. Decide expiry behavior after unpinning and configuration ranges within this epic.

### PERSONAL-E6: MVP: Deliver private file storage with folders and recovery

Prerequisites: PERSONAL-E2, PERSONAL-E3, PERSONAL-E4. **Delivered.**

#### Outcome

Small personal files can be uploaded, organized, downloaded, and recovered through web and CLI, with file bytes on a local volume and metadata in PostgreSQL.

#### Implementation plan

1. Implement files and a folder tree, metadata, configurable individual/total limits, upload, download, rename, move, and delete.
2. Use safe internal storage addressing; enforce authentication and application permissions for every byte retrieval and mutation.
3. Integrate Trash for files and folders, preserving recoverable bytes and explicitly handling restore collisions and structural changes.
4. Define authenticated stable file references that Knowledge can link to without a separate attachment store.
5. Handle interrupted uploads and failures between metadata and file writes; coordinate cleanup with the later consistent-backup workflow.
6. Deliver web/API/CLI parity, file-name search contributions, dashboard summaries, and refresh.

#### Acceptance criteria

- A file uploaded through either client can be organized and downloaded through the other with matching bytes.
- Path traversal, unauthorized direct downloads, out-of-quota writes, and stale structural edits are rejected.
- Deleted files remain recoverable for the Trash period; expiry removes the appropriate metadata and bytes.
- Folder deletion/restoration and name collisions have tested, documented behavior.
- Renaming or moving a file does not invalidate its stable reference.
- Integration tests exercise interrupted uploads, cleanup, and permission enforcement with real storage.

#### Constraints and later details

No object storage, preview, sharing links, Office editing, streaming, or complex file versioning. Choose concrete default limits and collision policies here. Links to files reuse this area rather than creating a second attachment store.

### PERSONAL-E7: MVP: Deliver Markdown Knowledge with stable links and page history

Prerequisites: PERSONAL-E2, PERSONAL-E3, PERSONAL-E4, PERSONAL-E6. **Delivered.**

#### Outcome

The owner and agents maintain lasting personal knowledge as Markdown, with a manageable page hierarchy, stable references, simple revision history, and portable export.

#### Implementation plan

1. Implement titled pages and a bounded hierarchy with create, read, edit, move, rename, delete, and restore.
2. Separate page identity from display title and position so existing direct links remain valid after rename or move.
3. Use the shared affe Markdown source editor, formatting tools, preview, and safe renderer. Allow links to existing files through the Files module.
4. Add simple page revision history and recovery, preserving newer work and using concurrency guards for editing and restoration.
5. Export readable Markdown in an open package that preserves enough hierarchy and reference information for use outside personalaffe.
6. Deliver all ordinary page workflows through API, CLI, and web, plus contributions to search, dashboard, and refresh.

#### Acceptance criteria

- Both a human and an agent can create, read, edit, organize, and recover pages.
- A page URL still resolves after rename and move; invalid hierarchy operations cannot corrupt the tree.
- Markdown survives CLI/web round trips and the preview follows the adopted affe editing conventions.
- Earlier page content can be recovered; outdated updates and restores cannot silently overwrite newer content.
- File links honor Files availability and access permissions.
- Deleted pages enter Trash; unauthorized or disabled content is absent from normal reads and aggregate views.
- Export contains readable Markdown and usable structural/reference information; representative output is inspected.

#### Constraints and later details

Reuse existing components and libraries rather than selecting a new editor. Exact hierarchy depth, stable address spelling, revision retention, and export packaging can be chosen here. Tags, automatic backlinks, and direct Scratchpad conversion are not additional MVP commitments.

### PERSONAL-E8: MVP: Deliver simple personal task lists

Prerequisites: PERSONAL-E2, PERSONAL-E3, PERSONAL-E4. **Delivered.**

#### Outcome

The owner can capture and complete personal commitments in named lists, with optional descriptions and due dates and a manually controlled order.

#### Implementation plan

1. Implement task lists and tasks with title, optional description, optional due date, open/completed state, and manual ordering.
2. Deliver quick capture, editing, completing, reopening, deleting, and recovering through API, CLI, and web.
3. Apply stale-update protection to content, state, and order changes, and define list deletion/restoration behavior.
4. Provide upcoming/open task summaries, search contributions, and cross-device refresh.
5. Exercise date and completed-state behavior on desktop and phone.

#### Acceptance criteria

- Multiple named lists keep tasks clearly separated and manually ordered.
- Human and agent can perform the same ordinary task workflows.
- Optional due dates appear consistently in lists and the dashboard without unintended timezone shifts.
- Deleted tasks and lists follow Trash rules; stale changes cannot silently overwrite newer work.
- Read-only and excluded agent access are enforced across lists, direct tasks, and summaries.
- Tests cover ordering conflicts, state changes, dates, permissions, and recovery.

#### Constraints and later details

No recurring tasks, assignments, sprints, project planning, dependency graphs, or time tracking. Date display conventions, ordering interaction, and description presentation can be decided here; use the established editor if Markdown is used.

### PERSONAL-E9: MVP: Bring the workspace together with dashboard, global search, and weather

Prerequisites: PERSONAL-E4, PERSONAL-E5, PERSONAL-E6, PERSONAL-E7, PERSONAL-E8. **Delivered.**

#### Outcome

The home page shows what is useful or pending, and one search finds permitted content across the four applications.

#### Implementation plan

1. Compose a predefined dashboard with individually showable/hideable tiles for open/upcoming tasks and relevant recent knowledge, lists, Scratchpad entries, and files.
2. Implement global search over knowledge pages, tasks, Scratchpad text, and file names using the existing application/database infrastructure.
3. Apply application enablement, caller permissions, deletion, and expiry rules before returning results, counts, snippets, or summaries.
4. Integrate automatic refresh so external changes and permission/enablement changes are reflected without manual reload.
5. Add configurable-location weather if a proportionate provider is available; cache results and make temporary unavailability non-blocking.
6. Verify useful defaults, result navigation, empty states, and phone layouts.

#### Acceptance criteria

- The owner can hide/show eligible tiles within a fixed layout.
- Search finds representative content from every core application and opens the correct stable target.
- File contents are not indexed; disabled, deleted, expired, or inaccessible content does not leak through results or counts.
- Changes made through CLI or another device appear without manual reload.
- Weather uses the configured location, makes freshness/failure clear, and does not block the workspace if unavailable.
- Cross-application integration tests verify permission and lifecycle filtering; browser checks cover dashboard and search.

#### Constraints and later details

Choose ranking, pagination, default tiles, freshness targets, and weather provider here. If no proportionate weather integration is found, record the evidence and return that scope decision to the owner under VISION.md's existing condition. *Settled: ranking is `ts_rank` over a `simple` vector weighted A for what a thing is called and B for what it says; there is no cursor, only a limit with `has_more`; all five tiles are shown by default; a reading is held for fifteen minutes; and the provider is Open-Meteo, which needs no account, no key and no secret — so the condition is met and the scope decision does not return to the owner ([ADR 0009](adr/0009-the-index-is-a-column-and-the-weather-waits-on-nobody.md)).* No free-form dashboard builder, custom data ingestion, finance ticker, or notification center in the MVP.

### PERSONAL-E10: MVP: Prove secure operation, backup, upgrades, and release readiness

Prerequisites: PERSONAL-E1, PERSONAL-E2, PERSONAL-E3, PERSONAL-E4, PERSONAL-E5, PERSONAL-E6, PERSONAL-E7, PERSONAL-E8, PERSONAL-E9. **Delivered.**

#### Outcome

A technically experienced owner can install personalaffe behind a public HTTPS subdomain, use all four applications, upgrade it, and recover the entire instance from a verified backup.

#### Implementation plan

1. Complete production Compose, reverse-proxy guidance, persistent-volume configuration, health checks, and operator documentation using the existing affe delivery conventions.
2. Implement/document a coordinated backup of PostgreSQL and file storage. A short maintenance pause must stop relevant writers, including cleanup, while consistency is established.
3. Prove restoration onto a fresh instance, including file bytes, knowledge revisions, recoverable Trash, owner security material, application settings, and agent permissions.
4. Exercise forward schema migration and an upgrade from an earlier populated build. Document recovery from a failed upgrade using a pre-upgrade backup.
5. Finish security and end-to-end checks across browser, API, CLI, disabled modules, permission revocation, retention, and concurrent edits.
6. Prepare image/CLI release artifacts and install documentation; verify the first-release journey and record evidence.

#### Acceptance criteria

- A clean host can reach a working, authenticated instance behind its own TLS-terminating reverse proxy using documented steps.
- All four applications pass the everyday workflows in VISION.md through web and CLI.
- Backup captures mutually consistent database and file state; a fresh restore is verified by actual reads/downloads and representative file checksums.
- Owner recovery without SMTP, TOTP recovery, agent revocation, deactivation, and cleanup behavior are exercised.
- A populated earlier build upgrades successfully; the rollback-by-restore procedure is tested.
- Backend, web, CLI, and contract CI checks pass; browser and operational evidence is recorded.
- Release artifacts and instructions can be followed without undocumented local assumptions or secrets.

#### Constraints and later details

This is the final acceptance gate, not a reason to postpone operational work: start deployment and backup coordination with the foundation and content storage work. No additional queue, search cluster, mandatory object storage, native app, or offline mode. Exact supported platforms and operator commands are settled here. Preparing this plan does not itself publish a release.
