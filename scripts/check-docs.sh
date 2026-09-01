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

# ---- 4. every install URL agrees with the version --------------------------
#
# The tag is the release (rules/release.md), so an unpinned URL is honest only
# while no tag exists and a URL pinned to a tag that is not cut is a 404 for every
# consumer. There are fourteen of them across seven files and nothing else looks at
# them: check 1 skips http(s) links on purpose.
version=$(sed 's/<!--.*-->//g' Directory.Build.props \
  | sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' | head -1)

# Ask the remote, not the local ref store: a CI checkout carries no tags unless the
# workflow sets fetch-tags, so reading `git tag -l` there would call every pinned URL
# unreleased and turn CI red for good the day the first tag lands. The local list is
# the offline fallback, and it is also what lets a release pin its URLs before the tag
# is pushed (rules/release.md).
tagged=$(git ls-remote --tags origin "refs/tags/v$version" 2>/dev/null || true)
[ -n "$tagged" ] || tagged=$(git tag -l "v$version" 2>/dev/null || true)
urls=0
while IFS= read -r url; do
  [ -n "$url" ] || continue
  urls=$((urls + 1))
  case "$url" in
    *"#v$version")
      [ -n "$tagged" ] || note "$url pins v$version, which is not a tag yet" ;;
    *'#'*)
      note "$url pins something other than v$version" ;;
    *)
      [ -z "$tagged" ] || note "$url tracks main, but v$version is tagged" ;;
  esac
done < <(grep -a -rho 'https://github.com/yingyeothon/csharplib\.git?path=[^ )`]*' \
  README.md docs packages/*/README.md)

# ---- 5. the pre-release prose agrees with the tag --------------------------
#
# Check 4 gates the URLs; without this a release could pin every one of them and
# still ship seven files saying no release has been tagged.
notice='No release has been tagged yet'
for file in README.md docs/getting-started.md docs/unity.md packages/*/README.md; do
  says=$(grep -a -c -F "$notice" "$file" 2>/dev/null || true)
  if [ -n "$tagged" ] && [ "${says:-0}" -gt 0 ]; then
    note "$file still says \"$notice\", but v$version is tagged"
  elif [ -z "$tagged" ] && [ "${says:-0}" -eq 0 ]; then
    note "$file carries an unpinned install URL but does not say why"
  fi
done

[ "$fail" -eq 0 ] && echo "docs: $links relative links resolve, $urls install URLs match v$version, no orphan page, every public type documented"
exit "$fail"
