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

**2026-09-01**, at commit `f5fce56`, on Unity Personal, against **2021.3.45f2** (the
floor) and **6000.0.25f1**. Identical results on both:

| Check | Result |
| --- | --- |
| EditMode, all four packages | 459 tests — 448 passed, **0 failed**, 11 ignored |
| `StandaloneLinux64` Mono | built, and the player ran and logged from every package |
| `StandaloneLinux64` IL2CPP, stripping **High** | built, and the player ran the same |
| WebGL | compiled and linked; the guard was not exercised in a browser |

## Making states reachable without infrastructure

Use the injection seams rather than standing up servers:

- A capturing `ILogWriter` to observe internal decisions.
- `FakeWebSocket` to produce any close code on cue, including ones a real gateway
  will not send.
- `FakeClock` to reach a timeout instantly.
- A local `HttpListener` for the real transport.

Never add a verification-only member to the public API. If a state is unreachable
through the public surface, that is a design finding.
