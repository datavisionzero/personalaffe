# One frame, four switches, and a screen that asks again

PERSONAL-E4 lands before there is any content either, and what it decides is
the shape every screen of the four applications is drawn inside. Four
decisions, and the reasons they were made that way rather than the other way.

**An application is switched off by a row in a table, and switched off means
absent.** The alternative was a column per application on one settings row, and
it loses on the shape the switch is actually used in: every read is about one
application, and the one write changes one of them. The four rows are seeded by
the migration that creates the table, so a read is a read and never a read that
might have to insert, and no code has to decide what a missing row would have
meant.

What being switched off *does* is in one place — `ReachingAnApplication` — and
every operation inside an application goes through it, so the answer cannot
differ between two of them. It asks two questions in a deliberate order: access
first, then the switch. A caller who may not reach an application is refused
`forbidden` whether it is on or off, so a refusal says nothing about how the
owner has configured their workspace. A caller who may reach a switched-off one
is refused `disabled`, a code of its own rather than `not-found`, because the
content is there and the owner can have it back by switching the application on.

In the browser, switched off means the row is *absent* from the navigation and
from the palette rather than greyed out: a row that cannot be pressed is a
promise the application does not keep. The home page is the exception and shows
all four including the ones that are off, because "where did my Tasks go" is a
question the navigation cannot answer by leaving them out.

And the switch reaches nothing that runs on a deadline. `ITrash.PurgeAsync`
takes a deadline and a cancellation token and nothing else — `TheSafeguardsHold`
asserts that signature by reflection — so retention runs whether an application
is on or off. A deadline that stopped while an application was off would be a
way to keep expired content for ever, and content that vanished on switching one
back on would be the same surprise from the other side.

**The screen asks again on a timer, and the timer is the whole mechanism.** The
workspace has more than one writer: the owner has a phone and a desk, and agents
write over the API at any time. A screen that only ever showed what was true
when it was opened would be wrong most of the time it is looked at.

Server-sent events were the alternative, and what they would have cost is a
long-lived connection to keep alive through somebody's reverse proxy, a
reconnect and backoff policy, and an in-process change signal the foundation
deliberately does not have — no `LISTEN`/`NOTIFY`, by
[ADR 0001](./0001-adopt-the-existing-affe-stack-and-components.md)'s list of
what is not taken. What they would have bought is the difference between
"instantly" and "within fifteen seconds" for one person's workspace. One request
every fifteen seconds is cheaper than the machinery that avoids it.

Three rules make polling bearable, and they are what `shared/ask.ts` is:

- **A refresh is quiet.** What is on the screen stays on it while the next
  answer is on its way, and a refresh that fails leaves the last answer standing
  and says "not answering" in the header. Only a change of address or asking on
  purpose puts a screen back to "asking": a spinner every fifteen seconds is how
  a refreshing screen becomes an unreadable one.
- **A hidden tab asks nothing**, and asks once the moment it is looked at. A
  workspace left open on a second monitor for a day costs nothing.
- **`hold` stops it dead.** What somebody is typing is the newest version of it.
  A screen with unsaved work asks nothing, so nothing arrives underneath it.

That last one is the criterion the whole mechanism is dangerous for: PERSONAL-E4
asks both that changes made elsewhere appear without a reload *and* that unsaved
edits survive, and the cheap way to show the new version is over whatever the
person at the keyboard had written.

**The frame asks what this workspace has, once.** `useApplications` reads
`/api/applications` in the shell and hands the answer to the sidebar, the
palette and every route. Three of them asking on their own would be three
requests and three moments at which they could disagree about what this
workspace is. The permission the frame reads is the one beside the switch rather
than the one in the credential: they are the same answer, and this is the one
that is asked again while the screen is open.

**The browser checks are a job of their own, because jsdom lays nothing out.**
Everything under `src/web/src` runs in jsdom, and it cannot say whether the
sidebar is a drawer at a phone's width, whether CodeMirror — which measures a
selection it draws — works at all, or whether a screen brings a change made
elsewhere onto itself while nobody touches it. `docs/codebase.md` said in
PERSONAL-E1 that a subject none of the six jobs covers is what would justify a
seventh; this is it, and it is the only place in the repository where the
refresh is proved against a real instance.

Chromium and nothing else: what these check is layout, the keyboard and a real
editor, none of which is engine trivia, and a second engine would double the job
for a product one person opens in one browser.

## What follows from this

`/editor` is a scaffold and says so on the screen. The Markdown field is
finished here and has no application to live in until PERSONAL-E5, which would
otherwise leave it untried by anybody and unreachable by a browser check — a
component nobody has run is indistinguishable from one that does not work. It
writes nowhere, reads nothing, and goes when Knowledge arrives.

The seam a link inside a body plugs into is `shared/links.ts`. Today nothing
leads inside this workspace, so every link in a body is somebody else's and is
treated as one: `http`, `https`, `mailto`, and anything else stays text.
PERSONAL-E6 and PERSONAL-E7 give a file and a page an address, and that is where
they arrive.
