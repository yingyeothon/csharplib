# Security

Inherited from tslib's own adversarial reviews, trimmed to what a client can get
wrong.

## The token

- The channel JWT travels in the WebSocket subprotocol list (`["bearer", token]`),
  never in the URL, where it would land in access logs.
- It must never reach a log line, at any level. Client-side logs name the channel,
  the game, the user and the close code — nothing else. There is a test for this in
  both client suites, with a positive control.
- A subprotocol carrying non-token characters is refused at construction rather than
  at connect time, so a malformed credential fails visibly instead of retrying — but
  **that refusal's message must not quote the character it found**. The second
  subprotocol *is* the token; the message became a close reason that `Stop()` logs at
  `Info`, so one character of the credential reached the log. Report the **index**.
  This rule has no severity threshold, and "only one character" is not an exemption.
- The two "never writes the token" tests both drive `FakeWebSocketFactory` with a
  well-formed token, so neither can reach the real transport's validation. A test for
  that path has to use the real factory and a token that can actually fail it.

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
