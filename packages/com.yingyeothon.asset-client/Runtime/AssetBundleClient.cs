using System;
using Yingyeothon.Logger;

namespace Yingyeothon.Assets
{
    /// <summary>Options for <see cref="AssetBundleClient.Create"/>. The client copies them, and copies the key.</summary>
    public sealed class AssetBundleClientOptions
    {
        /// <summary>
        /// <c>https://{cdn}/assets/{bundleId}/</c> for a live bundle, plus <c>{version}/</c> for
        /// one version of a versioned bundle. The CDN is <c>d.yyt.life</c>, <c>dev-d.yyt.life</c>
        /// on dev; there is no default.
        /// </summary>
        public string? BaseUrl { get; set; }

        /// <summary>
        /// The key of an encrypted bundle as <c>yyt asset key show</c> prints it (<c>yak1.…</c>).
        /// Set this or <see cref="KeyBytes"/>, or neither for a plain bundle. Never logged.
        /// </summary>
        public string? Key { get; set; }

        /// <summary>The key's 32 raw bytes, instead of <see cref="Key"/>. Copied; the caller's array is untouched.</summary>
        public byte[]? KeyBytes { get; set; }

        /// <summary>
        /// Send only CORS-safelisted request headers (<c>Range</c>, never <c>If-Range</c> or
        /// <c>Cache-Control</c>) and read only the headers the yyt CDN exposes to scripts
        /// (<c>ETag</c>, <c>Content-Length</c>); a ranged read then costs a <c>HEAD</c> first.
        /// Null is true in a WebGL player and false everywhere else.
        /// </summary>
        public bool? CorsSafe { get; set; }

        /// <summary>The HTTP seam. Null is <see cref="AssetHttpClientTransport.Default"/>; a WebGL build passes <c>AssetUnityWebRequestTransport.Instance</c>.</summary>
        public IAssetTransport? Transport { get; set; }

        /// <summary>Where <c>asset request</c> lines go. Null is <c>NullLogger.Instance</c>.</summary>
        public ILogger? Logger { get; set; }

        /// <summary>How long a request may wait for its response headers before it fails as <c>network</c>. Null is 30 seconds.</summary>
        public TimeSpan? ResponseTimeout { get; set; }

        /// <summary>
        /// How long a body may go without delivering its next piece before the read fails as
        /// <c>network</c> — a connection gone quiet, as when a phone changes networks. Null is 30 seconds.
        /// </summary>
        public TimeSpan? BodyIdleTimeout { get; set; }
    }

    /// <summary>Creates asset bundle clients.</summary>
    public static class AssetBundleClient
    {
        /// <summary>The bound used when a timeout option is null.</summary>
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        /// <summary>The longest timeout accepted: what a cancellation timer can count.</summary>
        public static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        /// <summary>
        /// Creates a client for one bundle. Throws <see cref="AssetClientException"/>
        /// (<c>bad_key</c>) for a key that is not canonical or given in both forms at once, and
        /// <see cref="ArgumentException"/> for a <c>BaseUrl</c> that is not an http(s) URL —
        /// or, with a key, not of the bundle or version shape — or for a timeout outside
        /// <c>(0, MaxTimeout]</c>.
        /// </summary>
        public static IAssetBundleClient Create(AssetBundleClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var encrypted = options.Key != null || options.KeyBytes != null;
            var bundle = AssetPaths.ParseBaseUrl(options.BaseUrl, encrypted);
            var responseTimeout = Checked(options.ResponseTimeout, nameof(AssetBundleClientOptions.ResponseTimeout));
            var bodyIdleTimeout = Checked(options.BodyIdleTimeout, nameof(AssetBundleClientOptions.BodyIdleTimeout));
            var key = encrypted ? AssetKey.Parse(options.Key, options.KeyBytes) : null;
            return new AssetBundleClientImpl(
                bundle,
                key,
                options.CorsSafe ?? DefaultCorsSafe,
                options.Transport ?? AssetHttpClientTransport.Default,
                options.Logger ?? NullLogger.Instance,
                responseTimeout,
                bodyIdleTimeout);
        }

        /// <summary>Every request from a WebGL player is cross-origin to the CDN.</summary>
        internal const bool DefaultCorsSafe =
#if UNITY_WEBGL && !UNITY_EDITOR
            true;
#else
            false;
#endif

        private static TimeSpan Checked(TimeSpan? value, string name)
        {
            var timeout = value ?? DefaultTimeout;
            if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
            {
                throw new ArgumentException(name + " must be positive and at most AssetBundleClient.MaxTimeout");
            }

            return timeout;
        }
    }
}
