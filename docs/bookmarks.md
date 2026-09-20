# Saved links

Bookmarks keeps HTTP(S) addresses and your own notes about them. Open
`/bookmarks` for favorites, frequently opened links, the reading list and recent
additions. A card opens its address in a new tab. Only an explicit opening in
personalaffe contributes to the last 30 UTC calendar days of usage; reading,
searching and editing do not. Nothing downloads favicons, crawls destinations
or checks whether a target is alive.

Use **Add bookmark** to paste a URL, change its suggested title and optionally
add a description, folder, tags or **Read later**. An existing URL produces a
hint; you can deliberately save another copy. Pin useful links and reorder
favorites by dragging or with the earlier/later buttons. **Mark as read** only
changes the reading list; its Undo preserves the previous queue position.

**Manage bookmarks** opens `/bookmarks/manage`. Search, folder, favorite, tag
and reading filters work together; multiple tags require every selected tag.
Folders include their descendants. Lists page through 100 links at a time.
The same controls work in the compact phone layout and with the keyboard.
Select rows to move, tag, mark as read or delete them. Ordinary bulk operations
report successes and failures individually; unchanged rows keep their data.
Edits that conflict with a newer version retain your input for review.

Folders are independent of Files. A private folder hides all its descendants.
**Private mode** reveals them only in the current tab, until reload or sign-out;
another tab starts with it off. Turning it off immediately removes private
content, open dialogs and bookmark filter values. Home, global search, the
palette and Trash use the same visibility. This is a visibility setting, not
an extra password or encryption. Application permissions remain necessary.
Moving content from a private location to a public one requires confirmation.

**Import HTML** reads a browser's Netscape bookmark export. Choose a destination,
review valid/rejected links and skipped duplicates, then confirm. Changed plans
must be previewed again. **Export HTML** normally includes only public links;
private content needs both private mode and the explicit export checkbox.
Limits are 2 MiB, 5000 entries and 32 folder levels; choose a smaller subtree
when exporting a larger collection. HTML loses private markings, favorites,
tags and reading status, so keep a private export safe and import it into a
private folder. A complete owner backup preserves these fields instead.

**Review duplicates** compares scheme, host and default ports while preserving
path case, query order and fragments. Choose one link to keep and the copies
to remove, review their different metadata, and confirm. All selected versions
must still match or nothing is removed. The keeper remains unchanged; nothing
merges descriptions, tags or statistics. Review supports 100000 visible links,
50 copies per page and 100 removals per action.

Deleted bookmarks and folders can be restored from Trash during retention.
Private origin stays private even when an old parent no longer exists.
Permanently deleting a copy also removes its separate opening statistics.
Switching Bookmarks off hides the application without erasing its contents.
Existing agent tokens receive no permission automatically: grant Bookmarks
access explicitly. See [API](api.md#bookmarks) and [CLI](cli.md) for automation;
`pea --include-private` applies only to that invocation.
