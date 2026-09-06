using System;
using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore
{
    /// <summary>Reads what the store answers: headers, numbers and the error body.</summary>
    internal static class KvReplies
    {
        /// <summary>
        /// The longest body the client will read, in characters: the codec's own
        /// frame limit, which is far above a page of a hundred 16 KiB values.
        /// </summary>
        internal const int MaxBodyChars = Codec.Json.MaxLength;

        internal static bool IsSuccess(HttpReply reply) => reply.Status >= 200 && reply.Status <= 299;

        /// <summary>The first header with this name, case-insensitively, or null.</summary>
        internal static string? Header(HttpReply reply, string name)
        {
            foreach (var header in reply.Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }

        /// <summary><c>"3"</c>, <c>W/"3"</c> and a bare <c>3</c> all mean version 3; anything else is null.</summary>
        internal static long? Version(string? etag)
        {
            if (etag == null)
            {
                return null;
            }

            var text = etag.Trim();
            if (text.StartsWith("W/", StringComparison.Ordinal))
            {
                text = text.Substring(2);
            }

            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                text = text.Substring(1, text.Length - 2);
            }

            return Digits(text);
        }

        /// <summary>The <c>ETag</c> as a version, or null when the reply carried none.</summary>
        internal static long? VersionOf(HttpReply reply) => Version(Header(reply, "ETag"));

        /// <summary>The <c>X-KV-Expires-At</c> as Unix seconds, or null when absent.</summary>
        internal static long? ExpiresAtOf(HttpReply reply)
        {
            var raw = Header(reply, "X-KV-Expires-At");
            if (raw == null)
            {
                return null;
            }

            return Digits(raw.Trim()) ?? throw BadBody(reply);
        }

        /// <summary>A run of one to fifteen ASCII digits, or null.</summary>
        internal static long? Digits(string text)
        {
            if (text.Length < 1 || text.Length > 15)
            {
                return null;
            }

            long value = 0;
            foreach (var c in text)
            {
                if (c < '0' || c > '9')
                {
                    return null;
                }

                value = value * 10 + (c - '0');
            }

            return value;
        }

        /// <summary>The body as a JSON value, or <see cref="KvErrorCodes.BadBody"/>.</summary>
        internal static JsonValue Json(HttpReply reply)
            => Codec.Json.TryParse(reply.Body, out var value) ? value : throw BadBody(reply);

        /// <summary>A required integer member of an object, or <see cref="KvErrorCodes.BadBody"/>.</summary>
        internal static long Integer(HttpReply reply, JsonValue owner, string key)
            => OptionalInteger(reply, owner, key) ?? throw BadBody(reply);

        /// <summary>An integer member that may be absent or null; a non-integer is <see cref="KvErrorCodes.BadBody"/>.</summary>
        internal static long? OptionalInteger(HttpReply reply, JsonValue owner, string key)
        {
            if (!owner.TryGetMember(key, out var member) || member.IsNull)
            {
                return null;
            }

            if (member.Kind != JsonKind.Number)
            {
                throw BadBody(reply);
            }

            var number = member.AsNumber();
            if (number != Math.Floor(number) || Math.Abs(number) > KvRules.MaxSafeInteger)
            {
                throw BadBody(reply);
            }

            return (long)number;
        }

        /// <summary>A required string member, or <see cref="KvErrorCodes.BadBody"/>.</summary>
        internal static string String(HttpReply reply, JsonValue owner, string key)
            => owner.GetString(key) ?? throw BadBody(reply);

        /// <summary>A required non-negative integer member that fits an <c>int</c>, or <see cref="KvErrorCodes.BadBody"/>.</summary>
        internal static int Count(HttpReply reply, JsonValue owner, string key)
        {
            var value = Integer(reply, owner, key);
            return value >= 0 && value <= int.MaxValue ? (int)value : throw BadBody(reply);
        }

        /// <summary>
        /// Whether a wire-supplied code or reason is one the store could have written:
        /// lower-case letters, digits and underscores, at most 32 of them. Anything
        /// else is a proxy's or an attacker's, and it must not reach an exception
        /// message the game will log.
        /// </summary>
        internal static bool IsWireCode(string? text)
        {
            if (text == null || text.Length < 1 || text.Length > 32)
            {
                return false;
            }

            foreach (var c in text)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        internal static KvScope Scope(HttpReply reply, string? text)
        {
            switch (text)
            {
                case "team":
                    return KvScope.Team;
                case "project":
                    return KvScope.Project;
                case "user":
                    return KvScope.User;
                default:
                    throw BadBody(reply);
            }
        }

        internal static KvStoreException BadBody(HttpReply reply)
            => new KvStoreException(reply.Status, KvErrorCodes.BadBody);

        /// <summary>
        /// The exception for a failure reply: the server's <c>{error:{code, details}}</c>
        /// when the body has that shape, else <see cref="KvErrorCodes.Http"/> with the
        /// status — a proxy or the edge answered, not the store.
        /// </summary>
        internal static KvStoreException Error(HttpReply reply)
        {
            string code = KvErrorCodes.Http;
            string? reason = null;
            long? current = null;
            var hasCurrent = false;

            if (Codec.Json.TryParse(reply.Body, out var body)
                && body.TryGetMember("error", out var error)
                && error.Kind == JsonKind.Object)
            {
                var wireCode = error.GetString("code");
                code = IsWireCode(wireCode) ? wireCode! : KvErrorCodes.Http;
                if (error.TryGetMember("details", out var details) && details.Kind == JsonKind.Object)
                {
                    var wireReason = details.GetString("reason");
                    reason = IsWireCode(wireReason) ? wireReason : null;
                    if (details.TryGetMember("current", out var live))
                    {
                        hasCurrent = true;
                        if (live.Kind == JsonKind.Number)
                        {
                            var number = live.AsNumber();
                            current = number == Math.Floor(number) ? (long?)number : null;
                        }
                    }
                }
            }

            return new KvStoreException(reply.Status, code, reason, current, hasCurrent, null);
        }

        internal static IReadOnlyList<KeyValuePair<string, string>> NoHeaders { get; } = new KeyValuePair<string, string>[0];
    }
}
