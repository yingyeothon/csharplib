using System;
using System.Globalization;
using System.Text;

namespace Yingyeothon.KvStore
{
    /// <summary>Builds the store's paths and query strings.</summary>
    /// <remarks>
    /// A collection ref, an owner and a key are placed on the path as they are: the
    /// grammars in <see cref="KvRules"/> admit nothing that needs escaping, and the
    /// edge is not transparent to an encoded segment. Query values are the one place
    /// free text goes, and every one of them is percent-encoded.
    /// </remarks>
    internal static class KvPaths
    {
        internal static string Shared(string collection) => "/kv/" + collection + "/entries";

        internal static string Owned(string collection, string owner) => "/kv/" + collection + "/u/" + owner + "/entries";

        internal static string Meta(string collection) => "/kv/" + collection;

        internal static string Entry(string entries, string key) => entries + "/" + key;

        internal static string TtlQuery(int? ttl)
            => ttl == null ? string.Empty : "?ttl=" + ttl.Value.ToString(CultureInfo.InvariantCulture);

        internal static string ListQuery(KvListOptions? options)
        {
            if (options == null)
            {
                return string.Empty;
            }

            var query = new StringBuilder();
            if (options.Prefix != null)
            {
                Add(query, "prefix", options.Prefix);
            }

            if (options.Cursor != null)
            {
                Add(query, "cursor", options.Cursor);
            }

            if (options.Limit != null)
            {
                Add(query, "limit", options.Limit.Value.ToString(CultureInfo.InvariantCulture));
            }

            if (options.Order != null)
            {
                Add(query, "order", options.Order.Value == KvOrder.Desc ? "desc" : "asc");
            }

            if (options.Values)
            {
                Add(query, "values", "1");
            }

            return query.ToString();
        }

        private static void Add(StringBuilder query, string name, string value)
            => query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
    }
}
