#!/usr/bin/env bash
# Does this foundation actually hang together?
#
# Nine checks against a running instance, and they are the ones nothing else
# proves from the outside: that the API answers, that the door in front of it is
# shut, that a refusal is the document the contract promises rather than a bare
# status, that the web application is served from the same origin, that the line
# between them holds, that the contract the instance serves is the one that is
# checked in, and that both clients can still be generated from it — with `pea`,
# built here from that document, asking the instance the same question the
# browser asks.
#
#   scripts/smoke.sh [url]        # default http://localhost:5000
#
# It writes nothing into the repository and needs no credential: every operation
# it calls is one of the five that answer before anything has authenticated, and
# the one check about the door asks it for a refusal (docs/api.md).
#
# Needs: curl, python3, go. The TypeScript check also needs `npm ci` to have
# been run in src/web; it says so and fails rather than passing quietly.

set -euo pipefail

instance="${1:-http://localhost:5000}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

checks=0

step() {
  checks=$((checks + 1))
  printf '\n[%d] %s\n' "$checks" "$1"
}

fail() {
  printf '    FAILED: %s\n' "$1" >&2
  exit 1
}

printf 'personalaffe smoke test against %s\n' "$instance"

# ---------------------------------------------------------------- the API ----
step "The instance answers, and says what it is"
served_version="$(curl --fail --silent --max-time 10 "$instance/api/version" \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["version"])')" \
  || fail "$instance/api/version did not answer a version"
printf '    the instance is %s\n' "$served_version"

step "Liveness and readiness are both answered, and readiness means the database"
curl --fail --silent --max-time 10 "$instance/api/health/live" \
  | grep -q '"live"' || fail "/api/health/live did not say live"
curl --fail --silent --max-time 10 "$instance/api/health/ready" \
  | grep -q '"ready"' || fail "/api/health/ready did not say ready — is PostgreSQL up and migrated?"
printf '    live, and ready\n'

# ------------------------------------------------------------- the door ----
step "Everything but the five operations outside the door needs a credential"
answer="$(curl --silent --max-time 10 --output "$work/refusal.json" \
  --write-out '%{http_code} %{content_type}' "$instance/api/me")"
case "$answer" in
  "401 application/problem+json"*) ;;
  *) fail "/api/me answered $answer, and everything but the five is behind the door" ;;
esac
grep -q '"/problems/unauthenticated"' "$work/refusal.json" \
  || fail "/api/me refused without saying which refusal it was"
printf '    %s\n' "$answer"

step "The instance says whether it has an owner, and says nothing else"
curl --fail --silent --max-time 10 "$instance/api/setup" --output "$work/setup.json" \
  || fail "/api/setup did not answer"
grep -Eq '^\{"required":(true|false)\}$' "$work/setup.json" \
  || fail "/api/setup answered more than whether setup is needed: $(cat "$work/setup.json")"
printf '    %s\n' "$(cat "$work/setup.json")"

# --------------------------------------------------------- every refusal ----
step "A body the reader cannot make sense of is a document, not an empty 400"
# Against this instance rather than against a suite, because this is the one
# check whose answer depended on ASPNETCORE_ENVIRONMENT: a build that leaves
# `ThrowOnBadRequest` to the framework answers Development with a document and
# everything else with a status and no body at all (PERSONAL-70,
# docs/operations.md). Setup is the endpoint to ask because it takes a body
# outside the door — and both of these are refused while the reader is still
# parsing, so neither can claim an instance that has no owner.
malformed() {
  curl --silent --max-time 10 --output "$work/unreadable.json" \
    --write-out '%{http_code} %{content_type}' \
    --header 'Content-Type: application/json' --data "$1" "$instance/api/setup"
}

answer="$(malformed '{not json')"
case "$answer" in
  "400 application/problem+json"*) ;;
  *) fail "a malformed body answered $answer, and every refusal is one document" ;;
esac
grep -q '"/problems/validation"' "$work/unreadable.json" \
  || fail "a malformed body was refused without saying which refusal it was: $(cat "$work/unreadable.json")"

answer="$(malformed '{"email":"smoke@example.com","passwrd":"not-the-field"}')"
case "$answer" in
  "400 application/problem+json"*) ;;
  *) fail "a field the object does not define answered $answer" ;;
esac
grep -q '"/problems/unknown-field"' "$work/unreadable.json" \
  || fail "a field the object does not define was not unknown-field: $(cat "$work/unreadable.json")"
printf '    validation and unknown-field, both as problem+json\n'

# ------------------------------------------------------ the web application ----
step "The web application is served from the same origin as the API"
type="$(curl --fail --silent --max-time 10 --output "$work/index.html" --write-out '%{content_type}' "$instance/")" \
  || fail "$instance/ did not answer"
case "$type" in
  text/html*) ;;
  *) fail "/ answered $type, not a page. Has `npm run build` been run in src/web?" ;;
esac
grep -q '<div id="root">' "$work/index.html" \
  || fail "/ answered a page, but not this application's"
printf '    / is the application, over %s\n' "$type"

step "An address under /api that no endpoint took is an API error, not the page"
answer="$(curl --silent --max-time 10 --output /dev/null --write-out '%{http_code} %{content_type}' \
  "$instance/api/nothing-here")"
case "$answer" in
  "404 application/problem+json"*) ;;
  *) fail "/api/nothing-here answered $answer" ;;
esac
printf '    %s\n' "$answer"

# ----------------------------------------------------------- the contract ----
step "The contract the instance serves is the one that is checked in"
curl --fail --silent --max-time 10 "$instance/api/openapi/v1.json" --output "$work/served.json" \
  || fail "the instance served no contract"
python3 - "$work/served.json" "$root/docs/api/openapi.json" <<'PY' || fail "the served contract is not the checked-in one; recapture it with PERSONALAFFE_CAPTURE_CONTRACT=1"
import json, sys
served, checked_in = (json.load(open(path, encoding="utf-8")) for path in sys.argv[1:3])
sys.exit(0 if served == checked_in else 1)
PY
printf '    docs/api/openapi.json is what is being served\n'

# ------------------------------------------------------------ the clients ----
step "Both clients generate from that document, and pea asks the same question"
(cd "$root/src/cli" && go generate ./... >/dev/null) \
  || fail "the Go client could not be generated"
(cd "$root/src/cli" && go build -o "$work/pea" ./cmd/pea) \
  || fail "pea could not be built"

reported="$(PERSONALAFFE_URL="$instance" "$work/pea" version --json \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["instance"])')" \
  || fail "pea could not read the instance's version"
[ "$reported" = "$served_version" ] \
  || fail "pea says the instance is $reported and the API says $served_version"
printf '    pea reports %s, the same as the API\n' "$reported"

if [ -d "$root/src/web/node_modules" ]; then
  (cd "$root/src/web" && npm run --silent generate >/dev/null) \
    || fail "the TypeScript client could not be generated"
  printf '    the TypeScript client generates too\n'
else
  fail "src/web/node_modules is missing; run \`npm ci\` in src/web first"
fi

printf '\nAll %d checks passed. The browser, pea and this instance are the same API.\n' "$checks"
