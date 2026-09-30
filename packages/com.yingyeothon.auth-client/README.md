# Yingyeothon.Auth

The client's half of getting a yyt **channel JWT**. One `IAuthClient` per auth channel:
it reads the channel's public config, builds the browser sign-in URL with a nonce,
reads the redirect that comes back, exchanges a provider credential directly, and
checks a token. Opening the browser, receiving the redirect and storing the token are
the game's job. Nothing here logs, throws or returns a message that contains a token, a
fragment, a response body or a URL.

The wire contract is the `service` repository's: `services/auth/README.md` and
`docs/auth-game-contract.md` there are normative, and where the README and the code
under `services/auth/src/` disagree, the code is what answers.
[docs/authentication.md](../../docs/authentication.md) here is the integration guide,
including how a Unity build receives the redirect.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.auth-client
```

**No release has been tagged yet**, so this URL tracks `main`; append `#<tag>` to
pin one as soon as there is one.

Depends on `com.yingyeothon.codec` and `com.yingyeothon.logger`; a git-URL package
cannot resolve them, so add both first. It does not depend on `gamebase-client` or
`kvstore-client`, and neither depends on it: the token is a string either takes.

## Usage

```csharp
using Yingyeothon.Auth;

var auth = AuthClient.Create(new AuthClientOptions
{
    BaseUrl = "https://auth.yyt.life",       // https://auth-dev.yyt.life on dev
    ChannelId = "auth_0123456789abcdef",     // from the console
});

// Native: one request, no browser, for a player who already holds a provider token.
ChannelToken token = await auth.ExchangeAccessTokenAsync("github", gitHubAccessToken);
ChannelToken other = await auth.ExchangeIdTokenAsync("google", googleIdToken);

// Browser: open the start URL, keep the nonce, read the URL the browser returns to.
string nonce = AuthClient.NewNonce();
Uri start = auth.BuildStartUrl("github", new Uri("https://game.example/signin"), nonce);
// ... later, with the URL your redirect receiver got:
ChannelToken signedIn = auth.ParseRedirect(returned, nonce);   // then drop `returned`

// A kept token: still good?
ChannelToken? stillValid = await auth.VerifyAsync(token.Jwt);   // null on 401
```

`token.Jwt` is the value for `GatewayClientOptions.Token` and
`KvStoreClientOptions.Token`. It lives for the channel's `tokenTtlSec` (24 hours by
default); **there is no refresh**, so sign in again.

**GitHub takes an access token and Google an id token**, which is why they are two
methods: the service answers the wrong one with a `400`.

## What each call does

| Call | Request | Returns |
| --- | --- | --- |
| `FetchConfigAsync` | `GET /c/{channelId}/.well-known/config`, unauthenticated | `AuthChannelConfig` |
| `BuildStartUrl` | none — builds `/c/{channelId}/start?provider=…&redirect=…` | `Uri` |
| `ParseRedirect` | none — reads the returned URL's query and fragment | `ChannelToken` |
| `ExchangeAccessTokenAsync` / `ExchangeIdTokenAsync` | `POST /c/{channelId}/token` | `ChannelToken` |
| `VerifyAsync` | `GET /c/{channelId}/verify` with `Authorization: Bearer` | `ChannelToken`, or null on `401` |

Every call that makes a request answers `404` for an unknown channel id and `410` for an
expired or disabled channel, as `AuthException` with `Code` `http` — so a `VerifyAsync`
that throws `http (404)` means a wrong channel id, not a bad token.

`BuildStartUrl` puts the nonce in the redirect's query under `nonce` (or
`AuthClientOptions.NonceParameter`), replacing one already there; the service matches
the allowlist on origin and path prefix, so the query is admitted. `ParseRedirect`
compares it in constant time — exactly one, in the query — and then reads `token`,
`userId` and `exp` from the fragment, which the service writes with `URLSearchParams`. A
redirect must be `https`, or `http` only for `localhost`, `127.0.0.1` and `[::1]`, with
no userinfo, no fragment of its own and at most 2,048 characters with its nonce: the
service's own rules, which this client applies before building the URL so a mistake is an
exception in the game rather than an error page in the browser. `BaseUrl` follows the
same `https`-or-loopback rule, since the provider credential and the JWT travel to it.

## Failures

`AuthException` carries a `Code` from `AuthErrorCodes` and a `Status` (the HTTP status,
`0` when there was no reply). Its message is `auth {code} ({status})` and nothing else.

| `Code` | When |
| --- | --- |
| `http` | the service answered a status that is not a success (`VerifyAsync` turns a `401` into null instead) |
| `not_json` | a success reply that is not a JSON object, or — from a custom transport — over 1 MiB of UTF-8 |
| `missing_field` | a success reply without `jwt`, `userId` or `exp`, or a redirect fragment without `userId` or a numeric `exp` |
| `nonce_mismatch` | `ParseRedirect`: the returned URL's nonce is absent, repeated, or not yours |
| `missing_fragment` | `ParseRedirect`: no fragment, or no `token` in it |
| `network` | the transport threw (its exception is the `InnerException`) — the shipped transports throw for a reply over 1 MiB — or nothing came within `Timeout` (30 s by default) |

