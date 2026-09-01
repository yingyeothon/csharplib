# Rules Index

Compact, reusable lessons for agents working in this repository. Open only the files
relevant to the task at hand. `CLAUDE.md` (with `AGENTS.md` as its symlink) is the
entry point; `CONVENTIONS.md` at the repo root is the canonical API design document.

| File | Open it when |
| --- | --- |
| [architecture.md](architecture.md) | Adding or changing any public symbol, package layout, or dependency edge |
| [unity.md](unity.md) | Anything touching Unity, IL2CPP, WebGL, asmdefs, or the Poll contract |
| [workflow.md](workflow.md) | Starting or finishing any task |
| [testing.md](testing.md) | Writing tests or touching the fakes |
| [manual-verification.md](manual-verification.md) | Confirming a change works in a real consumer, after tests pass |
| [release.md](release.md) | Versioning, cutting a tag, or anything a consumer installs by version |
| [security.md](security.md) | Touching the wire protocol, auth, or logging |
| [tooling.md](tooling.md) | Build, format, test, or CI failures |
| [documentation.md](documentation.md) | Editing any README, `docs/` page, sample, or public API listing |

## Maintenance

- After each completed task, fold new durable lessons into the matching file.
- Add a row here whenever a rule file is created or removed.
- Keep rules in English, compact, and imperative. Point at canonical docs instead of
  copying them.
