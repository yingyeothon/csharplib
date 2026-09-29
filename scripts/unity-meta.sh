#!/usr/bin/env bash
#
# Every asset Unity imports from a package needs a committed .meta: a package installed
# by git URL is unpacked into Library/PackageCache, which is immutable, and there Unity
# generates nothing — an asset without one is ignored, silently, and the package
# contributes no assembly at all (rules/unity.md).
#
#   scripts/unity-meta.sh           check the working tree: what `git add` would commit
#                                   (tracked plus untracked, never ignored files)
#   scripts/unity-meta.sh --staged  check the index, which is what the commit will hold
#                                   (pre-commit runs this)
#   scripts/unity-meta.sh --write   create a .meta for each new asset, then check
#
# The asset list follows Unity's documented import rules: every file and folder below a
# package root, except a name starting with '.', a name ending in '~' (so Samples~ and
# everything under it), a file or folder named 'cvs', and a '*.tmp' file. The package
# root itself has no .meta. The matches are case-sensitive on purpose: treating a name
# as an asset costs at worst a .meta Unity never reads, while wrongly skipping one is
# the silent failure this script exists for.
#
# A .meta is exactly the two lines Unity 6 writes for a package asset, each ending in
# LF: `fileFormatVersion: 2` and `guid: <32 lower-case hex>` — sixty bytes. Anything
# else, an importer block included, is refused, so a .meta copied from a Unity project
# cannot carry settings into a consumer's build unseen. Symlinks are refused anywhere
# under packages/ for the same reason: git checks them out on Linux and macOS.
set -euo pipefail
cd "$(dirname "$0")/.."
# `[0-9a-f]` in a bash regex follows the locale's collation; pin it.
export LC_ALL=C

if [ "${BASH_VERSINFO[0]}" -lt 4 ]; then
  echo "unity-meta.sh needs bash 4 or later (macOS: brew install bash)" >&2
  exit 2
fi

mode=worktree
case "${1:-}" in
  "") ;;
  --staged) mode=staged ;;
  --write) mode=write ;;
  *) echo "usage: $0 [--staged | --write]" >&2; exit 2 ;;
esac

fail=0
note() { echo "FAIL: $*" >&2; fail=1; }

# Every path under packages/ this mode can see, as `<mode> TAB <path>` records,
# NUL-separated so no name is quoted or split. The mode is git's: 120000 is a symlink.
# The index for --staged; otherwise tracked plus untracked-but-not-ignored, less
# anything deleted from the working tree.
list_paths() {
  if [ "$mode" = staged ]; then
    git ls-files -z --stage -- packages/ \
      | while IFS= read -r -d '' record; do
          printf '%s\t%s\0' "${record%% *}" "${record#*$'\t'}"
        done
  else
    git ls-files -z --cached --others --exclude-standard -- packages/ \
      | while IFS= read -r -d '' path; do
          if [ -L "$path" ]; then
            printf '120000\t%s\0' "$path"
          elif [ -e "$path" ]; then
            printf '100644\t%s\0' "$path"
          fi
        done
  fi
}

# The .meta's bytes, from the index for --staged. `cat-file blob` applies no textconv
# or filter, so what is checked is what is committed.
raw_meta() {
  if [ "$mode" = staged ]; then
    git cat-file blob ":$1" 2>/dev/null || true
  else
    cat "$1"
  fi
}

# Unity hides a name that starts with '.', ends in '~', is 'cvs', or (a file) ends in
# '.tmp'. Returns 0 when every component below the package root is visible; the second
# argument is 'file' or 'dir'.
visible() {
  local name parts
  IFS=/ read -r -a parts <<<"${1#packages/*/}"
  for name in "${parts[@]}"; do
    case "$name" in
      .* | *~ | cvs) return 1 ;;
    esac
  done
  if [ "$2" = file ]; then
    case "$1" in *.tmp) return 1 ;; esac
  fi
  return 0
}

# Captured first: a producer failing inside a process substitution would hand the loop
# an empty list and the script a pass.
path_file=$(mktemp)
guid_file=$(mktemp)
trap 'rm -f "$path_file" "$guid_file"' EXIT
list_paths >"$path_file"

