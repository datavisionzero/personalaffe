#!/usr/bin/env bash
# What shipped in one version, out of CHANGELOG.md.
#
#   scripts/the-notes.sh 0.1.0
#
# The release workflow reads this twice: once before anything is built, to
# refuse a tag nobody wrote notes for, and once at the end to put them under the
# tag. That is the whole practice — the notes are written before the tag is
# pushed, because a release whose notes are written afterwards is a release
# nobody had to think about before cutting it.
#
# It prints the section and nothing else: no heading of its own, so what comes
# out is what an operator reads.

set -euo pipefail

version="${1:?the version, without the leading v}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# A release candidate is a candidate *for* a release, and what it ships is that
# release's notes: `0.1.0-rc.1` reads the `0.1.0` section. Writing the notes
# twice would mean two documents to keep true, and the second one is the one
# nobody updates.
section="${version%%-*}"

notes="$(awk -v want="$version" '
  # A section starts at "## <version>" and ends at the next "## ".
  /^## / {
    if (inside) { exit }
    split($0, head, " ")
    if (head[2] == want) { inside = 1; next }

  }
  inside { print }
' "$root/CHANGELOG.md")"

# Trimmed of the blank lines a section is padded with, so that two of these
# concatenated do not grow a gap between them.
notes="$(printf '%s' "$notes" | sed -e '/./,$!d' -e ':a' -e '/^\n*$/{$d;N;ba' -e '}')"

# Nothing under the exact version: read the release this is a candidate for, and
# say on standard error which section that was, so that it is never a surprise to
# whoever reads the log of a release.
if [ -z "$notes" ] && [ "$section" != "$version" ]; then
  notes="$("${BASH_SOURCE[0]}" "$section" 2>/dev/null || true)"

  if [ -n "$notes" ]; then
    printf '%s has no section of its own; reading %s.\n' "$version" "$section" >&2
  fi
fi

if [ -z "$notes" ]; then
  printf 'CHANGELOG.md has no section for %s.\n' "$version" >&2
  exit 1
fi

printf '%s\n' "$notes"
