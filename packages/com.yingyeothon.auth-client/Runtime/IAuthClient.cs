using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Auth
{
    /// <summary>
    /// The client's half of an auth channel: its public config, the browser sign-in URL and
    /// the redirect that comes back, a direct exchange of a provider credential, and a
    /// token check.
    /// </summary>
    /// <remarks>
    /// Opening the browser, receiving the redirect and storing the token are the game's
    /// job. Nothing here logs, throws or returns a message that contains a token, a
    /// fragment, a response body or a URL. The calls hold no state and may be made from any
    /// thread; a task resumes on the caller's synchronization context.
    /// </remarks>
    public interface IAuthClient
    {
        /// <summary><c>GET /c/{channelId}/.well-known/config</c>, unauthenticated.</summary>
        Task<AuthChannelConfig> FetchConfigAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The browser URL <c>/c/{channelId}/start?provider=…&amp;redirect=…</c>, with
        /// <paramref name="nonce"/> added to <paramref name="redirect"/>'s query. The
        /// redirect must be on the channel's allowlist (origin and path prefix; a query is
        /// admitted). The service appends <c>#token=…&amp;userId=…&amp;exp=…</c> to it once
        /// the player signed in. No request is made.
        /// </summary>
        Uri BuildStartUrl(string provider, Uri redirect, string nonce);

        /// <summary>
        /// Reads the token out of the URL the browser came back to: checks, in constant
        /// time, that its query carries <paramref name="expectedNonce"/>, then reads the
        /// fragment. Throws <see cref="AuthException"/> with
        /// <see cref="AuthErrorCodes.NonceMismatch"/> or
        /// <see cref="AuthErrorCodes.MissingFragment"/>. <b>Drop <paramref name="returned"/>
        /// afterwards</b>: its fragment is a credential. For pasted text use
        /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> first.
        /// </summary>
        ChannelToken ParseRedirect(Uri returned, string expectedNonce);

        /// <summary><c>POST /c/{channelId}/token</c> with a provider access token — GitHub's.</summary>
        Task<ChannelToken> ExchangeAccessTokenAsync(string provider, string accessToken, CancellationToken cancellationToken = default);

        /// <summary><c>POST /c/{channelId}/token</c> with a provider id token — Google's.</summary>
        Task<ChannelToken> ExchangeIdTokenAsync(string provider, string idToken, CancellationToken cancellationToken = default);

        /// <summary>
        /// <c>GET /c/{channelId}/verify</c> with the token as a bearer. Returns null on
        /// <c>401</c> — the token is expired, forged or for another channel — and throws
        /// <see cref="AuthException"/> for any other failure.
        /// </summary>
        Task<ChannelToken?> VerifyAsync(string jwt, CancellationToken cancellationToken = default);
    }
}
