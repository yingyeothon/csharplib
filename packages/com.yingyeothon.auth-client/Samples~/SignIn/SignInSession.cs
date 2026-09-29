#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Auth.Samples
{
    /// <summary>
    /// The two ways a game gets a channel JWT, and the check for one it kept, over one
    /// <see cref="IAuthClient"/>. Engine-free, so it runs in a console host too.
    /// </summary>
    /// <remarks>
    /// Receiving the browser's return is the game's own glue and differs per platform:
    /// <c>docs/unity.md</c> § Signing in has the options. Whatever
    /// receives it hands the URL to <see cref="CompleteBrowserSignIn"/> and drops it.
    /// </remarks>
    public sealed class SignInSession
    {
        private readonly IAuthClient _auth;

        /// <param name="authBaseUrl"><c>https://auth.yyt.life</c>, or <c>https://auth-dev.yyt.life</c> on dev.</param>
        /// <param name="authChannelId">The auth channel id from the console, <c>auth_…</c>.</param>
        /// <param name="transport">Null outside WebGL; <c>AuthUnityWebRequestTransport.Instance</c> on it.</param>
        public SignInSession(string authBaseUrl, string authChannelId, IAuthTransport? transport = null)
        {
            _auth = AuthClient.Create(new AuthClientOptions
            {
                BaseUrl = authBaseUrl,
                ChannelId = authChannelId,
                Transport = transport,
            });
        }

        /// <summary>One request, no browser: the player already holds a GitHub access token.</summary>
        public Task<ChannelToken> WithGitHubAsync(string accessToken, CancellationToken cancellationToken = default)
            => _auth.ExchangeAccessTokenAsync("github", accessToken, cancellationToken);

        /// <summary>One request, no browser: the player already holds a Google id token.</summary>
        public Task<ChannelToken> WithGoogleAsync(string idToken, CancellationToken cancellationToken = default)
            => _auth.ExchangeIdTokenAsync("google", idToken, cancellationToken);

        /// <summary>
        /// The browser flow's first half: the URL to open, and the nonce to keep until the
        /// browser comes back — in storage that survives the app being killed meanwhile.
        /// </summary>
        public Uri BeginBrowserSignIn(string provider, Uri redirect, out string nonce)
        {
            nonce = AuthClient.NewNonce();
            return _auth.BuildStartUrl(provider, redirect, nonce);
        }

        /// <summary>The second half. Throws <see cref="AuthException"/> for a foreign or missing nonce.</summary>
        public ChannelToken CompleteBrowserSignIn(Uri returned, string nonce) => _auth.ParseRedirect(returned, nonce);

        /// <summary>
        /// Whether a kept token still works: null when it expired or was never valid. A wrong
        /// channel id throws instead (<c>auth http (404)</c>, or 410 for an expired channel).
        /// </summary>
        public Task<ChannelToken?> CheckAsync(string jwt, CancellationToken cancellationToken = default)
            => _auth.VerifyAsync(jwt, cancellationToken);
    }
}
