using System;
using Yingyeothon.Logger;

namespace Yingyeothon.KvStore
{
    /// <summary>Options for <see cref="KvStoreClient.Create"/>.</summary>
    public sealed class KvStoreClientOptions
    {
        /// <summary>The store's origin: <c>https://doc.yyt.life</c>, or <c>https://doc-dev.yyt.life</c> on dev. Required; no default.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>
        /// The channel JWT a player holds, or the channel's doc apiKey on a server.
        /// Required. It travels in the <c>Authorization</c> header only and reaches no
        /// log line, no exception and no URL.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>The HTTP seam. Null is <c>HttpClientTransport.Default</c>; a WebGL build passes <c>UnityWebRequestTransport.Instance</c>.</summary>
        public IHttpTransport? Transport { get; set; }

        /// <summary>Where <c>kv request</c> lines go. Null is <c>NullLogger.Instance</c>.</summary>
        public ILogger? Logger { get; set; }

        /// <summary>
        /// How long one call may take before it fails with
        /// <see cref="KvErrorCodes.Timeout"/>. Null is 15 seconds; the most is
        /// <see cref="KvStoreClient.MaxTimeout"/>. It bounds the whole call, so a
        /// transport with a longer timeout of its own is cut short here.
        /// </summary>
        public TimeSpan? Timeout { get; set; }
    }

    /// <summary>Creates key-value store clients.</summary>
    public static class KvStoreClient
    {
        /// <summary>The per-call bound used when <see cref="KvStoreClientOptions.Timeout"/> is null.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The longest <see cref="KvStoreClientOptions.Timeout"/> accepted: what a cancellation timer can count, about 24 days.</summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        /// <summary>
        /// Creates a client. Throws <see cref="ArgumentException"/> when
        /// <see cref="KvStoreClientOptions.BaseUrl"/> is not <c>http(s)://host[/prefix]</c>
        /// with no userinfo, query or fragment, when <see cref="KvStoreClientOptions.Token"/>
        /// is empty or holds a character a header cannot carry, or when the timeout is
        /// outside <c>(0, MaxTimeout]</c>. The options are copied; changing them
        /// afterwards changes nothing.
        /// </summary>
        public static IKvStoreClient Create(KvStoreClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (string.IsNullOrEmpty(options.BaseUrl)
                || !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
                || baseUri.UserInfo.Length > 0 || baseUri.Query.Length > 0 || baseUri.Fragment.Length > 0)
            {
                // Userinfo would put a credential into every call's URL; a query or a
                // fragment would swallow the path the client appends.
                throw new ArgumentException(
                    "BaseUrl must be an absolute http(s) URL with no userinfo, query or fragment", nameof(options));
            }

            if (string.IsNullOrEmpty(options.Token))
            {
                throw new ArgumentException("Token is required", nameof(options));
            }

            // A header value may hold only visible ASCII. Refusing here makes a
            // malformed credential fail once and visibly, instead of as a header the
            // transport silently drops and an unexplained 401 — and the message
            // reports the index, never the character: the string is the token.
            var token = options.Token!;
            for (var i = 0; i < token.Length; i++)
            {
                if (token[i] < '!' || token[i] > '~')
                {
                    throw new ArgumentException(
                        "Token holds a character a header cannot carry, at index " + i, nameof(options));
                }
            }

            var timeout = options.Timeout ?? DefaultTimeout;
            if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
            {
                throw new ArgumentException("Timeout must be positive and at most KvStoreClient.MaxTimeout", nameof(options));
            }

            return new KvStoreClientImpl(
                baseUri,
                token,
                options.Transport ?? HttpClientTransport.Default,
                options.Logger ?? NullLogger.Instance,
                timeout);
        }
    }
}
