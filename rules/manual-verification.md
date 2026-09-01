# Manual Verification

Unit tests passing is not proof a change works for a game. After they pass, exercise
it where it will actually run.

**No tag is cut without a current run of this file** — a UPM consumer gets whatever
the tag points at, compiled by Unity's compiler. See [release.md](release.md).

The sibling `service` repository is checked out next to this one — `../service` from
the repo root — and every `service/...` path in these rules is relative to it. Read it;
never write to it from here ([workflow.md](workflow.md)).

## Against a real gateway

1. `dotnet build Yingyeothon.sln -c Release`.
2. Write a throwaway console app in a scratch directory (never commit it) that
   references the built assemblies, uses the **real** `WebSocketTransport.Default`,
   and drives a `while` loop calling `Poll()`.
3. Connect to the dev gateway with a channel JWT: expect `hello`, send a `pos`,
   expect a `snapshot`, then `Close()`.
   **Minting the JWT is a solved problem — do not ask for one and do not invent one.**
   The `service` repository already encodes the recipe in
   `scripts/smoke/gateway.mjs`: `POST {authBase}/debug/token` with an
   `x-debug-key` header. On dev that is
   `https://auth-dev.yyt.life/debug/token`, the key is
   `service/local/deploy/debug-key.dev`, and
   `service/local/deploy/morpg-channels.dev.json` names a live `authChannelId`,
   `lobbyChannelId` and `mapUrl`. `yyt channels list --scope all` confirms the
   channel is still active. Never print the token or commit it.
4. Force a reconnect (close the socket from the other side, or restart the gateway)
   and watch the backoff and the fresh `hello` arrive.

## In Unity

`dotnet build` proves nothing about the compiler Unity uses, about IL2CPP's stripper,
or about Mono's BCL. Run the suite and a player inside the editor before a release.
The four things below were all found this way and none of them is reachable from
`dotnet test`: a `ConfigureAwait(false)` that resumed a caller on a thread-pool
thread, two `double` differences, and a test harness Mono cannot host
([unity.md](unity.md)).

### Which editor

- **The floor is what matters.** `package.json` says `"unity": "2021.3"` and
  `LangVersion 9.0` exists because of it, so a Unity 6 run alone cannot catch a
  compiler problem the floor would.
- **2021.3.46f1 and later are Extended LTS** and refuse to start on a Personal
  licence: "This build of Unity 2021 is part of an Extended LTS release, which
  requires either a valid Unity Industry or Unity Enterprise license." The newest
  2021.3 a Personal licence can run is **2021.3.45f2** — which is also the newest one
  Unity's public release API lists, and that is the cheap way to tell: if
  `https://services.api.unity.com/unity/editor/release/v1/releases?version=<v>`
  returns nothing, the build is entitlement-gated. Do not burn a multi-GB download on
  a version the licence cannot open. (`unity releases` lists the gated ones too, and
  `unity install` downloads them happily; only the editor itself objects, after ~7 GB.)

### Two things Ubuntu 24.04 breaks in Unity 2021.3

Both present as a **hang**, not an error, and both cost an hour if you do not know them.
Unity 6 has neither: it bundles .NET 6 and a newer build backend.

1. **`No usable version of libssl was found`.** Unity 2021.3 bundles .NET 5, which
   speaks OpenSSL 1.1 only; 24.04 ships OpenSSL 3. The Roslyn host aborts with
   SIGABRT mid-compile and the editor waits on the corpse. Fix without touching the
   system: copy `libssl.so.1.1` and `libcrypto.so.1.1` out of
   `/snap/core20/*/usr/lib/x86_64-linux-gnu/` into a scratch directory and put it on
   `LD_LIBRARY_PATH` for the editor. The sonames differ from OpenSSL 3's, so nothing
   else is affected.
2. **`bee_backend --stdin-canary` never exits.** Its canary thread blocks process
   teardown while stdin is a pipe the editor holds open, so tundra prints
   *"Tundra build success"* and then hangs, and the editor waits forever. Confirm it
   by running `bee_backend` by hand: without the flag it exits, with the flag and a
   live pipe on stdin it does not. The only workaround found is to move
   `Editor/Data/bee_backend` aside and drop in a shell wrapper that strips
   `--stdin-canary` before `exec`ing the real binary. **Put the original back when you
   are done** — the flag exists so a build dies with the editor that started it.

### The scratch project

1. Build it **outside the repo**, in a scratch directory.
   `<editor>/Editor/Unity -batchmode -nographics -quit -createProject <path>`.
