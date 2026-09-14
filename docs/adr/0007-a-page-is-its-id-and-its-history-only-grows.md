# A page is its id, and its history only grows

PERSONAL-E7 is the third application and the first one that keeps any history.
Most of what it does is *not* a decision: it opens every act on
`ReachingAnApplication`, it guards every write with the version it was read at,
it draws the five states of `shell/States.tsx`, it applies `Restoration` as
PERSONAL-E3 wrote it, and it writes through the Markdown field PERSONAL-E4
built. It is also the second module to be a tree, and it borrowed everything it
could from the first
([ADR 0006](./0006-a-file-is-its-id-and-its-bytes-go-down-before-its-row.md)).

What follows is the part that was decided here.

## The id is the identity, and the title is a label

`GET /api/knowledge/pages/{id}` is where a page is, and the id is made once when
the page is written. Renaming it, moving it, rewriting it and recovering an old
version all leave that address answering.

`CONTEXT.md` already said this — "its identity survives renaming and moving" —
and `docs/mvp-plan.md` asked for it in so many words. The decision was only
*how*, and the answer is the one Files reached an epic earlier for the same
reason: the alternative is an address built out of the path, which breaks every
link the moment the owner organises anything, which is the one thing a knowledge
base is for.

`shared/links.ts` gains `page:` beside PERSONAL-E6's `file:`. The seam it was
written around is now filled from both sides.

**A `page:` link is followed and a `file:` link is downloaded.** One is an
address this application has a screen for, so following it keeps the tree beside
the reader where it was; the other is bytes only the instance can answer, and
asking the router for it would be asking the router for something it does not
have.

## History writes forward, and there is no other kind

Recovering a previous version leaves a revision of what was current until then.
History only grows.

The alternative — rewinding, dropping everything after the version being put
back — would make "undo" the one operation in this product that destroys work.
Every other destructive thing here asks, or is recoverable, or is the
Scratchpad; a quiet one hiding inside the safe-sounding verb would be the worst
place to put it.

Two consequences follow, and both were already written down by PERSONAL-E3
before there was anything to apply them to:

- **A recovery is guarded by the page's version, not the revision's.** A
  revision never changes, so it has no version worth holding. Recovering
  something read ten minutes ago must not discard an edit made five minutes ago
  — which is exactly the case the guard exists for, arriving through the one
  door that looks like it should be exempt.
- **Revisions belong to the page.** They go into the Trash with it, come back
  with it, and are removed for good with it. A revision that outlived its page
  would be content the owner believes they deleted.

**A write that changes nothing leaves no revision.** It is still guarded and
still checked — a write that agreed with what is stored is still a write
somebody made from a stale screen — but a history of moments when nothing
happened is a history nobody can read.

**Fifty versions, a count and not an age.** `Revisions.Kept` decided that an
epic ago and building the first real history changed nothing about it: a page
edited twice a year deserves its history as much as one edited twice a day.

## A revision keeps the title, and not the place

It keeps what the page said *and* what it was called, because a rename is a
change to the page like any other and a history that lost it would answer "what
did this say" and not "what was this called".

It does not keep where the page sat. A history that could move a page would be a
history that can break the tree — put a page back under something that has since
been deleted, or under itself — and recovering is meant to be the operation
nobody has to think twice about. Where a page is belongs to the tree, which is
what the owner is looking at when they wonder.

## The tree is eight deep, and Files' is thirty-two

The same shape, different numbers, and the difference is the point.

A file tree mirrors however somebody already filed things on a disk, and
refusing to hold what they already have would make the application useless to
them. A knowledge base is something they are building *in order to find things
in*, and eight levels of it is a base where nothing is findable. The limit is
low enough to be felt, and being felt is what it is for.

**A title is at most 200 characters, one line, and carries no `/` or `\`.**
Nothing server-side parses a title. Two things outside do: `pea knowledge`
addresses a page by a path of titles, and the export writes one file per page
under the path its titles make. A separator in a title would be a page the
console cannot name and an export that lands somewhere nobody asked for.

**Titles among siblings are one each whatever their capitals**, and a taken one
is `conflict` and never a silent rename — as file names are, and for the third
reason as well: two pages with one title in one place is two files with one name
when the export writes them out.

## The tree carries no bodies

`GET /api/knowledge/pages` answers every page's title and place, flat, and not a
word of what any of them says. `PageInTheTree` is the projection that keeps it
that way, and every structural question inside the instance goes through it too
— what is above a page, what is under it, is this title taken.

A knowledge base is navigated far more often than any one page is read. A
listing that carried every page's Markdown would get slower the more the owner
writes, which is the wrong direction for the one thing this application is for.
A history carries no bodies either, for the same reason at a smaller scale:
fifty versions of a mebibyte each is not a read anybody should make to see when
something changed.

**Flat and not nested.** A client draws the hierarchy from the parent of each
page in one pass; a nested document would be one every caller has to walk to
find anything, including the CLI, which wants a path, and the export, which
wants every page once.

## The export is a zip of Markdown files

The epic asked for "an open package that preserves enough hierarchy and
reference information for use outside personalaffe". The test of that phrase is
what somebody can do with it having never heard of this product: **unzip it and
read it.** One large JSON document, a SQL dump, an archive of our own — each
fails that on the first try.

So: one `.md` per page, at `pages/<the titles>/...`, because a directory layout
is the one form a person can see without being told anything, and because every
static site generator already reads it.

**What a directory layout cannot carry is in YAML front matter**: the id, which
is the identity and the thing every link names and which no path can hold; the
parent's id; the real title, before the handful of characters Windows refuses in
a name were replaced; and the timestamps. YAML because every tool that reads
Markdown in bulk already expects it, and because a person looking at the top of
the file can read it.

`knowledge.json` says the same thing in one place for whatever would rather not
open three hundred files to learn the shape of the tree. **It is a convenience
and never the only copy**: deleting it loses nothing that is not in the files.

**The body is not touched.** No reformatting, no re-indenting, no re-wrapping.
An export of something slightly different from what the owner has is not an
export.

It is built in memory, which is a decision: a personal knowledge base is a few
hundred pages that compress to very little, and streaming a zip would buy
nothing and mean an export that can fail halfway with a `200` already sent.

## The editor had a scaffold, and now it has work

`/editor` is gone. PERSONAL-E4 gave the Markdown field one screen because
otherwise it would have sat finished and untried for three epics
([ADR 0004](./0004-one-frame-four-switches-and-a-screen-that-asks-again.md)),
and that screen said in its own text that it would go away when Knowledge
arrived and the editor had real work. This is that, and the browser checks that
drove the scaffold now drive the real thing — which is the only way that
promise could be kept without quietly losing the coverage.

**The refresh holds while there is unsaved work**, and Knowledge is the first
screen that really needed it. What is in the field is the newest version of the
page; an answer landing on top is what would take it away. The browser check
writes a sentence, has something else rewrite the same page over the API, waits
out a refresh interval, and asserts the sentence is still there — which is the
acceptance criterion of PERSONAL-E4 finally being tested against something that
can lose work.

## What PERSONAL-E9 and PERSONAL-E10 will read

- **Search (PERSONAL-E9)** has three of the four applications to index now, and
  a page's body is a column in one table. `PageInTheTree` is what a result list
  wants; the body is what a snippet comes from.
- **Operations (PERSONAL-E10)** gains nothing new to back up: a page is rows,
  and rows are the database. The export is not a backup and must not be
  described as one — it carries no Trash, no revisions and no agent access.
