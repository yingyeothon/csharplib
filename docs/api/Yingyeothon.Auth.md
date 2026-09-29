# Yingyeothon.Auth

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.auth-client/README.md`](../../packages/com.yingyeothon.auth-client/README.md).

## Contents

- [`AuthChannelConfig`](#class-authchannelconfig)
- [`AuthClient`](#static-class-authclient)
- [`AuthClientOptions`](#class-authclientoptions)
- [`AuthErrorCodes`](#static-class-autherrorcodes)
- [`AuthException`](#class-authexception)
- [`AuthHttpClientTransport`](#static-class-authhttpclienttransport)
- [`AuthHttpRequest`](#class-authhttprequest)
- [`AuthHttpResponse`](#class-authhttpresponse)
- [`ChannelToken`](#class-channeltoken)
- [`IAuthClient`](#interface-iauthclient)
- [`IAuthTransport`](#interface-iauthtransport)

## class AuthChannelConfig

An auth channel's public config, from `GET /c/{channelId}/.well-known/config` .

| Member | Summary |
| --- | --- |
| `Audience : String get` | The token's `aud` . |
| `CallbackUrls : IReadOnlyDictionary<String, String> get` | The callback URL to register with each provider's OAuth app, by provider name ( `github` → `…/c/{channelId}/github/callback` ). |
| `ChannelId : String get` | The auth channel id. |
| `ExpiresAt : Nullable<Int64> get` | When the channel expires, as Unix seconds, or null when the reply named none. The service sends `253402300799` (the last second of year 9999) for a channel without an expiry. |
| `Issuer : String get` | The token's `iss` : `yyt-auth/{channelId}` . |
| `Providers : IReadOnlyList<String> get` | The providers the channel enables: `github` , `google` . |
| `Raw : JsonValue get` | The object as received, so a field this SDK does not model is still reachable. |
| `RedirectAllowlist : IReadOnlyList<String> get` | The URL prefixes a `redirect` must match. |
| `StartUrl : String get` | The channel's `/start` URL, absolute. |
| `TokenTtlSec : Int64 get` | How long a token lives, in seconds: 24 hours by default, up to 30 days. |
| `ctor(String, String, String, Int64, IReadOnlyList<String>, IReadOnlyDictionary<String, String>, String, IReadOnlyList<String>, Nullable<Int64>, JsonValue)` |  |

## static class AuthClient

Creates auth clients, and the nonces a browser sign-in needs.

| Member | Summary |
| --- | --- |
| `Create(AuthClientOptions) : IAuthClient` | Creates a client for one auth channel. Throws `ArgumentException` when `BaseUrl` is not `http(s)://host[/prefix]` with no userinfo, query or fragment, when `ChannelId` is empty, when the nonce parameter is not a plain query name, or when the timeout is outside `(0, MaxTimeout]` . The options are copied. |
| `DefaultTimeout : TimeSpan` | The per-call bound used when `Timeout` is null. |
| `MaxTimeout : TimeSpan` | The longest `Timeout` accepted: what a cancellation timer can count. |
| `NewNonce() : String` | A fresh nonce for `BuildStartUrl` : 32 bytes from the platform's cryptographic generator, base64url without padding (43 characters). Keep it until the redirect comes back — in storage that survives the app being killed while the browser is in front — and pass it to `ParseRedirect` . |

## class AuthClientOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `BaseUrl : String get set` | The auth service origin: `https://auth.yyt.life` , or `https://auth-dev.yyt.life` on dev. Required. |
| `ChannelId : String get set` | The auth channel id from the console, `auth_…` . Required. |
| `Logger : ILogger get set` | Where `auth request` lines go. Null is `NullLogger.Instance` . |
| `NonceParameter : String get set` | The query parameter the nonce rides in on the redirect URL. Null is `nonce` . |
| `Timeout : Nullable<TimeSpan> get set` | How long one call may take before it fails with `Network` . Null is 30 seconds; the most is `MaxTimeout` . |
| `Transport : IAuthTransport get set` | The HTTP seam. Null is `Default` ; a WebGL build passes `AuthUnityWebRequestTransport.Instance` . |
| `ctor()` |  |

## static class AuthErrorCodes

The `Code` values, in the vocabulary flutterlib's auth client shares.

| Member | Summary |
| --- | --- |
| `Http : String` | The service answered a status that is not a success; `Status` carries it. |
| `MissingField : String` | A success reply without a field the call needs: `jwt` , `userId` or `exp` . |
| `MissingFragment : String` | `ParseRedirect` : the redirect carries no fragment, or no `token` in it. |
| `Network : String` | No reply arrived: the transport threw, or nothing came within `Timeout` . |
| `NonceMismatch : String` | `ParseRedirect` : the redirect's nonce is absent or not the one you sent. |
| `NotJson : String` | A success reply whose body was not a JSON object, or was larger than any auth answer. |

## class AuthException

An auth call that did not produce a token or a config.