A cancellation of your own token is an `OperationCanceledException`, not a failure.
Local misuse — an empty channel id, a malformed `BaseUrl`, an empty provider or
credential, a JWT a header cannot carry — is an `ArgumentException` before any request,
and none of those messages quotes the value.

## Threads and WebGL

The client holds no state, so its calls may be made from any thread the transport allows:
`AuthHttpClientTransport.Default` any, `AuthUnityWebRequestTransport` the main thread
only. A task resumes on the caller's synchronization context.

**On WebGL only the browser flow works today.** `BuildStartUrl` and `ParseRedirect` make
no request, but `FetchConfigAsync`, both exchanges and `VerifyAsync` are cross-origin
calls, and the auth service answers them with no `Access-Control-Allow-Origin` (checked
on dev, 2026-09-30), so the browser blocks every reply and each call fails as
`network` — `FetchConfigAsync` did exactly that from a Chrome WebGL player. That is the
service's to change. In the same player the client's half of the browser flow worked —
`BuildStartUrl`, a same-tab navigation, the reloaded build reading query and fragment
from `Application.absoluteURL`, and `ParseRedirect` — with the provider hop replaced by a
synthesized return, since no dev channel has a provider configured. A real sign-in on
WebGL, end to end, has not been run.

`AuthUnityWebRequestTransport.Instance` (in `Runtime/Unity`, behind
`#if UNITY_5_3_OR_NEWER`) is the transport for a platform where `HttpClient` cannot send,
and on WebGL it will carry those calls the day the service allows it. It follows no
redirect (`redirectLimit = 0`) and on a native player hands a refused one back as its
`3xx`, which the client reports as `http`; on WebGL, where Unity fails the request on a
redirect, it is `network` (both seen in players, 2026-09-30;
[the browser run](../../rules/manual-verification.md#the-webgl-browser-run)). It checks its 1 MiB cap
only once the download finished, since `UnityWebRequest` buffers the whole reply.

## Nothing sensitive leaves the client

The provider credential travels in the `POST` body and the JWT in the `Authorization`
header of `/verify` — both through `IAuthTransport`, whose XML doc says so to the
implementer. Log lines are `auth request` at `Debug` with `{route, status}` and
`auth request failed` at `Warn` with `{route}`; `route` is `config`, `token` or
`verify`. A failed reply's body is never kept, because the service may quote the
credential back. `ChannelToken.ToString()` leaves the JWT out.

## Public API

- `AuthClient.Create(AuthClientOptions)` → `IAuthClient`: `FetchConfigAsync`,
  `BuildStartUrl`, `ParseRedirect`, `ExchangeAccessTokenAsync`, `ExchangeIdTokenAsync`,
  `VerifyAsync`. `AuthClient.NewNonce()`, `DefaultTimeout`, `MaxTimeout`.
- `AuthClientOptions`: `BaseUrl`, `ChannelId`, `Transport`, `Logger`, `Timeout`,
  `NonceParameter`.
- `AuthChannelConfig` (`ChannelId`, `Issuer`, `Audience`, `TokenTtlSec`, `Providers`,
  `CallbackUrls` by provider, `StartUrl`, `RedirectAllowlist`, `ExpiresAt`, `Raw`) and
  `ChannelToken` (`Jwt`, `UserId`, `ExpiresAt`, `IsExpired`).
- `AuthException` (`Code`, `Status`) and `AuthErrorCodes`.
- Transport seam: `IAuthTransport`, `AuthHttpRequest`, `AuthHttpResponse`,
  `AuthHttpClientTransport` (`Default`, `Create`), and `AuthUnityWebRequestTransport`
  in Unity builds.

## Differences from flutterlib's `yingyeothon_auth_client`

There is no tslib counterpart — tslib's `lambda-authorizer` packages are the server
half — so the model is flutterlib's package, and the vocabulary is shared with it.

- `AuthFailure(kind, status)` becomes `AuthException` with string codes in snake case
  (`nonce_mismatch`), the `KvStoreException` shape, rather than an enum.
- `exchange(provider, accessToken:, idToken:)` with its "exactly one" check becomes two
  methods, so the wrong combination does not compile.
- `AuthChannelConfig.ExpiresAt` is Unix seconds and `CallbackUrls` a map by provider,
  both as the service sends them; flutterlib reads the first as a date string and the
  second as a list, and both come back empty there.
- `ParseRedirect` refuses a fragment without `userId` or `exp`, and a repeated nonce,
  where flutterlib reads the first two as empty and zero.
- The HTTP seam is `IAuthTransport` rather than an `http.Client`, because Unity WebGL
  needs `UnityWebRequest`. Its types carry an `Auth` prefix so a game that also uses
  `Yingyeothon.KvStore` can import both namespaces without an ambiguous name.
- `NewNonce()` draws from `RandomNumberGenerator` rather than taking a `Random`.

## Samples

`Sign In` (_Package Manager → Yingyeothon Auth Client → Samples → Import_): an
engine-free `SignInSession` wrapping both flows and the token check. Receiving the
redirect is left to your build — [Unity § Signing in](../../docs/unity.md#signing-in). It replaces the `SignIn`
sample `gamebase-client` used to carry, which made the `POST` by hand.
