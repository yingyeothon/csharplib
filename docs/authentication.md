# Authentication

Every socket this SDK opens carries a **channel JWT**. This page is the client's half
of getting one.

> The auth service belongs to the [`service`](https://github.com/yingyeothon/service)
> repository. `services/auth/README.md` specifies the endpoints and
> `docs/auth-game-contract.md` the token; both are normative, and if they disagree with
> this page they are right.

## The shape of it

Your team provisions an **auth channel** in the console. It holds an OAuth app you
registered (GitHub or Google), an `audience`, a token lifetime, and an allowlist of URLs
it will hand a token back to. A player signs in through that provider, the auth service
issues a JWT for your channel, and you put it in `GatewayClientOptions.Token`.

Base URL: `https://auth.yyt.life` (dev: `https://auth-dev.yyt.life`).

`GET /c/{authChannelId}/.well-known/config` is unauthenticated and returns nine fields:
`channelId`, `issuer`, `audience`, `tokenTtlSec`, `providers`, `callbackUrls`,
`startUrl`, `redirectAllowlist` and `expiresAt`. Read it at startup and you hard-code
only a base URL and a channel id.

## Exchanging a provider credential

The shape a native Unity client wants, because it is one request and no browser:

```
POST https://auth.yyt.life/c/{authChannelId}/token
{ "provider": "github", "accessToken": "<the provider's access token>" }

200 { "jwt": "…", "userId": "8d0f…", "exp": 1767225600 }
```

**Google requires `idToken`** rather than `accessToken`, and GitHub requires
`accessToken`; sending the wrong one is a `400`. The `SignIn` sample is this request,
with the bounds and the never-log-the-body rule already in it.

## The browser redirect flow

When the player has no provider token yet, `GET /c/{ch}/start?provider=…&redirect=…`
sends them through the provider and finally redirects to **your** URL with the result in
the fragment: `{yourUrl}#token=…&userId=…&exp=…`. `redirect` must be on the channel's
allowlist or the request is refused with `403`, and
`yyt channels update <auth> --redirect …` **replaces the whole list**, so pass every URL
each time.

Two things a client must get right, and neither is optional:

- **Put a nonce of your own in the redirect and check it comes back.** Without it, a
  link someone else constructed completes a sign-in in your client, as them.
- **Discard the fragment** once read. It is a credential.

[Unity § Signing in](unity.md#signing-in) has the two ways a Unity build can be the
destination of that redirect.

## What the token contains

HS256, carrying only registered claims — no PII, by design, because claims reach logs.
The full claim table is in `docs/auth-game-contract.md`. Two of them matter to a client:

- **`sub` is the identity**, and `hello.UserId` echoes it. Compare avatars against what
  the gateway told you, not against the `userId` in the redirect fragment.
- `sub` is derived from `sha256(channelId + ":" + provider + ":" + providerUserId)`, so
  **a channel with two providers gives one human two identities** — two characters, two
  inventories, two party memberships. There is no account linking and none is planned.
  Pick one provider per channel.

## Lifetime, expiry and reconnect

- The token lives for the channel's `tokenTtlSec`, **24 hours by default**, up to 30
  days.
- **There is no refresh endpoint and no revocation.** Re-authenticating means running
  the flow again.
- **Reconnect with the same token.** This SDK does, and that is intended: the gateway
  caches the verification result until `exp`.
- One token serves the lobby socket, the dungeon socket, your own game API and the doc
  store. Nothing is ever re-signed.

An expired token is refused at the handshake, which a client can only see as a close
before the socket opened — so a stale token ends in `Stopped` rather than retrying
forever. See
[Connection lifecycle § Handshake failures](connection-lifecycle.md#handshake-failures).

Storing the token is your call, and it is a credential: it grants a player's identity
for as long as the channel's TTL says — a day by default. Prefer re-running the flow at launch over persisting it, and never
write it to a log — this SDK never does, at any severity.

## Checking a token by hand

```
GET https://auth.yyt.life/c/{authChannelId}/verify
Authorization: Bearer <jwt>
```

Answers `{ userId, exp, channelId }`, or `401`. This is the fastest way to tell a bad
token from a bad channel id when a connection will not open —
[Troubleshooting](troubleshooting.md#it-connects-then-immediately-stops) uses it as the
first check.
