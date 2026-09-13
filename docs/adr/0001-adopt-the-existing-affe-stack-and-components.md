# Adopt the existing affe stack and components

personalaffe uses a .NET backend, React with TypeScript, a Go CLI, and
PostgreSQL, delivered as one application with clear internal module boundaries.
We adopt suitable conventions, components, and library choices from planaffe
and hostingaffe, particularly their Markdown editor and rendering pipeline,
because repeating these selections would consume time without advancing the
personal workspace.

Start with their CodeMirror-based `MarkdownField`/`Editor`, `react-markdown`
with `remark-gfm` and `remark-breaks`, and Tailwind/Base UI components. Inspect
the existing implementations and lockfiles when adopting them; do not conduct
a new library selection exercise without a concrete unmet requirement.

Reuse does not import the other products' domain or multi-user model. The
owner remains the only human account, knowledge links must survive moves and
renames, and no runtime dependency on the other products is introduced.
Extracting a shared cross-product library is deferred until a concrete need
justifies it.

Reference implementations in both repositories: `src/web/src/shared/Editor.tsx`,
`MarkdownField.tsx`, `Markdown.tsx`, `markdownCommands.ts`, and `src/web/package.json`.
The corresponding rationale is recorded in planaffe's ADRs 0003, 0004, 0007,
0017, and 0020, and hostingaffe's `docs/codebase.md`.
