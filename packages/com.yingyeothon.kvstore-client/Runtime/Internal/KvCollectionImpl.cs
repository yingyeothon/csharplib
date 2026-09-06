using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.KvStore
{
    internal sealed class KvCollectionImpl : KvNamespaceImpl, IKvCollection
    {
        private readonly KvStoreClientImpl _client;

        internal KvCollectionImpl(KvStoreClientImpl client, string reference)
            : base(client, KvPaths.Shared(reference))
        {
            _client = client;
            Ref = reference;
            Mine = new KvNamespaceImpl(client, KvPaths.Owned(reference, KvRules.Me));
        }

        public string Ref { get; }

        public IKvNamespace Mine { get; }

        public IKvNamespace Owner(string ownerId)
        {
            if (!KvRules.IsOwnerId(ownerId))
            {
                throw new ArgumentException(
                    "ownerId must be 'me', a 32-hex player id or a {kind}:{id} group owner", nameof(ownerId));
            }

            return new KvNamespaceImpl(_client, KvPaths.Owned(Ref, ownerId));
        }

        public async Task<KvCollectionInfo> InfoAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _client.SendAsync("GET", KvRoutes.Meta, KvPaths.Meta(Ref), null, null, cancellationToken);
            if (!KvReplies.IsSuccess(reply))
            {
                throw KvReplies.Error(reply);
            }

            var body = KvReplies.Json(reply);
            if (body.Kind != Codec.JsonKind.Object)
            {
                throw KvReplies.BadBody(reply);
            }

            var encrypted = body.GetBool("encrypted") ?? throw KvReplies.BadBody(reply);
            return new KvCollectionInfo(
                KvReplies.String(reply, body, "id"),
                KvReplies.String(reply, body, "name"),
                KvReplies.Scope(reply, body.GetString("readScope")),
                KvReplies.Scope(reply, body.GetString("writeScope")),
                encrypted,
                KvReplies.Count(reply, body, "maxEntries"),
                KvReplies.Count(reply, body, "maxEntriesPerOwner"));
        }
    }
}
