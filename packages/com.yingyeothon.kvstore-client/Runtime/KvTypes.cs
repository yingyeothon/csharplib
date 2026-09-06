using System;
using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore
{
    /// <summary>Who may read or write a collection. A closed set on the server.</summary>
    public enum KvScope
    {
        /// <summary>The console and the CLI only. The API refuses, so this client never has the right.</summary>
        Team,

        /// <summary>Any credential of the collection's project: every player and the server key.</summary>
        Project,

        /// <summary>Each player on its own namespace; the server key on anyone's. Entries live under <c>/u/{ownerId}</c>.</summary>
        User,
    }

    /// <summary>A collection's shape, from <c>GET /kv/{col}</c>.</summary>
    public sealed class KvCollectionInfo
    {
        public KvCollectionInfo(
            string id,
            string name,
            KvScope readScope,
            KvScope writeScope,
            bool encrypted,
            int maxEntries,
            int maxEntriesPerOwner)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ReadScope = readScope;
            WriteScope = writeScope;
            Encrypted = encrypted;
            MaxEntries = maxEntries;
            MaxEntriesPerOwner = maxEntriesPerOwner;
        }

        /// <summary>The collection id, <c>kv_…</c>, whichever way it was addressed.</summary>
        public string Id { get; }

        /// <summary>The collection name as the console holds it.</summary>
        public string Name { get; }

        /// <summary>Who may read entries.</summary>
        public KvScope ReadScope { get; }

        /// <summary>Who may write entries. <see cref="KvScope.User"/> is what puts them under <see cref="IKvCollection.Mine"/>.</summary>
        public KvScope WriteScope { get; }

        /// <summary>Whether values are stored encrypted. Invisible to this client: the store decrypts on read.</summary>
        public bool Encrypted { get; }

        /// <summary>The cap on rows in the whole collection.</summary>
        public int MaxEntries { get; }

        /// <summary>The cap on rows one player may hold in a user namespace.</summary>
        public int MaxEntriesPerOwner { get; }
    }

    /// <summary>One entry with its version, from <see cref="IKvNamespace.GetEntryAsync"/>.</summary>
    public sealed class KvEntry
    {
        public KvEntry(JsonValue value, long version, long? expiresAt)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
            Version = version;
            ExpiresAt = expiresAt;
        }

        /// <summary>The stored value. A stored JSON <c>null</c> is <see cref="JsonValue.Null"/>, never a C# null.</summary>
        public JsonValue Value { get; }

        /// <summary>The version, for <see cref="KvPutOptions.IfMatch"/>. It climbs on every write and never resets.</summary>
        public long Version { get; }

        /// <summary>When the entry expires, in Unix seconds, or null when it does not.</summary>
        public long? ExpiresAt { get; }
    }

    /// <summary>Options for <see cref="IKvNamespace.PutAsync"/>.</summary>
    public sealed class KvPutOptions
    {
        /// <summary>
        /// Seconds until the entry expires: <see cref="KvRules.MinTtlSeconds"/> to
        /// <see cref="KvRules.MaxTtlSeconds"/>, or <c>0</c> to clear an expiry. Null keeps
        /// whatever the entry has (no expiry, on a create).
        /// </summary>
        public int? Ttl { get; set; }

        /// <summary>Write only if the stored version is this one; a mismatch is a 409 with <see cref="KvStoreException.CurrentVersion"/>. Needs the read right.</summary>
        public long? IfMatch { get; set; }

        /// <summary>Create only: refuse with a 409 when the key exists. Needs the read right. Cannot be combined with <see cref="IfMatch"/>.</summary>
        public bool IfNoneMatch { get; set; }
    }

    /// <summary>Options for <see cref="IKvNamespace.DeleteAsync"/>.</summary>
    public sealed class KvDeleteOptions
    {
        /// <summary>Delete only if the stored version is this one. Needs the read right.</summary>
        public long? IfMatch { get; set; }
    }

    /// <summary>What a write answered. Every field is null for a caller without the read right.</summary>
    /// <remarks>
    /// Whether the key existed and how many times it has been written are facts about
    /// stored data, so the store withholds them from a write-only caller: it answers
    /// <c>204</c> with no <c>ETag</c> for a create and an update alike.
    /// </remarks>
    public sealed class KvWriteResult
    {
        public KvWriteResult(bool? created, long? version, long? expiresAt)
        {
            Created = created;
            Version = version;
            ExpiresAt = expiresAt;
        }

        /// <summary>True for a create (<c>201</c>), false for an update (<c>204</c> with an <c>ETag</c>), null when the store said neither.</summary>
        public bool? Created { get; }

        /// <summary>The version this write produced, from the <c>ETag</c>; null without the read right.</summary>
        public long? Version { get; }

        /// <summary>The expiry this write set, in Unix seconds; null when the write set none.</summary>
        public long? ExpiresAt { get; }
    }

    /// <summary>List order by key.</summary>
    public enum KvOrder
    {
        /// <summary>Ascending key order, the server's default.</summary>
        Asc,

        /// <summary>Descending key order.</summary>
        Desc,
    }

    /// <summary>Options for <see cref="IKvNamespace.ListAsync"/>.</summary>
    public sealed class KvListOptions
    {
        /// <summary>Only keys starting with this. A non-empty prefix must satisfy <see cref="KvRules.IsKey"/>, as the store checks it with the key grammar.</summary>
        public string? Prefix { get; set; }

        /// <summary>The <see cref="KvPage.NextCursor"/> of the previous page.</summary>
        public string? Cursor { get; set; }

        /// <summary>Rows per page, <see cref="KvRules.MinListLimit"/> to <see cref="KvRules.MaxListLimit"/>. Null lets the server choose (50).</summary>
        public int? Limit { get; set; }

        /// <summary>Key order. Null is the server's default, ascending.</summary>
        public KvOrder? Order { get; set; }

        /// <summary>Whether each row carries its value. Off, a row is metadata only.</summary>
        public bool Values { get; set; }
    }

    /// <summary>One row of a listing.</summary>
    public sealed class KvListEntry
    {
        public KvListEntry(
            string? owner,
            string key,
            long version,
            long bytes,
            long? expiresAt,
            long updatedAt,
            JsonValue? value)
        {
            Owner = owner;
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Version = version;
            Bytes = bytes;
            ExpiresAt = expiresAt;
            UpdatedAt = updatedAt;
            Value = value;
        }

        /// <summary>The owner, only where owners are a namespace; null on a shared collection.</summary>
        public string? Owner { get; }

        /// <summary>The key.</summary>
        public string Key { get; }

        /// <summary>The version.</summary>
        public long Version { get; }

        /// <summary>The value's size in UTF-8 bytes, as stored.</summary>
        public long Bytes { get; }

        /// <summary>When the entry expires, in Unix seconds, or null.</summary>
        public long? ExpiresAt { get; }

        /// <summary>When the entry was last written, in Unix seconds.</summary>
        public long UpdatedAt { get; }

        /// <summary>The value when <see cref="KvListOptions.Values"/> was set, else a C# null. A stored JSON null is <see cref="JsonValue.Null"/>.</summary>
        public JsonValue? Value { get; }
    }

    /// <summary>One page of a listing.</summary>
    public sealed class KvPage
    {
        public KvPage(IReadOnlyList<KvListEntry> entries, string? nextCursor)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
            NextCursor = nextCursor;
        }

        /// <summary>The rows, in key order.</summary>
        public IReadOnlyList<KvListEntry> Entries { get; }

        /// <summary>Pass as <see cref="KvListOptions.Cursor"/> for the next page; null on the last one.</summary>
        public string? NextCursor { get; }
    }

    /// <summary>Options for <see cref="IKvNamespace.IncrAsync"/>.</summary>
    public sealed class KvIncrOptions
    {
        /// <summary>Seconds until the counter expires, with the same meaning as <c>KvPutOptions.Ttl</c>.</summary>
        public int? Ttl { get; set; }
    }

    /// <summary>What an <c>incr</c> answered.</summary>
    public sealed class KvIncrResult
    {
        public KvIncrResult(long value, long version, long? expiresAt)
        {
            Value = value;
            Version = version;
            ExpiresAt = expiresAt;
        }

        /// <summary>The counter after the increment.</summary>
        public long Value { get; }

        /// <summary>The version the increment produced.</summary>
        public long Version { get; }

        /// <summary>The expiry this call set, in Unix seconds; null when it set none.</summary>
        public long? ExpiresAt { get; }
    }
}
