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

  if ! grep -q "\"name\": \"$name\"" "$package/package.json"; then
    note "$name: package.json name does not match its folder"
  fi

  count=$(find "$package/Runtime" -name '*.asmdef' | wc -l)
  [ "$count" -eq 1 ] || note "$name: expected exactly one Runtime asmdef, found $count"
done

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
