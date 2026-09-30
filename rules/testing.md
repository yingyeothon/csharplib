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
  of any kind. The store client's `HttpClientTransport` is the exception: it runs
  against a local `HttpListener` (`[Category("Integration")]`), which is how "a
  redirect is not followed" is a test rather than a comment.
- The store client → `FakeTransport`, a queue of scripted replies with every call
  recorded; the test asserts the method, the URL, the headers and the body of what was
  sent. With no reply scripted it throws, which the client reports as a `network`
  failure — so a test of a *scripted* refusal asserts `Status` or `Code`; a bare
  `ThrowsAsync<KvStoreException>` passes on an empty script.
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
  has to see every runtime assembly at once and Unity must never import it. Reflection is
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
- **A `[TestCase]` string cannot carry a lone surrogate.** Attribute arguments are stored
  as UTF-8, so `"a\uD800b"` reaches the test with replacement characters (U+FFFD) where
  the surrogate was, and tests something else — observed as a failure, not assumed. Build such a string at run time (`"a" + (char)0xD800 + "b"`).

## Parsers and codecs

- Pin the wire format by exact string, not by round-trip alone. `Parse(Stringify(v))
  == v` passes just as well after someone changes `1E2` to `100.0`; only an exact
  assertion says the bytes are a decision. Re-parse each pinned string in the same
  test so a pin cannot drift into something invalid.
- The one exception is a value whose spelling the **runtime** owns. Subnormals print
  differently under Mono and under .NET, so pinning `5E-324` was a test that passed in
  CI and failed in the editor. Pin those by round-trip, in their own test, naming the
  difference — see [unity.md](unity.md). Reach for this only with evidence from a real
  second runtime; "it might differ" is not evidence, and it would gut the pins.
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

## Tests that only a second runtime can fail

- `dotnet test` is one runtime. Running the same `packages/*/Tests` sources inside the
  editor (`unity test --mode EditMode`, see [manual-verification.md](manual-verification.md))
  is what found the `ConfigureAwait(false)` that resumed a caller on a thread-pool
  thread and the two `double` differences above. The doubles are not expressible as a
  dotnet-hosted test, so the editor run — not a new unit test — is their regression
  guard; say so in the commit rather than inventing a test that cannot fail. A pool hop
  *is* expressible, once the test installs a context of its own — `PumpedContext`,
  below; a dotnet host without one hides it.
- **`Assert.ThrowsAsync` and `Assert.That(async () => …)` block the calling thread**,
  and inside the editor that thread is the main thread — the one every `await` in a
  client posts its continuation back to through Unity's synchronization context. Over
  a fake that completes synchronously the async method never yields and the assertion
  works on both runtimes; over a path that really yields (a timer, a socket, a
  `CancelAfter`) it deadlocks the whole EditMode run — the editor sits at 0 % CPU after
  `Running tests for ExecutionSettings`, writes no result file and never exits, and
  `dotnet test` is green. Found on the store client's timeout test. A test over a
  yielding path is `async Task` and awaits a helper that catches the expected
  exception (`Fails.WithKvStoreException` in the store tests; `Fails` is `internal`
  and per package — copy `Tests/Fails.cs`, there is no shared test assembly). A path
  yields when the fake returns an unsettled task (`FakeTransport.Hangs`, with a client
  `Timeout` or a cancelled token — `KvFailureTests.ATransportThatNeverAnswersIsATimeout`)
  or a real timer runs; every other `FakeTransport` reply settles inline, and the
  blocking assertions are fine there. **Real file IO yields too**: the asset client's
  `AssetFiles` tests blocked on `FileStream.WriteAsync` with `.Wait()` and
  `Assert.ThrowsAsync` and hung the whole 2026-09-30 editor run the same way, green under
  dotnet. Anything that touches the disk, the network or a real timer (not `FakeClock`)
  is `async Task` and awaits. The same context is why a test must not read
  `task.IsCanceled` or `IsCompleted` right after the call that settles it: under
  dotnet the continuation ran inline, in the editor it was posted, and the assertion
  is one frame early. Await the task instead.
- **A path a WebGL player runs must not need a second thread**, and dotnet cannot take
  the thread pool away to prove it. Install a context the test pumps itself and that
  counts every `Post` arriving from another thread (`PumpedContext` in
  `AssetClientTests`), have the fake complete with `RunContinuationsAsynchronously` as
  the Unity transports do, release and pump in a loop, and assert the task finished
  **and** the foreign-post count is 0. Reading `IsCompleted` is safe here because the
  test drained its own queue; restore the previous context in a `finally`. The test is
  synchronous `void` so no framework context sits between it and the pump. Hold the
  body reads as well as the response, or the per-chunk path takes the completed-task
  shortcut and is never tested, and cancel from the pumping thread so the count stays a
  clean 0. `AReadCompletesWithOnlyTheCallersContextPumped` and
  `ACancelledReadReleasesItsLateResponseThroughTheCallersContext` both failed on the
  `WhenAny` version ([unity.md](unity.md#webgl)); `AnAnswerThatArrivedBeforeTheCancelWins`
  pins that both sides of the race decide on the context in arrival order. **Every such
  test asserts two things**: foreign posts are 0 (work that came back from the pool) and
  the work finished on the caller's thread (work that ran on the pool and never posted,
  which leaves the count at 0). Record the thread from
  `task.ContinueWith(…, ExecuteSynchronously, TaskScheduler.Default)` on the task the
  public call returned, attached **before** the fake is released — or from the next
  `EventBroker` handler — never from an `await` or `FromCurrentSynchronizationContext()`,
  which always land on the caller. Put a `Thread.Sleep(1)` in the pump loop, so a pool
  hop finishes inside the loop and the thread assertion, not `IsCompleted`, reports it.
  Run it with the fake completing both plainly and with `RunContinuationsAsynchronously`:
  `MapFetchTests.AFetchAnsweredLaterFinishesOnTheCallersContext` and
  `EventBrokerTests.AHandlerThatFinishesLaterIsFollowedOnTheCallersContext` failed in
  both modes on the `ConfigureAwait(false)` version. `PumpedContext` is per package like
  `Fails` — there is no shared test assembly; copy it. Do not test
  `TaskScheduler.Current` after the await: an await continuation runs with the current
  task cleared, so it reads `Default` whatever the continuation's scheduler was — such a
  test cannot fail. The store and auth clients await their
  transports directly and need no such test until a combinator, a `ContinueWith` or a
  `ConfigureAwait(false)` enters their path.
- The editor's NUnit has no `Count` constraint for an array behind `IReadOnlyList<T>`
  (`Has.Count.EqualTo` fails with "Property Count was not found" there and passes under
  dotnet). Assert `list.Count` with `Is.EqualTo` instead.
- When a test cannot run on a runtime at all, `Assert.Ignore` with the reason. Eleven
  red tests nobody can act on teach people to ignore the suite; eleven ignores with a
  sentence teach them where the coverage actually comes from.

## What only an integration test can reach

- Subprotocol negotiation, a message fragmented across frames in the middle of a
  UTF-8 sequence, a refused handshake arriving as a close, and the closing handshake.
  The last one is how the "never answers a server close" defect was found: the test
  hung, because the server's `CloseAsync` was waiting for a reply that never came.
- They run against a local `HttpListener`; never a real gateway.
