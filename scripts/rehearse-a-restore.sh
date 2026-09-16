#!/usr/bin/env bash
# Take a backup, destroy the instance, put the backup back, and then read
# everything out again.
#
#   scripts/rehearse-a-restore.sh
#
# **A backup that has not been restored is not yet known to be a backup**, and a
# restore that is only ever exercised by hand is exercised for the first time on
# the day it is needed. So this is the rehearsal, it runs against the image an
# operator installs rather than against anything smaller, and CI runs it on
# every push.
#
# What it proves is proven by use and not by counting rows, and how it says all
# of that is in `an-instance.sh`, beside this — the same words the upgrade
# rehearsal uses, so that the two cannot drift.
#
# It destroys whatever is in deploy/docker-compose.yml's volumes, twice. Do not
# run it against an installation you care about.
#
# Needs: docker compose, curl, python3, shasum or sha256sum.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose=(docker compose -f "$root/deploy/docker-compose.yml")
instance="${PERSONALAFFE_URL:-http://127.0.0.1:8080}"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

password='correct horse battery staple'

# shellcheck source=scripts/an-instance.sh
. "$root/scripts/an-instance.sh"

printf 'Rehearsing a restore against %s\n' "$instance"

# ------------------------------------------------- an instance with a life ----
say "A fresh instance, claimed, with something in every application"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
"${compose[@]}" up -d --wait >/dev/null 2>&1
wait_for_it
put_a_life_in

# ------------------------------------------------------------- the backup ----
say "The backup"
"${compose[@]}" exec -T personalaffe personalaffe backup --to - > "$work/backup.tar" 2>"$work/backup.err"
grep -q 'held still' "$work/backup.err" || fail "the backup did not say how long it held the instance still"
ok "$(tr '\n' ' ' < "$work/backup.err")"
ok "$(wc -c < "$work/backup.tar" | tr -d ' ') bytes"

# --------------------------------------------------------- and the ending ----
say "Destroying the instance — both volumes, the way \`down -v\` does"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
"${compose[@]}" up -d --wait >/dev/null 2>&1
wait_for_it

test "$(curl -sS "$instance/api/setup")" = '{"required":true}' \
  || fail "the new instance is not a fresh one"
ok "a fresh instance, belonging to nobody"

say "Putting the backup back"
"$root/scripts/restore.sh" "$work/backup.tar" > "$work/restore.out" 2>&1 \
  || { cat "$work/restore.out"; fail "the restore did not finish"; }
wait_for_it
sed 's/^/    /' "$work/restore.out" | tail -12

# ------------------------------------------------------ reading it all out ----
say "The owner is the owner again, and no browser came back with them"
test "$(curl -sS "$instance/api/setup")" = '{"required":false}' \
  || fail "the restored instance has no owner"

# The cookie from before the restore, presented to the instance that now holds
# that same session's row. A restore signs every browser out; this is that,
# tested from outside — and it is the one thing an upgrade does not do.
still_in="$(status_of "$instance/api/me")"
test "$still_in" = "401" || fail "a session from before the backup still works ($still_in)"
ok "the old browser is signed out"

rm -f "$jar"
sign_in
ok "and the owner's password still admits them"

say "Everything that went in, read back out by using it"
read_the_life_out

say "Taking it down again"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
ok "both volumes gone"

printf '\nAll %d steps passed. That backup was a backup.\n' "$step"
