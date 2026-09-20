#!/usr/bin/env bash
# What a rehearsal says to an instance, and what a life in one is. Sourced,
# never run:
#
#   . "$root/scripts/an-instance.sh"
#
# Two scripts prove something about a running personalaffe by using it —
# `rehearse-a-restore.sh` and `rehearse-an-upgrade.sh` — and what they have in
# common is nearly the whole of it: how a browser's request is made, what a
# life in an instance consists of, and what it looks like when all of it is
# still there. One copy of that, so the two cannot drift into asserting
# different things about the same product, and so the difference between them
# stays the only interesting thing in either file.
#
# Whatever is asserted here is asserted **through the API**, by use and not by
# counting rows: bytes are downloaded and compared by checksum, a page's history
# is read back, a token is presented and admitted or refused, a deadline in the
# Trash is compared with the one it had before. Nothing here looks in the
# database.
#
# It reads three variables the caller sets before sourcing it: `instance`, the
# address; `work`, a directory of its own; and `password`, the owner's.
#
# Needs: curl, python3, shasum or sha256sum.

# shellcheck shell=bash
# The three above are the caller's, and a sourced file cannot see them being
# set; SC2154 would be raised once for each use of each of them.
# shellcheck disable=SC2154

# Where a browser's cookies go. One jar for the whole run, and the scripts
# delete it when they mean "a browser that has never been here".
jar="$work/cookies"

step=0

say() { step=$((step + 1)); printf '\n[%d] %s\n' "$step" "$1"; }
ok() { printf '    %s\n' "$1"; }
fail() { printf '    FAILED: %s\n' "$1" >&2; exit 1; }

# Every authenticated request a browser would make, cookie jar and all. The two
# headers are what a write from a browser proves it came from this application
# with (docs/api.md).
# --fail-with-body, and that is not a detail: without it a write that was
# refused prints its refusal and the script carries on to report that the thing
# it did not do was done. Every call made through this is one that must work.
api() {
  curl -sS --fail-with-body -b "$jar" -c "$jar" \
    -H "origin: $instance" -H 'X-Personalaffe-CSRF: 1' -H 'content-type: application/json' "$@"
}

# The same, as an agent: a bearer token and no cookie anywhere. Answers the
# status code, because what an agent's permission is worth is exactly that.
as_agent() { curl -sS -o /dev/null -w '%{http_code}' -H "authorization: Bearer $1" "${@:2}"; }

# The status code a browser got, whatever it was — for the two questions where
# the refusal is the answer.
status_of() { curl -sS -b "$jar" -c "$jar" -o /dev/null -w '%{http_code}' "$@"; }

field() { python3 -c 'import json,sys; print(json.load(sys.stdin)['"$1"'])'; }

# The version of one item out of a list, as an If-Match: a list cannot answer an
# ETag per item, so the value travels in the item (docs/api.md).
version_of() {
  python3 -c 'import json,sys
items = json.load(sys.stdin)[sys.argv[1]]
print("\"" + next(i["updated_at"] for i in items if i[sys.argv[2]] == sys.argv[3]) + "\"")' "$@"
}

# The version of one thing that has an address of its own, out of the header it
# answers with.
etag_of() { api -D - -o /dev/null "$1" | tr -d '\r' | awk '/^[Ee][Tt]ag:/ {print $2}'; }

sum() { if command -v sha256sum >/dev/null; then sha256sum "$1" | cut -d" " -f1; else shasum -a 256 "$1" | cut -d" " -f1; fi; }

wait_for_it() {
  for _ in $(seq 1 60); do
    curl --fail --silent --max-time 5 "$instance/api/health/ready" >/dev/null 2>&1 && return 0
    sleep 2
  done
  fail "the instance never became ready"
}

sign_in() {
  api -X POST "$instance/api/session" \
    -d "{\"email\":\"owner@example.com\",\"password\":\"$password\"}" -o /dev/null
}

# ------------------------------------------------------------- a life in one ----