2. **Copy** the four `packages/com.yingyeothon.*` folders into `<project>/Packages/`.
   Do not use a `file:` UPM dependency and do not symlink: Unity writes `.meta` files
   into whatever it imports, and `.meta` files are deliberately not committed here, so
   either would dirty the working tree.
3. In `Packages/manifest.json`, add `com.unity.test-framework` and list the four
   package names under `testables` — the `Tests` asmdefs carry
   `defineConstraints: ["UNITY_INCLUDE_TESTS"]` and compile only for a testable.
   **1.4.6 works on both editors** and is what runs the `async Task` tests; the 1.1.x
   that 2021.3 would otherwise resolve does not.

### Run the package tests inside the editor

```bash
unity test <project> --mode EditMode --output <path>.xml --report-format nunit
```

This is the strongest single check available — the same `packages/*/Tests` sources,
compiled by Unity's compiler, run on Unity's Mono. Read the XML's counts; do not trust
the exit code alone. `tests/Yingyeothon.PublicApi.Tests` lives outside `packages/` and
is correctly absent. Eleven transport tests report **ignored**, with the reason: Mono's
`HttpListener` cannot accept a WebSocket.

### Install the way a consumer installs, not only the way this recipe does

**The scratch project above copies the packages, and copying hides the defect that
matters most.** A copied package sits in `Packages/` and is *mutable*, so Unity writes
the `.meta` files it needs. A package a consumer installs — a git URL, a tarball, a
registry — is unpacked into `Library/PackageCache` and is *immutable*, and there Unity
generates nothing: an asset with no `.meta` is **ignored**, silently, one log line each.

Run this before any tag, from a bare clone so no uncommitted file can rescue it:

```bash
git clone --bare <repo> /tmp/x.git          # the URL must end in .git or UPM rejects it
# manifest.json: "com.yingyeothon.codec": "file:///tmp/x.git?path=/packages/com.yingyeothon.codec"
<editor> -batchmode -nographics -quit -projectPath <project> -logFile <log>
find <project>/Library -name 'Yingyeothon*.dll'          # must list all four
grep -c "immutable folder" <log>                         # must be 0
```

**`find` returning nothing is the failure, and nothing else reports it** — the editor
exits 0, no test fails, and `-executeMethod` still runs, because the consumer's own
scripts compile fine against a package that contributed no assemblies at all.

Two facts this check establishes that the copied project cannot:

- Unity **suppresses compiler warnings from an immutable package.** The same sources
  that print 192 CS8632 as an embedded copy print none from `Library/PackageCache`. So
  a warning count measured in the scratch project describes the *vendored* consumer,
  not the git-URL one — do not report it as "what a consumer sees" without saying
  which.
- `Samples~` and `csc.rsp` both travel correctly through an immutable install, once the
  assets are visible at all.

### Check the samples the way the Package Manager does

`package.json`'s `samples` array and the `#if UNITY_5_3_OR_NEWER` `MonoBehaviour`
halves are the part of this repository that **no** `dotnet` build compiles —
`tests/Yingyeothon.Samples.Build` takes the engine-free half by design. Drive the
editor's own API rather than clicking. Put this under `Assets/Editor/` (it needs
`UnityEditor`) and call it with `-executeMethod SampleImport.ImportAll`:

```csharp
using System.Linq;
using UnityEditor.PackageManager.UI;
using UnityEngine;

public static class SampleImport
{
    public static void ImportAll()
    {
        foreach (var package in new[] { "com.yingyeothon.codec", "com.yingyeothon.event-broker",
                                        "com.yingyeothon.gamebase-client", "com.yingyeothon.logger" })
        {
            var samples = Sample.FindByPackage(package, string.Empty).ToList();
            Debug.Log($"[SAMPLES] {package} count={samples.Count}");
            foreach (var sample in samples)
            {
                sample.Import(Sample.ImportOptions.OverridePreviousImports);
            }
        }
    }
}
```

`FindByPackage` reads the same `samples` array the import buttons are built from, so
each count must equal the length of that package's array — a mismatch means an entry is
malformed, not that someone added a sample. Then run the editor again with any
`-executeMethod`: it only runs once every script compiles.

**Reaching the method proves zero *errors*, not zero warnings** — a warning never stops
`-executeMethod`. For warnings, `grep -c 'warning CS' <log>` and attribute each one to
`Packages/`, `Assets/Samples/` or your own harness before reporting a number.

