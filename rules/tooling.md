# Toolchain, Build & CI

## Order of operations

- `dotnet build` → `dotnet format --verify-no-changes` → `dotnet test` →
  `scripts/validate-packages.sh`. CI runs exactly that order; `format` is a hard gate.

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
- Test package versions are pinned centrally in `Directory.Packages.props`.

## Gotchas already hit

- A `<see cref="X"/>` pointing at an overloaded method is `CS0419` under
  `TreatWarningsAsErrors`. Name the overload.
- `Uri.ToString()` **unescapes** for display; use `Uri.AbsoluteUri` for anything that
  goes on the wire, or a percent-escape becomes a raw space in the handshake URL.
- `UriBuilder` matches JS `new URL()` for these URLs, including normalising an empty
  path to `/` and not adding a port for `wss`. There is a test pinning the exact
  strings.