# Claims a fresh instance and puts something in every application, plus
# something deleted, an application switched off, two agents and an arranged
# home page — and writes down what it did in $work/the-life, so that what is
# read back afterwards is compared with what actually went in rather than with
# what this file happens to say today.
put_a_life_in() {
  api -X POST "$instance/api/setup" \
    -d "{\"email\":\"owner@example.com\",\"password\":\"$password\"}" -o /dev/null
  sign_in
  ok "claimed and signed in"

  # A file large enough to have been streamed rather than held in one buffer.
  head -c 3145728 /dev/urandom > "$work/photo.bin"
  local file_id
  file_id="$(curl -sS -b "$jar" -c "$jar" -H "origin: $instance" -H 'X-Personalaffe-CSRF: 1' \
    -H 'content-type: application/octet-stream' --data-binary "@$work/photo.bin" \
    -X POST "$instance/api/files/content?name=photo.bin" | field '"id"')"
  local photo_sha
  photo_sha="$(sum "$work/photo.bin")"
  ok "a 3 MiB file, $photo_sha"

  local page_id
  page_id="$(api -X POST "$instance/api/knowledge/pages" \
    -d '{"title":"What I know","parent":null,"markdown":"# First\n\nThe first version."}' | field '"id"')"
  api -X PUT "$instance/api/knowledge/pages/$page_id" -H "If-Match: $(etag_of "$instance/api/knowledge/pages/$page_id")" \
    -d '{"title":"What I know","parent":null,"markdown":"# Second\n\nThe second version."}' -o /dev/null
  ok "a page, written twice, so that it has a history"

  local list_id task_id
  list_id="$(api -X POST "$instance/api/tasks/lists" -d '{"name":"Everything"}' | field '"id"')"
  task_id="$(api -X POST "$instance/api/tasks/lists/$list_id/tasks" \
    -d '{"title":"Prove the restore","description":null,"due_on":"2026-12-24"}' | field '"id"')"
  ok "a task, due on a day rather than at a moment"

  local entry_id
  entry_id="$(api -X POST "$instance/api/scratchpad/entries" \
    -d '{"text":"Ünïcödé, and\ntwo lines.","pinned":true}' | field '"id"')"
  ok "a pinned Scratchpad entry"

  # Something deleted, so that the Trash has a deadline that must survive.
  local doomed_id expires_at
  doomed_id="$(api -X POST "$instance/api/knowledge/pages" \
    -d '{"title":"A page I deleted","parent":null,"markdown":"gone"}' | field '"id"')"
  api -X DELETE "$instance/api/knowledge/pages/$doomed_id" \
    -H "If-Match: $(etag_of "$instance/api/knowledge/pages/$doomed_id")" -o /dev/null
  expires_at="$(api "$instance/api/trash" \
    | python3 -c 'import json,sys; print(next(i["expires_at"] for i in json.load(sys.stdin)["items"] if i["application"] == "knowledge"))')"
  ok "a page in the Trash, expiring $expires_at"

  local kept_token revoked revoked_id revoked_token
  kept_token="$(api -X POST "$instance/api/agents" \
    -d '{"name":"the one that stays","permissions":{"scratchpad":"read_write","knowledge":"read","tasks":"none","files":"none"}}' \
    | field '"token"')"
  revoked="$(api -X POST "$instance/api/agents" \
    -d '{"name":"the one that goes","permissions":{"scratchpad":"read","knowledge":"none","tasks":"none","files":"none"}}')"
  revoked_id="$(printf '%s' "$revoked" | python3 -c 'import json,sys; print(json.load(sys.stdin)["agent"]["id"])')"
  revoked_token="$(printf '%s' "$revoked" | field '"token"')"
  api -X DELETE "$instance/api/agents/$revoked_id" -o /dev/null
  ok "one agent with two permissions, one revoked"

  api -X PUT "$instance/api/applications/tasks" \
    -H "If-Match: $(api "$instance/api/applications" | version_of items application tasks)" \
    -d '{"enabled":false}' -o /dev/null
  ok "Tasks switched off"

  api -X PUT "$instance/api/dashboard/tiles/weather" \
    -H "If-Match: $(api "$instance/api/dashboard" | version_of tiles tile weather)" \
    -d '{"shown":false}' -o /dev/null
  api -X PUT "$instance/api/weather/place" -H "If-Match: $(etag_of "$instance/api/weather")" \
    -d '{"name":"Am Schreibtisch","latitude":52.52,"longitude":13.405,"units":"metric"}' -o /dev/null
  ok "the weather tile hidden, and a place set"

  put_bookmarks_in

  cat > "$work/the-life" <<LIFE
file_id='$file_id'
photo_sha='$photo_sha'
page_id='$page_id'
task_id='$task_id'
entry_id='$entry_id'
expires_at='$expires_at'
kept_token='$kept_token'
revoked_token='$revoked_token'
LIFE
}

