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
  at connect time, so a malformed credential fails visibly instead of retrying.

## What not to log

- Never a receive buffer or a frame body. It is whatever the peer just sent — a
  stored value, a credential echo, a game payload. Log its size or its `type`.
- Never a close reason's text; log its length. The gateway may quote what the client
  sent back into it.
- Never an `event` payload, a `q` frame, or a `map()` body. Those are game data, and
  `Debug` is not an exemption: a consumer plugs in a writer that persists forever.
- For a refusal, log the code, not the message.
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
