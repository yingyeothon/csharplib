# Documentation

## Package README structure

Every `packages/<name>/README.md` uses the same sections, in this order:

1. `# <Assembly>` and a one-paragraph purpose statement.
2. `## Install` — the git URL, plus the dependencies a git-URL package cannot resolve.
3. `## Usage` — a short runnable C# snippet; for `gamebase-client`, the Unity
   `Update()` snippet too.
4. Any section the package genuinely needs (`## Poll, or nothing happens`,
   `## Reconnect policy`, `## Wire details worth knowing`).
5. `## Public API` — the _actual_ public surface of the assembly.
6. `## Differences from @yingyeothon/<name>` — every deliberate divergence, with the
   reason. This replaces tslib's "Migrating from the legacy package" section and is
   the most valuable part of the file: it is what stops a future reader from
   "fixing" the port back into a bug.

## Keeping docs true

- When the public API changes, update that package's `## Public API` in the same
  commit. Drifted API listings were a real defect class in tslib.
- When a dependency edge changes, update both the package table and the mermaid graph
  in the root `README.md`.
- `CONVENTIONS.md` is canonical for API design. Point at it; do not duplicate it.
- Do not create tracking documents in the repo root. Session notes belong in
  `.claude/` (git-ignored).
