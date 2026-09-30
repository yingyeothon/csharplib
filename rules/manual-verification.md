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
   `service/local/deploy/debug-key.dev`, and the channel comes from the CLI's **dev**
   profile — pass `--profile dev` every time, because the default profile may be prod.
   `yyt --profile dev channels list --scope all --json` lists the channels; the `id` of
   an `active` row of `kind: lobby` is the `lobbyChannelId`. `yyt --profile dev channels
   get <id> --json` gives its `config.authChannelId` (the channel to mint on),
   `config.mapUrl` and the rest of its settings. If no lobby is active, **ask; never
   create one** — dev channels are shared. (A `morpg-channels.dev.json` this step used to
   name is gone; the CLI is the source.) Never print the token or commit it.
4. Force a reconnect (close the socket from the other side, or restart the gateway)
   and watch the backoff and the fresh `hello` arrive.

## Against the dev store

The key-value store client is plain HTTP, so its live check is a console app over the
built `Yingyeothon.KvStore` with the **real** `HttpClientTransport.Default`, against
`https://doc-dev.yyt.life`. Everything it needs already exists on dev; create nothing:

1. The token is the same `POST /debug/token` recipe as above, on the auth channel of
   the dev project that `service/local/todo-archive/33-kvstore.md` names for the store —
   read the team, project and collection there, never copy them here
   ([security.md](security.md): `service/local/**` is private). `yyt channels list
   --team <team> --project <project> --json`, the `kind: auth` row, is the channel id.
   **The `userId` you pass must match `KV_OWNER_ID` in
   `packages/console-db/src/kvstore.ts`** — 32 lower-case hex is the easy form. The
   debug hook accepts any string and puts it in `sub` verbatim, and the store's owner
   grammar then answers every `/u/me` route `400`. Use a fresh id per run, so the
   per-owner cap below is reachable from an empty namespace.
2. The collection has `readScope: project`, `writeScope: user` and a small
   `maxEntriesPerOwner` (three): `Mine` covers every operation, the collection itself
   covers the every-owner list and the `wrong_namespace` 400, and the per-owner cap is
   what makes `owner_full` reachable in one run. There is no shared-namespace
   collection on dev; the shared path is exercised as its refusal.
3. Print statuses, versions and codes only — the app prints no token, value or URL —
   and delete every key it wrote. The reader's `404` on a delete of a missing key is
   visible only in the `kv request` log line (status 404) or through
   `HttpClientTransport.Default.SendAsync` directly; through `DeleteAsync` the same call
   must complete without throwing. It is the fact the state README's route table
   gets wrong ([architecture.md](architecture.md)).

## Against the dev CDN

`asset-client` reads public files, so its live check is a console app over the built
`Yingyeothon.Assets` with the **real** `AssetHttpClientTransport`, against a throwaway
encrypted live bundle. Unlike the store, the bundle is made for the check and removed
after it. Every command takes `--profile dev`; the default profile may be prod.

1. `yyt --profile dev --team <team> --project <project> asset create <name> --mode live
   --encrypted --json`, and keep the `id` it prints (`ab_…`): every later command takes
   the id, which needs no project context. Any team and project you can write to will
   do; `yyt --profile dev asset list --json` shows the `teamId`/`projectId` of existing
   bundles. The CLI must be `v0.12.0` or later (`yyt --version`).
2. With `umask 077`, `yyt --profile dev asset key show <id>` into a file in the session's
   scratch directory — never into this repository, never to the terminal.
3. From inside a scratch directory holding `manifest.json` (a few bytes of JSON) and one
   file over 128 KiB, so a range can cross two segment boundaries:
   `yyt --profile dev --team <team> --project <project> asset sync <id> . --mutable
   manifest.json`. ASCII paths only: the console refuses anything else. Dev throttles
   writes: a `rate_limited` line uploaded nothing, so wait a few seconds and sync again
   until it reports `0 failed` — a read before that is a `not_found`.
