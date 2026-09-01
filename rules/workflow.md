# Workflow

## Working agreement

- **Work on `main` and push.** Upstream is `git@github.com:yingyeothon/csharplib.git`
  and `main` tracks `origin/main`. Finish a task with `git commit` then `git push`; do
  not open a branch "to be safe", because `pre-push` runs the whole gate before
  anything leaves the machine. (CI also triggers on `pull_request`, so a PR works if
  one is ever wanted — nothing here has used one.)
- A push rejected as non-fast-forward is `git pull --rebase`, re-run the gate, push.
  **Never `--force`** — `security.md` reserves a history rewrite for a leak. If you
  must stop mid-task, leave the tree uncommitted and say what is unfinished rather
  than pushing a partial change to a public `main`.
- Talk to the user in Korean; write all repository content — code, comments, READMEs,
  `docs/`, rules, commit messages — in English. The audience for `docs/` is a game
  client developer who may not read Korean, and one language means one owner per
  sentence.
- Commit messages are English, imperative, one coherent purpose per commit.
- Stage intentionally, by path. Never `git add .` or `git add -A`: `artifacts/` and
  `.claude/` are git-ignored and safe, but a scratch console app, a Unity project or a
  file you did not mean to publish is not — and this repo is public. Run
  `git status --porcelain` first, then name each path.
- Work may be delegated to subagents, but **a review subagent reports; it never
  writes.** Tell each one explicitly: read only — no `Edit`, no `Write`, no
  `git add/commit/push/checkout/restore/stash`, no `dotnet build` or `dotnet test`.
  Only the main session builds, runs the tests, approves a `.received` file, edits the
  tree or touches git. `Directory.Build.props` points every project at one
  `artifacts/` tree, so two agents building at once collide in it, and three reviewers
  editing an uncommitted tree in parallel is how unstaged work disappears.
- **Do not edit any other repository under `~/git/yyt.life/`.** `service` and `tslib`
  are read here — the gateway's Go source is the normative wire spec — and that access
  is read-only. Changing them is the user's call.
- The repo is **public**. Never `--no-verify`, and never `git reset --hard` with
  uncommitted work in the tree — it takes the working tree with it. See
  [security.md](security.md).
- **`git checkout <file>` discards that file's unstaged edits**, silently and with no
  reflog to recover from. It is the wrong way to undo a scratch experiment on a file
  you have also been editing — copy the file aside first, or make the experiment in a
  copy of the repo under `/tmp`. Paid for this session: testing a guard against a
  deliberately broken `README.md` reverted an unrelated fix in the same file.
- Releases are git tags and are the **user's** call — see [release.md](release.md).
  **An unpushed commit on `main` that fails `check-docs.sh` on the install URLs is a
  pending release, not a broken tree.** Confirm with `git log -1 --stat`; if it only
  bumps the version and pins URLs, print `release.md`'s two commands and stop. Do not
  unpin the URLs and do not reset.
- `.claude/handover.md` is a **session note, not a rule**: where it and `rules/`
  disagree, `rules/` wins. Check its premise against `git log` and `git branch -a`
  before executing it; if the premise is gone, say so and delete it.

## Per-task completion ritual

1. Make the change testable, then cover the new or changed behaviour
   ([testing.md](testing.md)). Prose and rule files have no behaviour to cover — say
   so rather than inventing a test that cannot fail.
2. Verify beyond the unit tests, at the **highest** level the change reaches — the
   levels are ordered, so a Runtime source change reaches the top and takes both of
   the first two ([manual-verification.md](manual-verification.md)):
   1. runtime or wire behaviour → the live gateway;
   2. anything Unity compiles — Runtime sources, `Samples~`, `package.json`,
      `link.xml`, an asmdef → the Unity scratch project;
   3. `docs/`, `rules/` and scripts → the green gate, **and if the change altered a
      guard**, watching that guard refuse something ([security.md](security.md)).

   Name every level you ran and every one you skipped, with the reason. "Not
   applicable" is an answer; silence is not.