declare -A assets=()
declare -A metas=()
while IFS= read -r -d '' record; do
  entry_mode=${record%%$'\t'*}
  path=${record#*$'\t'}
  case "$path" in
    *$'\n'*) note "a path under packages/ contains a newline; rename it"; continue ;;
  esac
  if [ "$entry_mode" = 120000 ]; then
    note "$path is a symlink; nothing under packages/ may be one"
    continue
  fi
  [[ $path == packages/*/* ]] || { [[ $path == *.meta ]] && metas[$path]=1; continue; }
  if [[ $path == *.meta ]]; then
    metas[$path]=1
    continue
  fi
  visible "$path" file && assets[$path]=1
  # Git tracks files, not folders, so each folder is derived from the files in it —
  # including a hidden one, since Unity imports the folder that holds it.
  dir=${path%/*}
  while [[ $dir == packages/*/* ]]; do
    visible "$dir" dir && assets[$dir]=1
    dir=${dir%/*}
  done
done <"$path_file"

new_guid() {
  od -An -tx1 -N16 /dev/urandom | tr -d ' \n'
}

if [ "$mode" = write ]; then
  created=0
  for asset in "${!assets[@]}"; do
    if [ -z "${metas[$asset.meta]:-}" ] && [ ! -e "$asset.meta" ] && [ ! -L "$asset.meta" ]; then
      printf 'fileFormatVersion: 2\nguid: %s\n' "$(new_guid)" > "$asset.meta"
      metas[$asset.meta]=1
      echo "created $asset.meta"
      created=$((created + 1))
    fi
  done
  echo "$created .meta file(s) created"
fi

# Missing: an asset with no .meta beside it.
for asset in "${!assets[@]}"; do
  if [ -z "${metas[$asset.meta]:-}" ]; then
    if [ "$mode" = staged ] && [ -f "$asset.meta" ]; then
      note "$asset.meta exists but is not staged; git add it with its asset"
    else
      note "$asset has no .meta. A new asset: run scripts/unity-meta.sh --write. A renamed one: git mv its old .meta, so the guid follows it"
    fi
  fi
done

meta_shape=$'^fileFormatVersion: 2\nguid: [0-9a-f]{32}\n$'
for meta in "${!metas[@]}"; do
  target=${meta%.meta}
  # Orphaned: a .meta whose asset is gone, renamed, hidden from Unity, or itself a .meta.
  if [ -z "${assets[$target]:-}" ]; then
    note "$meta belongs to no asset Unity imports. A renamed asset: git mv this .meta to the new name. A deleted one: delete this too"
    continue
  fi
  # Malformed: not exactly the sixty bytes. Counting bytes first catches a NUL, which
  # a bash string would silently drop; the trailing 'x' keeps the final newline from
  # being stripped by the command substitution.
  bytes=$(raw_meta "$meta" | wc -c)
  content=$(raw_meta "$meta"; printf x)
  content=${content%x}
  if [ "$bytes" -ne 60 ] || ! [[ $content =~ $meta_shape ]]; then
    note "$meta is not exactly 'fileFormatVersion: 2' and 'guid: <32 lower-case hex>', LF-terminated. Keep its guid line and reduce it to those two lines"
    continue
  fi
  guid=${content#*guid: }
  printf '%s %s\n' "${guid%$'\n'}" "$meta" >> "$guid_file"
done

# Duplicated: two assets sharing a guid is how a copied .meta breaks references.
dupes=$(cut -d' ' -f1 "$guid_file" | sort | uniq -d)
if [ -n "$dupes" ]; then
  while IFS= read -r guid; do
    note "guid $guid is used by: $(grep -a -F "$guid " "$guid_file" | cut -d' ' -f2- | tr '\n' ' ')"
  done <<EOF
$dupes
EOF
fi

if [ "$fail" -eq 0 ]; then
  echo "unity meta ($mode): ${#assets[@]} assets, each with one .meta"
fi
exit "$fail"
