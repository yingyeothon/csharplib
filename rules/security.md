# Security

Inherited from tslib's own adversarial reviews, trimmed to what a client can get
wrong.

## Public repository

- This repo is **public** on GitHub and stays public. Treat every commit as
  world-readable, including commit messages.
- It has no infrastructure and needs no credentials, which makes the rule simple:
  **nothing of a credential's shape belongs here at all.** There is no `local/`, no
  `.env`, no deploy script — a file of that shape is a mistake, not configuration.
- Hostnames are allowed only where the sibling **public** `service` repo already
  publishes them (`gw.yyt.life`, and the dev endpoints plus the `POST /debug/token` +
  `x-debug-key` recipe named in [manual-verification.md](manual-verification.md)).
  Repeating what is already public is not a disclosure; being the first to publish
  something is. Check `git grep` in `service` before adding a new one, and never add
  a stateful host, database or account name — those live in the private ops repo.
- `docs/` names `console`, `auth`, `gw`, `d` and `match`.`yyt.life` because the public
  `service` repo publishes all five (`cli/README.md`, `services/auth/README.md`,
  `gateway/README.md`, `services/match/README.md`). **`POST /debug/token` and its
  `x-debug-key` stay out of `docs/`**: they are a dev-only hook, and a consumer document
  that teaches them invites a game to ship against them.
- `yyt-platform.md` (untracked, in the parent directory), `service/todo/**` and
  `service/local/**` are private. Nothing from them belongs here, however useful.
- The one credential-shaped literal in the tree is the test fixture
  `eyJ.secret-token.sig`, which is three dot-separated words that look like a JWT and
  are not one. The "never logs the token" tests need a literal to search for. Keep it
  obviously fake and keep the `.gitleaks.toml` allowlist entry pointed at that exact
  string, not at the files that hold it.
- **Defenses, all required, none optional:**
  - `.gitignore` — build output, `.meta`, `.env*`, `.envrc`, `local/`.
  - `scripts/git-hooks/pre-commit` — refuses those paths even when force-added, then
    `gitleaks protect --staged`.
  - `scripts/git-hooks/pre-push` — re-checks the pushed tip's whole tree and runs
    `gitleaks detect` over the **entire history reachable from that tip**, so a commit
    that got in with `--no-verify`, an amend or a rebase is still caught. Then the
    build gate, unless `SKIP_CI_GATE=1`.
  - CI `secrets-scan` (gitleaks, full history) and `tracked-paths` — the same checks
    on a machine whose hooks were never installed.
  - `scripts/install-git-hooks.sh` sets `core.hooksPath`, and `dotnet build` runs it
    once per working tree (`Directory.Build.targets`). A guard nobody remembers to
    install is not a guard.
- **Never `--no-verify`.** If a hook is wrong, fix the hook.
- Two ways a shell guard fails open, both paid for in the `service` repo and both
  written into every guard here — the hooks, `validate-packages.sh` and
  `check-docs.sh`. Do not "simplify" either away:
  - `grep -q` exits at the first match, SIGPIPEs the upstream command, and under
    `set -o pipefail` that 141 makes the test **false**. Capture and count instead.
  - One NUL byte anywhere in a stream makes grep call the rest binary and stop
    matching. Every grep in a guard passes `-a`.
- **Prove any change to a guard by watching it refuse.** A throwaway staged file that
  should be blocked and an ordinary edit that should pass, for a hook; a deliberately
  broken input for a script. A guard that has only ever been seen saying yes has not
  been tested — and doing this is what found a prefix match in `check-docs.sh` that let
  a renamed type through, and three fail-open reads in `validate-packages.sh`, one of
  which passed while the assembly and the manifests carried different versions.
- Make the broken input in a copy of the repo under `/tmp`, not in place. Reverting it
  with `git checkout <file>` also discards any unstaged edit you had in that file
  ([workflow.md](workflow.md)).
- A leak already in history is not fixed by a new commit. Rewrite it
  (`git filter-repo --replace-text`) and force-push, and assume anything already
  cloned, forked or cached stays out. If a real credential ever lands here, rotating
  it comes first.

## The token

- The channel JWT travels in the WebSocket subprotocol list (`["bearer", token]`),
  never in the URL, where it would land in access logs.
- It must never reach a log line, at any level. Client-side logs name the channel,
  the game, the user and the close code — nothing else. There is a test for this in
  both client suites, with a positive control.
