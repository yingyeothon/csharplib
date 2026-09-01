# Toolchain, Build & CI

## Order of operations

- `dotnet build` → `dotnet format --verify-no-changes` → `dotnet test` →
  `scripts/check-coverage.sh` → `scripts/validate-packages.sh`. CI runs exactly that
  order; `format` is a hard gate.
- `check-coverage.sh` runs each suite on its own and reads only the package that suite
  belongs to, so no package can hide behind the aggregate. The floor is line 80 /
  branch 70 (`COVERAGE_LINE_MIN` / `COVERAGE_BRANCH_MIN` override it). Merging the
  collector's reports by hand does not work: each report writes source paths relative
  to a different root, so the same file appears twice and the union is wrong.

## Project layout invariants

- `Directory.Build.props` sets `ArtifactsPath` + `UseArtifactsOutput` so build output
  lands in `artifacts/`. This is not a preference: a `bin/` or `obj/` inside a package
  folder becomes Unity project assets. `validate-packages.sh` checks it.
- `EnableDefaultCompileItems` is off, so every project declares its `Compile` items.
  That is what lets `Runtime/Unity/**` be excluded from the dotnet build while Unity
  still compiles it.
- `TreatWarningsAsErrors` is on. `CS1591` (missing XML comment) is the one suppressed
  warning; write the doc comment anyway on anything public.
- Test projects override `TargetFrameworks` to `net8.0` while referencing the
  netstandard output, so tests exercise the surface Unity will actually get.
- `tests/Yingyeothon.PublicApi.Tests` is deliberately outside `packages/`: both
  `validate-packages.sh` and `check-coverage.sh` walk `packages/*`, and Unity imports
  anything under a package folder. Being there is what keeps all three honest.
- Test package versions are pinned centrally in `Directory.Packages.props`.

## Gotchas already hit

- `EnableDefaultCompileItems` is off repository-wide, so a **new** project that forgets
  `<Compile Include="*.cs" />` builds successfully and contains nothing. `dotnet test`
  then says "No test is available", not "your project is empty".
- NUnit 3.14 does not discover a `[TestCaseSource]` whose source yields a `ValueTuple`;
  the fixture silently exposes no tests. Use explicit `[TestCase]` attributes.

- A `<see cref="X"/>` pointing at an overloaded method is `CS0419` under
  `TreatWarningsAsErrors`. Name the overload.
- `Uri.ToString()` **unescapes** for display; use `Uri.AbsoluteUri` for anything that
  goes on the wire, or a percent-escape becomes a raw space in the handshake URL.
- `UriBuilder` matches JS `new URL()` for these URLs, including normalising an empty
  path to `/` and not adding a port for `wss`. There is a test pinning the exact
  strings.