3. **Run three fresh-context subagents to review the change adversarially, in
   parallel, before committing — mandatory, not a judgement call.**
   - **Exempt, and only this: a change to text no tool reads and no reader sees** —
     an implementation comment (`//`, `/* */`) that is *not* a `///` XML doc comment,
     whitespace `dotnet format` would produce, or a git-ignored file that is never
     committed. Everything else is reviewed, including a `///` typo (it is regenerated
     into `docs/api/*.md`), a new or changed test, and any version bump. Reverting a
     commit is **not** exempt. If you are arguing about whether a change qualifies, it
     does not. Say which path you took; a skipped review that is not declared is the
     failure this rule exists to prevent.
   - Use a genuinely fresh context — a `general-purpose` agent, **never a fork**,
     which inherits this session and would review its own reasoning.
   - Hand each the **same explicit file list**: `git status --porcelain` *and*
     `git diff`, plus `git status --porcelain --ignored=matching -- .claude` when a
     session note is in play. A bare diff hides every untracked file, which is exactly
     what a new rule file or a new sample is — this rule exists because a new
     `rules/release.md` was nearly reviewed by nobody.
   - Reviewers verify against the checked-in sources and the approved snapshots as
     they stand; they do not build. A claim that only a test run can settle comes back
     as unverified, and the main session runs it.
   - Tell each to assume the work is wrong: a reviewer asked to "check this over"
     reports that it looks fine.

   Two angles are fixed — **correctness against the sources** (every claim, signature
   and constant cited against the C# sources, the approved snapshots, the tests, and
   the gateway's Go source for anything on the wire, plus a list of what could not be
   verified at all) and **the consumer's experience** (walk it as the game developer:
   does it compile, is anything missing, what will they misread). The third is chosen
   for the change, most-expensive-defect first: *security* if it touches the wire, the
   token or a log line ([security.md](security.md)); otherwise *concurrency and
   ordering* if it touches `Poll`, reconnect or settlement, which is where every
   expensive defect here has lived; otherwise *editing and structure*. When two apply,
   take security and say which one you displaced.

   [documentation.md](documentation.md) records what one such pass caught.
4. Fold durable lessons into `rules/*.md`; update `rules/index.md` if files changed.
   Then send **the rule diff only** to a fourth fresh reviewer with one question:
   *can an agent with no memory of this session follow this exactly, and what will it
   do when it cannot?* Rule text written in step 4 was not in the diff step 3 read, so
   without this it is the least-reviewed text in the change — and every future session
   obeys it.
5. Apply the feedback from all four. A finding you disagree with is answered by
   checking the source, not by weighing the reviewer's confidence. Re-run a reviewer
   only when this step changed the substance of what it read; editing rule text in
   answer to the rule reviewer does **not** re-trigger step 4, or you will not
   terminate — record the disagreement in the report instead. Name what the reviewers
   found and what you rejected, with the reason.
6. Run the green gate below, then commit and push to `main`. **The one exception is a
   release**: [release.md](release.md) step 4 pins the install URLs to a tag that does
   not exist yet, so `check-docs.sh` and therefore `pre-push` refuse until the user's
   local tag exists. Commit, do not push, and hand over.

## Green gate

```bash
dotnet build Yingyeothon.sln -c Release
dotnet format Yingyeothon.sln --verify-no-changes
dotnet test  Yingyeothon.sln -c Release
./scripts/check-coverage.sh
./scripts/validate-packages.sh
./scripts/check-docs.sh
```

`pre-push` runs all six, so this is a way to see the failure early rather than a step
anyone can forget, and it is what makes pushing straight to a public `main` with no PR
safe.

**A gate that was already red before your change is a separate task.** Confirm with
`git stash && <the failing command>`. Do not fold the repair into your commit — that
breaks "one coherent purpose per commit" — and do not push over it: report it, and
either fix it first as its own commit or stop. `SKIP_CI_GATE=1 git push` turns that gate off — the secret scans still run
([tooling.md](tooling.md)). **Do not use it.** It is for a machine that cannot run the
.NET SDK, not for a red tree and not for a slow one; on anything under `packages/`,
`tests/` or `scripts/` it publishes code no gate has seen. If you use it at all, say
so in the same message.

## Scope decisions already made

- Only the four client-usable tslib packages are ported. The rest are server
  libraries; the root README says which and why. Do not add one without a reason that
  survives "can this run on a phone?".
- The gateway wire protocol is owned by the `service` repository. When it changes,
  this SDK follows it — never the other way round.
