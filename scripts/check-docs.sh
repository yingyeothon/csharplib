#!/usr/bin/env bash
#
# Keeps the documentation honest about itself.
#
# The generated API reference is gated by tests/Yingyeothon.PublicApi.Tests and the
# per-package `## Public API` listings by the same suite. What neither can see is the
# guide: a link that rots, a page nothing reaches, a public type the guide never
# mentions, or an install URL no release serves. A promise the reader cannot follow is
# worse than a missing document, so these are checks and not conventions.
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
# A cut tag must also contain the package: a URL for a package added since is a 404.
# Check 1 skips http(s) links on purpose; check 6 counts the URLs per package.
version=$(sed 's/<!--.*-->//g' Directory.Build.props \
  | sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' | head -1)

# Ask the remote whether the tag is cut, not the local ref store: the local list is the
# offline fallback, and it is also what lets a release pin its URLs before the tag is
# pushed (rules/release.md). Which packages it ships is read from the tag's tree, which
# has to be here — so a CI checkout needs fetch-tags, and a clone that has not fetched a
# tag someone else pushed is told to.
tagged=$(git ls-remote --tags origin "refs/tags/v$version" 2>/dev/null || true)
[ -n "$tagged" ] || tagged=$(git tag -l "v$version" 2>/dev/null || true)

# Whether the cut tag ships packages/$1. Only called once the tag's tree is readable,
# so an empty answer is "not in the tag", never "could not look".
ships() { [ -n "$(git ls-tree -d --name-only "v$version" -- "packages/$1")" ]; }
tree_ok=0
if [ -n "$tagged" ]; then
  if git rev-parse -q --verify "v$version^{tree}" > /dev/null; then
    tree_ok=1
  else
    note "v$version is a tag on origin but not here, so its packages cannot be read: git fetch --tags"
  fi
fi

# Capture the list before reading it: grep's exit 1 is "no match", anything else is a
# search that failed, and that must not read as an empty list (rules/security.md).
# The class is what a package path and a tag are made of, so a quote, a bracket or a
# comma after the URL is not taken for part of it; a sentence's full stop is dropped.
# Any spelling Unity would accept is matched — another scheme, `www.`, the org's case,
# no `.git` — and refused unless it is the canonical https URL: the others ask the
# consumer for a key, or for nothing TLS would have checked, and check 6 counts only
# the canonical form. The name class is every legal UPM name segment, as in
# validate-packages.sh.
canonical='https://github.com/yingyeothon/csharplib.git?path='
found=$(grep -a -rhoiE '[a-z+]*(://|@)(www\.)?github\.com[:/]yingyeothon/csharplib(\.git)?\?path=[A-Za-z0-9._/#-]*' \
  README.md docs packages/*/README.md) || [ $? -eq 1 ] || note "cannot search for install URLs"
urls=0
while IFS= read -r url; do
  [ -n "$url" ] || continue
  url=${url%.}
  urls=$((urls + 1))
  case "$url" in "$canonical"*) ;; *) note "$url is not the https URL, which needs no credentials" ;; esac
  if [[ "$url" =~ \?path=/packages/(com\.yingyeothon\.[a-z0-9._-]+)(#|$) ]]; then
    name=${BASH_REMATCH[1]}
  else
    note "$url does not name one package as ?path=/packages/com.yingyeothon.<name>"
    continue
  fi
  case "$url" in
    *"#v$version")
      if [ -z "$tagged" ]; then
        note "$url pins v$version, which is not a tag yet"
      elif [ "$tree_ok" -eq 1 ]; then
        ships "$name" || note "$url names a package v$version does not ship, so it is a 404"
      fi ;;
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

# ---- 6. an install URL exactly where a release ships the package ------------
#
# rules/documentation.md wants each package's URL in its README's `## Install`, the
# root README's list and docs/unity.md § Installing, and check 4 refuses one a tag
# cannot serve. So a package added between releases has none of the three, and its
# README carries the fixed sentence below instead, until the release that ships it
# replaces the sentence with the URL (rules/release.md step 4). In the bump window no
# tag answers yet and every package is about to ship, so every package needs all three.
pending='Not in a release yet: the release that ships this package adds its install URL here.'
for dir in packages/com.yingyeothon.*/; do
  name=${dir%/}
  name=${name#packages/}
  readme="packages/$name/README.md"
  if [ -z "$tagged" ] || { [ "$tree_ok" -eq 1 ] && ships "$name"; }; then
    shipped=1
  elif [ "$tree_ok" -eq 1 ]; then
    shipped=0
  else
    continue # check 4 already said the tag cannot be read
  fi
  for file in "$readme" README.md docs/unity.md; do
    own=$(grep -a -c -F "${canonical}/packages/$name#" "$file") \
      || [ $? -eq 1 ] || { note "cannot search $file"; continue; }
    if [ "$shipped" -eq 1 ] && [ "$own" -eq 0 ]; then
      note "$file has no install URL for $name, but the release ships it"
    elif [ "$shipped" -eq 0 ] && [ "$own" -gt 0 ]; then
      note "$file has an install URL for $name, but v$version does not ship it"
    fi
  done
  # Exact for the line a README must hold; any trace of it for the one it must drop.
  said=$(grep -a -c -F -x "$pending" "$readme") \
    || [ $? -eq 1 ] || { note "cannot search $readme"; continue; }
  left=$(grep -a -c -F "${pending%%:*}" "$readme") \
    || [ $? -eq 1 ] || { note "cannot search $readme"; continue; }
  if [ "$shipped" -eq 1 ] && [ "$left" -gt 0 ]; then
    note "$readme still says it is not in a release yet, but the release ships it"
  elif [ "$shipped" -eq 0 ] && [ "$said" -eq 0 ]; then
    note "$readme has no URL yet, so its ## Install needs the line: $pending"
  fi
done

[ "$fail" -eq 0 ] && echo "docs: $links relative links resolve, $urls install URLs match v$version, no orphan page, every public type documented, every package URL where its release is"
exit "$fail"
