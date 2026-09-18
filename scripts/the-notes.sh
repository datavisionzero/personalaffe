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

if [ -z "$notes" ]; then
  printf 'CHANGELOG.md has no section for %s.\n' "$version" >&2
  exit 1
fi

printf '%s\n' "$notes"
