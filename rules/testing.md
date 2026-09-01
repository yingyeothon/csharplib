# Testing

## Non-negotiable

- No task is complete without tests covering the new or changed behaviour.
- Keep logic free of IO and ambient state so it is unit-testable without a socket, a
  server, or a clock. If a change is hard to test, the seam is wrong — fix the seam.

## Layout & running

- Tests live in `packages/<name>/Tests/*.cs` and use the package's **public** surface.
  There is no `InternalsVisibleTo`; if a test needs something, that is a signal the
  something should be public (that is how `LobbyFrames.Read` became public).
- NUnit **3.14**, the version the Unity Test Framework ships, so the same sources
  compile in both places. Use the `Assert.That` constraint model only — NUnit 4
  removed the classic asserts.
- `dotnet test Yingyeothon.sln`. Integration tests are `[Category("Integration")]`.

## Doubles

- WebSocket → `FakeWebSocket` / `FakeWebSocketFactory`, driven from the test as the
  server (`ServerOpen`, `ServerSend`, `ServerSendRaw`, `ServerSendBinary`,
  `ServerClose`, `ServerError`). It does no threading: every server action posts
  synchronously. `CreateOverride` is how "the socket cannot be constructed" is
  reached.
- Time → `FakeClock` with `Advance`. Never `Task.Delay`, never a real timeout.
- Randomness → `BackoffOptions.Random`. `() => 0` and `() => 0.999999` pin the jitter
  bounds; `Jitter = 0` gives exact delays for the reconnect tables.
- HTTP → a fake `IHttpFetcher`. The real one is only exercised in the integration
  tests.
- Logging → a capturing `ILogWriter`, never a spy on `Console`.

## Determinism comes from the pump

- A test is: arrange, act on the fake, `Poll()`, assert. Nothing happens in between,
  so there is no scheduler to race and no `flush()` to remember.
- `harness.Advance(ms)` advances the clock and pumps. `Advance(499)` then `Advance(1)`
  is what pins "no second socket at 499 ms, one at 500 ms".
- Add a test that asserts nothing is observed **without** `Poll()`; that is the whole
  contract in one assertion.

## Asserting that something was NOT logged

- A "never logs the token" test needs a **positive control**. `Does.Not.Contain(token)`
  passes just as well against an empty log, so assert in the same test that the lines
  you expect really were written.
- Assert the token, a distinctive fragment of it, and the word `bearer` separately: a
  leak often prints only part of it.

## Ordering is the behaviour, so assert the order

- For reconnects, a count proves nothing — the bug is always a sequence. Record one
  string per event (`disconnected:4002:True`, `reconnecting:1:500`, `connected`) and
  assert the whole array. That is what pins "disconnected before stopped" and "the
  backoff reset on a successful connect".

## A test that cannot fail is not coverage

- Before adding a test, ask what implementation it rejects. "Stops after N handshake
  failures" needs its pair — "a successful session resets the counter" — or it passes
  under an implementation that never resets.
- Do not write an assertion whose expected value is computed from the actual one.

## What only an integration test can reach

- Subprotocol negotiation, a message fragmented across frames in the middle of a
  UTF-8 sequence, a refused handshake arriving as a close, and the closing handshake.
  The last one is how the "never answers a server close" defect was found: the test
  hung, because the server's `CloseAsync` was waiting for a reply that never came.
- They run against a local `HttpListener`; never a real gateway.
