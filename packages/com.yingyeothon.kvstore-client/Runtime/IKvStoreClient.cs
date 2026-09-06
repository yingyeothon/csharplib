using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore
{
    /// <summary>A key-value store client bound to one base URL and one token.</summary>
    /// <remarks>
    /// Holds no state beyond its options and keeps no cache: every call is one
    /// request, and the store answers <c>Cache-Control: no-store</c>. A new token is a
    /// new client.
    /// </remarks>
    public interface IKvStoreClient
    {
        /// <summary>
        /// Addresses a collection by its name or its <c>kv_</c> id. Pure: nothing is
        /// sent until an operation is called. Throws <see cref="System.ArgumentException"/>
        /// for a segment that is neither an id nor a name the console could hold.
        /// </summary>
        IKvCollection Collection(string nameOrId);
    }

    /// <summary>One collection: its shape, its shared namespace, and its owner namespaces.</summary>
    /// <remarks>
    /// The collection itself is the <b>shared</b> namespace (<c>/kv/{col}/entries</c>),
    /// which is right for a collection whose write scope is <c>project</c>. A collection
    /// whose write scope is <c>user</c> keeps one namespace per owner, reached through
    /// <see cref="Mine"/> or <see cref="Owner"/>; using the wrong one is a 400 with
    /// <see cref="KvReasons.WrongNamespace"/>.
    /// </remarks>
    public interface IKvCollection : IKvNamespace
    {
        /// <summary>What was passed to <see cref="IKvStoreClient.Collection"/>.</summary>
        string Ref { get; }

        /// <summary>The collection's scopes, flag and caps. A 403 when both scopes are <c>team</c>.</summary>
        Task<KvCollectionInfo> InfoAsync(CancellationToken cancellationToken = default);

        /// <summary>The namespace of the player the token names: <c>/kv/{col}/u/me/entries</c>. A player token only.</summary>
        IKvNamespace Mine { get; }

        /// <summary>
        /// One owner's namespace: <c>/kv/{col}/u/{ownerId}/entries</c>. Throws
        /// <see cref="System.ArgumentException"/> unless <see cref="KvRules.IsOwnerId"/>.
        /// </summary>
        IKvNamespace Owner(string ownerId);
    }

    /// <summary>The six operations on one namespace of entries.</summary>
    /// <remarks>
    /// Every method throws <see cref="System.ArgumentException"/> before any request for
    /// input the store would refuse — see <see cref="KvRules"/> — and
    /// <see cref="KvStoreException"/> for what the store or the network refused. A
    /// cancelled token surfaces as <see cref="System.OperationCanceledException"/>.
    /// </remarks>
    public interface IKvNamespace
    {
        /// <summary>
        /// The stored value, or a C# null on a <c>404</c> — no such entry, and also no
        /// such collection, which this call does not tell apart. A stored JSON null is
        /// <see cref="JsonValue.Null"/>.
        /// </summary>
        Task<JsonValue?> GetAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>The stored value with its version and expiry, or null on a <c>404</c>, as for <see cref="GetAsync"/>.</summary>
        Task<KvEntry?> GetEntryAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, as its compact JSON text, byte for byte.</summary>
        Task<KvWriteResult> PutAsync(
            string key,
            JsonValue value,
            KvPutOptions? options = null,
            CancellationToken cancellationToken = default);

        /// <summary>Deletes an entry. A <c>404</c> — no such key, or no such collection — is treated as done.</summary>
        Task DeleteAsync(string key, KvDeleteOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>One page of entries, in key order.</summary>
        Task<KvPage> ListAsync(KvListOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds <paramref name="delta"/> to a stored integer atomically, creating it from
        /// zero. Needs the read right as well as the write right; a stored value that is
        /// not an integer is a 409 with <see cref="KvReasons.NotANumber"/>.
        /// </summary>
        Task<KvIncrResult> IncrAsync(
            string key,
            long delta,
            KvIncrOptions? options = null,
            CancellationToken cancellationToken = default);
    }
}
