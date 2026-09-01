# Documentation

Three layers, and each owns something the others must not restate.

| Layer | Owns | Gated by |
| --- | --- | --- |
| `docs/` | The integration guide: console → token → connected → every feature | `scripts/check-docs.sh` |
| `packages/<name>/README.md` | That package's own reference and its deliberate divergences from tslib | `EveryPublicTypeIsNamedInThePackageReadme` |
| `docs/api/*.md` | Every public signature and its summary — **generated** | `TheGeneratedReferenceMatchesTheAssembly` |

The `service` repository owns the wire protocol, the auth endpoints and the console.
Link to them; do not re-derive them. When this repository and `gateway/README.md`
disagree, that document is right.

## `docs/` — the guide

`docs/README.md` is the index and every other page must be reachable from it. One fact,
one owner: a duplicated limit or table drifts, and the first drift found here was a
close-code table that existed in three shapes at once.

Current owners, so a new page does not take one over by accident:

- `getting-started.md` — the ordered path, and nothing that is not on it
- `console-and-options.md` — every option, its default, and the console setting behind it
- `authentication.md` — the token: how to get one, what it contains, when it dies
- `lobby.md` / `dungeon.md` — the two channel kinds, feature by feature
- `connection-lifecycle.md` — `Poll`, threading, states, reconnect, shutdown
- `errors.md` — every refusal code, close code and exception, and the caps the SDK does
  **not** check
- `unity.md` — install, samples, IL2CPP, WebGL, the editor console
- `troubleshooting.md` — symptom → the one check → the link. Not a second explanation

## Package READMEs

Same sections, in this order:

1. `# <Assembly>` and a one-paragraph purpose statement.
2. `## Install` — the git URL, plus the dependencies a git-URL package cannot resolve.
3. `## Usage` — a short runnable snippet.
4. Any section the package genuinely needs (`## Poll, or nothing happens`,
   `## Reconnect policy`, `## Wire details worth knowing`).
5. `## Public API` — the *actual* public surface of the assembly.
6. `## Differences from @yingyeothon/<name>` — every deliberate divergence, with the
   reason. This is the most valuable part of the file: it is what stops a future reader
   from "fixing" the port back into a bug.

## Keeping docs true

- **The `## Public API` listing is a gate.** `tests/Yingyeothon.PublicApi.Tests` fails
  when a public type is not named in its package README, and separately when the
  assembly's surface differs from its approved snapshot. Approving is renaming the
  `.received.txt`, and the README edit is the point of the pause.
- **`docs/api/*.md` is generated and approved the same way.** Never edit it by hand; fix
  the XML doc comment instead. That is the same edit that improves a consumer's
  IntelliSense, which is where a game developer actually reads an API — so write the
  `<summary>` even though `CS1591` is suppressed.
- **Samples are code, and are compiled.** `tests/Yingyeothon.Samples.Build` builds the
  engine-free half of every `Samples~` folder, so a sample the docs point at cannot rot.
  The `MonoBehaviour` wrappers sit next to it behind `#if UNITY_5_3_OR_NEWER`, exactly
  as `Runtime/Unity/**` does. A sample also needs its entry in the package's
  `package.json` `samples` array, or Unity shows no Samples tab at all.
- When a dependency edge changes, update both the package table and the mermaid graph in
  the root `README.md`.
- `CONVENTIONS.md` is canonical for API design. Point at it; do not duplicate it.
- Do not create tracking documents in the repo root. Session notes belong in `.claude/`
  (git-ignored).

## What a doc review actually catches

Three fresh-context adversarial reviews of the guide — one checking every claim against
the sources, one walking it as a newcomer, one cutting redundancy — found things no gate
can. Worth repeating for any substantial doc change:

- **Claims that are plausible and wrong.** "The peer map is empty after a reconnect"
  read well and was false: the gateway restores a retained position and sends a
  `snapshot` itself, so the advice to re-announce `Pos` could earn a `move_too_far`.
- **Silence asserted where the server is loud.** A bad `dir` was documented as silently
  dropped; the gateway answers a typed `error` frame, and the SDK surfaces it on
  `Refused`.
- **The exception a consumer actually hits.** The first exception table listed the
  interesting ones and omitted `InvalidOperationException` from a sender called while
  reconnecting — which is the common one.
- **Code samples that do not compile.** `UnityEngine.ILogger` collides with
  `Yingyeothon.Logger.ILogger`, and `ConsoleLogger` writes where the editor cannot see.
