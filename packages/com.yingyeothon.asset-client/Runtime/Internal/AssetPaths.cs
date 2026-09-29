using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Yingyeothon.Assets
{
    /// <summary>A bundle's base URL, checked once at construction.</summary>
    internal sealed class BundleBase
    {
        internal BundleBase(string url, string? adPrefix)
        {
            Url = url;
            AdPrefix = adPrefix;
        }

        /// <summary>Always ends in <c>/</c>.</summary>
        internal string Url { get; }

        /// <summary>
        /// What precedes a file's path in its associated data: empty for a live bundle,
        /// <c>{version}/</c> for a versioned one; null for a URL of neither shape.
        /// </summary>
        internal string? AdPrefix { get; }
    }

    internal static class AssetPaths
    {
        private static readonly Regex BundlePath = new Regex("^/assets/[^/]+/(?:([^/]+)/)?$", RegexOptions.CultureInvariant);

        /// <summary>
        /// An http(s) URL with a host and no userinfo, query or fragment. An encrypted
        /// bundle's base must also have the <c>/assets/{bundleId}/</c> or
        /// <c>/assets/{bundleId}/{version}/</c> shape, because the associated data every file
        /// was encrypted under is derived from it. The message never quotes the URL.
        /// </summary>
        internal static BundleBase ParseBaseUrl(string? baseUrl, bool encrypted)
        {
            if (string.IsNullOrEmpty(baseUrl)
                || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                || url.Host.Length == 0
                || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                throw new ArgumentException("asset BaseUrl must be an http(s) URL with a host and a path and nothing else");
            }

            var path = url.AbsolutePath.EndsWith("/", StringComparison.Ordinal) ? url.AbsolutePath : url.AbsolutePath + "/";
            string? adPrefix = null;
            var match = BundlePath.Match(path);
            if (match.Success)
            {
                adPrefix = match.Groups[1].Success ? Uri.UnescapeDataString(match.Groups[1].Value) + "/" : string.Empty;
            }

            if (encrypted && adPrefix == null)
            {
                throw new ArgumentException(
                    "an encrypted bundle's BaseUrl must be https://{cdn}/assets/{bundleId}/ or .../{bundleId}/{version}/");
            }

            return new BundleBase(url.GetLeftPart(UriPartial.Authority) + path, adPrefix);
        }

        /// <summary>
        /// A file's path below the bundle: segments separated by <c>/</c>, no leading slash,
        /// no empty, <c>.</c> or <c>..</c> segment, no backslash, no control character and no
        /// lone surrogate (it has no UTF-8 form). The message never quotes the path.
        /// </summary>
        internal static string CheckPath(string? path)
        {
            var ok = !string.IsNullOrEmpty(path);
            for (var i = 0; ok && i < path!.Length; i++)
            {
                var c = path[i];
                if (c < ' ' || c == '\u007f' || c == '\\')
                {
                    ok = false;
                }
                else if (char.IsHighSurrogate(c))
                {
                    ok = i + 1 < path.Length && char.IsLowSurrogate(path[i + 1]);
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    ok = false;
                }
            }

            if (ok)
            {
                foreach (var segment in path!.Split('/'))
                {
                    if (segment.Length == 0 || segment == "." || segment == "..")
                    {
                        ok = false;
                        break;
                    }
                }
            }

            if (!ok)
            {
                throw new ArgumentException(
                    "asset path must be relative segments separated by '/', with no '.', '..' or empty segment", nameof(path));
            }

            return path!;
        }

        /// <summary>Each segment percent-encoded: <c>v1/데이터/노래.db</c> is a valid object key.</summary>
        internal static string FileUrl(BundleBase bundle, string path)
        {
            var url = new StringBuilder(bundle.Url);
            var segments = path.Split('/');
            for (var i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                {
                    url.Append('/');
                }

                url.Append(Uri.EscapeDataString(segments[i]));
            }

            return url.ToString();
        }
    }
}