# And the other direction. Whatever happened to the instance in between — a
# backup and a restore, or an upgrade and a rollback — this is what it means for
# all of it to have come through: every application answers with what went in,
# by being used.
#
# It expects a signed-in browser, because whether one survived is the caller's
# subject and not this file's.
read_the_life_out() {
  # shellcheck source=/dev/null
  . "$work/the-life"

  curl -sS -b "$jar" -c "$jar" "$instance/api/files/$file_id/content" -o "$work/downloaded.bin"
  test "$(sum "$work/downloaded.bin")" = "$photo_sha" \
    || fail "the file that came back is not the file that went in"
  ok "the file, by its bytes: $photo_sha"

  api "$instance/api/knowledge/pages/$page_id" > "$work/page.json"
  grep -q 'The second version' "$work/page.json" || fail "the page is not what it was"
  local revisions
  revisions="$(api "$instance/api/knowledge/pages/$page_id/revisions" \
    | python3 -c 'import json,sys; print(len(json.load(sys.stdin)["items"]))')"
  test "$revisions" -ge 1 || fail "the page came back without its history"
  ok "the page, and $revisions revision(s) behind it"

  local expires_now
  expires_now="$(api "$instance/api/trash" \
    | python3 -c 'import json,sys; print(next(i["expires_at"] for i in json.load(sys.stdin)["items"] if i["application"] == "knowledge"))')"
  test "$expires_now" = "$expires_at" \
    || fail "the deadline moved: $expires_at became $expires_now"
  ok "the Trash, still expiring $expires_now — the same moment"

  api "$instance/api/applications" \
    | python3 -c 'import json,sys
tasks = next(a for a in json.load(sys.stdin)["items"] if a["application"] == "tasks")
sys.exit(0 if not tasks["enabled"] else 1)' \
    || fail "Tasks is not switched off any more"

  api -X PUT "$instance/api/applications/tasks" \
    -H "If-Match: $(api "$instance/api/applications" | version_of items application tasks)" \
    -d '{"enabled":true}' -o /dev/null
  local due
  due="$(api "$instance/api/tasks/$task_id" | field '"due_on"')"
  test "$due" = "2026-12-24" || fail "the due date is $due"
  ok "Tasks still switched off; the task still due on $due"

  api "$instance/api/scratchpad/entries/$entry_id" > "$work/entry.json"
  python3 -c 'import json,sys; e=json.load(open(sys.argv[1])); assert e["pinned"], e; assert "Ünïcödé" in e["text"], e' \
    "$work/entry.json" || fail "the entry came back changed"
  ok "the Scratchpad entry, pinned, and every character of it"

  local admitted denied gone
  admitted="$(as_agent "$kept_token" "$instance/api/scratchpad/entries")"
  test "$admitted" = "200" || fail "the agent that stayed is not admitted ($admitted)"

  # Its permissions came with it: read on Knowledge means no write.
  denied="$(as_agent "$kept_token" -X POST "$instance/api/knowledge/pages" \
    -H 'content-type: application/json' -d '{"title":"nope","parent":null,"markdown":"x"}')"
  test "$denied" = "403" || fail "the agent's Knowledge permission is not read-only ($denied)"

  gone="$(as_agent "$revoked_token" "$instance/api/scratchpad/entries")"
  test "$gone" = "401" || fail "a revoked token came back working ($gone)"
  ok "the agents: admitted 200, its read-only permission 403, the revoked one 401"

  api "$instance/api/dashboard" \
    | python3 -c 'import json,sys
weather = next(t for t in json.load(sys.stdin)["tiles"] if t["tile"] == "weather")
sys.exit(0 if not weather["shown"] else 1)' \
    || fail "the hidden tile came back shown"
  api "$instance/api/weather" | grep -q 'Am Schreibtisch' \
    || fail "the weather place did not come back"
  ok "the dashboard: the tile still hidden and the place still set"
  read_bookmarks_out
}