**Unity prints a warning once, when the assembly is actually rebuilt.** A second
batch-mode run reports zero warnings whether or not any exist, and reading that as
"clean" is how 192 CS8632 warnings survived a previous verification. Force the rebuild
by deleting `<project>/Library/ScriptAssemblies`, and confirm from the log that a
compile actually happened (`CompileScripts`) — touching the sources is not enough on
its own, because Unity re-hashes content. When a compiler flag is what you are
testing, take it away and watch the warnings return: **delete the rsp from the scratch
project's `Packages/` copy, never from this repository**, and re-copy afterwards.

### Build and run a player

A build that succeeds proves nothing about stripping — the player has to run. Put a
`MonoBehaviour` in the scene that reaches every package through its factories
(`GatewayLobbyClient.Create`, `GatewayGameClient.Create`, `EventBroker.Create` and its
generic `On<T>`, `Json.Parse`/`Stringify`, `LogWriters.FromAction`, and
`GamebaseRunner.CreatePersistent`), build with
`ManagedStrippingLevel.High`, then run the player with `-batchmode -nographics
-logFile` and grep the log for what it printed. That is what tests `Runtime/link.xml`.

- **Mono** and **IL2CPP** `StandaloneLinux64`, both built and both run.
- **WebGL** as a compile-and-link check for the guarded default transport. Confirming
  the guard actually throws needs a browser; say which of the two you did.

### Last verified

Always record the **commit** as well as the date: a release asks whether this run
covers the code being tagged, and a date alone cannot answer it
([release.md](release.md)).

**2026-09-01**, at the commit whose parent is `70c334d` and whose tree adds the eight
`csc.rsp` files — **replace this clause with the sha before cutting any tag**
([release.md](release.md) step 2). Unity Personal, Ubuntu 24.04.

| Check | 2021.3.45f2 | 6000.0.25f1 |
| --- | --- | --- |
| Samples listed and imported | one per `samples[]` entry: 4 / 1 / 1 / 1 | same |
| Imported samples compile in `Assets/` | 0 errors, 0 warnings from `Packages/`, `Assets/Samples/` and the pasted snippet | same |
| Without `csc.rsp`, embedded | 192 CS8632 (158 `Runtime`, 34 `Tests`) | same |
| `docs/getting-started.md` §4 pasted into a fresh script | compiles | same |
| EditMode, all four packages | **0 failed**; ignored are exactly the eleven named above. 459/448/11 at this commit, not a threshold | same |
| `StandaloneLinux64` Mono, stripping **High** | built, and the player ran and logged from every package | same |
| `StandaloneLinux64` IL2CPP, stripping **High** | built, and the player ran the same | same |
| WebGL | compiled and linked; the guard was not exercised in a browser | same |

