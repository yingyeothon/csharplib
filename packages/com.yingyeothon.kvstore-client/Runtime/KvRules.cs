namespace Yingyeothon.KvStore
{
    /// <summary>
    /// The store's own limits and grammars, so a caller can check before sending.
    /// </summary>
    /// <remarks>
    /// Every constant here is the server's, cited to
    /// <c>packages/console-db/src/kvstore.ts</c> in the <c>service</c> repository, and
    /// the client refuses locally exactly what the server would refuse — nothing
    /// stricter. A key or a name that passes these needs no escaping on the path,
    /// which is why the grammars are what they are.
    /// </remarks>
    public static class KvRules
    {
        /// <summary>The largest value, in UTF-8 bytes of its JSON text (<c>MAX_KV_VALUE_BYTES</c>).</summary>
        public const int MaxValueBytes = 16 * 1024;

        /// <summary>The longest key (<c>KV_KEY_RE</c>: <c>^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$</c>).</summary>
        public const int MaxKeyLength = 128;

        /// <summary>The longest collection name (<c>^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$</c>, the console's grammar).</summary>
        public const int MaxCollectionNameLength = 64;

        /// <summary>The smallest <c>ttl</c> in seconds (<c>KV_TTL_MIN_SECONDS</c>). <c>0</c> is allowed too: it clears the expiry.</summary>
        public const int MinTtlSeconds = 1;

        /// <summary>The largest <c>ttl</c> in seconds: 366 days (<c>KV_TTL_MAX_SECONDS</c>).</summary>
        public const int MaxTtlSeconds = 366 * 24 * 60 * 60;

        /// <summary>The smallest list <c>limit</c> (<c>kvPageLimit</c>).</summary>
        public const int MinListLimit = 1;

        /// <summary>The largest list <c>limit</c> (<c>KV_LIST_LIMIT_MAX</c>). Omitted, the server uses 50.</summary>
        public const int MaxListLimit = 100;

        /// <summary>The largest magnitude an <c>incr</c> delta or a counter may have: JavaScript's safe integer.</summary>
        public const long MaxSafeInteger = 9007199254740991;

        /// <summary>The owner alias for the player the token names.</summary>
        public const string Me = "me";

        /// <summary>Whether <paramref name="key"/> matches the key grammar.</summary>
        public static bool IsKey(string? key)
            => key != null && key.Length >= 1 && key.Length <= MaxKeyLength
                && IsAlphanumeric(key[0]) && Rest(key, 1, allowColon: true);

        /// <summary>Whether <paramref name="nameOrId"/> is a collection id (<c>kv_</c> and 26 of <c>[0-9a-z]</c>).</summary>
        public static bool IsCollectionId(string? nameOrId)
        {
            if (nameOrId == null || nameOrId.Length != 29 || !nameOrId.StartsWith("kv_", System.StringComparison.Ordinal))
            {
                return false;
            }

            for (var i = 3; i < nameOrId.Length; i++)
            {
                var c = nameOrId[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether <paramref name="nameOrId"/> is a collection id, or a name the store
        /// would look up: the name grammar, and not id-shaped once lower-cased, which
        /// the console refuses and the store answers without a lookup.
        /// </summary>
        public static bool IsCollectionRef(string? nameOrId)
            => IsCollectionId(nameOrId)
                || (nameOrId != null && nameOrId.Length >= 1 && nameOrId.Length <= MaxCollectionNameLength
                    && IsAlphanumeric(nameOrId[0]) && Rest(nameOrId, 1, allowColon: false)
                    && !IsCollectionId(nameOrId.ToLowerInvariant()));

        /// <summary>
        /// Whether <paramref name="ownerId"/> is <see cref="Me"/>, a player id (32 lower-case
        /// hex, what a token's <c>sub</c> holds) or a group owner <c>{kind}:{id}</c>
        /// (<c>KV_OWNER_ID</c>).
        /// </summary>
        public static bool IsOwnerId(string? ownerId)
        {
            if (ownerId == null)
            {
                return false;
            }

            if (ownerId == Me)
            {
                return true;
            }

            if (ownerId.Length == 32)
            {
                var hex = true;
                foreach (var c in ownerId)
                {
                    if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    {
                        hex = false;
                        break;
                    }
                }

                if (hex)
                {
                    return true;
                }
            }

            // {kind}:{id} — [a-z]{1,8} ':' [A-Za-z0-9_-]{1,48}
            var colon = ownerId.IndexOf(':');
            if (colon < 1 || colon > 8)
            {
                return false;
            }

            for (var i = 0; i < colon; i++)
            {
                var c = ownerId[i];
                if (c < 'a' || c > 'z')
                {
                    return false;
                }
            }

            var idLength = ownerId.Length - colon - 1;
            if (idLength < 1 || idLength > 48)
            {
                return false;
            }

            for (var i = colon + 1; i < ownerId.Length; i++)
            {
                var c = ownerId[i];
                if (!(IsAlphanumeric(c) || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAlphanumeric(char c)
            => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

        private static bool Rest(string value, int from, bool allowColon)
        {
            for (var i = from; i < value.Length; i++)
            {
                var c = value[i];
                if (!(IsAlphanumeric(c) || c == '.' || c == '_' || c == '-' || (allowColon && c == ':')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
