using System;
using System.Collections.Generic;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// A parsed tracer DSN, <c>https://{writeKey}@{host}/{trc_id}</c>, as
    /// <c>yyt tracer dsn &lt;res&gt;</c> prints it.
    /// </summary>
    /// <remarks>
    /// <see cref="ToString"/> never prints the write key, and parsing never throws: a DSN
    /// comes from a build define, and a typo in one must not take the game down.
    /// </remarks>
    public sealed class TracerDsn
    {
        private TracerDsn(string writeKey, string resourceId, Uri envelopeUri)
        {
            WriteKey = writeKey;
            ResourceId = resourceId;
            EnvelopeUri = envelopeUri;
        }

        /// <summary>The resource's write key; a credential, never logged.</summary>
        public string WriteKey { get; }

        /// <summary>The resource id, <c>trc_…</c>.</summary>
        public string ResourceId { get; }

        /// <summary>The <c>POST</c> target, <c>{base}/api/{trc_id}/envelope/</c>, trailing slash included.</summary>
        public Uri EnvelopeUri { get; }

        /// <summary>
        /// Parses <paramref name="dsn"/>. False when it is not
        /// <c>https://{key}@{host}[:port][/prefix]/{trc_id}</c>, carries a query or a
        /// fragment, the key is shorter than 16 characters, is not printable ASCII or holds
        /// one of <c>, " = \</c>, or the id is not the console's <c>trc_</c> shape.
        /// <c>http</c> is accepted only when <paramref name="allowHttpLoopback"/> is true
        /// <b>and</b> the host is <c>127.0.0.1</c>, <c>localhost</c>, <c>::1</c> or
        /// <c>10.0.2.2</c>.
        /// </summary>
        public static bool TryParse(string? dsn, bool allowHttpLoopback, out TracerDsn? result)
        {
            result = null;
            try
            {
                result = ParseOrNull(dsn, allowHttpLoopback);
            }
            catch (Exception)
            {
                result = null;
            }

            return result != null;
        }

        /// <summary>The resource id only; the key stays out of every log line.</summary>
        public override string ToString() => "TracerDsn(" + ResourceId + ")";

        private static readonly HashSet<string> LoopbackHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "127.0.0.1", "localhost", "[::1]", "::1", "10.0.2.2",
        };

        private static TracerDsn? ParseOrNull(string? dsn, bool allowHttpLoopback)
        {
            if (dsn == null || !Uri.TryCreate(dsn.Trim(), UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            {
                return null;
            }

            var http = uri.Scheme == Uri.UriSchemeHttp;
            if (uri.Scheme != Uri.UriSchemeHttps && !http)
            {
                return null;
            }

            if (http && !(allowHttpLoopback && LoopbackHosts.Contains(uri.Host)))
            {
                return null;
            }

            if (uri.Query.Length != 0 || uri.Fragment.Length != 0)
            {
                return null;
            }

            var userInfo = uri.UserInfo;
            var colon = userInfo.IndexOf(':');
            var key = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo.Substring(0, colon));
            if (!IsKey(key))
            {
                return null;
            }

            var segments = new List<string>();
            foreach (var segment in uri.AbsolutePath.Split('/'))
            {
                if (segment.Length != 0)
                {
                    segments.Add(segment);
                }
            }

            if (segments.Count == 0 || !IsResourceId(segments[segments.Count - 1]))
            {
                return null;
            }

            var id = segments[segments.Count - 1];
            segments.RemoveAt(segments.Count - 1);
            var prefix = segments.Count == 0 ? string.Empty : "/" + string.Join("/", segments);
            var server = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
            if (!Uri.TryCreate(server + prefix + "/api/" + id + "/envelope/", UriKind.Absolute, out var envelope))
            {
                return null;
            }

            return new TracerDsn(key, id, envelope);
        }

        private static bool IsKey(string key)
        {
            if (key.Length < 16 || key.Length > 256)
            {
                return false;
            }

            foreach (var c in key)
            {
                // Printable ASCII, without what would change the meaning of the
                // X-Sentry-Auth parameter list.
                if (c < 33 || c > 126 || c == ',' || c == '"' || c == '=' || c == '\\')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsResourceId(string id)
        {
            if (id.Length < 5 || id.Length > 44 || !id.StartsWith("trc_", StringComparison.Ordinal))
            {
                return false;
            }

            for (var i = 4; i < id.Length; i++)
            {
                var c = id[i];
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'z'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
