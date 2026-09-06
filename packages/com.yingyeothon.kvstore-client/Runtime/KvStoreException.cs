using System;

namespace Yingyeothon.KvStore
{
    /// <summary>The <see cref="KvStoreException.Code"/> values this client produces or passes through.</summary>
    /// <remarks>
    /// The server's codes are the <c>error.code</c> of its JSON error body; the
    /// client's own carry <see cref="KvStoreException.Status"/> <c>0</c> when nothing
    /// was answered, or the status of a reply the client could not read.
    /// </remarks>
    public static class KvErrorCodes
    {
        /// <summary>No reply arrived: the transport threw. The cause is the inner exception.</summary>
        public const string Network = "network";

        /// <summary>No reply arrived within <see cref="KvStoreClientOptions.Timeout"/>.</summary>
        public const string Timeout = "timeout";

        /// <summary>A success reply whose body or headers were not what the store sends.</summary>
        public const string BadBody = "bad_body";

        /// <summary>A failure reply that carried no <c>{error:{code}}</c> body — a gateway or proxy answered.</summary>
        public const string Http = "http";

        /// <summary>400. A grammar or a header the store refused; often with a <see cref="KvReasons"/>.</summary>
        public const string BadRequest = "bad_request";

        /// <summary>401. The token did not verify, or its channel is gone.</summary>
        public const string Unauthorized = "unauthorized";

        /// <summary>403. The collection's scope does not admit this caller for this operation.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>404. No such collection in the caller's project. <c>GetAsync</c>, <c>GetEntryAsync</c> and <c>DeleteAsync</c> fold a 404 into null or done instead of throwing it.</summary>
        public const string NotFound = "not_found";

        /// <summary>409. A lost compare-and-set, a full collection, or a value that is not a counter.</summary>
        public const string Conflict = "conflict";

        /// <summary>413. The value is over <see cref="KvRules.MaxValueBytes"/>; the client refuses this first.</summary>
        public const string PayloadTooLarge = "payload_too_large";

        /// <summary>503. The store cannot serve values right now; see <see cref="KvReasons"/>.</summary>
        public const string Unavailable = "unavailable";
    }

    /// <summary>The <see cref="KvStoreException.Reason"/> values the store attaches to a refusal.</summary>
    public static class KvReasons
    {
        /// <summary>409: the collection holds <c>maxEntries</c> rows already.</summary>
        public const string CollectionFull = "collection_full";

        /// <summary>409: this owner holds <c>maxEntriesPerOwner</c> rows already.</summary>
        public const string OwnerFull = "owner_full";

        /// <summary>409: the console cannot write an encrypted collection. Not reachable from this client.</summary>
        public const string Encrypted = "encrypted";

        /// <summary>409 on <c>incr</c>: the stored value is not a safe integer.</summary>
        public const string NotANumber = "not_a_number";

        /// <summary>409 on <c>incr</c>: the result would leave the safe integer range.</summary>
        public const string Overflow = "overflow";

        /// <summary>400: the shared path was used on a user-namespace collection, or the reverse.</summary>
        public const string WrongNamespace = "wrong_namespace";

        /// <summary>503: the stage has no usable key; every kv route answers this.</summary>
        public const string EncryptionNotConfigured = "kv_encryption_not_configured";

        /// <summary>503: one stored value would not open.</summary>
        public const string ValueUnreadable = "kv_value_unreadable";
    }

    /// <summary>A store request that did not succeed.</summary>
    /// <remarks>
    /// The message is <c>"kv {code} ({status})"</c> and nothing else — never a key, a
    /// value, a URL or the token — so it is safe to log as it is. A refusal the client
    /// can settle locally (a bad key, an oversize value, a ttl out of range) is an
    /// <see cref="ArgumentException"/> thrown before any request, not this.
    /// </remarks>
    public sealed class KvStoreException : Exception
    {
        /// <summary>Creates an exception for a status and a code, with no reason and no version.</summary>
        public KvStoreException(int status, string code)
            : this(status, code, null, null, false, null)
        {
        }

        /// <summary>Creates an exception carrying everything the error body said.</summary>
        /// <param name="status">The HTTP status, or 0 when no reply arrived.</param>
        /// <param name="code">The <see cref="KvErrorCodes"/> value.</param>
        /// <param name="reason">The <c>details.reason</c>, when the body had one.</param>
        /// <param name="currentVersion">The <c>details.current</c> of a lost compare-and-set; null when the entry is absent.</param>
        /// <param name="hasCurrentVersion">Whether <c>details.current</c> was present at all, so a null version can be told from an omitted one.</param>
        /// <param name="innerException">The transport failure behind a <see cref="KvErrorCodes.Network"/> or <see cref="KvErrorCodes.Timeout"/>.</param>
        public KvStoreException(
            int status,
            string code,
            string? reason,
            long? currentVersion,
            bool hasCurrentVersion,
            Exception? innerException)
            : base("kv " + (code ?? throw new ArgumentNullException(nameof(code))) + " (" + status + ")", innerException)
        {
            Status = status;
            Code = code;
            Reason = reason;
            CurrentVersion = currentVersion;
            HasCurrentVersion = hasCurrentVersion;
        }

        /// <summary>The HTTP status, or 0 when no reply arrived (<see cref="KvErrorCodes.Network"/>, <see cref="KvErrorCodes.Timeout"/>).</summary>
        public int Status { get; }

        /// <summary>The error code: the server's <c>error.code</c>, or one of the client's own.</summary>
        public string Code { get; }

        /// <summary>The server's <c>details.reason</c>, when it named one. See <see cref="KvReasons"/>.</summary>
        public string? Reason { get; }

        /// <summary>
        /// The live version a compare-and-set lost to, or null when the entry does not
        /// exist. Only sent to a caller with the read right; check
        /// <see cref="HasCurrentVersion"/> before reading a null as "absent".
        /// </summary>
        public long? CurrentVersion { get; }

        /// <summary>Whether the reply carried <c>details.current</c> at all.</summary>
        public bool HasCurrentVersion { get; }

        /// <summary>A 409: a lost compare-and-set, a cap, or a value that is not a counter.</summary>
        public bool IsConflict => Status == 409;

        /// <summary>A 403: the collection's scope does not admit this caller, or a conditional write without the read right.</summary>
        public bool IsForbidden => Status == 403;

        /// <summary>A 401: the token did not verify.</summary>
        public bool IsUnauthorized => Status == 401;

        /// <summary>A 409 whose reason is <see cref="KvReasons.CollectionFull"/> or <see cref="KvReasons.OwnerFull"/>.</summary>
        public bool IsFull => Status == 409
            && (Reason == KvReasons.CollectionFull || Reason == KvReasons.OwnerFull);
    }
}
