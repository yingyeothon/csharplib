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

# ---- 4. every install URL pins the version --------------------------------
#
# The tag is the release (rules/release.md): a URL pinned to a tag that is not cut
# is a 404 for every consumer, and a URL with no tag tracks main. Tags are never
# deleted, so since the first one an unpinned URL is refused, even for a package the
# last tag lacks. The one pin that fails honestly is v$version before its tag exists —
# a release's bump window, which the local tag ends (rules/release.md steps 5 and 6).
# Nothing else looks at the URLs: check 1 skips http(s) links on purpose.
version=$(sed 's/<!--.*-->//g' Directory.Build.props \
  | sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' | head -1)

# Ask the remote, not the local ref store: a CI checkout carries no tags unless the
# workflow sets fetch-tags, so reading `git tag -l` there would call every pinned URL
# unreleased and turn CI red for good the day the first tag lands. The local list is
# the offline fallback, and it is also what lets a release pin its URLs before the tag
# is pushed (rules/release.md).
tagged=$(git ls-remote --tags origin "refs/tags/v$version" 2>/dev/null || true)
[ -n "$tagged" ] || tagged=$(git tag -l "v$version" 2>/dev/null || true)

# Capture the list before reading it: grep's exit 1 is "no match", anything else is a
# search that failed, and that must not read as an empty list (rules/security.md).
# The class is what a package path and a tag are made of, so a quote, a bracket or a
# comma after the URL is not taken for part of it; a sentence's full stop is dropped.
found=$(grep -a -rho 'https://github.com/yingyeothon/csharplib\.git?path=[A-Za-z0-9._/#-]*' \
  README.md docs packages/*/README.md) || [ $? -eq 1 ] || note "cannot search for install URLs"
urls=0
while IFS= read -r url; do
  [ -n "$url" ] || continue
  url=${url%.}
  urls=$((urls + 1))
  case "$url" in
    *"#v$version")
      [ -n "$tagged" ] || note "$url pins v$version, which is not a tag yet" ;;
    *'#'*)
      note "$url pins something other than v$version" ;;
    *)
      note "$url has no tag, so it tracks main" ;;
  esac
done <<< "$found"

# ---- 5. no page still calls the SDK unreleased -----------------------------
#
# Check 4 gates the URLs; without this a release could pin every one of them and
# still ship a page saying no release has been tagged — false wherever it appears,
# since the first tag. Case-insensitive, so a lower-case quotation is caught too.
notice='No release has been tagged yet'
stale=$(grep -a -rli -F "$notice" README.md docs packages/*/README.md) \
  || [ $? -eq 1 ] || note "cannot search for the pre-release notice"
while IFS= read -r file; do
  [ -n "$file" ] || continue
  note "$file still says \"$notice\", but a release has been tagged"
done <<< "$stale"

[ "$fail" -eq 0 ] && echo "docs: $links relative links resolve, $urls install URLs match v$version, no orphan page, every public type documented"
exit "$fail"
