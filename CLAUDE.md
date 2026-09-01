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
  - `README.md` — what the library is for, the package list and the dependency graph.
  - `docs/` — the consumer's integration guide, indexed by `docs/README.md`.
    `docs/api/*.md` is **generated**; fix the XML doc comment, never the file.
  - Each `packages/<name>/README.md` — that package's public API and the deliberate
    differences from its tslib original.
  - `rules/documentation.md` says which layer owns what. One fact, one owner.
- The normative wire spec for `gamebase-client` is the gateway's own README and
  `gateway/internal/lobby/protocol.go` in the `service` repository, not tslib.

## Required Rule Lookup

- Before non-trivial work, open `rules/index.md` and the relevant rule files.
- Keep this file short; put reusable lessons in `rules/`.
  (`AGENTS.md` is a symlink to this file — edit `CLAUDE.md` only.)
- After each completed task, fold any **durable** lesson into the relevant `rules/*.md`
  (and `rules/index.md` if files were added or removed). "Nothing durable" is an
  answer; say it.

## Essential Commands

```bash
dotnet build Yingyeothon.sln -c Release
dotnet format Yingyeothon.sln --verify-no-changes   # CI gate
dotnet test  Yingyeothon.sln -c Release
./scripts/check-coverage.sh                        # per-package floor, line 80 / branch 70
./scripts/validate-packages.sh
./scripts/check-docs.sh                            # links, index, API coverage
```

## Non-Negotiables

- Follow `CONVENTIONS.md` and `rules/architecture.md` for every public symbol.
- No reflection, no `UnityEngine` in a Runtime assembly, no ambient state — see
  `rules/unity.md`.
- Never interpolate untrusted data into a wire protocol, and never log a token, a frame
  body, a payload or a close reason; log ids, codes and counts — see
  `rules/security.md`.
- New or changed behavior ships with tests — see `rules/testing.md`.
- A changed public surface updates the package README **and** its XML doc comment; the
  generated reference is approved by rename — see `rules/documentation.md`.
- Verify against a real Unity project on Mono and IL2CPP before a release —
  see `rules/manual-verification.md`.
- **Work on `main`; commit then push.** No topic branches — `rules/workflow.md`.
- Follow the per-task completion ritual in `rules/workflow.md`, including its
  three-subagent adversarial review before every commit that is not covered by the
  narrow exemption in that file.
- A release is a git tag, the user cuts it, and the version lives in three places that
  must agree — `rules/release.md`.