# Older rehearsal images may predate Bookmarks or its optional metadata. Seed
# only capabilities they actually expose; compare every field they wrote after
# upgrade/restore. The current image always exercises the complete application.
put_bookmarks_in() {
  rm -f "$work/bookmark-life.json"
  if ! api "$instance/api/applications" | python3 -c 'import json,sys; sys.exit(0 if any(i["application"] == "bookmarks" for i in json.load(sys.stdin)["items"]) else 1)'; then
    return
  fi
  local folder bookmark public deleted version
  folder="$(api -X POST "$instance/api/bookmarks/folders" -H 'Personalaffe-Private: true' -d '{"name":"Private reading","private":true}')"
  printf '%s' "$folder" > "$work/bookmark-folder.json"
  bookmark="$(api -X POST "$instance/api/bookmarks" -H 'Personalaffe-Private: true' -d "{\"title\":\"Private guide\",\"url\":\"https://example.com/guide\",\"description\":\"Preserve this\",\"folder\":\"$(printf '%s' "$folder" | field '"id"')\"}")"
  printf '%s' "$bookmark" > "$work/bookmark.json"
  python3 - "$work/bookmark.json" > "$work/bookmark-edit.json" <<'PYBOOK'
import json,sys
b=json.load(open(sys.argv[1]))
body={k:b[k] for k in ("title","url","description","folder")}
if "tags" in b: body["tags"]=["research","restore"]
if "read_later" in b: body["read_later"]=True
print(json.dumps(body))
PYBOOK
  bookmark="$(printf '%s' "$bookmark" | field '"id"')"
  version="$(api -D - -o /dev/null "$instance/api/bookmarks/$bookmark" -H 'Personalaffe-Private: true' | tr -d '\r' | awk '/^[Ee][Tt]ag:/ {print $2}')"
  api -X PUT "$instance/api/bookmarks/$bookmark" -H 'Personalaffe-Private: true' -H "If-Match: $version" --data-binary "@$work/bookmark-edit.json" > "$work/bookmark.json"
  version="$(python3 -c 'import json,sys; print(json.dumps(json.load(open(sys.argv[1]))["updated_at"]))' "$work/bookmark.json")"
  api -X PUT "$instance/api/bookmarks/$bookmark/favorite" -H 'Personalaffe-Private: true' -H "If-Match: $version" -d '{"favorite":true}' > "$work/bookmark.json"
  api -X POST "$instance/api/bookmarks/$bookmark/open" -H 'Personalaffe-Private: true' -d '{"event_id":"0199f0c4-0000-7000-8000-000000000001"}' -o /dev/null
  public="$(api -X POST "$instance/api/bookmarks" -d '{"title":"Public guide","url":"https://example.com/public"}')"
  printf '%s' "$public" > "$work/bookmark-public.json"
  deleted="$(api -X POST "$instance/api/bookmarks" -H 'Personalaffe-Private: true' -d "{\"title\":\"Deleted private guide\",\"url\":\"https://example.com/deleted\",\"folder\":\"$(printf '%s' "$folder" | field '"id"')\"}")"
  version="$(printf '%s' "$deleted" | python3 -c 'import json,sys; print(json.dumps(json.load(sys.stdin)["updated_at"]))')"
  api -X DELETE "$instance/api/bookmarks/$(printf '%s' "$deleted" | field '"id"')" -H 'Personalaffe-Private: true' -H "If-Match: $version" -o /dev/null
  api "$instance/api/trash?application=bookmarks" -H 'Personalaffe-Private: true' > "$work/bookmark-trash.json"
  python3 - "$work" > "$work/bookmark-life.json" <<'PYBOOK'
import json,sys,pathlib
p=pathlib.Path(sys.argv[1]); print(json.dumps({k:json.loads((p/("bookmark-"+k+".json" if k!="bookmark" else "bookmark.json")).read_text()) for k in ("folder","bookmark","public","trash")}))
PYBOOK
  api -X PUT "$instance/api/applications/bookmarks" -H "If-Match: $(api "$instance/api/applications" | version_of items application bookmarks)" -d '{"enabled":false}' -o /dev/null
  test "$(status_of "$instance/api/bookmarks")" = "409" || fail "disabled Bookmarks still answers"
  api -X PUT "$instance/api/applications/bookmarks" -H "If-Match: $(api "$instance/api/applications" | version_of items application bookmarks)" -d '{"enabled":true}' -o /dev/null
  ok "public and private bookmarks, folder, favorite, optional tags/reading status and private Trash"
}

read_bookmarks_out() {
  test -f "$work/bookmark-life.json" || return 0
  local folder bookmark public
  folder="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["folder"]["id"])' "$work/bookmark-life.json")"
  bookmark="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["bookmark"]["id"])' "$work/bookmark-life.json")"
  public="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["public"]["id"])' "$work/bookmark-life.json")"
  test "$(status_of "$instance/api/bookmarks/$bookmark")" = "404" || fail "private bookmark became public"
  api "$instance/api/bookmarks/folders/$folder" -H 'Personalaffe-Private: true' > "$work/bookmark-folder.json"
  api "$instance/api/bookmarks/$bookmark" -H 'Personalaffe-Private: true' > "$work/bookmark.json"
  api "$instance/api/bookmarks/$public" > "$work/bookmark-public.json"
  api "$instance/api/trash?application=bookmarks" -H 'Personalaffe-Private: true' > "$work/bookmark-trash.json"
  python3 - "$work" <<'PYBOOK'
import json,sys,pathlib
p=pathlib.Path(sys.argv[1]); before=json.loads((p/"bookmark-life.json").read_text())
for key, expected in before.items():
    actual=json.loads((p/("bookmark-"+key+".json" if key!="bookmark" else "bookmark.json")).read_text())
    for field,value in expected.items(): assert actual[field]==value, (key,field,actual[field],value)
PYBOOK
  test "$(as_agent "$kept_token" "$instance/api/bookmarks" -H 'Personalaffe-Private: true')" = "403" || fail "old agent silently gained Bookmarks permission"
  ok "bookmark metadata, privacy, favorite order, Trash and old agent permissions survived"
}
