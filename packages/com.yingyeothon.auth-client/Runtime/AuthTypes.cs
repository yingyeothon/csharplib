using System;
using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Auth
{
    /// <summary>An auth channel's public config, from <c>GET /c/{channelId}/.well-known/config</c>.</summary>
    public sealed class AuthChannelConfig
    {
        public AuthChannelConfig(
            string channelId,
            string issuer,
            string audience,
            long tokenTtlSec,
            IReadOnlyList<string> providers,
            IReadOnlyDictionary<string, string> callbackUrls,
            string startUrl,
            IReadOnlyList<string> redirectAllowlist,
            long? expiresAt,
            JsonValue raw)
        {
            ChannelId = channelId;
            Issuer = issuer;
            Audience = audience;
            TokenTtlSec = tokenTtlSec;
            Providers = providers;
            CallbackUrls = callbackUrls;
            StartUrl = startUrl;
            RedirectAllowlist = redirectAllowlist;
            ExpiresAt = expiresAt;
            Raw = raw;
        }

        /// <summary>The auth channel id.</summary>
        public string ChannelId { get; }

        /// <summary>The token's <c>iss</c>: <c>yyt-auth/{channelId}</c>.</summary>
        public string Issuer { get; }

        /// <summary>The token's <c>aud</c>.</summary>
        public string Audience { get; }

        /// <summary>How long a token lives, in seconds: 24 hours by default, up to 30 days.</summary>
        public long TokenTtlSec { get; }

        /// <summary>The providers the channel enables: <c>github</c>, <c>google</c>.</summary>
        public IReadOnlyList<string> Providers { get; }

        /// <summary>
        /// The callback URL to register with each provider's OAuth app, by provider name
        /// (<c>github</c> → <c>…/c/{channelId}/github/callback</c>).
        /// </summary>
        public IReadOnlyDictionary<string, string> CallbackUrls { get; }

        /// <summary>The channel's <c>/start</c> URL, absolute.</summary>
        public string StartUrl { get; }

        /// <summary>The URL prefixes a <c>redirect</c> must match.</summary>
        public IReadOnlyList<string> RedirectAllowlist { get; }

        /// <summary>
        /// When the channel expires, as Unix seconds, or null when the reply named none.
        /// The service sends <c>253402300799</c> (the last second of year 9999) for a
        /// channel without an expiry.
        /// </summary>
        public long? ExpiresAt { get; }

        /// <summary>The object as received, so a field this SDK does not model is still reachable.</summary>
        public JsonValue Raw { get; }

        internal static AuthChannelConfig FromJson(JsonValue json)
        {
            var expires = json.GetNumber("expiresAt");
            return new AuthChannelConfig(
                json.GetString("channelId") ?? string.Empty,
                json.GetString("issuer") ?? string.Empty,
                json.GetString("audience") ?? string.Empty,
                (long)(json.GetNumber("tokenTtlSec") ?? 0),
                Strings(json, "providers"),
                StringMap(json, "callbackUrls"),
                json.GetString("startUrl") ?? string.Empty,
                Strings(json, "redirectAllowlist"),
                expires.HasValue ? (long?)expires.Value : null,
                json);
        }

        private static IReadOnlyDictionary<string, string> StringMap(JsonValue json, string key)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (json.TryGetMember(key, out var value) && value.Kind == JsonKind.Object)
            {
                foreach (var member in value.AsObject())
                {
                    if (member.Value.Kind == JsonKind.String)
                    {
                        map[member.Key] = member.Value.AsString();
                    }
                }
            }

            return map;
        }

        private static IReadOnlyList<string> Strings(JsonValue json, string key)
        {
            var list = new List<string>();
            if (json.TryGetMember(key, out var value) && value.Kind == JsonKind.Array)
            {
                foreach (var item in value.AsArray())
                {
                    if (item.Kind == JsonKind.String)
                    {
                        list.Add(item.AsString());
                    }
                }
            }

            return list;
        }
    }

    /// <summary>What the auth service issued: the token, whom it is for, and when it expires.</summary>
    public sealed class ChannelToken
    {
        public ChannelToken(string jwt, string userId, long expiresAt)
        {
            Jwt = jwt ?? throw new ArgumentNullException(nameof(jwt));
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            ExpiresAt = expiresAt;
        }

        /// <summary>The value for <c>GatewayClientOptions.Token</c> and <c>KvStoreClientOptions.Token</c>. A credential: never log it.</summary>
        public string Jwt { get; }

        /// <summary>The identity the token carries; the gateway echoes it as <c>hello.UserId</c>.</summary>
        public string UserId { get; }

        /// <summary>When the token stops working, as Unix seconds. There is no refresh: sign in again.</summary>
        public long ExpiresAt { get; }

        /// <summary>Whether <paramref name="now"/> is at or past <see cref="ExpiresAt"/>. The clock is the caller's.</summary>
        public bool IsExpired(DateTimeOffset now) => now.ToUnixTimeSeconds() >= ExpiresAt;

        /// <summary>
        /// Deliberately leaves the token out, and renders the user id capped and stripped of
        /// characters that break a log line: it came off a URL or a reply.
        /// </summary>
        public override string ToString() => "ChannelToken(userId: " + Diagnostic(UserId) + ", expiresAt: " + ExpiresAt + ")";

        private static string Diagnostic(string value)
        {
            const int max = 64;
            var length = Math.Min(value.Length, max);
            var buffer = new char[length];
            for (var i = 0; i < length; i++)
            {
                var c = value[i];
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                buffer[i] = category == System.Globalization.UnicodeCategory.Control
                    || category == System.Globalization.UnicodeCategory.Format
                    || category == System.Globalization.UnicodeCategory.LineSeparator
                    || category == System.Globalization.UnicodeCategory.ParagraphSeparator
                    || category == System.Globalization.UnicodeCategory.Surrogate
                    ? '?'
                    : c;
            }

            return length < value.Length ? new string(buffer) + "\u2026" : new string(buffer);
        }
    }
}
