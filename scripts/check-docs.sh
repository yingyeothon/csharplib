#!/usr/bin/env bash
#
# Keeps the documentation honest about itself.
#
# The generated API reference is gated by tests/Yingyeothon.PublicApi.Tests and the
# per-package `## Public API` listings by the same suite. What neither can see is the
# guide: a link that rots, a page nothing reaches, or a public type the guide never
# mentions. A promise the reader cannot follow is worse than a missing document, so
# these three are checks and not conventions.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
note() { echo "FAIL: $*" >&2; fail=1; }

# ---- 1. every relative link resolves --------------------------------------
#
# Only same-repo links are checked; an http(s) link is somebody else's uptime.
# `grep -a` because one NUL byte would otherwise make grep call the rest binary and
# stop matching, and never `grep -q`, which SIGPIPEs its upstream and reads as false
# under pipefail (rules/security.md).
links=0
while IFS= read -r file; do
  dir=$(dirname "$file")
  while IFS= read -r target; do
    [ -n "$target" ] || continue
    case "$target" in
      http*|mailto:*|'#'*) continue ;;
    esac
    path=${target%%#*}
    fragment=""
    case "$target" in *#*) fragment=${target#*#} ;; esac
    links=$((links + 1))

    dest="$dir/$path"
    [ -n "$path" ] || dest="$file"
    if [ ! -e "$dest" ]; then
      note "$file links to $target, which does not exist"
      continue
    fi

    # A heading anchor rots exactly as quietly as a path does, and GitHub renders a
    # dead one as a jump to the top of the page rather than as an error.
    if [ -n "$fragment" ] && [ "${dest##*.}" = "md" ]; then
      found=$(grep -a -E '^#{2,6} ' "$dest" \
        | sed 's/^#* //; s/`//g' \
        | tr '[:upper:]' '[:lower:]' \
        | sed 's/[^a-z0-9 -]//g; s/^ *//; s/ *$//; s/ /-/g' \
        | grep -a -c -x -F "$fragment" || true)
      [ "$found" -gt 0 ] || note "$file links to $target, but $dest has no such heading"
    fi
  done < <(grep -a -oE '\]\([^)]+\)' "$file" | sed 's/^](//; s/)$//')
done < <(find README.md CONVENTIONS.md docs packages rules -name '*.md' -not -path '*/Samples~/*')

# ---- 2. no orphan page ----------------------------------------------------
#
# A page nothing links to is a page nobody reads. docs/README.md is the index, so
# every other guide page has to be reachable from it.
index=docs/README.md
for page in docs/*.md; do
  [ "$page" = "$index" ] && continue
  name=$(basename "$page")
  found=$(grep -a -c "($name" "$index" || true)
  [ "$found" -gt 0 ] || note "$page is not linked from $index"
done

# ---- 3. every public type is in the generated reference --------------------
#
# The per-package README gate proves a type is *named* somewhere; this proves the
# reference actually documents it, so the guide cannot silently lose a feature when a
# type is added.
for approved in tests/Yingyeothon.PublicApi.Tests/Approved/*.approved.txt; do
  assembly=$(basename "$approved" .approved.txt)
  reference="docs/api/$assembly.md"
  if [ ! -f "$reference" ]; then
    note "$reference is missing; run dotnet test to generate it"
    continue
  fi

  # A type line is unindented: "class Foo", "enum Bar", "interface IBaz".
  # -x, because "## interface IEventBroker" is a prefix of
  # "## interface IEventBrokerRenamed" and a prefix match would pass a rename.
  while IFS= read -r type; do
    [ -n "$type" ] || continue
    found=$(grep -a -c -F -x "## $type" "$reference" || true)
    [ "$found" -gt 0 ] || note "$reference does not document $type"
  done < <(grep -a -E '^(class|enum|interface|struct|static class) ' "$approved")
done

[ "$fail" -eq 0 ] && echo "docs: $links relative links resolve, no orphan page, every public type documented"
exit "$fail"
