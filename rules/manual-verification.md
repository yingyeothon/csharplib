# Manual Verification

Unit tests passing is not proof a change works for a game. After they pass, exercise
it where it will actually run.

## Against a real gateway

1. `dotnet build Yingyeothon.sln -c Release`.
2. Write a throwaway console app in a scratch directory (never commit it) that
   references the built assemblies, uses the **real** `WebSocketTransport.Default`,
   and drives a `while` loop calling `Poll()`.
3. Connect to the dev gateway with a channel JWT: expect `hello`, send a `pos`,
   expect a `snapshot`, then `Close()`. Ask the user for a token; do not invent one.
4. Force a reconnect (close the socket from the other side, or restart the gateway)
   and watch the backoff and the fresh `hello` arrive.

## In Unity

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
