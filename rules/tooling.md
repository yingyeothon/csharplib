# Toolchain, Build & CI

## Order of operations

- `dotnet build` → `dotnet format --verify-no-changes` → `dotnet test` →
  `scripts/check-coverage.sh` → `scripts/validate-packages.sh` →
  `scripts/check-docs.sh`. CI runs exactly that order in its `ci` job; `format` is a
  hard gate. `pre-push` runs the same six, so a push cannot land red (`SKIP_CI_GATE=1 git push` skips only the build gate, never the
  secret checks).
- CI has two more jobs because the repo is public: `secrets-scan` (gitleaks over the
  full history) and `tracked-paths` (nothing of a forbidden shape is tracked). They
  duplicate the local hooks on purpose — a contributor whose hooks were never
  installed is exactly the case the hooks cannot cover. See
  [security.md](security.md).
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
- `Directory.Build.targets` installs the git hooks on the first build of a working
  tree, stamped by `artifacts/.git-hooks-installed`. It is inert in CI (`$(CI)`),
  without a `.git` directory, and under `YYT_SKIP_HOOK_INSTALL=1`, and it never fails
  a build — the hooks matter when committing, not when compiling.
- `TreatWarningsAsErrors` is on. `CS1591` (missing XML comment) is the one suppressed
  warning; write the doc comment anyway on anything public.
- Test projects override `TargetFrameworks` to `net8.0` while referencing the
  netstandard output, so tests exercise the surface Unity will actually get.
- `tests/Yingyeothon.PublicApi.Tests` is deliberately outside `packages/`: both
  `validate-packages.sh` and `check-coverage.sh` walk `packages/*`, and Unity imports
  anything under a package folder. Being there is what keeps all three honest.
- `tests/Yingyeothon.Samples.Build` is there for the same reason. It compiles the
  engine-free half of every `Samples~` folder and carries no tests, so it fails the
  build when a sample stops matching the API without disturbing the coverage gate.
- `check-docs.sh` matches a type heading with `grep -F -x`. A prefix match let
  `## interface IEventBroker` satisfy a lookup for a type that had been renamed to
  `IEventBrokerRenamed` — found by testing the guard against a change it should have
  refused, which is the only way a guard gets tested.
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

## The `unity` CLI

`~/.local/bin/unity` (v1.0.0-beta.5) drives the editors. It is worth using — `unity
test` and `unity build` remove a lot of scaffolding — but it has traps that cost real
time:

- **Module installs can silently do nothing, and then claim success.** Both
  `unity install <v> -m …` and `unity install-modules` exited 0 and reported
  `Installed`, while `linux-il2cpp` had not been downloaded at all and `webgl` had
  landed as an `Editor/` folder and nothing else. All they had written was
  `"selected": true` (and `"isInstalled": true` on the 2021.3 schema) into
  `~/Unity/Hub/Editor/<version>/modules.json`. The failures come much later and name
  the wrong thing — "Currently selected scripting backend (IL2CPP) is not installed",
  and a WebGL build that gets all the way to *"Build target 'WebGL' not supported"* in
  postprocess. `--reinstall -f` does not repair it, because the Hub reads the same
  file and agrees the module is there. **The repair:** set that module's `selected`
  *and* `isInstalled` back to `false` in `modules.json`, then install through the Hub —
  `unityhub -- --headless install-modules --version <v> --module <id> --childModules`.
  **Confirm every module on the filesystem**, never from a status column:
  `PlaybackEngines/LinuxStandaloneSupport/Variations/` must contain `*_il2cpp`
  entries, and `PlaybackEngines/WebGLSupport/` must contain `BuildTools` and
  `Variations`, not just `Editor`.
- `unity editors`' `Platforms` column shows *selected* modules, not installed ones.
- `unity install <v> --list-components` and `unity modules list <v>` both refuse a
  version that is not installed, so plan the module set after the base install.
- `--report-format both` is documented in `--help` and rejected at runtime; pass
  `nunit` or `junit`. `unity test` has no `--no-tail`. `unity releases` rejects the
  global `--no-pager`; use `UNITY_NO_PAGER=1`.
- `unity releases` needs `--limit` raised (default 20) to reach anything old, and its
  output pages by default — set `UNITY_NO_PAGER=1 UNITY_NON_INTERACTIVE=1` for every
  scripted call.