- **The token crosses a public extension point.** `IWebSocketFactory.Create` is handed
  `WebSocketCreateContext.SubProtocols`, which is `["bearer", "<the raw JWT>"]`, and a
  WebGL build is *expected* to implement one. A credential crossing an extension point
  needs the warning at the extension point, not only in a rules file: say so in the XML
  doc comment on the seam, so it reaches the implementer's IDE.
- A subprotocol carrying non-token characters is refused at construction rather than
  at connect time, so a malformed credential fails visibly instead of retrying — but
  **that refusal's message must not quote the character it found**. The second
  subprotocol *is* the token; the message became a close reason that `Stop()` logs at
  `Info`, so one character of the credential reached the log. Report the **index**.
  This rule has no severity threshold, and "only one character" is not an exemption.
- The two "never writes the token" tests both drive `FakeWebSocketFactory` with a
  well-formed token, so neither can reach the real transport's validation. A test for
  that path has to use the real factory and a token that can actually fail it.

## Building what goes out

- **Escape at one choke-point, and test the escaping.** Everything this SDK puts on the
  wire goes through `JsonWriter`/`JsonObjectBuilder` or `GatewayUrl.Build`; never
  assemble a frame or a URL by concatenation somewhere else. `rules/tooling.md` records
  the trap that has already been paid for here — `Uri.ToString()` **unescapes** for
  display, so anything going on the wire uses `Uri.AbsoluteUri` — and that is a
  security finding, not a formatting one.
- **A size cap is in bytes; a string length is in characters.** The inbound cap is
  derived from the gateway's outbound frame size, which Go counts in bytes. Measuring
  it with `string.Length` lets a multi-byte payload through at up to three times the
  intended size. The same applies to every field limit the gateway states.
- Never interpolate untrusted data into a wire protocol. The client's own fields are no
  exception: a zone name or a `dir` built from player input is peer data by the time it
  reaches the frame.

## What not to log

- Never a receive buffer or a frame body. It is whatever the peer just sent — a
  stored value, a credential echo, a game payload. Log its size or its `type`.
- Never a close reason's text; log its length. The gateway may quote what the client
  sent back into it.
- Never an `event` payload, a `q` frame, or a `map()` body. Those are game data, and
  `Debug` is not an exemption: a consumer plugs in a writer that persists forever.
- For a refusal, log the code, not the message.
- **A peer-chosen string is not a safe diagnostic.** A frame's `type` is whatever the
  peer put there, and `"expected hello, got " + type` reached a consumer's log writer
  unbounded and with its control characters intact — a log-volume and log-injection
  vector. Cap it and strip the control characters (`Normalize.Diagnostic`).
- A URL the server named is not automatically safe to log either: `mapUrl` is public
  today, but a pre-signed one would put its signature in a persistent writer. Log the
  length.
- **An exception message built from the input is a frame body.** The JSON parser used
  to say `Number '123456789e400' is out of range` and `Unknown escape sequence '\Q'`,
  and both went straight into `ProtocolError` and from there into whatever writer the
  consumer installed. A refusal now reports a `JsonParseError` and an offset, and the
  test that keeps it that way asserts the message equals a template computed from the
  code and the offset — an equality nothing derived from the input can satisfy.

## Trusting the wire

- `enter` and `leave` are the gateway's own bookkeeping — they decide which member a
  connection speaks for — so the client refuses to send them locally. Removing that
  check is a regression, not a simplification.
- `connectionId` is the only field an actor may trust. A client must not invent one.
- Capability checks in this SDK are a courtesy that gives a fast local error; the
  gateway enforces them. Never treat a client-side check as the enforcement.

## Fetching the map

- The map asset is public and immutable, so the request carries no credentials.
  Keep it that way: adding a header there sends the token to a CDN.
- The URL still comes off the wire, so the fetcher needs bounds even without
  credentials: a timeout, a response-size cap, and a small redirect budget. Without
  them a channel can point the client at an arbitrary host for 100 seconds and two
  gigabytes, and hand the body to the game.

## Review habit

- A change touching the **wire protocol, the token, or what reaches a log line** takes
  security as its third review angle ([workflow.md](workflow.md) owns the slot). The
  defects tslib inherited this way — command injection through interpolated user data,
  a credential logged one level up from the secret — fail no test.
- **The leak is usually one level up from the secret.** Logging a whole frame to report
  a refusal prints the payload; logging a close reason prints what the peer chose;
  logging an exception message built from the input prints the input. Log the decision,
  not the object that carried it.
