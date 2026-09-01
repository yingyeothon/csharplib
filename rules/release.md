# Release & Versioning

**The tag is the release.** A Unity consumer installs
`…csharplib.git?path=/packages/<name>#<tag>`, so a tag is the whole distribution
mechanism: no registry, no publish step, no staging window, nothing to yank.

- **Never move or delete a tag.** A consumer who adds `#<tag>` after you moved it gets
  different code from the one who added it before — Unity records the resolved commit
  in the project's `packages-lock.json`, so the two teams disagree and neither can see
  why. Fix a mistake with a new tag. The one exception is a tag that failed to push:
  it exists only locally, so `git tag -d` and re-tag.
- Nothing publishes to NuGet, and **no CI job holds a publish credential** — that is
  part of what keeps this repo safe to leave public ([security.md](security.md)).
  `PackageId` on the four library `.csproj` files is preparation, not a pipeline;
  every test and sample project sets `IsPackable=false`. tslib's npm rules
  (OIDC, provenance, dist-tags, deprecation) have no equivalent here; do not port them.

## Versioning

- **One version across all four packages**, in **three places that must agree**:
  `Directory.Build.props` `<Version>` (what the assembly carries),
  each `packages/*/package.json` `"version"` (what a Unity consumer sees), and each
  manifest's `"dependencies"` pins on its sibling packages.
  `scripts/validate-packages.sh` fails on any disagreement. The third is the one a
  bump forgets, and it leaves a manifest demanding a version that no longer exists.
- `check-docs.sh` check 4 compares every install URL against the cut tag, asking the
  **remote** — a CI checkout has no tags unless the workflow sets `fetch-tags`. If you
  change either, change both.
- Stable semver only: a git URL has no dist-tag, so Package Manager cannot tell an
  `-rc` tag from a release. The current version is `0.1.0` and **no tag has been cut
  yet**,
  so every install URL in `README.md`, `docs/getting-started.md` and `docs/unity.md`
  currently tracks `main` and says so. **The first release must delete those "no
  release has been tagged yet" sentences and pin the URLs** — leaving them is
  shipping a lie.
- **While the version is `0.x`, a breaking change bumps the minor** (`0.1.0` →
  `0.2.0`) and everything else the patch. `1.0.0` is a deliberate statement that the
  surface is stable, never a side effect of breaking it; from there, normal semver.
  The approved snapshot under `tests/Yingyeothon.PublicApi.Tests/Approved/` is the
  evidence of whether a break happened — diff it against the previous tag first.

## Release flow

**The tag and the push are the user's; everything that can be undone is yours.** Do
steps 1–4, leave the bump **committed but unpushed**, print the two commands, and
stop. Never run `git tag` and never push a tag yourself, even when told to "just do
it".

The order below is not arbitrary. Step 4 pins the install URLs to a tag that does not
exist yet, and `check-docs.sh` refuses that — so the local tag has to exist before
anything is pushed, and the commit and the tag then go together or not at all.

1. **[agent]** `main` is green (the gate in [workflow.md](workflow.md)) and pushed.
2. **[agent]** Run [manual-verification.md](manual-verification.md) in full **on the current
   `main` tip** and record that sha in its *Last verified* row — the tag is compiled by
   Unity's compiler on Unity's Mono, and that run is the only thing that has ever
   caught the difference. The bump commit that follows may differ from the verified sha
   in exactly three ways — `Directory.Build.props`, `packages/*/package.json`, and the
   install URLs — none of which Unity compiles differently. Anything else landing
   between the run and the tag means running it again. Name the verified sha in the tag
   message.
3. **[agent]** Bump the version in all three: `Directory.Build.props` `<Version>`, every
   `packages/*/package.json` `"version"`, and every `com.yingyeothon.*` pin under
   those manifests' `"dependencies"`. `./scripts/validate-packages.sh` proves it.
4. **[agent]** Pin every install URL to `#vX.Y.Z` — there are fourteen, across `README.md`,
   `docs/getting-started.md`, `docs/unity.md` and all four
   `packages/*/README.md`. `./scripts/check-docs.sh` fails when a URL and the version
   disagree in either direction, so run it rather than counting by hand.

   **Retract every pre-release claim in the same commit**, or a released repo keeps
   asserting it has released nothing: the "no release has been tagged yet" sentences,
   `CONVENTIONS.md`'s *"Nothing has been released yet"* paragraph, and this file's own
   "no tag has been cut yet" clause. A rule file that states a fact a release
   invalidates has to be listed here, or it will not be found.
5. **[agent]** Commit the bump. **Do not push** — `check-docs.sh` fails while the URLs name a tag
   that does not exist, so `pre-push` would refuse it, correctly.
6. **[user]** Finish it, on that commit:

   ```bash
   git tag -a vX.Y.Z -m "<the release note>"     # local tag: check-docs.sh now passes
   git push --atomic origin main vX.Y.Z          # both land, or neither does
   ```

   The tag message is the only release note a consumer ever sees, so it says what
   changed on the public surface, what they must change on upgrade, and which Unity
   editors *Last verified* names. There is deliberately no `CHANGELOG.md`: the tag
   messages are the log, and `git tag -n99 -l` reads them.
7. **[after the user pushes — ask to be resumed]** Confirm the URL actually installs,
   from a **new** scratch Unity project. The tag is the product and nothing else tested
   it. Add it **by git URL** here, rather than by the folder copy
   [manual-verification.md](manual-verification.md) prescribes for pre-release runs: a
   git URL lands in `Library/PackageCache`, outside the repo, which is what proves the
   tag resolves. (Both files still forbid a `file:` dependency or a symlink, which
   would write `.meta` into this working tree.) Note that the package ships the
   **whole** `packages/<name>/` directory, `Tests/` included — the
   `UNITY_INCLUDE_TESTS` define constraint on the test asmdefs is what keeps those from
   compiling in a consumer's project, so check it survived.

## When a release half-lands

- **Bump commit pushed without the tag** (someone skipped `--atomic`). The docs now
  advertise a tag that resolves for nobody. Push the tag; this is a minutes-long
  window, not a rollback. If the commit itself was wrong, fix forward with a second
  bump and a new number.
- **Tag pushed at the wrong commit.** It is live and immutable. Bump to the next
  version, fix, tag again, and say in the new tag's message which one it supersedes.
  Never `git tag -f`, never `git push --delete`.
- **`--atomic` rejected because someone pushed to `main` first.** `git pull --rebase`,
  re-run the gate, delete the local tag and re-create it on the rebased commit — it was
  never pushed, so it may still move.
- **`pre-push` failed on the tag push.** Fix the failure and push again; never
  `--no-verify`.
