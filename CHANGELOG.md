# What shipped, and what it asks of whoever runs it

One section per version, written **before** the tag is pushed: the release
workflow reads this file first and refuses a tag it has no section for
([`scripts/the-notes.sh`](scripts/the-notes.sh)). What goes in a section is what
an operator needs — what is new, what changed under them, and what an upgrade
asks of them — and not a list of commits, which the repository already has.

Versions are semantic. `pea` talks to an instance of its own minor version and
older, so a minor is the number that asks somebody to upgrade their CLI, and a
major is the number that asks them to read before upgrading at all.

**A release candidate has no section of its own.** `0.1.0-rc.1` is a candidate
for `0.1.0` and ships that section: notes written twice are notes that disagree,
and the second copy is the one nobody updates.

## 0.1.0

The first release of personalaffe: a private workspace belonging to one person,
with four applications in it, reachable from a browser, from an HTTP API and
from a console.

### What is in it

- **The instance.** One container beside a PostgreSQL and two volumes. It is
  claimed once by its owner, who signs in with an email address, a password and,
  if they want one, a code from an authenticator with recovery codes behind it.
  Agents are let in with named tokens and a permission per application. Nothing
  but five operations answers without a credential.
- **Scratchpad.** Plain text put down on one device and read on another, pinned
  when it is worth keeping, and destroyed by the instance a week after it was
  last changed when it is not.
- **Files.** The owner's storage on this instance's disk, with folders, a
  download that is one link, and an address made from an id — so a link to a
  file survives every rename and every move.
- **Knowledge.** Markdown in a tree, with a history behind every page that only
  ever grows, and an export that is a zip of Markdown files anybody can read.
- **Tasks.** Named lists, a due date that is a day rather than a moment, and an
  order you set.
- **One search** over all four, a **home page** of five tiles you can hide one
  at a time, and a **weather tile** at an address of its own so that a server
  somewhere else can never hold the home page up.
- **`pea`**, the console client: the same API the browser uses, JSON on demand,
  never interactive, and a version handshake that says which side is older.
- **A backup that is one command**, carrying the database and the files as of
  one moment, and a restore that is one script. The whole circle is rehearsed on
  every push.

### What it asks of whoever runs it

- A host with Docker, and a reverse proxy in front of it holding the
  certificate. [`docs/install.md`](docs/install.md) is the whole installation;
  [`docs/operations.md`](docs/operations.md) has the variables, the volumes, the
  backups and the way back from an upgrade.
- `POSTGRES_PASSWORD` in `deploy/.env`, and `PERSONALAFFE_IMAGE` naming this
  release. Everything else has a default that works.
- Set `PERSONALAFFE_TRUSTED_PROXY` **before the instance is claimed**, so that
  the throttle on failed sign-ins counts the caller rather than the proxy.

### Upgrading

There is nothing to upgrade from. From here on, an upgrade is `docker compose
pull` and `docker compose up -d --wait`, with a backup taken first: migrations
apply themselves on start and only ever forward, and the way back is the backup.
