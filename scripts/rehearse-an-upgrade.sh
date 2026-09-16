#!/usr/bin/env bash
# Put a life into an earlier build, upgrade it, and then go back the only way
# there is.
#
#   scripts/rehearse-an-upgrade.sh
#
# An upgrade is the one operation an owner performs on an instance that already
# has everything in it, and **a way back that has only ever been written down is
# a way back nobody has walked.** So this walks it, against the images an
# operator installs, and CI runs it on every push.
#
# It is in two acts, and they start from two different earlier builds, which
# needs a word. There is no released personalaffe yet, so "the earlier build" is
# a commit out of this repository's own history — and one commit cannot play
# both parts, because the two acts want opposite things from it:
#
#   * The upgrade wants a build whose **schema is behind this one**, so that the
#     migration that runs is a real migration over a database with real content
#     in it, and so that the refusal it earns afterwards — that build, put back
#     in front of the schema this one wrote — is real rather than staged. That
#     build is worked out rather than named: the commit that added the newest
#     migration, minus one. It is exactly one migration set behind, whatever the
#     history looks like by then.
#
#   * The way back wants a build that **can take a backup and put one back**,
#     because the first step of the documented upgrade is the backup. The verbs
#     arrived in PERSONAL-63 and PERSONAL-64, well after the build above, so
#     this act starts at the newest release — or, until there is one, at the
#     commit that brought `scripts/restore.sh`.
#
# Saying that out loud is cheaper than one rehearsal that quietly proves half of
# what it claims. Either can be named instead:
#
#   PERSONALAFFE_ONE_BEHIND     a git ref whose schema is behind this one
#   PERSONALAFFE_KNOWS_A_BACKUP a git ref that has `backup` and `restore`
#
# Each is built once into an image tagged with its commit, so a second run
# reuses it and cannot reuse a stale one. It needs the repository's history, so
# a shallow clone is not enough.
#
# It destroys whatever is in deploy/docker-compose.yml's volumes, twice. Do not
# run it against an installation you care about.
#
# Needs: docker compose, git, curl, python3, shasum or sha256sum.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose=(docker compose -f "$root/deploy/docker-compose.yml")
instance="${PERSONALAFFE_URL:-http://127.0.0.1:8080}"
upgraded="${PERSONALAFFE_IMAGE:-personalaffe:local}"

migrations="$root/src/Personalaffe.Infrastructure/Persistence/Migrations"

# The build one schema behind: the commit that added the newest migration, minus
# one. Naming a branch instead would stop working the day this work is on it.
added_the_newest_migration() {
  local newest
  # A migration is the file whose name starts with the moment it was
  # scaffolded at; the model snapshot beside them is neither, and it sorts last.
  newest="$(git -C "$root" ls-files "$migrations" \
    | grep -E '/[0-9]{14}_[^/]+\.cs$' | grep -v '\.Designer\.cs$' | sort | tail -1)"
  test -n "$newest" || return 1
  git -C "$root" log --diff-filter=A -1 --format=%H -- "$newest"
}

one_behind="${PERSONALAFFE_ONE_BEHIND:-$(added_the_newest_migration)^}"

# And the build the way back returns to: the newest release once there is one,
# and until then the commit that taught this product to put a backup back.
knows_a_backup="${PERSONALAFFE_KNOWS_A_BACKUP:-$(git -C "$root" describe --tags --abbrev=0 2>/dev/null \
  || git -C "$root" log --diff-filter=A -1 --format=%H -- "$root/scripts/restore.sh")}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

password='correct horse battery staple'

# shellcheck source=scripts/an-instance.sh
. "$root/scripts/an-instance.sh"

# The image of an earlier build, out of this repository's history, in a worktree
# of its own. The tag carries the commit rather than the ref, so that a second
# run reuses the image it built and a moved branch builds a new one.
built=""
an_image_of() {
  local ref="$1" commit tree
  commit="$(git -C "$root" rev-parse --short "$ref")" \
    || fail "$ref is not a commit this repository has. A shallow clone is the usual reason."
  built="personalaffe:before-$commit"

  ok "$(git -C "$root" log -1 --format='%h %s' "$commit")"

  if docker image inspect "$built" >/dev/null 2>&1; then
    ok "$built, built earlier"
    return
  fi

  tree="$work/$commit"
  git -C "$root" worktree add --detach "$tree" "$commit" >/dev/null 2>&1 \
    || fail "could not check $commit out beside this one"

  ok "building $built — a few minutes, once"
  docker build -f "$tree/deploy/Dockerfile" -t "$built" \
    --build-arg "REVISION=$commit" "$tree" >"$work/build-$commit.log" 2>&1 \
    || { tail -25 "$work/build-$commit.log" >&2; fail "the image of $ref did not build"; }

  git -C "$root" worktree remove --force "$tree" >/dev/null 2>&1 || true
  ok "$built"
}

