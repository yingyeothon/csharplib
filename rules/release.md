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
  `PackageId` on the library `.csproj` files is preparation, not a pipeline;
  every test and sample project sets `IsPackable=false`. tslib's npm rules
  (OIDC, provenance, dist-tags, deprecation) have no equivalent here; do not port them.

## Versioning

- **One version across every package**, in **three places that must agree**:
  `Directory.Build.props` `<Version>` (what the assembly carries),
  each `packages/*/package.json` `"version"` (what a Unity consumer sees), and each
  manifest's `"dependencies"` pins on its sibling packages.
  `scripts/validate-packages.sh` fails on any disagreement. The third is the one a
  bump forgets, and it leaves a manifest demanding a version that no longer exists.
- `check-docs.sh` check 4 compares every install URL against the cut tag, asking the
  **remote** — a CI checkout has no tags unless the workflow sets `fetch-tags`. If you
  change either, change both.
- Stable semver only: a git URL has no dist-tag, so Package Manager cannot tell an
  `-rc` tag from a release. The version lives in the three places above and in every
  install URL's `#v…` fragment, and **nowhere in prose** — check 4 guards the URLs and
  `validate-packages.sh` the rest, and nothing guards a sentence. Each release moves
  every pin to its own tag.
- **While the version is `0.x`, a breaking change bumps the minor** (`0.1.0` →
  `0.2.0`) and everything else the patch. `1.0.0` is a deliberate statement that the
  surface is stable, never a side effect of breaking it; from there, normal semver.
  The approved snapshot under `tests/Yingyeothon.PublicApi.Tests/Approved/` is the
  evidence of a source break — diff it against the previous tag first — but not the
  only kind: a behaviour change that stops working consumer code (a call that now
  deadlocks when blocked on, work that moves to another thread) breaks too, and the
  tag message says why. v0.2.0 is the worked case: no snapshot change, and a minor.

## Release flow

**The tag and the push are the user's; everything that can be undone is yours.** Do
steps 1–4, leave the bump **committed but unpushed**, print the two commands, and
stop. Never run `git tag` and never push a tag yourself, even when told to "just do
it".

The order below is not arbitrary. Step 4 pins the install URLs to a tag that does not
exist yet, and `check-docs.sh` refuses that — so the local tag has to exist before
anything is pushed, and the commit and the tag then go together or not at all.

1. **[agent]** `main` is green (the gate in [workflow.md](workflow.md)) and pushed.
2. **[agent]** Step 2 is already satisfied, with no new run, when
   [manual-verification.md](manual-verification.md)'s *Last verified* names a sha S and
   `git diff --name-only S HEAD` lists only files under `rules/`. Otherwise run that file
   in full **on the current `main` tip**, record it as its *Last verified* intro says, and
   push that record (step 1). The tag is compiled by Unity's compiler on Unity's Mono, and
   that run is the only thing that has ever caught the difference. Between S and the tag
   may land only commits that touch nothing outside `rules/` (the one recording the run
   among them) and the bump — `Directory.Build.props`, `packages/*/package.json`, and the
   files step 4 edits — none of which Unity compiles differently. Check with
   `git diff --name-only S HEAD` before step 3 and again before printing step 6's
   commands; anything else means running it again. Name S in the tag message.
3. **[agent]** Bump the version in all three: `Directory.Build.props` `<Version>`, every
   `packages/*/package.json` `"version"`, and every `com.yingyeothon.*` pin under
   those manifests' `"dependencies"`. `./scripts/validate-packages.sh` proves it.
4. **[agent]** Pin every install URL to `#vX.Y.Z` — in every file
   `grep -rlF 'csharplib.git?path=' README.md docs packages/*/README.md` lists; the summary line of a green `check-docs.sh` prints the
   count, and while the URLs pin an uncut tag it prints nothing. `./scripts/check-docs.sh` fails when a URL and the version
   disagree in either direction, so run it rather than counting by hand.

   **No sentence names the version** (Versioning, above), so the URLs are all step 4
   moves; grep `v[0-9]+\.[0-9]+\.[0-9]+` across `README.md`, `docs/`, every
   `packages/*/README.md` and `rules/` to confirm
   none crept in — other tools' versions (the `yyt` CLI, `unity`, tslib) and this
   file's own examples and history are expected matches. The first release also retracted every pre-release claim — the "no release has
   been tagged yet" sentences and `CONVENTIONS.md`'s *"Nothing has been released yet"*
   paragraph. A rule file that states a fact a release invalidates has to be listed
   here, or it will not be found.
5. **[agent]** Commit the bump. **Do not push** — `check-docs.sh` fails while the URLs name a
   tag that does not exist, so `pre-push` would refuse it, correctly. In that window a
   clean bump fails only with check 4's *"pins vX.Y.Z, which is not a tag yet"*, once per
   URL, and that clears once the local tag exists. Any other failure of checks 4 and 5
   is something step 4 missed — *"pins something other than vX.Y.Z"* (a URL still on
   the previous tag), *"has no tag"* (an unpinned URL) or *"still says"* (the
   pre-release notice). Fix it and amend it into the bump commit before handing over, so
   the release stays one commit; never unpin a URL to make the gate pass.
6. **[user]** Finish it, on that commit:

   ```bash
   git tag -a vX.Y.Z -F <the release note file>  # local tag: check-docs.sh now passes
   git push --atomic origin main vX.Y.Z          # both land, or neither does
   ```

   The tag message is the only release note a consumer ever sees, so it says what
   changed on the public surface, what they must change on upgrade, and which Unity
   editors *Last verified* names. The agent writes it to a file under `.claude/`
   (git-ignored) and prints that path: a note with quotes does not survive `-m "…"`.
   There is deliberately no `CHANGELOG.md`: the tag messages are the log, and
   `git tag -n99 -l` reads them.
7. **[after the user pushes — ask to be resumed]** Confirm the URL actually installs,
   from a **new** scratch Unity project. The tag is the product and nothing else tested
   it. Add it **by git URL** here, rather than by the folder copy
   [manual-verification.md](manual-verification.md) prescribes for pre-release runs: a
   git URL lands in `Library/PackageCache`, outside the repo, which is what proves the
   tag resolves. (Both files still forbid a `file:` dependency or a symlink, which
   would let Unity write into this working tree.) Note that the package ships the
   **whole** `packages/<name>/` directory, `Tests/` included — the
   `UNITY_INCLUDE_TESTS` define constraint on the test asmdefs is what keeps those from
   compiling in a consumer's project, so check it survived. When a previous tag exists,
   also walk the upgrade `docs/unity.md` § Installing describes: a new project with
   every package at the previous tag, then every fragment moved to the new one in one
   edit of `manifest.json` — and drop that item from *Not covered* once it has run.

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
- **`pre-push` failed on the tag push.** `--atomic`, so neither landed. A dirty working
  tree or a missing tool is fixed in place and the same push retried. Anything else is in
  the bump commit — step 1 pushed everything before it through the same hook — so
  **[agent]** fixes it and amends it into that commit, which must still pass step 2's
  `git diff --name-only S HEAD`, pushes nothing, and hands back; **[user]** then runs
  `git tag -d vX.Y.Z` and step 6 again. The tag was never pushed, so it may still move,
  and pushing the old one would publish the unfixed commit. Never `--no-verify`.
