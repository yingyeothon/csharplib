using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Auth
{
    internal sealed class AuthClientImpl : IAuthClient
    {
        /// <summary>An auth answer is a few hundred bytes; a body past this many UTF-8 bytes is not the service talking.</summary>
        internal const int MaxBodyBytes = 1024 * 1024;

        /// <summary>The service refuses a longer <c>redirect</c> (<c>services/auth/src/app.ts</c>).</summary>
        internal const int MaxRedirectLength = 2048;

        private static readonly IReadOnlyList<KeyValuePair<string, string>> NoHeaders = new KeyValuePair<string, string>[0];

        private readonly string _origin;
        private readonly string _channelPath;
        private readonly IAuthTransport _transport;
        private readonly ILogger _logger;
        private readonly TimeSpan _timeout;
        private readonly string _nonceParameter;

        internal AuthClientImpl(
            string origin,
            string channelId,
            IAuthTransport transport,
            ILogger logger,
            TimeSpan timeout,
            string nonceParameter)
        {
            _origin = origin;
            _channelPath = "/c/" + Uri.EscapeDataString(channelId);
            _transport = transport;
            _logger = logger;
            _timeout = timeout;
            _nonceParameter = nonceParameter;
        }

        public async Task<AuthChannelConfig> FetchConfigAsync(CancellationToken cancellationToken = default)
        {
            var reply = await SendAsync("GET", "config", "/.well-known/config", NoHeaders, null, cancellationToken);
            return AuthChannelConfig.FromJson(Decode(reply));
        }

        public Uri BuildStartUrl(string provider, Uri redirect, string nonce)
        {
            if (string.IsNullOrEmpty(provider))
            {
                throw new ArgumentException("provider is required", nameof(provider));
            }

            if (redirect == null)
            {
                throw new ArgumentNullException(nameof(redirect));
            }

            // The service's own rules, checked here so a mistake is an exception in the game
            // rather than an error page in the player's browser: https (http only for
            // loopback), no credentials, and no fragment — the service appends the token as one.
            if (!redirect.IsAbsoluteUri
                || !AuthClient.IsSecureOrLoopback(redirect)
                || redirect.UserInfo.Length > 0
                || redirect.Fragment.Length > 0)
            {
                throw new ArgumentException(
                    "redirect must be an https URL (http only for localhost) with no userinfo or fragment", nameof(redirect));
            }

            if (string.IsNullOrEmpty(nonce))
            {
                throw new ArgumentException("nonce is required; use AuthClient.NewNonce()", nameof(nonce));
            }

            var query = new StringBuilder();
            foreach (var pair in SplitPairs(redirect.Query))
            {
                if (string.Equals(FormDecode(NameOf(pair)), _nonceParameter, StringComparison.Ordinal))
                {
                    continue;
                }

                query.Append(pair).Append('&');
            }

            query.Append(_nonceParameter).Append('=').Append(Uri.EscapeDataString(nonce));

            // The host in its ASCII (punycode) form: the service hands the redirect back
            // verbatim as a Location header, which must not carry raw Unicode.
            // IdnHost drops an IPv6 literal's brackets, so that one keeps Host's form.
            var host = redirect.HostNameType == UriHostNameType.IPv6 ? redirect.Host : redirect.IdnHost;
            var back = redirect.Scheme + "://" + host
                + (redirect.IsDefaultPort ? string.Empty : ":" + redirect.Port.ToString(CultureInfo.InvariantCulture))
                + redirect.AbsolutePath + "?" + query;
            if (back.Length > MaxRedirectLength)
            {
                throw new ArgumentException("redirect with its nonce must be at most " + MaxRedirectLength + " characters", nameof(redirect));
            }

            return new Uri(
                _origin + _channelPath + "/start?provider=" + Uri.EscapeDataString(provider)
                + "&redirect=" + Uri.EscapeDataString(back),
                UriKind.Absolute);
        }

        public ChannelToken ParseRedirect(Uri returned, string expectedNonce)
        {
            if (returned == null)
            {
                throw new ArgumentNullException(nameof(returned));
            }

            if (string.IsNullOrEmpty(expectedNonce))
            {
                throw new ArgumentException("expectedNonce is required", nameof(expectedNonce));
            }

            if (!returned.IsAbsoluteUri)
            {
                throw new AuthException(AuthErrorCodes.MissingFragment, 0);
            }

            // Exactly one nonce, in the query: a second one is an ambiguity nobody sent on
            // purpose, and one in the fragment is not where BuildStartUrl put it.
            string? nonce = null;
            var nonces = 0;
            foreach (var pair in SplitPairs(returned.Query))
            {
                if (string.Equals(FormDecode(NameOf(pair)), _nonceParameter, StringComparison.Ordinal))
                {
                    nonce = FormDecode(ValueOf(pair));
                    nonces++;
                }
            }

            if (nonces != 1 || !ConstantTimeEquals(nonce!, expectedNonce))
            {
                throw new AuthException(AuthErrorCodes.NonceMismatch, 0);
            }

            string? jwt = null;
            string? userId = null;
            string? exp = null;
            foreach (var pair in SplitPairs(returned.Fragment))
            {
                switch (FormDecode(NameOf(pair)))
                {
                    case "token":
                        jwt = FormDecode(ValueOf(pair));
                        break;
                    case "userId":
                        userId = FormDecode(ValueOf(pair));
                        break;
                    case "exp":
                        exp = FormDecode(ValueOf(pair));
                        break;
                }
            }

            if (string.IsNullOrEmpty(jwt))
            {
                throw new AuthException(AuthErrorCodes.MissingFragment, 0);
            }

            // The service always writes all three; a fragment without them is not its answer,
            // and a token with no expiry would read as expired at once.
            if (string.IsNullOrEmpty(userId)
                || !long.TryParse(exp, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt))
            {
                throw new AuthException(AuthErrorCodes.MissingField, 0);
            }

            return new ChannelToken(jwt!, userId!, expiresAt);
        }

        public Task<ChannelToken> ExchangeAccessTokenAsync(string provider, string accessToken, CancellationToken cancellationToken = default)
            => ExchangeAsync(provider, "accessToken", accessToken, nameof(accessToken), cancellationToken);

        public Task<ChannelToken> ExchangeIdTokenAsync(string provider, string idToken, CancellationToken cancellationToken = default)
            => ExchangeAsync(provider, "idToken", idToken, nameof(idToken), cancellationToken);

        public async Task<ChannelToken?> VerifyAsync(string jwt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(jwt))
            {
                throw new ArgumentException("jwt is required", nameof(jwt));
            }

            // A header value may hold only visible ASCII. The message never quotes the
            // character, and names no index either: the string is the credential.
            foreach (var c in jwt)
            {
                if (c <= ' ' || c > '~')
                {
                    throw new ArgumentException("jwt holds a character a header cannot carry", nameof(jwt));
                }
            }

            var headers = new[] { new KeyValuePair<string, string>("Authorization", "Bearer " + jwt) };
            var reply = await SendAsync("GET", "verify", "/verify", headers, null, cancellationToken, allowUnauthorized: true);
            if (reply.Status == 401)
            {
                return null;
            }

            var json = Decode(reply);
            var userId = json.GetString("userId");
            if (string.IsNullOrEmpty(userId) || !TryExpiry(json, out var expiresAt))
            {
                throw new AuthException(AuthErrorCodes.MissingField, reply.Status);
            }

            return new ChannelToken(jwt, userId!, expiresAt);
        }

        private async Task<ChannelToken> ExchangeAsync(
            string provider,
            string field,
            string credential,
            string parameter,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(provider))
            {
                throw new ArgumentException("provider is required", nameof(provider));
            }

            if (string.IsNullOrEmpty(credential))
            {
                throw new ArgumentException(parameter + " is required", parameter);
            }

            var body = Json.Stringify(Json.Object().Set("provider", provider).Set(field, credential).Build());
            var headers = new[] { new KeyValuePair<string, string>("Content-Type", "application/json") };
            var reply = await SendAsync("POST", "token", "/token", headers, body, cancellationToken);
            var json = Decode(reply);
            var jwt = json.GetString("jwt");
            var userId = json.GetString("userId");
            if (string.IsNullOrEmpty(jwt) || string.IsNullOrEmpty(userId) || !TryExpiry(json, out var expiresAt))
            {
                throw new AuthException(AuthErrorCodes.MissingField, reply.Status);
            }

            return new ChannelToken(jwt!, userId!, expiresAt);
        }

        private async Task<AuthHttpResponse> SendAsync(
            string method,
            string route,
            string suffix,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            string? body,
            CancellationToken cancellationToken,
            bool allowUnauthorized = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new AuthHttpRequest(
                method, new Uri(_origin + _channelPath + suffix, UriKind.Absolute), headers, body, _timeout);

            AuthHttpResponse reply;
            using (var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bound.CancelAfter(_timeout);
                try
                {
                    reply = await _transport.SendAsync(request, bound.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error)
                {
                    // The message is the transport's, which must name neither the URL nor
                    // a credential; the log line names only the route.
                    _logger.Warn("auth request failed", Json.Object().Set("route", route).Build());
                    throw new AuthException(AuthErrorCodes.Network, 0, error);
                }
            }

            if (reply == null)
            {
                throw new AuthException(AuthErrorCodes.Network, 0);
            }

            _logger.Debug(
                "auth request",
                Json.Object().Set("route", route).Set("status", (double)reply.Status).Build());

            if (reply.Status == 401 && allowUnauthorized)
            {
                return reply;
            }

            if (reply.Status < 200 || reply.Status > 299)
            {
                // The body may quote the credential back; the status is the whole report.
                throw new AuthException(AuthErrorCodes.Http, reply.Status);
            }

            return reply;
        }

        /// <summary>
        /// <c>exp</c> as whole Unix seconds; a fractional, negative or out-of-range number is no
        /// expiry at all — the same rule <see cref="ParseRedirect"/> applies to the fragment.
        /// </summary>
        private static bool TryExpiry(JsonValue json, out long expiresAt)
        {
            var exp = json.GetNumber("exp");
            if (exp.HasValue && exp.Value >= 0 && exp.Value <= 9007199254740991d && Math.Floor(exp.Value) == exp.Value)
            {
                expiresAt = (long)exp.Value;
                return true;
            }

            expiresAt = 0;
            return false;
        }

        private static JsonValue Decode(AuthHttpResponse reply)
        {
            // Counted in bytes, as the cap is stated; the character check first keeps the
            // count itself cheap for a body that is plainly too big.
            if (reply.Body.Length > MaxBodyBytes
                || Encoding.UTF8.GetByteCount(reply.Body) > MaxBodyBytes
                || !Json.TryParse(reply.Body, out var parsed)
                || parsed.Kind != JsonKind.Object)
            {
                throw new AuthException(AuthErrorCodes.NotJson, reply.Status);
            }

            return parsed;
        }

        /// <summary><c>?a=b&amp;c=d</c> or <c>#a=b&amp;c=d</c>, still escaped, into its pairs.</summary>
        private static IEnumerable<string> SplitPairs(string part)
        {
            if (part.Length <= 1)
            {
                yield break;
            }

            foreach (var pair in part.Substring(1).Split('&'))
            {
                if (pair.Length > 0)
                {
                    yield return pair;
                }
            }
        }

        private static string NameOf(string pair)
        {
            var equals = pair.IndexOf('=');
            return equals < 0 ? pair : pair.Substring(0, equals);
        }

        private static string ValueOf(string pair)
        {
            var equals = pair.IndexOf('=');
            return equals < 0 ? string.Empty : pair.Substring(equals + 1);
        }

        /// <summary>
        /// <c>application/x-www-form-urlencoded</c>: the service builds the fragment with
        /// <c>URLSearchParams</c>, which writes a space as <c>+</c>. A malformed escape is left
        /// as it is rather than thrown, so nothing about the credential reaches a message.
        /// </summary>
        private static string FormDecode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

        /// <summary>Every byte compared, whatever the first difference, so the time says nothing about the nonce.</summary>
        private static bool ConstantTimeEquals(string a, string b)
        {
            var x = Encoding.UTF8.GetBytes(a);
            var y = Encoding.UTF8.GetBytes(b);
            var diff = x.Length ^ y.Length;
            for (var i = 0; i < x.Length && i < y.Length; i++)
            {
                diff |= x[i] ^ y[i];
            }

            return diff == 0;
        }
    }
}
