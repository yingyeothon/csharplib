#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Samples
{
    /// <summary>
    /// The two things a game does with the store: read what the team published, and
    /// keep the player's own record. Engine-free; <c>KvStoreQuickstart</c> is the
    /// <c>MonoBehaviour</c> over it.
    /// </summary>
    /// <remarks>
    /// The console holds two collections for this: <c>announcements</c> with
    /// <c>readScope: project</c> and <c>writeScope: team</c>, and <c>profile</c> with
    /// both scopes <c>user</c>. The token is the same channel JWT the gateway client
    /// uses; this class never stores it anywhere else.
    /// </remarks>
    public sealed class KvStoreSession
    {
        private readonly IKvCollection _announcements;
        private readonly IKvNamespace _mine;

        /// <param name="baseUrl">The store origin, <c>https://doc.yyt.life</c>.</param>
        /// <param name="channelJwt">The channel JWT from your auth channel.</param>
        /// <param name="transport">Null on every platform but WebGL, which passes <c>UnityWebRequestTransport.Instance</c>.</param>
        public KvStoreSession(string baseUrl, string channelJwt, IHttpTransport? transport = null)
        {
            var kv = KvStoreClient.Create(new KvStoreClientOptions
            {
                BaseUrl = baseUrl,
                Token = channelJwt,
                Transport = transport,
            });
            _announcements = kv.Collection("announcements");
            _mine = kv.Collection("profile").Mine;
        }

        /// <summary>
        /// The announcements, values included, in descending key order — name the keys
        /// by date (<c>2026-09-06</c>) and that is newest first. One page is enough for
        /// a notice board; <c>NextCursor</c> pages the rest.
        /// </summary>
        public async Task<IReadOnlyList<KvListEntry>> ReadAnnouncementsAsync(CancellationToken cancellationToken = default)
        {
            var page = await _announcements.ListAsync(
                new KvListOptions { Values = true, Order = KvOrder.Desc, Limit = 20 },
                cancellationToken);
            return page.Entries;
        }

        /// <summary>The player's saved settings, or null the first time.</summary>
        public Task<JsonValue?> LoadSettingsAsync(CancellationToken cancellationToken = default)
            => _mine.GetAsync("settings", cancellationToken);

        /// <summary>Saves the settings. An unconditional write: the last save wins, which is what a settings screen wants.</summary>
        public Task SaveSettingsAsync(JsonValue settings, CancellationToken cancellationToken = default)
            => _mine.PutAsync("settings", settings, null, cancellationToken);

        /// <summary>Counts a play atomically and returns the total, so two devices cannot lose a count between them.</summary>
        public async Task<long> CountPlayAsync(CancellationToken cancellationToken = default)
        {
            var result = await _mine.IncrAsync("plays", 1, null, cancellationToken);
            return result.Value;
        }
    }
}
