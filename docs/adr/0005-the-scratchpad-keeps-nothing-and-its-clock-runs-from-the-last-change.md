# The Scratchpad keeps nothing, and its clock runs from the last change

PERSONAL-E5 is the first content in this workspace, and the first application to
inherit what PERSONAL-E2, PERSONAL-E3 and PERSONAL-E4 built. Most of what it
does is therefore *not* a decision: it opens every act on
`ReachingAnApplication`, it guards every write with the version it was read at,
it draws the five states of `shell/States.tsx`, and it is reached the same way
in a browser and from `pea`. What follows is the part that was decided here,
and the reasons it was decided that way rather than the other way.

## The one application that inherits the guard and not the Trash

A Scratchpad entry is **destroyed** when it is deleted. It does not go into the
Trash, its table carries no `deleted_at`, the module registers no `ITrash`, and
`deleted` is a refusal code this application never answers — a second delete is
`not-found`, like a delete of something that never existed.

That was already the shape `docs/api.md` described before there was a Scratchpad
to apply it to, and building it made the reason concrete rather than changing
it: a Trash holding one person's thirty days of pasted wifi passwords, SSH
fingerprints and half-typed messages is not a service to anybody, and an owner
who deletes one of those means it.

**The absence is asserted rather than remembered.** A test says the type does
not implement `IRecoverable`, another says the store is not an `ITrash`, another
says the table has exactly five columns, and another deletes five entries and
then reads an empty Trash. The day somebody wires this application into the
Trash by habit — which is the habit every other module will teach — a deletion
the owner was told is final quietly stops being final, and no diff makes that
obvious. Four red tests do.

The consequence for `pea` is the one thing on that page that had to be
rewritten: **"there is no verb that destroys anything" was true and is not.**
`pea scratchpad rm` destroys something, and it is allowed to because
`read_write` has included deletion since PERSONAL-E2 — permanent in the
Scratchpad, into the Trash for lasting content. The sentence that stays true is
the one that matters: `pea` cannot empty the Trash, remove lasting content for
good, issue a credential, or switch an application.

## The clock runs from `updated_at`, and pinning stops it

`expires_at` is `updated_at + retention`, and it is `null` while an entry is
pinned. The alternative was `created_at + retention`, and it loses twice:

- An entry somebody edited this morning would disappear tonight because it was
  pasted a week ago. The thing they just touched is the thing they are using.
- **An entry unpinned after a year would be destroyed by the next sweep.** That
  is the epic's open question — "decide expiry behavior after unpinning" — and
  counting from the last change answers it without a rule of its own: unpinning
  *is* a change, so it starts a full period from that moment. There is no second
  concept, no "unpinned_at", and nothing to keep in step.

Pinning is the only way to keep an entry, and there is no per-entry expiry, no
"remind me", and no notice before something goes. The list shows `expires_at`
for every entry that has one, and that is the whole of the warning.

## Two periods, and both are the operator's

`PERSONALAFFE_SCRATCHPAD_RETENTION`, seven days by default, beside
`PERSONALAFFE_TRASH_RETENTION`'s thirty. They answer different questions — how
long a mistake can be undone, and how long a note pasted between two devices is
worth keeping — so one number would have to be wrong for one of them.

Both are variables rather than switches in the workspace, for the reason
[ADR 0003](./0003-content-is-guarded-by-what-it-was-read-at-and-deleted-by-being-set-aside.md)
already gives about the Trash's: an owner who wants something gone sooner
deletes it and one who wants something kept pins it, and both are verbs they
already have. A screen for a number almost nobody changes is one more thing to
get wrong.

The sweep runs in `RetentionService`, beside the Trash's, on the same clock and
the same hourly tick — and **it takes a deadline and nothing else**, as
`ITrash.PurgeAsync` does, so that switching the Scratchpad off cannot suspend
it and there is no parameter for the switch to arrive through.
`TheSafeguardsHoldTests` asserts that signature by reflection.

It takes **no advisory lock**, where the Trash's purge does. That is the one
place the two deliberately differ: the purge fans out over four modules and
removes file bytes beside rows, which two instances must not do at once; this is
one statement against one table, and a second instance running it at the same
moment removes nothing and says so.

## One `PUT` carries the text and the pin

There is no `POST /entries/{id}/pin`. A pin is a change to the entry, not an
event of its own, and a second address would be a second place the guard has to
be got right — and a second write for a client to hold a version for.

What that costs is that a client changing only the pin has to send the text back
with it, which is why `pea scratchpad pin` reads the entry first and why the
browser sends `entry.text` unchanged. What it buys is that every write to an
entry goes through one handler, one act, and one guard.

A write that asks for what is already stored changes nothing and does not move
`updated_at`. The guard is still checked: a write that happened to agree with
what is there is still a write somebody made from a stale screen.

## Plain text, so the screen has no editor

VISION §6.2 says plain text, and the screen takes that literally: the capture
box is a `<textarea>`, not `MarkdownField`. A browser opening `/scratchpad`
downloads neither the Markdown pipeline nor CodeMirror behind it, and a browser
check asserts that against what the page actually requested rather than against
somebody's reading of the imports. That is what PERSONAL-E4's lazy chunks were
for, and this is the first screen that proves they work.

It also corrects something PERSONAL-E4 wrote down: `/editor` is scaffolding
"until PERSONAL-E5 gives the Markdown field real work". It does not. Nothing in
the Scratchpad edits Markdown, so the field's only screen is scaffolding until
Knowledge in PERSONAL-E7.

## Deleting asks first, and it is the only thing in this workspace that does

Every other deletion in personalaffe can be taken back, so none of them asks.
This one cannot, so it does — and what the dialog says is not "are you sure" but
what is about to be true: the entry is destroyed, it does not go to the Trash,
and nobody can bring it back, including whoever runs the server.

`pea scratchpad rm` does *not* ask, because nothing in `pea` ever prompts, and
it has no `--force` either: a flag that made this one verb feel dangerous would
make every other verb feel safe. Its help says plainly that it is permanent.

There is no "empty the Scratchpad", in either client. A loop over `list` is a
script anybody can write; a single verb that destroys everything is one typo
away from being the thing this product is sorry about.

## Copying is the point, and it can fail

The Scratchpad exists so that something typed on one device is on the clipboard
of another. `navigator.clipboard` is unavailable outside a secure context, and
`docs/operations.md` says instances reached over plain HTTP at a LAN address
happen — so the copy control falls back to selecting the entry's text and saying
why, rather than being a button that quietly does nothing.

## What PERSONAL-E9 will read, and what is built for it

Nothing. The dashboard, the search and the counts are PERSONAL-E9's, and the
seam they will plug into is the one every application already has: an act that
opens on `ReachingAnApplication`, which is what filters an aggregate view by
permission and by the switch. `ReadTheEntries` is the read a tile or a search
contribution will be written beside — not extended, and not anticipated with a
parameter nothing passes today.