| Member | Summary |
| --- | --- |
| `Code : String get` | What went wrong; one of `AuthErrorCodes` . |
| `Status : Int32 get` | The HTTP status, or 0 when there was no reply to report. |
| `ctor(String, Int32)` | Creates an exception for a code and a status. |
| `ctor(String, Int32, Exception)` | Creates an exception carrying the transport failure behind a `Network` . |

## static class AuthHttpClientTransport

The default `IAuthTransport` , over `HttpClient` .

| Member | Summary |
| --- | --- |
| `Create(HttpClient) : IAuthTransport` | A transport over a client the caller owns: a proxy, a certificate policy. It should not follow redirects, and its own `Timeout` (100 seconds unless changed) also bounds every call. Replies are read with the same 1 MiB cap. |
| `Default : IAuthTransport get` | One shared client for the process, with no default headers, no redirect following — a redirect would carry the credential to whatever host it named — and a timeout of its own of five minutes, a backstop behind `Timeout` for a stream that does not honour cancellation. A reply over 1 MiB is refused as it streams, and the call fails as `network` . |

## class AuthHttpRequest

One HTTP request the auth client wants sent.

| Member | Summary |
| --- | --- |
| `Body : String get` | The UTF-8 JSON body, or null. On `/token` it holds the provider credential. |
| `Headers : IReadOnlyList<KeyValuePair<String, String>> get` | The request headers, in order. On `/verify` one of them is the credential. |
| `Method : String get` | `GET` or `POST` . |
| `Timeout : TimeSpan get` | How long the client will wait. The cancellation token the transport receives fires at this bound too; this is for a transport whose own timer is the only one that can run — a WebGL build has no thread for the token's. |
| `Url : Uri get` | The absolute URL. Send `AbsoluteUri` , never `ToString()` , which unescapes. |
| `ctor(String, Uri, IReadOnlyList<KeyValuePair<String, String>>, String, TimeSpan)` |  |

## class AuthHttpResponse

What the service answered: the status and the body as text.

| Member | Summary |
| --- | --- |
| `Body : String get` | The body decoded as UTF-8, or an empty string. It may hold a token; never log it. |
| `Status : Int32 get` | The HTTP status code. |
| `ctor(Int32, String)` |  |

## class ChannelToken

What the auth service issued: the token, whom it is for, and when it expires.

| Member | Summary |
| --- | --- |
| `ExpiresAt : Int64 get` | When the token stops working, as Unix seconds. There is no refresh: sign in again. |
| `IsExpired(DateTimeOffset) : Boolean` | Whether `now` is at or past `ExpiresAt` . The clock is the caller's. |
| `Jwt : String get` | The value for `GatewayClientOptions.Token` and `KvStoreClientOptions.Token` . A credential: never log it. |
| `ToString() : String` | Deliberately leaves the token out, and renders the user id capped and stripped of characters that break a log line: it came off a URL or a reply. |
| `UserId : String get` | The identity the token carries; the gateway echoes it as `hello.UserId` . |
| `ctor(String, String, Int64)` |  |

## interface IAuthClient

The client's half of an auth channel: its public config, the browser sign-in URL and the redirect that comes back, a direct exchange of a provider credential, and a token check.

| Member | Summary |
| --- | --- |
| `BuildStartUrl(String, Uri, String) : Uri` | The browser URL `/c/{channelId}/start?provider=…&redirect=…` , with `nonce` added to `redirect` 's query. The redirect must be on the channel's allowlist (origin and path prefix; a query is admitted). The service appends `#token=…&userId=…&exp=…` to it once the player signed in. No request is made. |
| `ExchangeAccessTokenAsync(String, String, CancellationToken?) : Task<ChannelToken>` | `POST /c/{channelId}/token` with a provider access token — GitHub's. |
| `ExchangeIdTokenAsync(String, String, CancellationToken?) : Task<ChannelToken>` | `POST /c/{channelId}/token` with a provider id token — Google's. |
| `FetchConfigAsync(CancellationToken?) : Task<AuthChannelConfig>` | `GET /c/{channelId}/.well-known/config` , unauthenticated. |
| `ParseRedirect(Uri, String) : ChannelToken` | Reads the token out of the URL the browser came back to: checks, in constant time, that its query carries `expectedNonce` , then reads the fragment. Throws `AuthException` with `NonceMismatch` or `MissingFragment` . Drop afterwards `returned` : its fragment is a credential. For pasted text use `TryCreate` first. |
| `VerifyAsync(String, CancellationToken?) : Task<ChannelToken>` | `GET /c/{channelId}/verify` with the token as a bearer. Returns null on `401` — the token is expired, forged or for another channel — and throws `AuthException` for any other failure. |

## interface IAuthTransport

The HTTP seam the auth client sends through.

| Member | Summary |
| --- | --- |
| `SendAsync(AuthHttpRequest, CancellationToken) : Task<AuthHttpResponse>` | Sends one request and returns whatever status the service answered with. |
