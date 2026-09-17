# The pause makes two stores agree, and the way back is the backup

PERSONAL-E10 adds no feature. What it adds is everything a person needs before
they can put something they would mind losing into this product: an installation
somebody else can follow, a backup that carries both halves of an instance, a
restore that has been done rather than described, an upgrade that has been
walked, a pass over the security surface now that all of it exists, and the
artifacts a release is made of.

Most of it is *not* a decision. The Compose file, the two volumes, the health
checks and the reverse-proxy boundary were settled in PERSONAL-E1 and written
down as they were built. What follows is the part this epic decided.

## One backup, two stores, and the pause that makes them agree

An instance is a database and a directory of bytes. A copy of one without the
other is not a backup of anything: rows naming files that were never copied, or
files no row knows about. **So there is one archive, and it carries both** —
`database.sql`, `files/…` at the addresses the instance uses, and a
`manifest.json` describing the two, **written last**, so that an archive without
one is an interrupted backup and is refused as such.

The two are made to agree by holding the instance still while they are taken.
The pause has two halves, and both are necessary:

- **Outside**, `MaintenanceGuard` refuses unsafe methods with `503
  /problems/paused` and a `Retry-After`. **Reads never stop** — an owner looking
  something up while a backup runs is not a problem anybody has. The two
  anonymous writes, claiming the instance and signing in, are deliberately left
  open: neither touches content or a byte on the volume, and refusing them would
  lock the owner out of their own workspace for the minute a backup takes.
- **Inside**, the backup takes the lock the Trash sweep takes and holds it for
  the whole run. A purge cannot start while a backup is running, and one already
  running finishes before the backup begins. That is asserted by running the
  sweep and reading that it swept nothing, rather than by timing anything.

**The pause ends whether or not anything ends it.** It is `since` and `until` in
one row, and it holds while `now < until`. A backup killed between two steps
leaves a row that lapses by itself; there is nothing to find and edit by hand at
two in the morning. The deadline is a ceiling and not an estimate, which is why
`Retry-After` says five seconds and not the whole of it — measured, the
stillness is under half a second for a workspace with megabytes in it.

**The thing that dumps a PostgreSQL is a PostgreSQL**, and this product must
never learn how. `pg_dump` is in the image, pinned to the major version the
Compose file runs, and both sides are asked for their version **before anything
is held still**: a database this image has grown too old for is one sentence and
no pause.

## A restore is a new instance, so nobody is still signed in

Putting a backup back is not resuming — it is an instance that now holds
somebody else's moment. Two things follow, and both are deliberate:

- **The browsers are signed out.** Sessions do not survive a restore. A cookie
  from before it is a claim about a database that no longer exists, and the
  owner's password admits them again in the same breath. An upgrade is the
  opposite case and leaves sessions exactly where they were: nothing about who
  is signed in changed.
- **The pause the dump carried is let go.** The database was dumped while the
  instance was held still, so the row saying so is *in the dump*. A fresh
  instance restored from it would refuse every write until a deadline set by a
  backup that finished minutes ago. The restore clears it, and there is a test,
  because a rehearsal found this by accident and the next one like it might not.

Everything else comes back as it was, including time that had already passed:
something deleted twenty-nine days before the backup has one day left in the
Trash after it is put back. A deadline that restarted would quietly give the
owner thirty more days of content they thought was gone.

A restore refuses before it changes anything: no manifest, a file the manifest
names and the archive lacks, a length or a checksum that has moved, a schema
this build does not know, or an instance that already has something in it —
`--over-a-populated-instance` is the way past, and it is a mouthful on purpose.

## Migrations only go forward, so the way back is the backup

There is no downgrade and there will not be one. Migrations apply themselves on
start, only ever forward; two containers starting at once take an advisory lock
rather than migrating against each other; and a build put in front of a schema a
newer one wrote refuses to serve and names what it does not know.

**Rolling back is restoring**, with the earlier image named — one command, and
no new verb was needed to find that out. What it costs is the window: everything
written between the backup and the rollback is gone, which is why the procedure
begins with taking a backup and why the rehearsal ends by asking for a page
written after it and getting a `404`. The cost is shown rather than described.

Writing a `pg_dump` into the upgrade instructions would have been the tempting
version of this, and it is wrong: a dump is the database without the files, so
the rollback it buys is an instance whose every download is a `404`.

## What is released, for whom, and what a version means

- **Four `pea` binaries**: `darwin/arm64`, `darwin/amd64`, `linux/amd64`,
  `linux/arm64`. A personal workspace is reached from the machine its owner sits
  at and from whatever a script runs on. There is no Windows binary because
  nobody has asked for one and WSL answers the question meanwhile; it is one
  line in the release workflow the day somebody does.
- **Two image architectures**: `linux/amd64` and `linux/arm64`. An instance runs
  on a small server somebody rents or on a box at home, and those two are what
  such machines are.
- **The tag is the version, and the only source of one.** Both sides default to
  `0.0.0-dev`, which says plainly that nobody released them. The release build
  passes the tag into the image and compiles it into the binary, and the
  workflow runs the binary it just built and refuses to publish one that says
  anything else.
- **`pea` talks to an instance of its own minor version and older**, and stops
  with exit 9 saying which side moves when it will not. A development build on
  either side is not checked: it has no number to compare.
- **A tag with a hyphen in it is a pre-release** — published, marked as one, and
  `latest` does not move. That is what makes the release workflow rehearsable
  without telling anybody they have a new version.
- **The notes are written before the tag.** The workflow reads `CHANGELOG.md`
  before it builds anything and refuses a tag it has no section for, because a
  release whose notes are written afterwards is a release nobody had to think
  about before cutting it.

## What CI rehearses, and what stays an operator's

CI runs the two rehearsals **on every push**, because they are the things nobody
wants to perform for the first time in anger: a life put into an instance,
backed up, both volumes destroyed, put back, and read out again through the API;
and a life put into an earlier build, upgraded while a second container starts
beside it, then the whole way back from an upgrade that went wrong. They are the
same scripts an operator runs, which is what keeps the rehearsal and the
documented procedure one thing rather than two that drift.

What CI deliberately does not do is install anything. The first installation,
the certificate, the proxy, the claim and the decision to cut a tag are an
operator's, and they are walked by hand and written down on the ticket that
walked them. The gate holds no credential at all; the release workflow holds the
one the run is handed, and nothing else in this repository writes anywhere
outside it.

## What is not here

- **No backup schedule, and no script wrapping the backup verb.** The verb is
  one command; when to run it, where to put it and how long to keep it are an
  operator's, and a wrapper around one command is a second place to be wrong.
  (`scripts/restore.sh` exists because a restore genuinely is several steps.)
- **No offsite anything.** The archive is written to standard output. What
  carries it away is the operator's own tooling.
- **No downgrade**, as above, and no migration that runs backwards.
- **No incremental or continuous backup.** One archive, one moment.
- **No auto-update.** An instance upgrades when somebody decides it does.
