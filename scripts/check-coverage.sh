#!/usr/bin/env bash
#
# Runs the tests and gates each package on its OWN suite's coverage.
#
# The aggregate is not the interesting number: a package with no tests of its own
# still shows up covered because another package's suite walks through it. So each
# report is read only for the assembly the suite belongs to, and every package has
# to carry its own weight.
#
# Requires a prior `dotnet build -c Release`; it runs the tests with --no-build.
set -euo pipefail
cd "$(dirname "$0")/.."

line_min=${COVERAGE_LINE_MIN:-80}
branch_min=${COVERAGE_BRANCH_MIN:-70}

results=$(mktemp -d)
trap 'rm -rf "$results"' EXIT

fail=0
printf '%-32s %-16s %-16s\n' package line branch

for tests in packages/*/*.Tests.csproj; do
  # Yingyeothon.Codec.Tests.csproj -> Yingyeothon.Codec; the runtime project sits
  # next to its suite and shares the name, so no mapping table is needed.
  assembly=$(basename "$tests" .Tests.csproj)
  out="$results/$assembly"

  dotnet test "$tests" -c Release --no-build \
    --collect:"XPlat Code Coverage" --results-directory "$out" \
    >"$out.log" 2>&1 || { cat "$out.log"; echo "FAIL: $assembly tests failed" >&2; exit 1; }

  report=$(find "$out" -name coverage.cobertura.xml | head -1)
  if [ -z "$report" ]; then
    echo "FAIL: $assembly produced no coverage report" >&2
    fail=1
    continue
  fi

  # One <package> element per assembly the suite touched; only its own counts.
  attrs=$(grep -o "<package name=\"$assembly\"[^>]*>" "$report" | head -1)
  if [ -z "$attrs" ]; then
    echo "FAIL: $assembly is absent from its own coverage report" >&2
    fail=1
    continue
  fi

  rate() { echo "$attrs" | sed -n "s/.* $1=\"\([0-9.]*\)\".*/\1/p"; }
  line=$(awk -v r="$(rate line-rate)" 'BEGIN { printf "%.2f", r * 100 }')
  branch=$(awk -v r="$(rate branch-rate)" 'BEGIN { printf "%.2f", r * 100 }')

  status=ok
  awk -v v="$line" -v m="$line_min" 'BEGIN { exit !(v + 0 < m + 0) }' && status="below line $line_min%"
  awk -v v="$branch" -v m="$branch_min" 'BEGIN { exit !(v + 0 < m + 0) }' && status="below branch $branch_min%"

  printf '%-32s %-16s %-16s %s\n' "$assembly" "$line%" "$branch%" "$status"
  [ "$status" = ok ] || fail=1
done

if [ "$fail" -ne 0 ]; then
  echo "FAIL: a package is under the coverage floor (line $line_min%, branch $branch_min%)" >&2
  exit 1
fi

echo "coverage is over the floor in every package"
