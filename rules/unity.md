# Unity, IL2CPP and WebGL

The whole point of this repository is that Unity can consume it. These are the
constraints that follow, and none of them fail at `dotnet build`.

## Target surface

- `netstandard2.0` **and** `netstandard2.1`. Unity's .NET Standard 2.1 profile is the
  common case; 2.0 keeps older projects working.
- `LangVersion 9.0` — Unity 2021.3 LTS compiles no higher. No file-scoped namespaces,
  no `required`, no `record`, no `init` (`IsExternalInit` is not in netstandard2.0).
- No third-party packages in a Runtime assembly. A NuGet dependency does not travel
  through UPM.

## Assembly boundaries

- Every Runtime asmdef sets `noEngineReferences: false`, and that is **settled, not
  an oversight**: the engine glue in `Runtime/Unity/**` lives inside the same assembly
  and references `UnityEngine`. The enforcement that keeps the sources
  plain-`dotnet build`-able is the `.csproj` compile glob excluding `Runtime/Unity/**`,
  and it is a real gate — the dotnet build fails the moment a non-`Runtime/Unity` file
  touches the engine. Splitting the two glue files into their own asmdefs would let the
  flag be `true`, at the cost of an extra asmdef reference in every consumer that wants
  `GamebaseRunner`; not worth it for a flag whose job the glob already does. Do not
  "restore" the flag without moving the glue first: it will not compile in Unity.
- Engine-facing glue lives in `Runtime/Unity/`, guarded by `#if UNITY_5_3_OR_NEWER`,
  and is excluded from the `.csproj` compile glob. Adding a file there means checking
  both builds.
- asmdef `references` are written as **names**, not GUIDs, so they survive the `.meta`
  regeneration a fresh clone triggers.
- `.meta` files are not committed; Unity generates them on import. If stable GUIDs
  ever matter, generate them once from a real editor and commit the lot.

## Nullable, and the one line that carries it

`Directory.Build.props` sets `<Nullable>enable</Nullable>`, and **that reaches the
dotnet build only**. An asmdef has no equivalent and there is no project-wide switch, so
Unity compiles the same sources with nullable off and every reference-type `?` in them
becomes a CS8632 — one per annotation, so the count tracks the annotations and is not a
threshold. **The invariant is zero CS8632 with the rsp files in place.** For scale:
with them removed, both 2021.3.45f2 and 6000.0.25f1 emitted 192 (158 `Runtime`, 34
`Tests`, the latter only in a project that lists the package under `testables`), and
that is what a consumer who **vendors** the sources sees. A consumer who installs
normally sees none of them either way — Unity suppresses warnings from an immutable
package — so this is a fix for the vendored path and for anyone reading the sources,
not a wall every user was staring at. What carries the setting is a `csc.rsp` beside
each asmdef:

```
-nullable:enable
```

Four things about that file, each of which cost something to learn:

- **It holds exactly that one line.** A `#` comment is not stripped: the tokens reach
  csc as arguments and each word comes back as `error CS2001: Source file '<word>'
  could not be found`, so the *whole project* stops compiling — 140 errors from one
  three-word comment, observed on 2021.3.45f2. Roslyn's own response-file parser does
  treat `#` as a comment, so this is Unity's handling, not csc's; either way, do not
  put one there. A bare-flag test passing says nothing about a commented one.
- **Every asmdef under `packages/` needs its own**, `Samples~` excepted — which is
  exactly what `scripts/validate-packages.sh` enforces, in both directions. The file is
  per-assembly, not per-package. An asmdef outside `packages/` is covered by no guard;
  if you add one, extend the script.
- **`event-broker` has no annotations at all** and its two rsp files silence nothing.
  They exist so the rule is "every asmdef" rather than "every asmdef that currently
  needs it", which is the version a new file can violate silently.
- **A `csc.rsp` is compiler arguments injected into a consumer's build** — `-r:`,
  `-define:`, `-analyzer:` and `-unsafe` are all accepted there. That is why
  `scripts/validate-packages.sh` pins the content and also checks the reverse
  direction: every rsp in the tree must sit beside an asmdef and carry nothing else.

A sample is **not** covered by any of this: `Samples~` is copied into the consumer's
`Assets/`, out of reach of a package rsp, so a sample file that uses a nullable
annotation carries its own `#nullable enable` — three of the eight sample `.cs` files
today, and the guard requires it of any sample that annotates, so a new one cannot
regress silently. Note that the directive turns on the flow analysis
(CS8600/8602/8618) as well as the annotations, not only the CS8632 it was reached for;
these are clean under both.

`scripts/validate-packages.sh`'s `rsp_line` is the **enforcing** copy of that string:
change it there first, then the eight files, then this section and `docs/unity.md`. If
they ever disagree, the script is right.

