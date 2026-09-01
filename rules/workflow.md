# Workflow

## Working agreement

- Talk to the user in Korean; write all repository content — code, comments, READMEs,
  `docs/`, rules, commit messages — in English. The audience for `docs/` is a game
  client developer who may not read Korean, and one language means one owner per
  sentence.
- Commit messages are English, imperative, one coherent purpose per commit.
- Stage intentionally. Never `git add .` while `artifacts/` or `.claude/` exist (they
  are git-ignored — keep them that way).
- The repo is **public**. Never `--no-verify`, and never `git reset --hard` with
  uncommitted work in the tree — it takes the working tree with it. See
  [security.md](security.md).

## Per-task completion ritual

1. Make the change testable, then cover the new or changed behaviour
   ([testing.md](testing.md)).
2. Verify beyond the unit tests ([manual-verification.md](manual-verification.md)).
3. Run fresh-context adversarial reviews of the change. The defects that matter here
   — the unanswered close handshake, the cache published after the load, the
   settlement racing the hello handler — were all ordering bugs that tests found only
   once someone thought to ask the question.
4. Apply the feedback.
5. Fold durable lessons into `rules/*.md`; update `rules/index.md` if files changed.
6. Run the green gate below, then commit.

## Green gate

```bash
dotnet build Yingyeothon.sln -c Release
dotnet format Yingyeothon.sln --verify-no-changes
dotnet test  Yingyeothon.sln -c Release
./scripts/check-coverage.sh
./scripts/validate-packages.sh
./scripts/check-docs.sh
```

`pre-push` runs all six, so this is a way to see the failure early rather than a
step anyone can forget.

## Scope decisions already made

- Only the four client-usable tslib packages are ported. The rest are server
  libraries; the root README says which and why. Do not add one without a reason that
  survives "can this run on a phone?".
- The gateway wire protocol is owned by the `service` repository. When it changes,
  this SDK follows it — never the other way round.
