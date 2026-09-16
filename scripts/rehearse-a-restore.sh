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
# What it proves is proven by use and not by counting rows: bytes are downloaded
# and compared by checksum, a page's history is read back, a token is presented
# and admitted or refused, a deadline in the Trash is compared with the one it
# had before. Nothing here inspects the database.
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
jar="$work/cookies"
trap 'rm -rf "$work"' EXIT

password='correct horse battery staple'
step=0

say() { step=$((step + 1)); printf '\n[%d] %s\n' "$step" "$1"; }
ok() { printf '    %s\n' "$1"; }
fail() { printf '    FAILED: %s\n' "$1" >&2; exit 1; }

# Every authenticated request a browser would make, cookie jar and all. The two
# headers are what a write from a browser proves it came from this application
# with (docs/api.md).
# --fail-with-body, and that is not a detail: without it a write that was
# refused prints its refusal and the script carries on to report that the thing
# it did not do was done. Every call here is one that must work.
api() {
  curl -sS --fail-with-body -b "$jar" -c "$jar" \
    -H "origin: $instance" -H 'X-Personalaffe-CSRF: 1' -H 'content-type: application/json' "$@"
}

# The version of one item out of a list, as an If-Match: a list cannot answer an
# ETag per item, so the value travels in the item (docs/api.md).
version_of() {
  python3 -c 'import json,sys
items = json.load(sys.stdin)[sys.argv[1]]
print("\"" + next(i["updated_at"] for i in items if i[sys.argv[2]] == sys.argv[3]) + "\"")' "$@"
}

# The same, as an agent: a bearer token and no cookie anywhere.
as_agent() { curl -sS -o /dev/null -w '%{http_code}' -H "authorization: Bearer $1" "${@:2}"; }

field() { python3 -c 'import json,sys; print(json.load(sys.stdin)['"$1"'])'; }
sum() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d" " -f1; else shasum -a 256 "$1" | cut -d" " -f1; fi; }

wait_for_it() {
  for _ in $(seq 1 60); do
    curl --fail --silent --max-time 5 "$instance/api/health/ready" >/dev/null 2>&1 && return 0
    sleep 2
  done
  fail "the instance never became ready"
}

printf 'Rehearsing a restore against %s\n' "$instance"

# ------------------------------------------------- an instance with a life ----
say "A fresh instance, claimed"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1 || true
"${compose[@]}" up -d --wait >/dev/null 2>&1
wait_for_it

api -X POST "$instance/api/setup" \
  -d "{\"email\":\"owner@example.com\",\"password\":\"$password\"}" -o /dev/null
api -X POST "$instance/api/session" \
  -d "{\"email\":\"owner@example.com\",\"password\":\"$password\"}" -o /dev/null
ok "claimed and signed in"

say "Something in every application, and something in the Trash"

# A file large enough to have been streamed rather than held in one buffer.
head -c 3145728 /dev/urandom > "$work/photo.bin"
file_id="$(curl -sS -b "$jar" -c "$jar" -H "origin: $instance" -H 'X-Personalaffe-CSRF: 1' \
  -H 'content-type: application/octet-stream' --data-binary "@$work/photo.bin" \
  -X POST "$instance/api/files/content?name=photo.bin" | field '"id"')"
sum "$work/photo.bin" > "$work/photo.sha"
ok "a 3 MiB file, $(cat "$work/photo.sha")"

page_id="$(api -X POST "$instance/api/knowledge/pages" \
  -d '{"title":"What I know","parent":null,"markdown":"# First\n\nThe first version."}' | field '"id"')"
page_tag="$(api -D - -o /dev/null "$instance/api/knowledge/pages/$page_id" | tr -d '\r' | awk '/^[Ee][Tt]ag:/ {print $2}')"
api -X PUT "$instance/api/knowledge/pages/$page_id" -H "If-Match: $page_tag" \
  -d '{"title":"What I know","parent":null,"markdown":"# Second\n\nThe second version."}' -o /dev/null
ok "a page, written twice, so that it has a history"

list_id="$(api -X POST "$instance/api/tasks/lists" -d '{"name":"Everything"}' | field '"id"')"
task_id="$(api -X POST "$instance/api/tasks/lists/$list_id/tasks" \
  -d '{"title":"Prove the restore","description":null,"due_on":"2026-12-24"}' | field '"id"')"
ok "a task, due on a day rather than at a moment"

entry_id="$(api -X POST "$instance/api/scratchpad/entries" \
  -d '{"text":"Ünïcödé, and\ntwo lines.","pinned":true}' | field '"id"')"
ok "a pinned Scratchpad entry"

# Something deleted, so that the Trash has a deadline that must survive.
doomed_id="$(api -X POST "$instance/api/knowledge/pages" \
  -d '{"title":"A page I deleted","parent":null,"markdown":"gone"}' | field '"id"')"
doomed_tag="$(api -D - -o /dev/null "$instance/api/knowledge/pages/$doomed_id" | tr -d '\r' | awk '/^[Ee][Tt]ag:/ {print $2}')"
api -X DELETE "$instance/api/knowledge/pages/$doomed_id" -H "If-Match: $doomed_tag" -o /dev/null
api "$instance/api/trash" > "$work/trash-before.json"
expires_before="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["items"][0]["expires_at"])' "$work/trash-before.json")"
ok "a page in the Trash, expiring $expires_before"

say "Two agents, one of them revoked, and the workspace arranged"
kept_token="$(api -X POST "$instance/api/agents" \
  -d '{"name":"the one that stays","permissions":{"scratchpad":"read_write","knowledge":"read","tasks":"none","files":"none"}}' \
  | field '"token"')"
