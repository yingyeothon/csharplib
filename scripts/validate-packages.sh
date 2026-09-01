#!/usr/bin/env bash
#
# Structural checks Unity cares about but dotnet build does not.
set -euo pipefail
cd "$(dirname "$0")/.."

fail=0
note() { echo "FAIL: $*" >&2; fail=1; }

for package in packages/*/; do
  name=$(basename "$package")

  # Unity imports every file under a package folder, so a build output directory
  # left there becomes project assets.
  if [ -d "$package/bin" ] || [ -d "$package/obj" ]; then
    note "$name has bin/ or obj/; ArtifactsPath should keep them in artifacts/"
  fi

  [ -f "$package/package.json" ] || note "$name has no package.json"
  [ -f "$package/README.md" ] || note "$name has no README.md"

  # Not `grep -q`, and always `-a`: -q exits at the first match and SIGPIPEs its
  # upstream, which under `set -o pipefail` reads as false, and one NUL byte makes
  # grep call the rest binary and stop matching. Both fail open. See rules/security.md.
  matches=$(grep -a -c "\"name\": \"$name\"" "$package/package.json" 2>/dev/null || true)
  if [ "${matches:-0}" -eq 0 ]; then
    note "$name: package.json name does not match its folder"
  fi

  count=$(find "$package/Runtime" -name '*.asmdef' | wc -l)
  [ "$count" -eq 1 ] || note "$name: expected exactly one Runtime asmdef, found $count"
done

# The UPM manifest is what a Unity consumer resolves, Directory.Build.props is what
# the assembly carries, and a sibling pin is what another manifest demands. Any
# disagreement ships a package claiming two versions.
#
# Strip comments before reading the version and require exactly one match: `head -1`
# happily took a commented-out <Version> sitting above the live one, and the guard
# then passed while the assembly and the manifests disagreed.
props_versions=$(sed 's/<!--.*-->//g' Directory.Build.props \
  | sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p')
props_count=$(printf '%s\n' "$props_versions" | grep -a -c . || true)
if [ "$props_count" -ne 1 ]; then
  note "Directory.Build.props declares $props_count <Version> elements; expected exactly 1"
else
  props_version=$props_versions
  for package in packages/*/; do
    name=$(basename "$package")
    # A missing manifest is already reported above; reading it here would kill the
    # script under `set -e` before the accumulated failures are printed.
    [ -f "$package/package.json" ] || continue

    # `|| true` on every extraction: a manifest with no "version" key made grep exit 1
    # and aborted the whole script, skipping the reflection and ambient-state checks
    # below with no diagnostic at all.
    manifest_version=$(grep -a -m1 '"version"' "$package/package.json" 2>/dev/null \
      | sed 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/' || true)
    if [ -z "$manifest_version" ]; then
      note "$name: package.json declares no version"
    elif [ "$manifest_version" != "$props_version" ]; then
      note "$name: package.json version is $manifest_version, Directory.Build.props says $props_version"
    fi

    # The character class covers every legal UPM name segment, not just [a-z-]: a pin
    # on a name carrying a digit was invisible, which is exactly the pin a bump forgets.
    pins=$(grep -a -oE '"com\.yingyeothon\.[a-z0-9._-]+"[[:space:]]*:[[:space:]]*"[^"]*"' \
      "$package/package.json" 2>/dev/null \
      | sed 's/"\([^"]*\)"[[:space:]]*:[[:space:]]*"\([^"]*\)"/\1=\2/' || true)
    while IFS= read -r pin; do
      [ -n "$pin" ] || continue
      dependency=${pin%%=*}
      pinned=${pin#*=}
      if [ "$pinned" != "$props_version" ]; then
        note "$name: depends on $dependency $pinned, but the version is $props_version"
      fi
    done <<EOF
$pins
EOF
  done
fi

# Every asmdef needs a csc.rsp carrying -nullable:enable, and every csc.rsp must sit
# beside an asmdef and carry nothing else: it is compiler arguments injected into a
# consumer's build. rules/unity.md has why, and what a stray token does to it.
rsp_line='-nullable:enable'
while IFS= read -r asmdef; do
  [ -n "$asmdef" ] || continue
  rsp="$(dirname "$asmdef")/csc.rsp"
  if [ ! -f "$rsp" ]; then
    note "${asmdef#packages/}: no csc.rsp beside it; Unity would warn CS8632 on every annotation"
  fi
done <<EOF
$(find packages -name '*.asmdef' -not -path '*/Samples~/*' | sort)
EOF

while IFS= read -r rsp; do
  [ -n "$rsp" ] || continue
  dir=$(dirname "$rsp")
  if [ -z "$(find "$dir" -maxdepth 1 -name '*.asmdef')" ]; then
    note "${rsp#packages/}: a csc.rsp with no asmdef beside it applies to nothing"
  fi
  # Blank lines contribute no token, so they are allowed; anything else is not.
  content=$(tr -d '\r' < "$rsp" | grep -v '^[[:space:]]*$' || true)
  if [ "$content" != "$rsp_line" ]; then
    note "${rsp#packages/}: its only non-blank line must be '$rsp_line'"
  fi
done <<EOF
$(find packages -name 'csc.rsp' | sort)
EOF

# A sample lands in the consumer's Assets/, out of reach of any package csc.rsp, so a
# sample that uses a nullable annotation carries its own directive. rules/unity.md.
while IFS= read -r sample; do
  [ -n "$sample" ] || continue
  if grep -aqE '[A-Za-z0-9_>)]\? [A-Za-z_]' "$sample" \
     && ! grep -aq '^#nullable' "$sample"; then
    note "${sample#packages/}: uses a nullable annotation but has no '#nullable enable'"
  fi
done <<EOF
$(find packages -path '*/Samples~/*' -name '*.cs' | sort)
EOF

# Reflection-based serialization is what IL2CPP's managed stripper breaks, and it
# breaks it silently at runtime rather than at build time.
if grep -rnE '\bActivator\.CreateInstance|GetType\(\)\.GetPropert|GetType\(\)\.GetField|Reflection\.Emit' \
    packages/*/Runtime >/dev/null 2>&1; then
  note "runtime code uses reflection; see rules/unity.md"
fi

# Library code must not read ambient state.
if grep -rnE 'Environment\.GetEnvironmentVariable|DateTime\.(Now|UtcNow)|Console\.(Write|Read)' \
    packages/*/Runtime --include='*.cs' \
    | grep -v 'LogWriters.cs' >/dev/null 2>&1; then
  note "runtime code reaches for ambient state; see rules/architecture.md"
fi

[ "$fail" -eq 0 ] && echo "packages look importable"
exit "$fail"
