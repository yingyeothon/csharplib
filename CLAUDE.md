# Repository Instructions

## Project Shape

- `csharplib` holds four C# packages ported from
  [tslib](https://github.com/yingyeothon/tslib) — the ones a **game client** can use.
  Each is a UPM package and a pair of `.csproj` files over the same sources.
- Everything targets `netstandard2.0` + `netstandard2.1`, C# 9, with no third-party
  dependencies and no engine references in any Runtime assembly, so Unity's Mono and
  IL2CPP backends both compile it.
- Source of truth documents:
  - `CONVENTIONS.md` — C# API design rules. Canonical; do not restate or contradict.
  - `README.md` — package list, dependency graph, what was not ported and why.
  - Each `packages/<name>/README.md` — that package's public API and the deliberate
    differences from its tslib original.
- The normative wire spec for `gamebase-client` is the gateway's own README and
  `gateway/internal/lobby/protocol.go` in the `service` repository, not tslib.

## Required Rule Lookup

- Before non-trivial work, open `rules/index.md` and the relevant rule files.
- Keep this file short; put reusable lessons in `rules/`.
  (`AGENTS.md` is a symlink to this file — edit `CLAUDE.md` only.)
- After each completed task, update the relevant `rules/*.md` (and `rules/index.md`
  if files were added or removed).

## Essential Commands

```bash
dotnet build Yingyeothon.sln -c Release
dotnet format Yingyeothon.sln --verify-no-changes   # CI gate
dotnet test  Yingyeothon.sln -c Release
./scripts/check-coverage.sh                        # per-package floor, line 80 / branch 70
./scripts/validate-packages.sh
```

## Non-Negotiables

- Follow `CONVENTIONS.md` and `rules/architecture.md` for every public symbol.
- No reflection, no `UnityEngine` in a Runtime assembly, no ambient state — see
  `rules/unity.md`.
- Never log a token, a frame body, a payload or a close reason; log ids, codes and
  counts — see `rules/security.md`.
- New or changed behavior ships with tests — see `rules/testing.md`.
- Verify against a real Unity project on Mono and IL2CPP before a release —
  see `rules/manual-verification.md`.
- Follow the per-task completion ritual in `rules/workflow.md`.
