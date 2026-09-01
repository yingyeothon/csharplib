using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Samples
{
    /// <summary>What the auth service issued: the token, who it is for, and when it expires.</summary>
    public readonly struct ChannelToken
    {
        public ChannelToken(string jwt, string userId, long expiresAtUnixSeconds)
        {
            Jwt = jwt;
            UserId = userId;
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }

        /// <summary>The value that goes into <c>GatewayClientOptions.Token</c>.</summary>
        public string Jwt { get; }

        /// <summary>The identity the token carries. The gateway echoes it as <c>hello.userId</c>.</summary>
        public string UserId { get; }

        /// <summary>When the token stops working. There is no refresh; sign in again.</summary>
        public long ExpiresAtUnixSeconds { get; }
    }

    /// <summary>
    /// Exchanges a provider credential for a yyt channel JWT.
    /// </summary>
    /// <remarks>
    /// This is a sample, not part of the SDK: the auth service is owned by the
    /// <c>service</c> repository, and a game that already signs players in through a
    /// launcher or a provider SDK will have its own version of this. It is here
    /// because a client cannot connect at all without a token, and one HTTP request
    /// is the whole of it.
    /// </remarks>
    public static class ChannelSignIn
    {
        /// <summary>
        /// Posts a provider credential to <c>/c/{authChannelId}/token</c> and returns
        /// the channel JWT.
        /// </summary>
        /// <param name="authBaseUrl">The auth service origin, e.g. <c>https://auth.yyt.life</c>.</param>
        /// <param name="authChannelId">The auth channel id from the console, <c>auth_…</c>.</param>
        /// <param name="provider"><c>github</c> or <c>google</c>, whichever the channel enables.</param>
        /// <param name="credential">The provider's access token, or its id token for Google.</param>
        /// <param name="credentialIsIdToken">True to send the credential as <c>idToken</c>.</param>
        public static async Task<ChannelToken> ExchangeAsync(
            string authBaseUrl,
            string authChannelId,
            string provider,
            string credential,
            bool credentialIsIdToken = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(authBaseUrl) || string.IsNullOrEmpty(authChannelId))
            {
                throw new ArgumentException("authBaseUrl and authChannelId are required");
            }

            var body = Json.Object()
                .Set("provider", provider)
                .Set(credentialIsIdToken ? "idToken" : "accessToken", credential)
                .Build();

            var url = authBaseUrl.TrimEnd('/') + "/c/" + Uri.EscapeDataString(authChannelId) + "/token";

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            using (var content = new StringContent(Json.Stringify(body), Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(url, content, cancellationToken).ConfigureAwait(false))
            {
                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    // The body may quote the credential back, so report the status only.
                    throw new InvalidOperationException(
                        "sign-in failed with status " + (int)response.StatusCode);
                }

                if (!Json.TryParse(text, out var parsed))
                {
                    throw new InvalidOperationException("sign-in answered something that is not JSON");
                }

                var jwt = parsed.GetString("jwt");
                if (string.IsNullOrEmpty(jwt))
                {
                    throw new InvalidOperationException("sign-in answered no jwt");
                }

                return new ChannelToken(
                    jwt!,
                    parsed.GetString("userId") ?? string.Empty,
                    (long)(parsed.GetNumber("exp") ?? 0));
            }
        }
    }
}