4. `BaseUrl` is `https://dev-d.yyt.life/assets/<id>/` (`yyt --profile dev asset files <id>`
   shows each file's URL). Read, in both `CorsSafe` modes: the manifest, the file whole,
   a range across the two boundaries, the tail, an interrupted
   `AssetFiles.DownloadToFileAsync` resumed from its part, a missing file (`not_found`,
   403) and the vector key (`asset_corrupt`). Change the manifest, `sync` again, and read
   it with `NoCache`. Print booleans and statuses only.
5. `yyt --profile dev asset delete <id>`, then delete the key file.

If a step cannot be done — no CLI, one too old, no team you can write to — do not
substitute another bundle: record the gap under *Not covered* below and ask.

**2026-09-30**: every step above passed against the code of the commit that adds
`asset-client` (rerun after its review round changed the timeout paths); the bundle was
deleted and its key removed.

## In Unity

`dotnet build` proves nothing about the compiler Unity uses, about IL2CPP's stripper,
or about Mono's BCL. Run the suite and a player inside the editor before a release.
The four things below were all found this way and none of them was reachable from
`dotnet test` as it then stood: a `ConfigureAwait(false)` that resumed a caller on a
thread-pool thread (a test that installs its own context now can —
[testing.md](testing.md)), two `double` differences, and a test harness Mono cannot host
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
2. **Copy** every `packages/com.yingyeothon.*` folder into `<project>/Packages/`.
   Do not use a `file:` UPM dependency and do not symlink: Unity writes into whatever
   it imports — a `.meta` for any asset that lacks one, a re-serialized one where its
   format changed — so either would dirty the working tree.
3. In `Packages/manifest.json`, add `com.unity.test-framework` and list every
   package name under `testables` — the `Tests` asmdefs carry
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
is correctly absent. Twelve tests report **ignored**, each with its reason: eleven
transport tests, because Mono's `HttpListener` cannot accept a WebSocket, and one asset
test, because `File.CreateSymbolicLink` needs .NET 6.

### Install the way a consumer installs, not only the way this recipe does

**The scratch project above copies the packages, and copying hides the defect that
matters most.** A copied package sits in `Packages/` and is *mutable*, so Unity writes
the `.meta` files it needs. A package a consumer installs — a git URL, a tarball, a
registry — is unpacked into `Library/PackageCache` and is *immutable*, and there Unity
generates nothing: an asset with no `.meta` is **ignored**, silently, one log line each.

Run this before any tag, on **both** editors, from a bare clone so no uncommitted file
can rescue it:

```bash
git clone --bare <repo> /tmp/x.git          # the URL must end in .git or UPM rejects it
# manifest.json: "com.yingyeothon.codec": "file:///tmp/x.git?path=/packages/com.yingyeothon.codec"
<editor> -batchmode -nographics -quit -projectPath <project> -logFile <log>
find <project>/Library -name 'Yingyeothon*.dll'          # must list every package
grep -c "immutable folder" <log>                         # must be 0
```

**`find` returning nothing is the failure, and nothing else reports it** — the editor
exits 0, no test fails, and `-executeMethod` still runs, because the consumer's own
scripts compile fine against a package that contributed no assemblies at all.

Two facts this check establishes that the copied project cannot:

- Unity **suppresses compiler warnings from an immutable package.** The same sources
  that printed 192 CS8632 as an embedded copy (four packages, 2026-09-01) print none from `Library/PackageCache`. So
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
                                        "com.yingyeothon.gamebase-client", "com.yingyeothon.kvstore-client",
                                        "com.yingyeothon.auth-client", "com.yingyeothon.asset-client",
                                        "com.yingyeothon.logger" })
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
(`GatewayLobbyClient.Create`, `GatewayGameClient.Create`, `KvStoreClient.Create` and
`UnityWebRequestTransport.Instance`, `AuthClient.Create` and
`AuthUnityWebRequestTransport.Instance`, `AssetBundleClient.Create` and
`AssetUnityWebRequestTransport.Instance`, `EventBroker.Create` and its generic `On<T>`,
`Json.Parse`/`Stringify`, `LogWriters.FromAction`, and `GamebaseRunner.CreatePersistent`), build with
`ManagedStrippingLevel.High`, then run the player with `-batchmode -nographics
-logFile` and grep the log for what it printed. That is what tests `Runtime/link.xml`.
Have it also decrypt one conformance vector through an in-memory `IAssetTransport` (the
asset crypto on that backend), and point each Unity transport at the `302` server in
[the auth/asset run](#the-run-that-added-auth-client-and-asset-client): each must answer
`http (302)` and the server must never see its redirect target.

- **Mono** and **IL2CPP** `StandaloneLinux64`, both built and both run.
- **WebGL** built, then **run in a browser** — below. A link alone is how every asset
  read hung in a browser, unnoticed, until 2026-09-30 ([unity.md](unity.md#webgl)).

### Run the WebGL player in a browser

**A full run before a tag includes this**; any part of it that could not run is named in
the run's record. The probe is **not in this repository**: write it in the scratch
project, outside the repo, like everything else under [In Unity](#in-unity).

1. **Build.** A method in `Assets/Editor/` that sets
   `PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled` (so a plain
   static server works) and calls `BuildPipeline.BuildPlayer` for `BuildTarget.WebGL`
   with stripping **High**, run with `-executeMethod`. The editor needs its WebGL module;
   if it is missing, say so and ask before installing one.
2. **Serve three origins.** The build on `127.0.0.1:18080`
   (`python3 -m http.server 18080 --bind 127.0.0.1 -d <build>`); a redirect server on
   `:18777` and a control on `:18778`, both answering every method — `OPTIONS` with `204` —
   with `Access-Control-Allow-Origin: *`, `-Allow-Headers: *`, `-Allow-Methods: *` and
   `-Expose-Headers: *`. The store and auth transports send headers that make the browser
   preflight, so without those a request fails on CORS, not on what is under test.
   `:18777` answers `302 → http://127.0.0.1:18777/elsewhere` and prints `FOLLOWED` when
   `/elsewhere` is hit; `:18778` answers `404`. Extend the native probe's `302` server (in
   [the auth/asset run](#the-run-that-added-auth-client-and-asset-client)); one server on
   `:18777` serves both the players and the browser.
3. **Run it headless.** The Chrome the browser extension drives may have **no WebGL at
   all** (`getContext('webgl')` and `'webgl2'` both null; a Unity page freezes the tab),
   so do not use it. Run a throwaway Chrome with software GL, which prints the page's
   console to stderr as `…:INFO:CONSOLE(…)] "<text>"`:

   ```bash
   timeout 150 google-chrome --headless=new --user-data-dir=<scratch>/chrome --no-first-run \
     --use-angle=swiftshader --enable-unsafe-swiftshader --enable-logging=stderr --v=0 \
     http://127.0.0.1:18080/index.html > <log> 2>&1
   ```

   No `google-chrome`: try `chromium`. Neither, or a log with no WebGL context: do not
   install a browser and do not fall back to the extension — record WebGL as *built, not
   run in a browser* with the reason, and ask.

The probe `MonoBehaviour` prints one `[PROBE] <check> <result>` line per check — codes
and booleans, never a URL or a token — and you grep the log for `[PROBE]`:

- the gateway guard: `ConnectAsync()` over the default factory fails
  (`GatewayStoppedException`, state `Closed`);
- consumer tasks that finish a frame later on the main thread, completed plainly and
  with `RunContinuationsAsynchronously`: `MapAsync` over an `IHttpFetcher` of the probe's
  own (the lobby opened by a fake `IWebSocketFactory` that sends a `hello`), and
  `FireAsync` past a handler returning such a task, with a second handler after it. Each
  must print `finished=True`, the map `ok=True onMain=True`, the broker
  `secondOnMain=True`. The native players run the same checks, plus `MapAsync` over
  `HttpFetcher.Default` against `:18778`: `status=404 offMain=True`, waiting by wall-clock
  time (up to 20 s), not by frames — an unthrottled batch-mode player runs hundreds of
  frames before Mono's first `HttpClient` reply
  ([The consumer-task hang](#the-consumer-task-hang)). Two more shapes, on every player,
  each expecting the same lines: the map check on a **new** lobby client (a map is
  cached per client, and a cached one skips the path under test) with `MapAsync` given an
  uncancelled `new CancellationTokenSource().Token` while the fetch is still pending —
  that is `MapFetcher.Observe`, which completes the caller's task from a `ContinueWith`
  rather than an `await`; and both checks with the fetch and the handler written as `async`
  methods awaiting a `TaskCompletionSource` the probe completes from the next `Update`,
  never `Task.Delay`, which needs the pool;
- each Unity transport against `:18777` → `network (0)`, and no `FOLLOWED`; against
  `:18778` → `kv` null, `auth http (404)`, `asset not_found (404)`. A control that reads
  `network (0)` means the servers, not the transports, are wrong;
- `FetchConfigAsync` against `https://auth-dev.yyt.life` with an active `auth` channel from
  `yyt --profile dev channels list --scope all --json` → `network (0)`, because the service
  sends no CORS headers. A success means the service added them: that is a finding for
  `auth-client`'s README § Threads and WebGL, not a probe failure;
- a bundle made as [Against the dev CDN](#against-the-dev-cdn) makes it, in both `CorsSafe`
  modes: the manifest, the file whole, a range over two segment boundaries, the tail, a
  missing file, and the vector key (`asset_corrupt`). Resume-from-part and `NoCache` are
  that section's, not this one's: a browser player has no file to resume. Get the key as
  that section's step 2 does; if this session's permissions refuse `asset key show`, do
  not work around it — hand the user a script that runs it with `umask 077` and writes
  only `internal static class BundleKey { public const string Value = "…"; }` into each
  scratch project's `Assets/`, never printing it, and wait. Never read the key back. After
  the run delete the bundle, every `BundleKey.cs`, the key file, the WebGL build, each
  scratch project's `Library` (it holds the key compiled into `Assembly-CSharp`) and
  `<scratch>/chrome`. If the user declines, record the CDN check as not covered;
- the auth browser flow's client half: `BuildStartUrl`, the nonce in `sessionStorage`
  through a `.jslib` in the scratch project's `Assets/`, a same-tab
  `window.location.assign` to a synthesized return URL, and `ParseRedirect` of
  `Application.absoluteURL` after the reload, plus a foreign nonce → `nonce_mismatch`. The
  fragment's token is a fixed fake (`eyJ.fake-token.sig`), **never** one minted with
  `/debug/token`. The provider hop needs a channel with an OAuth app, which dev does not
  have; say so in the record.

**A check that never prints waited on a thread WebGL does not have**
([unity.md](unity.md#webgl)). Tell that from a slow network with a control that records,
without waiting, whether `Task.Run`, `Task.Delay(1)`, `CancelAfter(1)` and a `Task.WhenAny`
over a `RunContinuationsAsynchronously` task (the actual hang) have run a
couple of seconds later — `false` for all four confirms the player has no pool — and a
per-check `start` line, so the last one printed names the check that hung.

### Last verified

This subsection holds the last **full, two-editor** run, which is what a release needs. Always record the
**commit** as well as the date: a release asks whether this run
covers the code being tagged, and a date alone cannot answer it
([release.md](release.md)). Run from `git archive S` (the embedded projects) and a bare
clone of S (the git-URL projects), never from the working tree, and record S in a commit
that touches nothing outside `rules/`; which later commits keep S valid is
[release.md](release.md) step 2's rule. When you replace this record, grep the file for
the old sha and for `#last-verified`: a link here must not be paired with a sha.

A full run is every check in [In Unity](#in-unity) on both editors. The live sections
([gateway](#against-a-real-gateway), [store](#against-the-dev-store),
[CDN](#against-the-dev-cdn)) are rerun when a non-Unity source they exercise changed since
their last dated run. A recipe step that was skipped is named in the record, and stops the
run being full unless the record says why it does not matter for a tag. The dated
sections after this one are either partial runs, which say what they cover, or pieces of
an earlier full run, which say which one.

**2026-09-30**, at commit `d8ce422`, on Unity Personal, Ubuntu 24.04, with the 2021.3
workarounds above (the `bee_backend` wrapper was put back and its checksum matched).
Everything in Unity ran from `git archive` of that commit and from a bare clone of it
(the live gateway run below is the exception it names); the browser
was headless Chrome 154 with SwiftShader against a dev CDN bundle made for it; the
bundle, its key, the `BundleKey.cs` files, the builds, the projects' `Library` and the
Chrome profiles were deleted afterwards, as the recipe says.

| Check | 2021.3.45f2 | 6000.0.25f1 |
| --- | --- | --- |
| EditMode, all seven packages, one per run | **0 failed**; 890 / 878 / 12 ignored — the twelve [named above](#run-the-package-tests-inside-the-editor) | same |
| Bare-clone git-URL install | 7 in `Library/PackageCache`, 0 *immutable folder*, 7 `Yingyeothon*.dll`, each with `NullableContextAttribute` | same |
| Samples listed and imported | 1 / 1 / 3 / 1 / 1 / 1 / 1 (codec, event-broker, gamebase, kvstore, auth, asset, logger) | same |
| Clean compile of the git-URL project with the samples (`ScriptAssemblies` deleted) | `CompileScripts` seen, 0 errors, 0 warnings — an immutable install suppresses package warnings, so this counts the samples and the harness | same |
| `StandaloneLinux64` Mono, stripping **High** | ran every package through its factories, decrypted an asset vector, each Unity transport answered a `302` as `http (302)` and nothing followed it; the consumer checks all `finished=True` on the main thread; `HttpFetcher.Default` `status=404 offMain=True` | same |
| `StandaloneLinux64` IL2CPP, stripping **High** | same as Mono | same |
| WebGL | linked; in the browser every check of [the recipe](#run-the-webgl-player-in-a-browser) printed what it expects, the per-check `start` lines included: the four consumer checks `finished=True` on the main thread, 3 redirects and 0 followed, and with `CorsSafe` off both ranges `http (206)` `no Content-Range`, as the asset README documents; the no-pool control: all four `false`, as expected | same |

One seam: in the first pass both Mono players printed `default-fetcher finished=False`
— the probe waited by frames, which the recipe now forbids. Only the scratch probe
changed; both players of each editor were rebuilt from the same `d8ce422` packages and
printed `status=404 offMain=True` and every other line as before, so the run stays full.

Skipped from the recipe: the vector-key read ran in the default `CorsSafe` mode only,
not in both; it tests the key, not the request plan the mode changes. Not called for, so
not run: taking a `csc.rsp` away (no compiler flag changed), and pasting
`docs/getting-started.md` (unchanged since [the first tag](#the-first-tag-installed), and
the approved public API with it). The previous full run, at `6ac4ccf`, is the one the
first tag was cut from.

The live sections: [Against a real gateway](#against-a-real-gateway) ran on the tree of
`d8ce422` before its last doc-comment edits (to `GatewayLobbyClient.cs` and a sample):
`hello` with `aoi`, `MapAsync` over `HttpFetcher.Default` parsed the dev lobby's map to an
object and a second call returned the cached one, one `snapshot` for a `pos`, a clean
close. Its step 4, the forced reconnect, was not repeated: no reconnect code changed. The
store's and the auth service's live runs were not repeated: `kvstore-client` and
`auth-client` changed only under `Runtime/Unity/` since. Nor the CDN's console run:
`asset-client`'s one change outside `Runtime/Unity/` since is `BodyReader.Race`
(`6ac4ccf`), whose path without a synchronization context — the console app's — keeps
the old behaviour and is covered by the asset tests; its context path is the WebGL row.
The provider hop of the auth flow and a stalled server in a browser are in *Not covered*
below.

The two editors agreed on every row. When they do not, that is the
finding — 2021.3 is the floor for a reason ([Which editor](#which-editor)).

**Do not carry a player row forward on "the sources did not change".** The 2026-09-01
run added a `csc.rsp` per asmdef, which is not a source change and which
`git diff -- 'packages/**/Runtime/**'` cannot show at all while the files are untracked
— and turning the nullable context on makes Roslyn emit `NullableAttribute`,
`NullableContextAttribute` and `EmbeddedAttribute` into the Unity-built assemblies. That
is exactly the metadata `ManagedStrippingLevel.High` and `Runtime/link.xml` are tested
against. A compiler flag is a build change even when no `.cs` moved.

### The first tag, installed

**2026-09-30**, a partial run: [release.md](release.md) step 7 for the first tag, which
the user cut on commit `c2f6339` from the verified sha `6ac4ccf`. It adds nothing to
that full run. On 2021.3.45f2 (with the
2021.3 workarounds above; the `bee_backend` wrapper was put back and its checksum
matched) and 6000.0.25f1, each editor got two **new** scratch projects whose
`manifest.json` names
`https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.<name>#<the tag>`
— GitHub itself, not a local clone. The URLs were written into the manifest in one edit.

| Check | 2021.3.45f2 | 6000.0.25f1 |
| --- | --- | --- |
| All seven packages | 7 in `Library/PackageCache`, every `packages-lock.json` hash `c2f6339`, 0 *immutable folder*, 0 `error CS`, 7 `Yingyeothon*.dll`, **no** `Yingyeothon*Tests*.dll` anywhere in `Library` | same |
| Only [getting-started](../docs/getting-started.md) §1's four URLs, with its §3 and §4 code pasted into `Assets/` | 4 in `PackageCache`, 4 `Yingyeothon*.dll`, `CompileScripts` seen, 0 errors, 0 warnings from the pasted scripts (the packages' own are suppressed from an immutable install) | same |

The page as of `c2f6339`: §3's lines 63–70 verbatim inside an
`async Task<string> SignIn(string providerAccessToken, string idToken)`, plus the
`ExchangeIdTokenAsync("google", idToken)` call its prose names, and §4's `LobbyQuickstart`
byte for byte. §5's line is the same `Pos` call §4 compiles, with placeholder arguments.
No sample was imported from this install; the full runs import them from a bare clone
([Last verified](#last-verified)).

### The second tag, installed, and the upgrade to it

**2026-09-30**, a partial run: [release.md](release.md) step 7 for the second tag, which
the user cut on commit `22741f7` from the verified sha `d8ce422`. It is not a *Last
verified* run and does not satisfy release.md step 2. Same editors, workarounds and
GitHub URLs as [the first tag](#the-first-tag-installed); each editor got two **new**
projects, one installed at the second tag and one installed at the first and then
upgraded, the URLs written into `manifest.json` in one edit each time.

| Check | 2021.3.45f2 | 6000.0.25f1 |
| --- | --- | --- |
| All seven packages at the second tag | 7 in `Library/PackageCache`, every `packages-lock.json` hash `22741f7`, 0 *immutable folder*, 0 `error CS`, 7 `Yingyeothon*.dll`, **no** `Yingyeothon*Tests*.dll` anywhere in `Library` | same |
| The upgrade: all seven at the first tag, opened, then every `#v…` fragment moved to the second in one `manifest.json` edit and the project opened again | before: every lock hash `c2f6339`; after: every lock hash `22741f7`, 7 in `PackageCache` and none left at the old commit, `CompileScripts` seen, 0 errors, 0 warnings, 7 `Yingyeothon*.dll`, no Tests dll; all seven cached trees match `git archive` of the tag file for file; the recompiled dlls differ from the fresh project's (2021.3 does not build deterministically) | same, except that each cached `package.json` gains Unity's `_fingerprint` line; all seven recompiled dlls are byte-identical to the fresh project's |

The upgrade reopened the project in batch mode; what it did not reach is under *Not
covered*. `getting-started.md` was not pasted again: only its four URLs changed since the
first tag.

### The consumer-task hang

**2026-09-30**, 6000.0.25f1, WebGL in headless Chrome, a control run: the probe's
consumer checks (above) against the packages of the first tag (`c2f6339`). All four
printed `finished=False` — `MapAsync` over the probe's own fetcher and `FireAsync` past
a handler, each completed a frame later both plainly and with
`RunContinuationsAsynchronously` — while `DiagProbe` showed no pool
(`Task.Run completes=False`). The cause is in [unity.md](unity.md#webgl): under
`ConfigureAwait(false)` the runtime will not inline a continuation on a thread with a
synchronization context, so it went to the pool. The fix's own run is
[Last verified](#last-verified), at `d8ce422`.

### The WebGL browser run

**2026-09-30**, Unity Personal, Ubuntu 24.04, headless Chrome 154 with SwiftShader, the
recipe in [Run the WebGL player in a browser](#run-the-webgl-player-in-a-browser), in the
commit that records it (its parent is `79f981d`). The first pass, at `79f981d`, found the
`Task.WhenAny` hang; the second ran the fix this commit carries — its `AssetHttp.cs` as
committed but for comments — copied into both scratch projects, with the asset EditMode
suite first (**0 failed**, 109 / 108 / 1 skipped, on each editor; the ordering test
`AnAnswerThatArrivedBeforeTheCancelWins` was added after it and runs in the full run). Only the WebGL rows are
covered here; the full two-editor run that covered this code was at `6ac4ccf`.

| Check | at `79f981d`, both editors | with the fix, 2021.3.45f2 and 6000.0.25f1 |
| --- | --- | --- |
| Gateway guard | `GatewayStoppedException`, `Closed` | same |
| kv / auth redirect | `network (0)` each, nothing followed | same |
| asset redirect | **never finished**; nothing after it ran | `network (0)`, nothing followed |
| `:18778` control | not reached | `kv` null, `auth http (404)`, `asset not_found (404)` |
| dev auth config | not reached | `network (0)` |
| dev CDN, `CorsSafe` on | not reached | manifest, whole, range over two boundaries, tail: all correct; missing `not_found (403)`; vector key `asset_corrupt` |
| dev CDN, `CorsSafe` off | not reached | manifest and whole correct; both ranges `http (206)` `no Content-Range` |
| auth browser flow, client half | not reached | reload kept query and fragment; `ParseRedirect` ok; foreign nonce `nonce_mismatch` |
| `Task.Run`, `Task.Delay`, `CancelAfter`, `WhenAny` over an asynchronously completed task | — | none ran (6000.0.25f1); a plain `await` resumed |

The hang is [unity.md](unity.md#webgl)'s: every asset read through the Unity transport
waited on `Task.WhenAny`.

### The run that added `auth-client` and `asset-client`

**2026-09-30**, Unity Personal, Ubuntu 24.04, **6000.0.25f1 only**, for `8e4ad6f` (the
commit that records this run; its parent carries the code). One seam: the WebGL build
and the non-asset EditMode runs used the Unity transports one revision before
`d24488d`'s last edit (the path that returns a refused `3xx` as `http` was narrowed to
`ConnectionError` and to a request that did not time out). The Mono and IL2CPP players
and the asset EditMode run were repeated on the final code, with the same results.
EditMode compiles those transports but no test calls them, so the one thing unrun on the
final code was the WebGL link (since run: [Last verified](#last-verified)).

**This run did not satisfy [release.md](release.md) step 2**: 2021.3.45f2 was not run,
because that session was not permitted to swap a binary inside the editor install (the
`bee_backend` wrapper of
[Two things Ubuntu 24.04 breaks](#two-things-ubuntu-2404-breaks-in-unity-20213)). The full
run it called for was at `6ac4ccf`.

| Check | 6000.0.25f1 |
| --- | --- |
| EditMode, all seven packages, one package per run | **0 failed**: 883 / 871 / 12 skipped — the eleven WebSocket ones above and one symbolic-link test that needs .NET 6 |
| Bare-clone git-URL install | all seven resolved into `Library/PackageCache`, **0** *immutable folder* lines, all seven `Yingyeothon*.dll`, each carrying `NullableContextAttribute` |
| Samples listed and imported | one per `samples[]` entry: 1 / 1 / 3 / 1 / 1 / 1 / 1 (codec, event-broker, gamebase, kvstore, auth, asset, logger) |
| Clean compile (`ScriptAssemblies` deleted) | `CompileScripts` seen, 0 errors, 0 warnings |
| `StandaloneLinux64` Mono, stripping **High** | built and ran: every package through its factories, an asset vector decrypted in the player (through an in-memory transport), and all three Unity transports against a local `302` — none followed it, each answered `http (302)` |
| `StandaloneLinux64` IL2CPP, stripping **High** | built and ran, the same lines |
| WebGL | compiled and linked; not opened in a browser |

Two things only this run could have found. The first attempt hung the whole EditMode run
at `Running tests for ExecutionSettings` — the asset client's file-download tests blocked
the main thread on real file IO ([testing.md](testing.md)); per-package runs are what
located it. And the kvstore `UnityWebRequestTransport` used Unity's default of 32
redirects, so a redirect would have carried the player's `Authorization` header to
whatever host it named; this run is what added `redirectLimit = 0` to it (auth's and
asset's had it). With the limit at 0 Unity reports a refused redirect as a
`ConnectionError` that still carries the `3xx`, which the transports first surfaced as
`network (0)`; they now hand it back as the reply it is. The probe, for the next run: a
`MonoBehaviour` pointing each client at `http://127.0.0.1:18777` through its Unity
transport, and beside the player

```python
import http.server
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path.startswith("/elsewhere"): print("FOLLOWED", flush=True)
        self.send_response(302); self.send_header("Location", "http://127.0.0.1:18777/elsewhere")
        self.send_header("Content-Length", "0"); self.end_headers()
http.server.HTTPServer(("127.0.0.1", 18777), H).serve_forever()
```

A `FOLLOWED` line is the failure.

### The run that added `kvstore-client`

**2026-09-06**, at the commit that adds `kvstore-client` (the child of `3714e39`; this
sentence is the one line its verification did not cover), Unity Personal, Ubuntu
24.04, same recipe. Unity 6 ran the full matrix; 2021.3 ran EditMode only, with the
libssl and `bee_backend` workarounds above (the wrapper was put back and the file
size checked).

| Check | 2021.3.45f2 | 6000.0.25f1 |
| --- | --- | --- |
| EditMode, all five packages | **0 failed**; 661 / 650 / 11 ignored, the eleven above | **0 failed**; 661 / 649 / 11, then the one `KvStore` failure fixed and its 202 rerun alone with `--filter Yingyeothon.KvStore.Tests`: 202 / 202 / 0 |
| Samples listed and imported | not run | one per `samples[]` entry: 1 / 1 / 4 / 1 / 1 |
| Clean compile (`ScriptAssemblies` deleted) | not run | `CompileScripts` seen, 0 errors, 0 warnings |
| `StandaloneLinux64` Mono, stripping **High** | not run | built; the player reached every package through its factories and `KvStoreClient` over `UnityWebRequestTransport.Instance` answered `kv unauthorized (401)` from `doc-dev` with a fake token, which is the round trip |
| `StandaloneLinux64` IL2CPP, stripping **High** | not run | built, and the player printed the same |
| WebGL | not run | compiled and linked; not opened in a browser |

Two editor-only failures came out of it and neither is reachable from `dotnet test`:
a blocking `Assert.ThrowsAsync` over a yielding path deadlocked the whole run, and
`Has.Count` refused an array behind `IReadOnlyList` under the editor's NUnit
([testing.md](testing.md)). The `HttpListener` transport tests run under Mono as
plain HTTP; only the WebSocket half of the gateway's is ignored there.

### The install path was broken, and the 2026-09-01 run is what found it

**A git-URL install of `70c334d` compiled nothing.** All four packages resolved, and then
every asset in them was ignored — 448 log lines of *"has no meta file, but it's in an
immutable folder"* — so `Library` ended up with **zero** `Yingyeothon*.dll`, and
`docs/getting-started.md` §1 and `docs/unity.md` § Installing described a path that did
not work. UPM requires a `.meta` for every asset of a package consumed from an immutable
source, and this repository did not commit them. Copying the packages hides it, and
copying is what every verification before it had done.

Confirmed both directions on 6000.0.25f1, from bare clones:

| Bare clone contains | Assets ignored | Assemblies compiled | Samples |
| --- | --- | --- | --- |
| no `.meta` (the tree at `70c334d`) | 448 | **none** | — |
| `.meta` committed | 32 (the unpaired `csc.rsp.meta`) | all four | — |
| `.meta` **and** `csc.rsp` committed | **0** | all four, 0 errors, 0 CS8632 | **7 import** |

**Fixed by committing them**, in the commit that adds `scripts/unity-meta.sh`. The 160 files there are the ones Unity 6 wrote into an embedded copy, each
given a final newline, and the same bare-clone check against that tree on 6000.0.25f1
gave: all five packages resolved into `Library/PackageCache`, **0** *immutable folder*
lines, all five `Yingyeothon*.dll`, all eight samples imported (1 / 1 / 4 / 1 / 1), and a
forced clean compile (`ScriptAssemblies` deleted, `CompileScripts` seen) with 0 errors
and 0 warnings. **2021.3.45f2 was not run**: its `bee_backend` hang needs the wrapper in
[Two things Ubuntu 24.04 breaks](#two-things-ubuntu-2404-breaks-in-unity-20213), and
that session was not permitted to swap a binary inside the editor install. The floor ran
in the full run at `6ac4ccf`.

The same install settles whether a `csc.rsp` is honoured from `Library/PackageCache`,
which a warning count cannot — Unity suppresses warnings from an immutable package. The
metadata can: nullable context makes Roslyn emit `NullableContextAttribute`, and each
of the five `Yingyeothon*.dll` carried it while `Assembly-CSharp-Editor.dll`, which has
no rsp, did not (`grep -a -c NullableContextAttribute <dll>`: 1 each, control 0).

### Against the dev gateway, 2026-09-01

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

- **The asset client's conformance vectors under a git-URL install.** Its EditMode run
  reads them from
  `Packages/com.yingyeothon.asset-client/Tests/Fixtures`, which only an embedded (copied)
  package has — the recipe copies them, and a git-URL install does not compile its tests
  at all. Its wire half is in [Against the dev CDN](#against-the-dev-cdn); auth-client's
  was checked against `auth-dev` with the real `AuthHttpClientTransport` as committed
  (2026-09-30): config, verify (a live token and a forged one), a fake provider token
  (`401`), the wrong credential kind (`400`), and `/start` for a redirect off the
  allowlist (`403`) and on it with the nonce query (`302` to the provider).
- **`docs/getting-started.md`'s code compiled in Unity** — only as of the first tag
  ([The first tag, installed](#the-first-tag-installed)). A later edit to its code blocks
  is uncompiled until pasted into a scratch project again. The rest of `docs/` — the
  alias in `docs/unity.md` § Logging to the editor console among them — has never been
  pasted at all; only the samples stand in for it.
- **The upgrade in a running editor, through the Package Manager window, or one that
  adds a package the old tag lacked.** The one upgrade run
  ([The second tag, installed](#the-second-tag-installed-and-the-upgrade-to-it)) reopened
  the project in batch mode, with the same seven packages on both tags.
- **The probe's cancellable-token and `async`-method shapes**
  ([the WebGL recipe](#run-the-webgl-player-in-a-browser)). No run has recorded them;
  drop this once one does.
- **Adding the URLs one at a time in Package Manager.** Every run wrote them into
  `manifest.json` at once, so the dependency-first order of `docs/getting-started.md`
  §1 and `docs/unity.md` § Installing, and what Package Manager reports without it, has
  never been exercised. `UnityEditor.PackageManager.Client.Add` per URL through
  `-executeMethod` is the headless form of that click.
- **A stalled server in a browser.** On WebGL only `UnityWebRequest.timeout` bounds a
  request; no browser run has waited it out.
- **The auth provider hop in a browser.** No dev auth channel has a provider, so the
  browser flow ran with a synthesized return; and the service calls fail as `network`
  there until the service sends CORS headers.
- **`CorsSafe` in Firefox and Safari.** Only Chrome has run it.
- The provider exchange with a **real** GitHub access token. There is no provider
  credential here, so only its refusal path was exercised.
- **The gateway actually sending `4002`.** Not reachable from a client for the reason
  above, and this repository does not own the gateway to restart it. What the client
  does with the close is covered through the public `IWebSocketFactory` seam, where a
  fake socket delivers it: the `4002` case of `LobbyReconnectTests`' reconnect test
  (the backoff timing, a fresh `hello`, an empty peer map) and of `GameClientTests`'
  (a reconnect and a working `Send`; a `q` channel has neither `hello` nor peers).

## Making states reachable without infrastructure

Use the injection seams rather than standing up servers:

- A capturing `ILogWriter` to observe internal decisions.
- `FakeWebSocket` to produce any close code on cue, including ones a real gateway
  will not send.
- `FakeClock` to reach a timeout instantly.
- A local `HttpListener` for the real transport.

Never add a verification-only member to the public API. If a state is unreachable
through the public surface, that is a design finding.