# What a container said it did to the schema when it started: the count and the
# names, out of its own log. A recreated container has a log of its own, so
# there is nothing older in here to filter out.
what_it_migrated() {
  "${compose[@]}" -f "$work/both-at-once.yml" logs --no-color "$1" 2>/dev/null \
    | sed -n 's/.*Applying \([0-9][0-9]*\) migration(s): \(.*\)/\1 migration(s): \2/p' | tail -1
}

counted() { grep -c "$1" "$2" 2>/dev/null || true; }

the_image_serving_now() {
  docker inspect --format '{{.Config.Image}}' "$("${compose[@]}" ps -q personalaffe)"
}

# A second application container from the same image, beside the one Compose
# already runs: no published port, because there is one port and the instance
# has it, and no healthcheck, because what is interesting about this container
# is over before it would answer one. It exists for the length of one `up`.
cat > "$work/both-at-once.yml" <<'YAML'
services:
  personalaffe-again:
    image: ${PERSONALAFFE_IMAGE:-personalaffe:local}
    depends_on:
      db:
        condition: service_healthy
    environment:
      ConnectionStrings__Postgres: >-
        Host=db;Port=5432;Database=personalaffe;Username=personalaffe;Password=${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in deploy/.env}
      PERSONALAFFE_STORAGE_ROOT: /var/lib/personalaffe/files
    volumes:
      - personalaffe-files:/var/lib/personalaffe/files
YAML

printf 'Rehearsing an upgrade against %s\n' "$instance"
printf 'Upgrading to %s.\n' "$upgraded"

# ============================================================ act one: up ====
printf '\n--- The upgrade -------------------------------------------------\n'

say "The build this upgrade starts from"
an_image_of "$one_behind"
earlier="$built"

say "An instance of it, with a life in it"
export PERSONALAFFE_IMAGE="$earlier"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
"${compose[@]}" up -d --wait >/dev/null 2>&1
wait_for_it
before="$(what_it_migrated personalaffe)"
test -n "$before" || fail "the earlier build did not say what it migrated"
ok "it applied ${before%%:*} against an empty database"
put_a_life_in

say "The upgrade itself, with a second container starting beside it"
# Two containers at once is not a contrivance: a restart policy and an operator
# in a hurry produce it, and it is the one moment the advisory lock the
# migration takes is worth anything. So the upgrade is performed that way —
# `up -d --wait`, the operator's own command, with one more service in front of
# it.
export PERSONALAFFE_IMAGE="$upgraded"
given="$SECONDS"
"${compose[@]}" -f "$work/both-at-once.yml" up -d --wait >/dev/null 2>&1
wait_for_it
ok "it answered again $((SECONDS - given)) seconds after the command was given"

what_it_migrated personalaffe > "$work/one.txt"
what_it_migrated personalaffe-again > "$work/other.txt"
"${compose[@]}" -f "$work/both-at-once.yml" logs --no-color personalaffe personalaffe-again \
  > "$work/both.log" 2>&1

applying="$(counted 'Applying [0-9]* migration(s)' "$work/both.log")"
idle="$(counted 'nothing to migrate' "$work/both.log")"

test "$applying" = "1" || fail "$applying of the two containers migrated; exactly one had to"
test "$idle" = "1" || fail "$idle of the two containers found nothing to do; exactly one had to"
ok "one of them applied $(cat "$work/one.txt" "$work/other.txt")"
ok "the other found nothing to migrate — which is the lock, from outside"

"${compose[@]}" -f "$work/both-at-once.yml" rm -sf personalaffe-again >/dev/null 2>&1

say "The browser that was signed in before the upgrade still is"
# The opposite of a restore, and deliberately: nothing about the owner's
# sessions was replaced, so an upgrade is not a reason to sign anybody out. The
# whole of the reading below is done through that same session — it is never
# signed in again.
still_in="$(status_of "$instance/api/me")"
test "$still_in" = "200" || fail "the upgrade signed the owner's browser out ($still_in)"
ok "the same cookie, and the instance still knows it"

say "Everything that was put in before the upgrade, read back out by using it"
read_the_life_out

