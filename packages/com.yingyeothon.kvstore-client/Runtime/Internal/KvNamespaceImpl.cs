using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore
{
    internal class KvNamespaceImpl : IKvNamespace
    {
        private readonly KvStoreClientImpl _client;
        private readonly string _entries;

        internal KvNamespaceImpl(KvStoreClientImpl client, string entries)
        {
            _client = client;
            _entries = entries;
        }

        public async Task<JsonValue?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var reply = await ReadAsync(key, cancellationToken);
            return reply == null ? null : KvReplies.Json(reply);
        }

        public async Task<KvEntry?> GetEntryAsync(string key, CancellationToken cancellationToken = default)
        {
            var reply = await ReadAsync(key, cancellationToken);
            if (reply == null)
            {
                return null;
            }

            var version = KvReplies.VersionOf(reply) ?? throw KvReplies.BadBody(reply);
            return new KvEntry(KvReplies.Json(reply), version, KvReplies.ExpiresAtOf(reply));
        }

        public async Task<KvWriteResult> PutAsync(
            string key,
            JsonValue value,
            KvPutOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CheckKey(key);
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var text = Json.Stringify(value);
            // Bytes, not characters: the cap is the server's and it counts UTF-8.
            if (Encoding.UTF8.GetByteCount(text) > KvRules.MaxValueBytes)
            {
                throw new ArgumentException("value exceeds KvRules.MaxValueBytes as JSON text", nameof(value));
            }

            var headers = new List<KeyValuePair<string, string>>(1);
            int? ttl = null;
            if (options != null)
            {
                ttl = CheckTtl(options.Ttl, nameof(options));
                if (options.IfMatch != null && options.IfNoneMatch)
                {
                    throw new ArgumentException("IfMatch and IfNoneMatch cannot be combined", nameof(options));
                }

                if (options.IfMatch != null)
                {
                    headers.Add(IfMatch(options.IfMatch.Value, nameof(options)));
                }
                else if (options.IfNoneMatch)
                {
                    headers.Add(new KeyValuePair<string, string>("If-None-Match", "*"));
                }
            }

            var reply = await _client.SendAsync(
                "PUT",
                KvRoutes.Entry,
                KvPaths.Entry(_entries, key) + KvPaths.TtlQuery(ttl),
                text,
                headers,
                cancellationToken);
            if (!KvReplies.IsSuccess(reply))
            {
                throw KvReplies.Error(reply);
            }

            // 201 is a create and 204 an update — but only for a caller with the read
            // right, who also gets the ETag. A 204 with no ETag says neither.
            var version = KvReplies.VersionOf(reply);
            bool? created = reply.Status == 201 ? true : version != null ? false : (bool?)null;
            return new KvWriteResult(created, version, KvReplies.ExpiresAtOf(reply));
        }

        public async Task DeleteAsync(string key, KvDeleteOptions? options = null, CancellationToken cancellationToken = default)
        {
            CheckKey(key);
            var headers = new List<KeyValuePair<string, string>>(1);
            if (options?.IfMatch != null)
            {
                headers.Add(IfMatch(options.IfMatch.Value, nameof(options)));
            }

            var reply = await _client.SendAsync(
                "DELETE", KvRoutes.Entry, KvPaths.Entry(_entries, key), null, headers, cancellationToken);

            // A reader is told 404 when the key was not there; a write-only caller
            // gets 204 either way. A delete of a missing key is done, not failed.
            if (!KvReplies.IsSuccess(reply) && reply.Status != 404)
            {
                throw KvReplies.Error(reply);
            }
        }

        public async Task<KvPage> ListAsync(KvListOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (options?.Limit != null
                && (options.Limit.Value < KvRules.MinListLimit || options.Limit.Value > KvRules.MaxListLimit))
            {
                throw new ArgumentException("Limit must be within KvRules.MinListLimit..MaxListLimit", nameof(options));
            }

            // The store checks a non-empty prefix with the key grammar.
            if (!string.IsNullOrEmpty(options?.Prefix) && !KvRules.IsKey(options!.Prefix))
            {
                throw new ArgumentException("Prefix does not match the store's key grammar", nameof(options));
            }

            var reply = await _client.SendAsync(
                "GET", KvRoutes.Entries, _entries + KvPaths.ListQuery(options), null, null, cancellationToken);
            if (!KvReplies.IsSuccess(reply))
            {
                throw KvReplies.Error(reply);
            }

            var body = KvReplies.Json(reply);
            if (body.Kind != JsonKind.Object)
            {
                throw KvReplies.BadBody(reply);
            }

            var rows = body.GetArrayOrEmpty("entries");
            var entries = new KvListEntry[rows.Count];
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Kind != JsonKind.Object)
                {
                    throw KvReplies.BadBody(reply);
                }

                JsonValue? value = null;
                var valueText = row.GetString("valueText");
                if (valueText != null)
                {
                    if (!Json.TryParse(valueText, out var parsed))
                    {
                        throw KvReplies.BadBody(reply);
                    }

                    value = parsed;
                }

                entries[i] = new KvListEntry(
                    row.GetString("owner"),
                    KvReplies.String(reply, row, "key"),
                    KvReplies.Integer(reply, row, "version"),
                    KvReplies.Integer(reply, row, "bytes"),
                    KvReplies.OptionalInteger(reply, row, "expiresAt"),
                    KvReplies.Integer(reply, row, "updatedAt"),
                    value);
            }

            return new KvPage(entries, body.GetString("nextCursor"));
        }

        public async Task<KvIncrResult> IncrAsync(
            string key,
            long delta,
            KvIncrOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CheckKey(key);
            if (delta > KvRules.MaxSafeInteger || delta < -KvRules.MaxSafeInteger)
            {
                // Past 2^53 a double cannot carry it, and the server refuses it anyway.
                throw new ArgumentOutOfRangeException(nameof(delta), "delta must be within ±KvRules.MaxSafeInteger");
            }

            var ttl = options == null ? null : CheckTtl(options.Ttl, nameof(options));
            var body = Json.Stringify(Json.Object().Set("incr", (double)delta).Build());
            var reply = await _client.SendAsync(
                "PATCH",
                KvRoutes.Incr,
                KvPaths.Entry(_entries, key) + KvPaths.TtlQuery(ttl),
                body,
                null,
                cancellationToken);
            if (!KvReplies.IsSuccess(reply))
            {
                throw KvReplies.Error(reply);
            }

            var result = KvReplies.Json(reply);
            if (result.Kind != JsonKind.Object)
            {
                throw KvReplies.BadBody(reply);
            }

            return new KvIncrResult(
                KvReplies.Integer(reply, result, "value"),
                KvReplies.Integer(reply, result, "version"),
                KvReplies.ExpiresAtOf(reply));
        }

        /// <summary>GET of one entry: the reply on 200, null on 404, a throw otherwise.</summary>
        private async Task<HttpReply?> ReadAsync(string key, CancellationToken cancellationToken)
        {
            CheckKey(key);
            var reply = await _client.SendAsync(
                "GET", KvRoutes.Entry, KvPaths.Entry(_entries, key), null, null, cancellationToken);
            if (reply.Status == 404)
            {
                return null;
            }

            if (!KvReplies.IsSuccess(reply))
            {
                throw KvReplies.Error(reply);
            }

            return reply;
        }

        private static void CheckKey(string key)
        {
            if (!KvRules.IsKey(key))
            {
                throw new ArgumentException("key does not match the store's key grammar", nameof(key));
            }
        }

        private static int? CheckTtl(int? ttl, string parameter)
        {
            if (ttl != null && ttl.Value != 0 && (ttl.Value < KvRules.MinTtlSeconds || ttl.Value > KvRules.MaxTtlSeconds))
            {
                throw new ArgumentException("Ttl must be 0 or within KvRules.MinTtlSeconds..MaxTtlSeconds", parameter);
            }

            return ttl;
        }

        private static KeyValuePair<string, string> IfMatch(long version, string parameter)
        {
            if (version < 1)
            {
                throw new ArgumentException("IfMatch must be a version of 1 or more", parameter);
            }

            return new KeyValuePair<string, string>(
                "If-Match", "\"" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"");
        }
    }
}
