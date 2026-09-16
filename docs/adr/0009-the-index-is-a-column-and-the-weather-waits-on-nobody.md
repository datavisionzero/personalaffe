# The index is a column, and the weather waits on nobody

PERSONAL-E9 adds nothing to the four applications. What it adds is the two
things a workspace has that no application does — one search over all of them
and a home page that says what is pending — and a tile that comes from outside
the instance altogether.

Most of it is *not* a decision. The endpoints go behind the same door, the
dashboard's tiles are guarded writes with the version they were read at, the
screens draw the five states of `shell/States.tsx`, and the aggregate views
filter by permission and by the switch exactly as `GET /api/trash` already did.

What follows is the part that was decided here.

## Every word is a prefix, and there is no query language

`Needle` takes what somebody typed and breaks it into letters and digits:
`arch dec` is two words, both matched as beginnings, both required. `Budget-2026.final.pdf`
is four.

**A person searching their own workspace is remembering, not querying.** They
type the start of a word they half know and expect the thing to appear while
they are still typing. Requiring whole words would mean nothing appears until
the last letter; matching any word rather than all of them would mean a second
word makes the answer worse.

So there is no `AND`, no quoted phrase, no `-word`. A query language is a thing
to learn about a workspace one person is looking through, and its absence is
what lets the parsing be total: **nothing a caller types survives as syntax.**
A stray `&`, `:`, `*` or `'` is a word boundary and nothing else, which is what
makes the one hand-written SQL statement in this product safe to have written.

Single letters are dropped and at most eight words are used. Both are for the
same reason: a prefix of one letter is a prefix of most of a workspace.

## `simple`, not `english`

The vector is built with the `simple` text-search configuration, which
lowercases and does nothing else — no stemming, no stop words.

**This workspace is one person's, and one person writes in more than one
language.** `english` would stem the English notes and leave the German ones as
unstemmed noise, and would throw `the`, `a` and `is` away from titles that are
about those words. `simple` treats every language equally badly, which is the
only kind of fairness available without asking the owner to pick one — and it is
what makes "every word is a prefix" mean the same thing in every language.

A file name gets one more step: everything that is not a letter or a digit
becomes a space before it is tokenised, so `Budget-2026.final.pdf` is four
tokens rather than one that no prefix will ever reach. A needle is taken apart
the same way, which is what makes the two meet.

## The index is a stored generated column, so nothing maintains it

`search_vector` is `GENERATED ALWAYS AS (…) STORED` on `pages`, `tasks`,
`scratchpad_entries` and `files`, with a GIN index over it.

**There is no indexing step in this product, and there is nothing to keep in
sync.** Postgres computes the column from the row on every insert and update.
No trigger, no second write from the application, no queue, no reindex command
in `docs/operations.md`, and no way for the index to disagree with the content —
including for a row an agent wrote over the API, a row a restore brought back
out of the Trash, and a row a `pg_restore` put there without this application
ever running. The epic's suite asserts the third of those: restoring a page puts
it back into the search, and nothing re-indexes it.

The alternative — a separate search index, a table of terms, an external
service — is the "separate search cluster" VISION.md rules out, and it would be
one more thing a backup has to be consistent with.

What a thing is *called* is weighted `A` and what it *says* is weighted `B`.
That is the whole of the ranking: `ts_rank` reads the weights, so a page called
"Architecture" outranks a page that mentions architecture once, and nothing
downstream reorders anything.

## Hand-written SQL says `deleted_at is null` four times, out loud

The four statements in `Persistence/Search.cs` are the only SQL in this product
that EF did not write, which means they are the only reads that do **not** get
the query filter `RecoverableContent` applies to everything else.

That is a real risk and it is met head-on rather than hidden: each statement
carries the clause itself, the file says why in its own remarks, and the suite's
first assertion is that deleting a page takes it out of the search in the same
moment it takes it out of everything else. The Scratchpad's statement has no
such clause and must not grow one — its entries are destroyed rather than set
aside, and the column does not exist.

