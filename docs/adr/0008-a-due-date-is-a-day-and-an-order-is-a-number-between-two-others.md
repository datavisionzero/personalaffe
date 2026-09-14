# A due date is a day, and an order is a number between two others

PERSONAL-E8 is the fourth application, and with it **every content area
VISION.md names exists**. Most of what it does is *not* a decision: it opens
every act on `ReachingAnApplication`, it guards every write with the version it
was read at, it draws the five states of `shell/States.tsx`, it contributes to
the Trash, and it writes a description through the Markdown field PERSONAL-E4
built.

What follows is the part that was decided here.

## A due date is a date and never an instant

`due_on` is `2026-09-14`. A `DateOnly` in Domain, a `date` column in Postgres,
a `DateOnly?` in the contract, an `openapi_types.Date` in the Go client, and a
string the browser never turns into a `Date`.

**It is the one value in this product that is deliberately not a moment.** A
task due on the fourteenth is due on the fourteenth wherever the owner is
standing; an instant is a point on a line, and the point that reads as the
fourteenth in Berlin reads as the thirteenth in Los Angeles. The epic asked that
due dates "appear consistently in lists and the dashboard without unintended
timezone shifts", and every timezone bug this application could have had is that
one.

Keeping it true takes the same care at every layer, because each of them offers
a convenient way to lose it:

- **The column** is `date`. `TheTasksHoldTests` reads it back under a session
  fourteen hours ahead of UTC and one eleven behind, and gets the same day.
- **The console** sends the day through as it was typed rather than parsing it
  into a time and formatting it again, and reads the instance's own string back
  when it prints a list.
- **The browser** never calls `new Date("2026-09-14")` — which is midnight UTC,
  and therefore the thirteenth for everybody west of it. "Is this overdue" is a
  comparison of two strings, and today's comes from the browser's own calendar
  (`tasks/today.ts`).

## Completion is a moment, and ticking twice does not move it

`completed_at` rather than a boolean. "Open or completed" is the state
(`CONTEXT.md`); *when* is what a dashboard of "what did I finish this week"
needs, and PERSONAL-E9 is the epic after this one. A boolean would have to be
widened by a migration the first time anybody asked.

**Completing something already completed changes nothing and does not move the
moment.** Otherwise that dashboard would answer with whatever somebody last
touched, which is a worse answer than none.

## One write carries everything a task is

`PUT /api/tasks/{id}` takes the title, the description, the due date, the list,
whether it is done, and where it sits.

A task has more fields than anything else in this workspace and is **exactly
where a second address per field starts to look reasonable**: `POST
…/complete`, `PUT …/due`, `POST …/move`. Each would be another place the guard
has to be got right, and the owner who ticks a box and changes the date has made
one change either way. The Scratchpad established this shape with two fields and
Files and Knowledge kept it; the honest test of a convention is whether it
survives the case that argues against it.

`pea tasks done`, `undone` and `mv` are all that one write with one field
changed, and the screen's checkbox is too.

## An order is a number between its neighbours

A position is a `double` with room on either side of it, and a move is the
midpoint of the two it lands between.

The obvious scheme — positions 1 to n, renumbered on every move — changes every
row below the one that moved. In *this* workspace that is not a performance
question: every other holder of one of those rows is suddenly stale, because
`updated_at` is the version (ADR 0003). The owner's phone, the browser on their
desk and an agent halfway through a write would all be refused over a change
none of them made and none of them can see. **The guard would be doing its job
about nothing.**

So one row changes, and `TheTasksHoldTests` proves it by writing to the two
neighbours afterwards with the versions they were read at.

**The midpoint runs out, and that is handled rather than hoped about.** Halving
a gap fifty-odd times exhausts what a double can tell apart; `Positions.RoomBetween`
is the question the act asks first, and a list with no room left is renumbered.
It takes about fifty moves into the same gap, and `TasksTests` does sixty.
Pretending it cannot happen is how two tasks end up with one position and an
order nobody can explain.

**What travels on the wire is a neighbour and not a number.** `after` is the id
of the task something goes behind, or `null` for the top of its list. The
arithmetic is the module's; what a caller can see and act on is what is next to
what. A caller that is not moving anything sends the neighbour it already has,
which both clients do because they read first.

## A new task goes at the end, and so does a restored one

Capture is what happens when something occurs to somebody, and the order is what
they decide afterwards. A new task jumping the queue would reorder the list every
time they think of something.

A restored task comes back **at the end of its list** rather than where it sat:
that position is a number the list may have reused while it was away, and the
end is the one place that is always free. A restored *list* keeps its tasks'
order, because they went and came back together.

## The one application with no tree

A list is not in another list. VISION §6.4 asks for "multiple named lists" and
rules out project planning; a hierarchy of lists is the first step towards the
thing it rules out.

So `TasksTrash` is the one contributor that does not apply `Restoration` — there
are no ancestors for anything to come back with, and the only question a restore
has is the one every restore has: is the name free where it is going. **It says
so rather than applying a convention for the look of it.** A module that
inherited machinery it has no use for would be a module nobody can read the
intent of.

A task whose list was removed for good while it sat in the Trash comes back in
the first list there is, and the caller is told — the same promise
`RestoredTo.MovedToTheRoot` makes about a folder whose folder is gone.

## What PERSONAL-E9 and PERSONAL-E10 will read

- **The dashboard and search (PERSONAL-E9)** now have all four applications.
  `GET /api/tasks/lists` already carries `open` per list, which is the count a
  tile wants; `completed_at` is what "finished this week" comes from; and
  `due_on` is what "due soon" comes from, as a day, without a timezone anywhere
  in the arithmetic.
- **Operations (PERSONAL-E10)** gains nothing new to back up: a task is rows.

## And that is the four

`Awaited` is gone from `shell/States.tsx`. It was the state that named the epic
filling an application with no screen yet, and there is no longer one — so it
goes, rather than staying for a case that can no longer happen, which is
`docs/codebase.md`'s own rule about an empty folder claiming a future module.

What replaced it is stricter than it was: `screens` in `shell/Shell.tsx` is a
full `Record` over the four applications rather than a `Partial` one, so a fifth
application added without a screen stops compiling instead of falling back to an
apology.