What a consumer needs to know about this is one section in
[docs/unity.md § Installing](../docs/unity.md#installing) — that the flag does not follow
a file out of its folder. Everything above is the maintainer's half and stays here.

Unity hides none of this: warnings from a package assembly print like any other. They
were invisible only because nothing had forced a clean recompile —
[manual-verification.md](manual-verification.md) has how to force one.

## IL2CPP

- No reflection: no `Activator.CreateInstance`, no `GetType().GetProperty`, no
  `Reflection.Emit`, no attribute-driven serialization. The managed stripper removes
  what it cannot see being used and the failure appears at runtime, in a player
  build, months later. `scripts/validate-packages.sh` greps for this.
- `Runtime/link.xml` preserves the three runtime assemblies wholesale, because they
  are reached through interfaces and generic factories.
- Wire types parse and build themselves by hand. It is more code and it is the point.

## WebGL

- `ClientWebSocket` throws `PlatformNotSupportedException`, `HttpClient` does not
  work, and `Task.Run` has no thread. `WebSocketTransport.Default` throws there on
  purpose rather than failing quietly.
- A WebGL build passes its own `WebSocketFactory` (over a `.jslib` socket) and
  `HttpFetcher` (over `UnityWebRequest`). Both are options on the client, so this is
  configuration, not a fork.

## The Poll contract

- Nothing a client received is observed until `Poll()` runs, and timeouts and
  reconnect delays only advance there. A client that is never polled never
  reconnects — say so wherever `Poll` is documented.
- Call `Poll()` from `Update()` unconditionally, **before** any pause or
  `timeScale` check. A paused scene that skips it stalls the connection.
- Use a client from one thread at a time. What is enforced is **concurrency**, not
  thread identity: `Poll()` takes an interlocked claim and every other entry point
  refuses while another thread holds it. A handler calling `Send` during its own pump
  is fine, which is how a game sends from an event.
- Identity is deliberately not pinned. Two earlier attempts both broke a legitimate
  host: binding at construction refused an `await ConnectAsync()` that resumed on the
  pump thread, and binding on the first `Poll()` refused a console host whose every
  `await` resumes on a different pool thread. In Unity the synchronization context
  makes all of these the main thread anyway. Both were found by the manual
  verification, not by a test.
- `await ConnectAsync()` resumes on the pump thread by design, so `Send()` is legal
  straight after it. A `MapAsync()` continuation is a normal task continuation and
  may land elsewhere; marshal back before touching the client.
- **Never `ConfigureAwait(false)` on a task the caller awaits.** `GatewayLobbyClient`
  did, and on Unity's Mono the continuation was not inlined — `await ConnectAsync()`
  resumed on a thread-pool thread, which is exactly where `Send()` and every other
  entry point are illegal, and where touching a `Transform` throws. A dotnet host
  hides this because .NET inlines the same continuation. Settle a caller-visible task
  from a synchronous `ContinueWith(..., TaskContinuationOptions.ExecuteSynchronously,
  TaskScheduler.Default)` over the internal one, and leave the resumption context to
  the caller: Unity's synchronization context puts them back on the main thread, a
  console host resumes inline. `ConfigureAwait(false)` is still right inside the
  transport's own background loops and in `MapFetcher`, which never resume a caller.

## Numbers and culture

- The wire is Go `float64`, so positions are `double`, not `float`.
- Every conversion uses `CultureInfo.InvariantCulture`. A German or Turkish locale
  otherwise writes `1,5`, the gateway drops the frame as `bad_message`, and the
  client is told nothing. There is a culture-parameterised test; keep it.
- Mono's `double` formatting and parsing predate the IEEE-754 work in .NET Core 3.0,
  so two things differ inside the editor and in a player:
  - `double.TryParse("-0")` hands back **+0**, dropping the sign. `JsonParser`
    restores it, so the value tree has the same shape on both runtimes.
  - `ToString("R")` on a **subnormal** prints seventeen significant digits
    (`4.94065645841247E-324`) where .NET prints the shortest round-trip form
    (`5E-324`). Both read back to the same double, so the round-trip holds and the
    text does not. Pin subnormals by round-trip, everything else by exact text.
- `HttpListener` cannot accept a WebSocket on Mono — `AcceptWebSocketAsync` throws
  `NotImplementedException`. The server half of the transport integration tests
  therefore reports as ignored inside Unity, with the reason. The client half is
  covered against the real gateway; see [manual-verification.md](manual-verification.md).

## Allocation

- Frames allocate per parse. The gateway coalesces positions to one batch per `tick`
  (200 ms by default), so this is tick-rate work, not frame-rate work — measure
  before pooling.
- Avoid LINQ and `foreach` over interfaces on anything a game would call every frame.
