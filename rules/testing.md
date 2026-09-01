# Testing

## Non-negotiable

- No task is complete without tests covering the new or changed behaviour.
- Keep logic free of IO and ambient state so it is unit-testable without a socket, a
  server, or a clock. If a change is hard to test, the seam is wrong — fix the seam.

## Layout & running

- Tests live in `packages/<name>/Tests/*.cs` and use the package's **public** surface.
  There is no `InternalsVisibleTo`; if a test needs something, that is a signal the
  something should be public (that is how `LobbyFrames.Read` became public). The one
  exception is `tests/Yingyeothon.PublicApi.Tests` — see below.
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
- `FakeWebSocket.DeferClose` makes `Close()` record the request without reporting it,
  which is what the **real** transport does — its close event arrives on the receive
  thread later. Everything that happens in that window is invisible to the default
  synchronous fake, and it is where the "a late hello resurrects a connection the
  hello timeout gave up on" defect lived.
- Time → `FakeClock` with `Advance`. Never `Task.Delay`, never a real timeout.
- Randomness → `BackoffOptions.Random`. `() => 0` and `() => 0.999999` pin the jitter
  bounds; `Jitter = 0` gives exact delays for the reconnect tables.
- HTTP → a fake `IHttpFetcher`. `HttpFetcher.Default` itself has **no** test coverage;
  its bounds (timeout, size cap, redirect budget) are asserted only by reading them.
  Do not claim otherwise — that claim stood here while the real fetcher had no bound
  of any kind.
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

## The public surface is a gate, not a courtesy

- `tests/Yingyeothon.PublicApi.Tests` is the one test project outside `packages/`: it
  has to see all four assemblies at once and Unity must never import it. Reflection is
  fine there and nowhere else — it is a test assembly, so IL2CPP never sees it and
  `validate-packages.sh` only greps `packages/*/Runtime`.
- It snapshots each assembly's public members to `Approved/<assembly>.approved.txt`
  and fails on an unreviewed change, writing the actual surface next to it as
  `.received.txt` (git-ignored) so approving is a rename. It also fails when a public
  type is not named in that package's README — the drift it caught on its very first
  run was six types.

## A test that cannot fail is not coverage

- Before adding a test, ask what implementation it rejects. "Stops after N handshake
  failures" needs its pair — "a successful session resets the counter" — or it passes
  under an implementation that never resets.
- Do not write an assertion whose expected value is computed from the actual one.

## Parsers and codecs

- Pin the wire format by exact string, not by round-trip alone. `Parse(Stringify(v))
  == v` passes just as well after someone changes `1E2` to `100.0`; only an exact
  assertion says the bytes are a decision. Re-parse each pinned string in the same
  test so a pin cannot drift into something invalid.
- Cover the failing grammar as densely as the succeeding one: every escape truncated
  at EOF, every bracket mismatch, every place a digit is required. Assert the reason
  and the offset, not just "it was refused" — a single code covering everything is a
  parser that cannot be debugged in the field.
- Property tests over a fixed-seed corpus catch what a hand-written list does not:
  round-trip equality, write idempotence, `Equals` implying an equal hash, and — for
  anything that becomes a text frame — that the output survives a UTF-8 encode and
  decode. That last one is what caught the unpaired surrogate.
- A parser that carries a failure in a field instead of throwing needs a test that
  parses a bad document and a good one in the same loop. State that outlives a call
  poisons every later frame on the socket.
- Assert the boundary from both sides. "Depth 64 is accepted" and "depth 65 is
  refused" are one test each, and the pair is what pins an off-by-one that a single
  assertion happily agrees with.

## What only an integration test can reach

- Subprotocol negotiation, a message fragmented across frames in the middle of a
  UTF-8 sequence, a refused handshake arriving as a close, and the closing handshake.
  The last one is how the "never answers a server close" defect was found: the test
  hung, because the server's `CloseAsync` was waiting for a reply that never came.
- They run against a local `HttpListener`; never a real gateway.