revoked="$(api -X POST "$instance/api/agents" \
  -d '{"name":"the one that goes","permissions":{"scratchpad":"read","knowledge":"none","tasks":"none","files":"none"}}')"
revoked_id="$(printf '%s' "$revoked" | python3 -c 'import json,sys; print(json.load(sys.stdin)["agent"]["id"])')"
revoked_token="$(printf '%s' "$revoked" | field '"token"')"
api -X DELETE "$instance/api/agents/$revoked_id" -o /dev/null
ok "one agent with two permissions, one revoked"

apps_tag="$(api "$instance/api/applications" | version_of items application tasks)"
api -X PUT "$instance/api/applications/tasks" -H "If-Match: $apps_tag" -d '{"enabled":false}' -o /dev/null
ok "Tasks switched off"

# A list cannot answer an ETag per item, so the version travels in the item
# (docs/api.md, The guarded write).
tile_tag="$(api "$instance/api/dashboard" | version_of tiles tile weather)"
api -X PUT "$instance/api/dashboard/tiles/weather" -H "If-Match: $tile_tag" -d '{"shown":false}' -o /dev/null
place_tag="$(api -D - -o /dev/null "$instance/api/weather" | tr -d '\r' | awk '/^[Ee][Tt]ag:/ {print $2}')"
api -X PUT "$instance/api/weather/place" -H "If-Match: $place_tag" \
  -d '{"name":"Am Schreibtisch","latitude":52.52,"longitude":13.405,"units":"metric"}' -o /dev/null
ok "the weather tile hidden, and a place set"

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
# tested from outside.
still_in="$(curl -sS -b "$jar" -o /dev/null -w '%{http_code}' "$instance/api/me")"
test "$still_in" = "401" || fail "a session from before the backup still works ($still_in)"
ok "the old browser is signed out"

rm -f "$jar"
api -X POST "$instance/api/session" -d "{\"email\":\"owner@example.com\",\"password\":\"$password\"}" -o /dev/null
ok "and the owner's password still admits them"

say "The file, by its bytes"
curl -sS -b "$jar" -c "$jar" "$instance/api/files/$file_id/content" -o "$work/downloaded.bin"
test "$(sum "$work/downloaded.bin")" = "$(cat "$work/photo.sha")" \
  || fail "the file that came back is not the file that went in"
ok "$(sum "$work/downloaded.bin") — the same bytes"

say "The page, and the version it used to be"
api "$instance/api/knowledge/pages/$page_id" > "$work/page.json"
grep -q 'The second version' "$work/page.json" || fail "the page is not what it was"
revisions="$(api "$instance/api/knowledge/pages/$page_id/revisions" \
  | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["items"]))')"
test "$revisions" -ge 1 || fail "the page came back without its history"
ok "the page, and $revisions revision(s) behind it"

say "The Trash, still counting from the moment it was deleted"
api "$instance/api/trash" > "$work/trash-after.json"
expires_after="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["items"][0]["expires_at"])' "$work/trash-after.json")"
test "$expires_after" = "$expires_before" \
  || fail "the deadline moved: $expires_before became $expires_after"
ok "still expiring $expires_after"

say "The task, its day, and the application it is in"
api "$instance/api/applications" \
  | python3 -c 'import json,sys
tasks = next(a for a in json.load(sys.stdin)["items"] if a["application"] == "tasks")
sys.exit(0 if not tasks["enabled"] else 1)' \
  || fail "Tasks is not switched off any more"
ok "Tasks is still switched off"

apps_tag="$(api "$instance/api/applications" | version_of items application tasks)"
api -X PUT "$instance/api/applications/tasks" -H "If-Match: $apps_tag" -d '{"enabled":true}' -o /dev/null
due="$(api "$instance/api/tasks/$task_id" | field '"due_on"')"
test "$due" = "2026-12-24" || fail "the due date is $due"
ok "due on $due, the same day in every timezone"

say "The Scratchpad entry, pinned and unchanged"
api "$instance/api/scratchpad/entries/$entry_id" > "$work/entry.json"
python3 -c 'import json,sys; e=json.load(open(sys.argv[1])); assert e["pinned"], e; assert "Ünïcödé" in e["text"], e' \
  "$work/entry.json" || fail "the entry came back changed"
ok "pinned, and every character of it"

say "The agents: one admitted, one still revoked"
admitted="$(as_agent "$kept_token" "$instance/api/scratchpad/entries")"
test "$admitted" = "200" || fail "the agent that stayed is not admitted ($admitted)"

# Its permissions came back with it: read on Knowledge means no write.
denied="$(as_agent "$kept_token" -X POST "$instance/api/knowledge/pages" \
  -H 'content-type: application/json' -d '{"title":"nope","parent":null,"markdown":"x"}')"
test "$denied" = "403" || fail "the agent's Knowledge permission is not read-only ($denied)"

gone="$(as_agent "$revoked_token" "$instance/api/scratchpad/entries")"
test "$gone" = "401" || fail "a revoked token came back working ($gone)"
ok "admitted 200, its read-only permission 403, the revoked one 401"

say "The dashboard and the weather place"
api "$instance/api/dashboard" \
  | python3 -c 'import json,sys
weather = next(t for t in json.load(sys.stdin)["tiles"] if t["tile"] == "weather")
sys.exit(0 if not weather["shown"] else 1)' \
  || fail "the hidden tile came back shown"
api "$instance/api/weather" | grep -q 'Am Schreibtisch' \
  || fail "the weather place did not come back"
ok "the tile is still hidden and the place is still set"

say "Taking it down again"
"${compose[@]}" down -v --remove-orphans >/dev/null 2>&1
ok "both volumes gone"

printf '\nAll %d steps passed. That backup was a backup.\n' "$step"
