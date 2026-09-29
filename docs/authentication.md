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

## The client

`com.yingyeothon.auth-client` is this page in code — its
[README](../packages/com.yingyeothon.auth-client/README.md) has the install URL, every
call and every error code. One client per auth channel:

```csharp
using Yingyeothon.Auth;

var auth = AuthClient.Create(new AuthClientOptions
{
    BaseUrl = "https://auth.yyt.life",       // https://auth-dev.yyt.life on dev
    ChannelId = "auth_0123456789abcdef",
    // Transport = AuthUnityWebRequestTransport.Instance,   // on WebGL
});

AuthChannelConfig config = await auth.FetchConfigAsync();   // the nine fields above
```

## Exchanging a provider credential

The shape a native Unity client wants, because it is one request and no browser:

```csharp
ChannelToken token = await auth.ExchangeAccessTokenAsync("github", gitHubAccessToken);
ChannelToken other = await auth.ExchangeIdTokenAsync("google", googleIdToken);
```

On the wire that is `POST /c/{authChannelId}/token` with
`{ "provider": "github", "accessToken": "…" }`, answered `{ "jwt", "userId", "exp" }`.
**Google requires `idToken`** and GitHub `accessToken`; the wrong one is a `400`, which
is why they are two calls.

## The browser redirect flow

When the player has no provider token yet, `GET /c/{ch}/start?provider=…&redirect=…`
sends them through the provider and finally redirects to **your** URL with the result in
the fragment: `{yourUrl}#token=…&userId=…&exp=…`.

```csharp
string nonce = AuthClient.NewNonce();          // keep it until the browser comes back
Uri start = auth.BuildStartUrl("github", new Uri("https://game.example/signin"), nonce);
Application.OpenURL(start.AbsoluteUri);        // on WebGL, navigate the same tab instead

// ... once your build has received the URL the browser returned to:
ChannelToken token = auth.ParseRedirect(returned, nonce);
```

`redirect` must be on the channel's allowlist — matched on origin and path prefix, so the
nonce query `BuildStartUrl` adds is admitted — or the request is refused with `403`, and
`yyt channels update <auth> --redirect …` **replaces the whole list**, so pass every URL
each time. It must be `https`, or `http` only for `localhost`, `127.0.0.1` and `[::1]`,
with no userinfo or fragment — `BuildStartUrl` refuses those itself.
`/start` is a browser route: a refusal there is an error page in the browser, not a
status your game can read.

Two things a client must get right, and `ParseRedirect` does the first:

- **The nonce.** `BuildStartUrl` puts it in the redirect's query and `ParseRedirect`
  checks it in constant time. Without it, a link someone else constructed completes a
  sign-in in your client, as them.
- **Discard the returned URL** once read. Its fragment is a credential.

**Receiving the redirect is your build's job** — an app link on a phone, the page
itself on WebGL, a loopback page on desktop. On WebGL this flow is also the only one that
works today: the exchange, `VerifyAsync` and the config fetch are cross-origin calls the
auth service does not yet allow. [Unity § Signing in](unity.md#signing-in)
has each of them and what goes on the allowlist.

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

## Checking a token

```csharp
ChannelToken? stillValid = await auth.VerifyAsync(kept.Jwt);   // null on 401
```

That is `GET /c/{authChannelId}/verify` with `Authorization: Bearer <jwt>`, answered
`{ userId, exp, channelId }` or `401`; by hand it is one `curl`. A `404` is an unknown
channel id and a `410` an expired or disabled channel — `VerifyAsync` throws those as
`AuthException` (`http`) rather than returning null. This is the fastest way
to tell a bad token from a bad channel id when a connection will not open —
[Troubleshooting](troubleshooting.md#it-connects-then-immediately-stops) uses it as the
first check.