The dashboard is deliberately the other way round: ordinary LINQ over the
context, so the Trash is absent without `Persistence/Dashboard.cs` saying so.
Two files, two arrangements, and the reason each is what it is written where
somebody changing it will read it.

## A snippet is text, and the marking up happens in the browser

`ts_headline` is asked for empty start and stop selectors, so what leaves the
instance is a piece of the body with nothing around it.

**Nothing between here and a browser has to decide whether a snippet is safe to
render.** The application marks the words it asked with, over the words it
worked out itself, so what came back over the wire is only ever text. A client
that wants it marked another way has the words; a client that wants none has to
do nothing.

## The weather has its own address, and that is structural

`GET /api/dashboard` is one request for the whole home page — four applications,
one answer, one moment they agree about. The weather is **not in it**.

PERSONAL-E9 asks that the weather "does not block the workspace if
unavailable". An intention is not a guarantee: a tile inside the dashboard's
answer would mean the owner's home page cannot finish until a server on the
other side of the internet has answered or timed out, however carefully the
timeout is chosen. Two addresses makes it impossible rather than unlikely, and
the suite asserts it with a provider that never answers and a home page that
does not wait for it.

The clients keep it that way on purpose. The browser's weather tile makes its
own request on its own slower timer; `pea dashboard` prints a line saying the
weather is `pea weather` rather than helpfully fetching it.

## Open-Meteo, because it needs nothing

VISION.md makes the weather conditional: it is in the MVP "provided it does not
require a disproportionate external service". Open-Meteo needs no account, no
API key, no contract, no secret in an operator's environment, and its free tier
is meant for exactly this — one person's instance asking about one point four
times an hour. What it asks in return is attribution, which travels in the
answer and is shown beside the number in both clients.

So the condition is met and the scope decision does not go back to the owner.

Three things follow from it being an outside service:

- **It answers with nothing rather than throwing.** A refused connection, a 500,
  a timeout, a body that is not what was expected — all of them are "no
  reading", logged at information and never at warning. Somebody else's bad
  afternoon is not this instance's health, and an operator reading a log during
  a real outage should not have to rule it out first.
- **It can be switched off entirely.** `PERSONALAFFE_WEATHER=off`, and nothing
  in this product opens a socket to anywhere. An instance on a machine that is
  supposed to talk to nobody but its owner is a reasonable thing to run, and a
  tile is not a reason to take that away.
- **What is sent is a point.** Two coordinates rounded to four decimal places,
  and nothing about who is asking. The owner's name for the place never leaves
  the instance, and the geocoder is asked once, in Settings, rather than by a
  tile.

Coordinates and not a postcode or a provider's city id, so that the one
decision this epic made about a provider is not also a decision about what is
stored.

## Five tiles, in a fixed order, that an application's switch can take away

`DashboardTile` is an enum, not a table somebody adds rows to. VISION §6.1 asks
for a predefined dashboard with individually showable tiles and says free
arrangement comes later; a sixth tile is a decision, a case in the enum and a
migration, which is the right amount of friction.

**`shown` and `offered` are two answers, not one.** Hiding is the owner's
preference; being offered is whether the application behind it is switched on
and this caller can read it. Folding them together would mean switching Tasks
off and on again silently resets the tile — and the owner would have no way to
tell "I hid that" from "that is not available". A section of the answer is
`null` where its tile is not drawn and `[]` where it is drawn and empty, for the
same reason.

## What is not here

- **No free-form dashboard, no widget store, no custom data.** Five tiles.
- **No file contents in the index.** VISION §6.1 draws that line and this keeps
  it: a file contributes its name. It is the difference between one search over
  a workspace and a document search over a disk.
- **No cursor on the search.** A limit with `has_more`, like the Trash and the
  Scratchpad. A personal workspace that needs a second page of findings needs a
  better word, and the answer says so.
- **No search over the Trash.** What was deleted has left ordinary reads, and a
  search is an ordinary read. The Trash is its own list and already has one.
- **No weather history and no forecast beyond today.** A reading is asked for,
  held while it is fresh, and thrown away. This instance is not a weather
  archive and there is no table for one.