say "The earlier build, put back in front of the schema this one wrote"
export PERSONALAFFE_IMAGE="$earlier"
# `|| true`: this container is meant to fail, and whether Compose notices that
# before it returns is a race. What it did is read out of its log below, which
# is the only answer that means anything either way.
"${compose[@]}" up -d --no-deps personalaffe >/dev/null 2>&1 || true
# The log is written to a file and then read, rather than piped into a `grep`
# that stops at the first match: `grep -m1` closes the pipe under the writer,
# and `pipefail` then reports the whole pipeline as the broken pipe rather than
# as the match.
refused=""
for _ in $(seq 1 20); do
  "${compose[@]}" logs --no-color personalaffe > "$work/refused.log" 2>&1 || true
  if grep -m1 'does not know about' "$work/refused.log" > "$work/refusal.txt"; then
    refused=yes
    break
  fi
  sleep 2
done
test -n "$refused" || fail "the earlier build did not refuse the schema; it may be serving it"

# It names them. A refusal that said only "newer" would leave an operator
# guessing which build to go and find.
grep -q '20[0-9]\{12\}_' "$work/refusal.txt" \
  || fail "the refusal does not name the migrations it has never heard of"
sed 's/^.*\] //; s/^/    /' "$work/refusal.txt"

curl --fail --silent --max-time 5 "$instance/api/health/ready" >/dev/null 2>&1 \
  && fail "the earlier build refused the schema and went on serving anyway"
ok "and it is not serving: there is no downgrade path, only the way back below"

"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1

# ======================================================= act two: and back ===
printf '\n--- The way back ------------------------------------------------\n'

say "The build the way back returns to"
an_image_of "$knows_a_backup"
earlier="$built"

say "An instance of it, with a life in it"
export PERSONALAFFE_IMAGE="$earlier"
"${compose[@]}" up -d --wait >/dev/null 2>&1
wait_for_it
rm -f "$jar"
put_a_life_in

say "The backup the document puts first, taken by the build that is running"
"${compose[@]}" exec -T personalaffe personalaffe backup --to - \
  > "$work/before-the-upgrade.tar" 2>"$work/backup.err"
grep -q 'held still' "$work/backup.err" || fail "the backup did not say how long it held the instance still"
ok "$(tr '\n' ' ' < "$work/backup.err")"
ok "$(wc -c < "$work/before-the-upgrade.tar" | tr -d ' ') bytes, on this machine and not in either volume"

say "The upgrade"
export PERSONALAFFE_IMAGE="$upgraded"
"${compose[@]}" up -d --wait personalaffe >/dev/null 2>&1
wait_for_it
ok "$(the_image_serving_now) is serving"
# This build and that one share a schema, so nothing migrates here — the
# migration is the first act's subject and this one's is the way back. Said out
# loud, because a rehearsal that let it be assumed either way would be no use.
migrated="$(what_it_migrated personalaffe)"
ok "${migrated:-nothing to migrate: these two builds share a schema}"

say "And a page written afterwards, which is what the way back will cost"
# The upgrade is the thing that went wrong in this story. What an operator loses
# by going back is everything written between the backup and the rollback, and a
# rehearsal that did not write anything in that window could not show it.
after_id="$(api -X POST "$instance/api/knowledge/pages" \
  -d '{"title":"Written after the upgrade","parent":null,"markdown":"This is the part that does not survive."}' \
  | field '"id"')"
ok "one page, written on the new build"

say "The way back: stop, put the pre-upgrade backup back, start the earlier build"
# One sequence and one command, because the image the instance runs is a
# variable: `restore.sh` stops what is serving, puts the archive back with a
# one-off container from that image, and starts it again — so naming the earlier
# image is the whole of the rollback.
#
# `--over-a-populated-instance` is the irreversible step. Everything up to it is
# refusable and refuses; past it, what the instance had is gone.
export PERSONALAFFE_IMAGE="$earlier"
given="$SECONDS"
"$root/scripts/restore.sh" "$work/before-the-upgrade.tar" --over-a-populated-instance \
  > "$work/rollback.out" 2>&1 || { cat "$work/rollback.out"; fail "the rollback did not finish"; }
wait_for_it
sed 's/^/    /' "$work/rollback.out" | tail -8
ok "it answered again $((SECONDS - given)) seconds after the command was given"

say "A serving instance, on the earlier build, holding what it held before"
test "$(the_image_serving_now)" = "$earlier" \
  || fail "the instance is on $(the_image_serving_now), not on $earlier"
ok "$earlier is serving"

rm -f "$jar"
sign_in
ok "signed in again — a restore signs every browser out, and this one is a restore"

read_the_life_out

say "And the page written after the upgrade is not"
lost="$(status_of "$instance/api/knowledge/pages/$after_id")"
test "$lost" = "404" || fail "the page written after the backup survived the rollback ($lost)"
ok "404 — the window between the backup and the rollback is the cost, and it is real"

say "Taking it down again"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
ok "both volumes gone"

printf '\nAll %d steps passed. The upgrade is an upgrade, and the way back is a way back.\n' "$step"
