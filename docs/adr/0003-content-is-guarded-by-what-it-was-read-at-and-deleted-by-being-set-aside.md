# Content is guarded by what it was read at, and deleted by being set aside

PERSONAL-E3 lands before there is any content, and what it decides is what the
four applications inherit. Four decisions, and the reasons they were made that
way rather than the other way.

**A write says which version it replaces, in `If-Match`, and the version is the
object's `updated_at`.** Two people change one thing — the owner in a browser
while an agent works over the API — and the second write is refused rather than
silently winning. The value is `updated_at` because every object carries it
already and every client already reads it; a version counter beside it would be
a second thing that has to agree with the first. It travels in a header and not
in the body because PERSONAL-E6's uploads are writes whose body is the file, and
a guard that only half the writes in the product can use is not a guard.

A write that carries no `If-Match` is refused exactly like one carrying an old
one, and so are `*`, a weak tag and anything unparseable: one code, because the
caller's move is the same in every case. An endpoint where forgetting the guard
were cheaper than using it is an endpoint where it will be forgotten.

The early check catches everything except the case the guard exists for — two
writes that both read the same version and both pass it. `GuardedSave` closes
that with the database: a lasting object declares `UpdatedAt` a concurrency
token, EF writes `where … and updated_at = …`, and nothing changed is `stale`.
Two lines of configuration and one call are the whole of what a module inherits.

**`deleted_at` lives in each module's own table, and the Trash is a surface over
per-application contributors.** A central index was the alternative and loses
twice: it is the generic content entity `docs/codebase.md` rules out, and it is
a second place that has to agree with the first. An `ITrash` contributor is the
whole of what a content module adds to appear in the Trash, and an application
whose module does not exist registers nothing — which is why an instance today
answers an empty Trash rather than an error.

Exclusion is an EF query filter and not a convention somebody remembers: the
twelfth query written without thinking about deletion still cannot see a deleted
row, and the one read that wants them says `IgnoreQueryFilters` out loud. Who
deleted something is a copy of the caller and not a reference to it, because
agent access is revocable and the question the owner asks of that list is which
of their agents did this.

**Removing something for good is the owner's alone**, singly and in bulk. An
agent that could permanently remove one entry could bypass the Trash in two
steps instead of one, and the Trash exists so that an agent acting on the
owner's behalf cannot destroy the owner's content. `pea` therefore has no verb
that destroys anything — the same consequence, and for the same reason, as its
having no verb that issues a credential ([ADR 0002](./0002-one-owner-with-a-browser-and-agents-with-tokens.md)).

**Retention is thirty days, an operator's variable, and the sweep runs in
process.** `PERSONALAFFE_TRASH_RETENTION` is a whole number of days; there is no
setting in the workspace, because an owner who wants something gone sooner
removes it from the Trash, which is a verb they already have. The interval is a
constant for the mirror-image reason: nobody has a cause to tune how often
something looks.

The sweep works from the deadline and not from what it last did, so a week of
downtime costs nothing and running it twice removes nothing the second time. Two
containers over one database do the work once, through the advisory lock the
migrator already uses — a try and not a wait, because queueing to redo work
somebody has just done is a connection held open for nothing. One broken
contributor is recorded and the sweep carries on: a file whose bytes are already
gone must not be why the other three applications are never swept again.

**The purge asks nobody anything, and has no argument it could be told to skip
an application with.** PERSONAL-E4 adds the switch that hides an application;
disabling one must not suspend a retention deadline, and re-enabling one must
not resurrect what expired while it was off. The port has no room for an
enablement flag and a test says so by reflection, so that adding one is a red
build rather than a decision nobody notices.

## The two rules the hierarchies inherit

Restoring into a tree is settled once for Files and Knowledge. An ancestor in
the Trash **comes back with what needs it** — refusing until the folder above
has been restored first makes the owner walk the tree by hand, and doing neither
leaves content somewhere they cannot reach — and it comes back as itself rather
than with everything it used to contain. A name already taken is **`conflict`
and not a silent rename**, with `name` on the same call to put it back under
another one, because a product that quietly appends "(2)" has made a decision
the owner would have made differently.

One deletion is one Trash entry: a folder takes what is in it under one moment
and the whole thing returns together, while something deleted separately below
it is an entry of its own with an expiry of its own. That is what lets a chain
break at all — and a thing whose folder is gone for good is restored to the
root, with the answer saying so. Nothing in this product moves the owner's
content without saying that it did.

## History

Fifty previous versions of one thing, **by count and not by age**: a page edited
twice a year deserves its history as much as one edited twice a day, and "ninety
days" would quietly discard the whole history of everything the owner works on
slowly. Recovering an old version **writes forward** — it leaves a revision of
what it replaced, so undo is not the one act in this product that destroys work
— and it is guarded by the object's version rather than the revision's, because
a revision never changes and what must not be lost is the edit somebody made
five minutes ago.

Revisions belong to the thing they are of: into the Trash with it, back with it,
gone with it. A revision that outlived its page would be content the owner
believes they deleted.

## What was not done

No universal workflow or entity framework, which the epic rules out in so many
words. No base class every module inherits: `IRecoverable` is two properties and
`IRevision` is two, and each module owns its table, its columns and its queries.
No cursor over the Trash — a limit and `has_more` bound the answer, and four
cursors with a tie-break rule would be a lot of machinery for a list that fits
on a screen. And no screens: the shell is PERSONAL-E4's, and what this epic owes
it is a client that already carries the version and already tells the two 404s
apart.
