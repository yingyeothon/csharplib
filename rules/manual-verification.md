# Manual Verification

Unit tests passing is not proof a change works for a game. After they pass, exercise
it where it will actually run.

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

**Unity needs an activated licence, and batch mode will not prompt for one.** The
editors under `~/Unity/Hub/Editor` (6000.0.25f1 with Linux Mono/IL2CPP and WebGL,
plus a 2021.2.0b3 beta) are installed but their Personal entitlement has lapsed:
`-batchmode -createProject` dies with `No valid Unity Editor license found` before it
reads a single file. Someone has to sign in through Unity Hub interactively first;
until then every claim below is unverified and must be reported as such rather than
assumed. Note also that 2021.3 LTS — the version `package.json` and `LangVersion 9.0`
actually target — is **not** installed, so Unity 6 checks the packages, not the floor.

The dotnet build proves nothing about IL2CPP. Before a release:

- Add the four packages to a scratch Unity project by local path.
- Confirm each Runtime assembly compiles with `noEngineReferences`.
- Build a player on **Mono** and on **IL2CPP**, and run the connect/reconnect/close
  cycle in the built player, not just in the editor. Stripping only bites there.
- Compile for WebGL to confirm the guarded default transport fails with the message
  it promises rather than at link time.
- Attach `GamebaseRunner` and confirm handlers run on the main thread — touch a
  `Transform` from one; if the threading is wrong, Unity says so immediately.

## Making states reachable without infrastructure

Use the injection seams rather than standing up servers:

- A capturing `ILogWriter` to observe internal decisions.
- `FakeWebSocket` to produce any close code on cue, including ones a real gateway
  will not send.
- `FakeClock` to reach a timeout instantly.
- A local `HttpListener` for the real transport.

Never add a verification-only member to the public API. If a state is unreachable
through the public surface, that is a design finding.
