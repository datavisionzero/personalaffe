#!/usr/bin/env bash
# Put a backup back, in the order that is safe to do it in.
#
#   scripts/restore.sh personalaffe-20260916-143237.tar
#   scripts/restore.sh backup.tar --over-a-populated-instance
#
# Three steps and a stop between them, which is the whole reason this is a
# script rather than one more line in a document: the instance must not be
# serving while its database is replaced underneath it. What does the work is
# `personalaffe restore`, in a one-off container beside the stopped one, with
# the same volumes and the same connection string — and every refusal is its,
# not this script's (docs/operations.md).
#
# It is safe to run and find out. The verb changes nothing at all unless the
# archive is complete, every checksum matches, its schema is one this build
# knows, and the instance is empty — or you have said the second flag, which
# means: replace what is here.
#
# Needs: docker compose, and the `.env` an installation already has beside its
# Compose file.
#
# **It runs from a checkout and from an installation that is not one.** Whoever
# installed personalaffe the documented way has a directory with
# `docker-compose.yml` and `.env` in it and no repository anywhere
# (docs/install.md), and this is attached to every release so that they have it
# when they need it — which is the day something is wrong, and not before.
# `PERSONALAFFE_COMPOSE` names the Compose file where neither layout is what you
# have.

set -euo pipefail

archive="${1:-}"
anyway="${2:-}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ -n "${PERSONALAFFE_COMPOSE:-}" ]; then
  file="$PERSONALAFFE_COMPOSE"
elif [ -f "$root/deploy/docker-compose.yml" ]; then
  file="$root/deploy/docker-compose.yml"
else
  file="./docker-compose.yml"
fi

if [ ! -f "$file" ]; then
  printf 'restore: no Compose file at %s. Run this beside the one your instance uses,
' "$file" >&2
  printf '         or name it: PERSONALAFFE_COMPOSE=/path/to/docker-compose.yml
' >&2
  exit 2
fi

compose=(docker compose -f "$file")

if [ -z "$archive" ] || [ ! -f "$archive" ]; then
  cat >&2 <<USAGE
Usage: scripts/restore.sh ARCHIVE [--over-a-populated-instance]

ARCHIVE is what \`personalaffe backup --to -\` wrote. The second flag is what an
operator says when the instance already has something in it and they mean to
replace all of it.
USAGE
  exit 2
fi

if [ -n "$anyway" ] && [ "$anyway" != "--over-a-populated-instance" ]; then
  printf 'restore: `%s` is not a flag this takes.\n' "$anyway" >&2
  exit 2
fi

printf '1/3  Stopping the instance. The database stays up; it is about to be written to.\n'
"${compose[@]}" stop personalaffe

# `run --rm` and not `exec`: the serving container is stopped, and this is a
# second one from the same service definition — the same image, the same
# environment, the same volumes. `-T` because the archive arrives on its
# standard input.
printf '2/3  Putting it back.\n'
restored=0
"${compose[@]}" run --rm -T personalaffe restore --from - ${anyway:+"$anyway"} < "$archive" || restored=$?

printf '3/3  Starting the instance again.\n'
"${compose[@]}" up -d --wait personalaffe

if [ "$restored" -ne 0 ]; then
  printf '\nrestore: nothing was put back. The instance is running and is as it was.\n' >&2
  exit "$restored"
fi

printf '\nThe instance is serving what that backup says it is. Sign in again — a restore\n'
printf 'signs every browser out — and check something you would notice the loss of.\n'