The two editors agreed on every row. When they do not, that is the finding — 2021.3 is
the floor for a reason ([Which editor](#which-editor)).

**Do not carry a player row forward on "the sources did not change".** This run added a
`csc.rsp` per asmdef, which is not a source change and which
`git diff -- 'packages/**/Runtime/**'` cannot show at all while the files are untracked
— and turning the nullable context on makes Roslyn emit `NullableAttribute`,
`NullableContextAttribute` and `EmbeddedAttribute` into the Unity-built assemblies. That
is exactly the metadata `ManagedStrippingLevel.High` and `Runtime/link.xml` are tested
against. A compiler flag is a build change even when no `.cs` moved.

### The install path is broken, and this run is what found it

**A git-URL install of `70c334d` compiles nothing.** All four packages resolve, and then
every asset in them is ignored — 448 log lines of *"has no meta file, but it's in an
immutable folder"* — so `Library` ends up with **zero** `Yingyeothon*.dll`. This is not
a regression from this change; it is the state of the tree, and it makes
`docs/getting-started.md` §1 and `docs/unity.md` § Installing describe a path that does
not work.

The cause is a straight collision between two deliberate decisions: UPM requires `.meta`
files in a package consumed from an immutable source, and this repository does not commit
them (`.gitignore`, and [The scratch project](#the-scratch-project) is built around their
absence). Copying the packages hides it, and copying is what every verification here has
done.

Confirmed both directions on 6000.0.25f1, from bare clones:

| Bare clone contains | Assets ignored | Assemblies compiled | Samples |
| --- | --- | --- | --- |
| no `.meta` (the tree as it stands) | 448 | **none** | — |
| `.meta` committed | 32 (the unpaired `csc.rsp.meta`) | all four | — |
| `.meta` **and** `csc.rsp` committed | **0** | all four, 0 errors, 0 CS8632 | **7 import** |

So the fix works and is one decision: **commit the `.meta` files.** That reverses a
documented policy in `docs/unity.md`, `.gitignore` and this file, so it is the user's
call and not a thing to fold into an unrelated commit. Until it is made, **no tag**
([release.md](release.md)) — a consumer following the guide gets four packages with no
code in them.

### Against the dev gateway, same date

Verified live on the `morpg` dev channels, with a console app that never printed the
token. Read the channel's settings first — `yyt channels get <lobbyChannelId> --json`
gives `config.capabilities.pos`, `config.flushIntervalMs`, `config.defaultZone` and
`config.mapUrl`. Here `pos` was enabled and `flushIntervalMs` was 200; both matter
below. **Read those settings, do not set them.** They are shared dev infrastructure; if
a value has to change to tell a default from a configured one, change exactly one,
record the previous value here, and restore it in the same session.

| Claim | Where it lives | Result |
| --- | --- | --- |
| `.well-known/config` is unauthenticated and names the channel | `docs/authentication.md` | 200, nine fields; the page listed five and was corrected |
| GitHub with an `idToken` is a `400` | `docs/authentication.md` | 400 `github requires accessToken`, the documented reason verbatim |
| `verify` answers `{ userId, exp, channelId }` | `docs/authentication.md` | exactly those three |
| `tokenTtlSec` defaults to 24 h | `docs/authentication.md` | 86400 |
| Console setting → `hello` field | `docs/console-and-options.md` | **8 of 8 match**: `defaultZone`→`Zone`, `mapUrl`→`MapUrl`, `flushIntervalMs`→`Tick`, and `pos` / `say` / `party` / `event` / `debug`→`Capabilities` |
| A reconnect with a retained position gets a `snapshot` with no `Pos` | `docs/lobby.md` | **confirmed**, by the control below |
| `4000` Replaced is `Stop`, not a reconnect | `docs/errors.md` | `Stopped kind=Stop code=4000` |

**Vary one thing.** The first attempt at the reconnect claim compared a returning user
against *a different, never-announced user*, which varies identity and history at once
and cannot separate "this user's position was restored" from "the gateway always sends
one". The control that settles it uses **one** identity across three connections — and
it must be an identity with no history, so pass a `userId` you have never used to
`POST /debug/token` (it takes one; reusing the last one reproduces the confound):

| | Sends `Pos`? | Snapshot | `Peers.Zone` |
| --- | --- | --- | --- |
| A1, first ever connect | no | **none**, and no frames at all | empty |
| A2, same identity | yes | one | the zone |
| A3, same identity | no | **one, unprompted** | the zone |

A1 against A3 is the claim, and the only difference between them is that this identity
has announced since. Two preconditions the run depended on and a future one must keep:
the channel must have `pos` enabled (`gateway/internal/lobby/hub.go` restores only
then), and A2 must outlive one `flushIntervalMs` — the position reaches Redis from the
flush loop, not from the `pos` frame, so a reconnect faster than a tick restores
nothing.

Read from the source rather than observed, and marked so on purpose:

- **The idle timeout cannot be tripped by a frozen game loop.** In
  `gateway/internal/conn/conn.go`: the `PongHandler` set in the constructor (~:101)
  resets the read deadline, the write loop sends a **protocol-level** ping (~:257), and
  the read loop resets the deadline on *any* inbound frame (~:237) — so
  `docs/errors.md`'s "no pong within 75 seconds" is narrower than the code's own "no
  pong and no traffic". `ClientWebSocket` answers a protocol ping from its own receive
  loop, independent of `Poll()`. Cite the function, not the line: these numbers drift
  with the sibling repository, which is not pinned here. A live run can show a socket
  surviving 75 s unpolled; it cannot show why.

Not covered, and each is a real gap rather than a formality:

- The provider exchange with a **real** GitHub access token. There is no provider
  credential here, so only its refusal path was exercised.
- **`4002` Idle → reconnect, and the backoff.** Not reachable from a client for the
  reason above, and this repository does not own the gateway to restart it. It *is*
  reachable through the public `IWebSocketFactory` seam with a double that stops
  answering pings — that is a test to write, not a gateway run.
- The WebGL guard in a browser.
- Whether Unity honours a `csc.rsp` in an **immutable** package (a git-URL install
  resolves into `Library/PackageCache`). Both scratch projects used copied, embedded
  packages. Worth one run before a tag.

## Making states reachable without infrastructure

Use the injection seams rather than standing up servers:

- A capturing `ILogWriter` to observe internal decisions.
- `FakeWebSocket` to produce any close code on cue, including ones a real gateway
  will not send.
- `FakeClock` to reach a timeout instantly.
- A local `HttpListener` for the real transport.

Never add a verification-only member to the public API. If a state is unreachable
through the public surface, that is a design finding.
