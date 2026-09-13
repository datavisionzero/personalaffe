# personalaffe

The language of a private workspace belonging to one person. Its applications
have distinct purposes and share a common home.

## Language

**Owner**:
The sole human to whom an instance and all of its content belong.
_Avoid_: Tenant, team, member

**Agent access**:
A named, revocable authorization for an agent acting on the owner's behalf,
with no access, read access, or read/write access to each application.
_Avoid_: Second user, agent account

**Application**:
A focused area of the workspace that can be enabled or disabled independently.
_Avoid_: Plugin, project

**Scratchpad entry**:
A temporary piece of plain text kept for cross-device use.
_Avoid_: Knowledge page, clipboard history

**Pinned entry**:
A Scratchpad entry exempt from automatic expiry until it is unpinned.
_Avoid_: Archived page

**Knowledge page**:
A lasting piece of personal knowledge with a title and a place in the page
hierarchy; its identity survives renaming and moving.
_Avoid_: Document, Scratchpad entry

**Page revision**:
A previous version of a knowledge page that can be recovered.
_Avoid_: Backup

**Task list**:
A named, manually ordered collection of personal tasks.
_Avoid_: Project, sprint

**Task**:
A personal commitment that is open or completed, with an optional description
and due date.
_Avoid_: Issue, ticket

**File**:
A stored personal file with a name and a location in the folder hierarchy.
_Avoid_: Separate attachment

**Folder**:
A named container for files and other folders.
_Avoid_: Space, bucket

**Trash**:
The recoverable state of deleted permanent content before its retention period
ends or the owner permanently removes it.
_Avoid_: Archive, backup

**Dashboard tile**:
A compact view of useful or pending information on the home page.
_Avoid_: Report, application
