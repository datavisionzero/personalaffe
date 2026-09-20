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

**Bookmark**:
A saved HTTP or HTTPS address with a title, optional description and a stable
identity, optionally organised in a bookmark folder.
_Avoid_: Archived web page, file

**Bookmark folder**:
A named container for bookmarks and other bookmark folders, independent of the
file hierarchy. A private folder makes its descendants private too.
_Avoid_: File folder, collection

**Trash**:
The recoverable state of deleted permanent content before its retention period
ends or the owner permanently removes it.
_Avoid_: Archive, backup

**Dashboard tile**:
A compact view of useful or pending information on the home page. A tile can be
hidden without changing anything but the home page.
_Avoid_: Report, application, widget

**Finding**:
One thing a search matched, in whichever application holds it.
_Avoid_: Hit, document, result set

**Backup**:
One archive holding the database and the stored files as of one moment, with a
manifest describing both. Anything that carries only one of the two is not one.
_Avoid_: Export, dump, snapshot

**Maintenance pause**:
The short stillness a backup holds an instance in: reads continue and writes are
refused until it ends or its deadline passes.
_Avoid_: Downtime, maintenance mode, read-only mode

**Weather place**:
The point and the label the owner chose for the weather tile. It is not an
address and it is never resolved by a tile.
_Avoid_: Location service, city

**Instance appearance**:
What one installation is called and what its mark looks like: a title of the
owner's own, a colour from a closed set and one of two shapes. It names this
instance and never the product.
_Avoid_: Branding, theme, skin

**Instance title**:
The owner's own name for their installation, shown wherever the product name
would otherwise be. Plain text, never Markdown, and readable by whoever can
reach the instance.
_Avoid_: Workspace name, site name, tenant name

**Instance mark**:
The shape drawn beside the instance title, in the appearance's colour, carrying
up to two letters derived from the title. It is drawn by this repository and is
never an uploaded image.
_Avoid_: Logo, avatar, favicon
