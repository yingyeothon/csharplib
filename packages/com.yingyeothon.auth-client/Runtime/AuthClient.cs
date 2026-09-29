using System;
using System.Security.Cryptography;
using Yingyeothon.Logger;

namespace Yingyeothon.Auth
{
    /// <summary>Options for <see cref="AuthClient.Create"/>.</summary>
    public sealed class AuthClientOptions
    {
        /// <summary>The auth service origin: <c>https://auth.yyt.life</c>, or <c>https://auth-dev.yyt.life</c> on dev. Required.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>The auth channel id from the console, <c>auth_…</c>. Required.</summary>
        public string? ChannelId { get; set; }

        /// <summary>The HTTP seam. Null is <see cref="AuthHttpClientTransport.Default"/>; a WebGL build passes <c>AuthUnityWebRequestTransport.Instance</c>.</summary>
        public IAuthTransport? Transport { get; set; }

        /// <summary>Where <c>auth request</c> lines go. Null is <c>NullLogger.Instance</c>.</summary>
        public ILogger? Logger { get; set; }

        /// <summary>How long one call may take before it fails with <see cref="AuthErrorCodes.Network"/>. Null is 30 seconds; the most is <see cref="AuthClient.MaxTimeout"/>.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>The query parameter the nonce rides in on the redirect URL. Null is <c>nonce</c>.</summary>
        public string? NonceParameter { get; set; }
    }

    /// <summary>Creates auth clients, and the nonces a browser sign-in needs.</summary>
    public static class AuthClient
    {
        /// <summary>The per-call bound used when <see cref="AuthClientOptions.Timeout"/> is null.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The longest <see cref="AuthClientOptions.Timeout"/> accepted: what a cancellation timer can count.</summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        /// <summary>
        /// Creates a client for one auth channel. Throws <see cref="ArgumentException"/> when
        /// <see cref="AuthClientOptions.BaseUrl"/> is not <c>http(s)://host[/prefix]</c> with
        /// no userinfo, query or fragment, when <see cref="AuthClientOptions.ChannelId"/> is
        /// empty, when the nonce parameter is not a plain query name, or when the timeout is
        /// outside <c>(0, MaxTimeout]</c>. The options are copied.
        /// </summary>
        public static IAuthClient Create(AuthClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // Plain http would send the provider credential and the JWT in cleartext, so it
            // is allowed only for a service on this machine.
            if (string.IsNullOrEmpty(options.BaseUrl)
                || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                || !IsSecureOrLoopback(baseUri)
                || baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
            {
                throw new ArgumentException(
                    "BaseUrl must be an https URL (http only for localhost) with no userinfo, query or fragment",
                    nameof(options));
            }

            if (string.IsNullOrEmpty(options.ChannelId))
            {
                throw new ArgumentException("ChannelId is required", nameof(options));
            }

            var nonceParameter = options.NonceParameter ?? "nonce";
            if (!IsQueryName(nonceParameter))
            {
                throw new ArgumentException("NonceParameter must be letters, digits, '-' or '_'", nameof(options));
            }

            var timeout = options.Timeout ?? DefaultTimeout;
            if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
            {
                throw new ArgumentException("Timeout must be positive and at most AuthClient.MaxTimeout", nameof(options));
            }

            return new AuthClientImpl(
                baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
                options.ChannelId!,
                options.Transport ?? AuthHttpClientTransport.Default,
                options.Logger ?? NullLogger.Instance,
                timeout,
                nonceParameter);
        }

        /// <summary>
        /// A fresh nonce for <see cref="IAuthClient.BuildStartUrl"/>: 32 bytes from the
        /// platform's cryptographic generator, base64url without padding (43 characters).
        /// Keep it until the redirect comes back — in storage that survives the app being
        /// killed while the browser is in front — and pass it to
        /// <see cref="IAuthClient.ParseRedirect"/>.
        /// </summary>
        public static string NewNonce()
        {
            var bytes = new byte[32];
            using (var generator = RandomNumberGenerator.Create())
            {
                generator.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// <c>https</c>, or <c>http</c> for <c>localhost</c>, <c>127.0.0.1</c> or <c>[::1]</c> —
        /// the auth service's own rule for a redirect (<c>services/auth/src/redirect.ts</c>).
        /// </summary>
        internal static bool IsSecureOrLoopback(Uri url)
        {
            if (url.Scheme == Uri.UriSchemeHttps)
            {
                return true;
            }

            return url.Scheme == Uri.UriSchemeHttp
                && (string.Equals(url.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || url.Host == "127.0.0.1"
                    || url.Host == "[::1]");
        }

        private static bool IsQueryName(string name)
        {
            if (name.Length == 0 || name.Length > 64)
            {
                return false;
            }

            foreach (var c in name)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
