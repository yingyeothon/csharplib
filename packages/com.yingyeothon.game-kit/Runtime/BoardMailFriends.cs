using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;
using Yingyeothon.Platform;

namespace Yingyeothon.GameKit
{
    /// <summary>Purpose-shaped leaderboard.</summary>
    public interface IKitBoard
    {
        /// <summary>Board metadata.</summary>
        Task<JsonValue> InfoAsync(CancellationToken cancellationToken = default);
        /// <summary>Stored scores plus each own period rank.</summary>
        Task<JsonValue> SubmitAsync(PlatformScoreOptions options, CancellationToken cancellationToken = default);
        /// <summary>Leaderboard page.</summary>
        Task<JsonValue> TopAsync(PlatformBoardPageOptions options, CancellationToken cancellationToken = default);
        /// <summary>Own rank or null.</summary>
        Task<JsonValue?> MeAsync(string period, CancellationToken cancellationToken = default);
        /// <summary>Neighborhood around own rank; span must be zero to 49.</summary>
        Task<JsonValue> AroundAsync(string period, int span, CancellationToken cancellationToken = default);
    }
    /// <summary>Player inbox with authenticated writer stamps.</summary>
    public interface IKitMail
    {
        /// <summary>All letters, ordered by write timestamp descending.</summary>
        Task<IReadOnlyList<KvListEntry>> ListAsync(CancellationToken cancellationToken = default);
        /// <summary>One letter, or null.</summary>
        Task<KvEntry?> ReadAsync(string id, CancellationToken cancellationToken = default);
        /// <summary>Conditionally deletes and returns a letter; a lost claim reports already_claimed.</summary>
        Task<KvEntry> ClaimAsync(string id, CancellationToken cancellationToken = default);
        /// <summary>Removes a letter.</summary>
        Task RemoveAsync(string id, CancellationToken cancellationToken = default);
        /// <summary>Sends create-only player mail using an authenticated sender prefix.</summary>
        Task SendAsync(KitMailSendOptions options, CancellationToken cancellationToken = default);
        /// <summary>Raised once per observed letter id in this session.</summary>
        event Action<KvListEntry> Arrived;
    }
    /// <summary>Player mail input.</summary>
    public sealed class KitMailSendOptions
    {
        /// <summary>Recipient owner id.</summary>
        public string To { get; set; } = "";
        /// <summary>JSON letter.</summary>
        public JsonValue Value { get; set; } = JsonValue.Null;
        /// <summary>Optional TTL in seconds.</summary>
        public int? Ttl { get; set; }
    }
    /// <summary>Social profiles, relations and presence.</summary>
    public interface IKitFriends
    {
        /// <summary>Friends joined with gateway online state.</summary>
        Task<JsonValue> ListAsync(CancellationToken cancellationToken = default);
        /// <summary>Incoming and outgoing requests.</summary>
        Task<JsonValue> RequestsAsync(CancellationToken cancellationToken = default);
        /// <summary>Requests a relation.</summary>
        Task RequestAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Accepts a relation request.</summary>
        Task AcceptAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Declines a relation request.</summary>
        Task DeclineAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Removes a relation.</summary>
        Task RemoveAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Blocks a player.</summary>
        Task BlockAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Unblocks a player.</summary>
        Task UnblockAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Own profile.</summary>
        Task<JsonValue?> ProfileAsync(CancellationToken cancellationToken = default);
        /// <summary>Edits own profile.</summary>
        Task<JsonValue> SetProfileAsync(PlatformProfileOptions options, CancellationToken cancellationToken = default);
        /// <summary>Batch profiles of at most fifty players.</summary>
        Task<JsonValue> ProfilesAsync(IReadOnlyList<string> userIds, CancellationToken cancellationToken = default);
        /// <summary>Invites an online friend to the current room.</summary>
        Task InviteAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>A new incoming request observed by foreground polling.</summary>
        event Action<JsonValue> Requested;
    }
    internal sealed class BoardImpl : IKitBoard
    {
        private readonly KitRuntime _r; private readonly string _name; internal BoardImpl(KitRuntime r, string name) { _r = r; _name = name; }
        private IPlatformBoard Board() => _r.Platform().Board(_name);
        public Task<JsonValue> InfoAsync(CancellationToken ct = default) => _r.Run("board", () => Board().InfoAsync(ct), ct);
        public Task<JsonValue> TopAsync(PlatformBoardPageOptions o, CancellationToken ct = default) => _r.Run("board", () => Board().TopAsync(o, ct), ct);
        public Task<JsonValue?> MeAsync(string period, CancellationToken ct = default) => _r.Run("board", () => Board().MeAsync(period, ct), ct);
        public Task<JsonValue> SubmitAsync(PlatformScoreOptions o, CancellationToken ct = default) => _r.Run("board", async () =>
        {


            var generation = _r.Generation; var b = Board(); var info = await b.InfoAsync(ct); _r.Check(generation); if (info.GetString("submit") == "server")
            {
                throw new KitException("board", "server_only");
            }

            var submitted = await b.SubmitAsync(o, ct); _r.Check(generation); var ranks = new List<JsonValue>();
            foreach (var bucket in submitted.GetArrayOrEmpty("periods")) { var period = bucket.GetString("period") ?? throw new KitException("board", "unavailable"); var me = await b.MeAsync(period, ct); _r.Check(generation); if (me == null) { throw new KitException("board", "unavailable"); } ranks.Add(Json.Object().Set("period", period).Set("rank", me.GetNumber("rank") ?? 0).Set("score", bucket.GetNumber("score") ?? 0).Set("endsAt", bucket.TryGetMember("periodEndsAt", out var at) ? at : JsonValue.Null).Build()); }
            return Json.Object().Set("periods", JsonValue.Array(ranks)).Build();
        }, ct);
        public Task<JsonValue> AroundAsync(string period, int span, CancellationToken ct = default) => _r.Run("board", async ()

 =>
        { if (span < 0 || span > 49) { throw new ArgumentException("Span must be 0 to 49", nameof(span)); } var b = Board(); var me = await b.MeAsync(period, ct); if (me == null) { return Json.Object().Set("entries", JsonValue.Array(Array.Empty<JsonValue>())).Set("total", 0).Build(); } return await b.TopAsync(new PlatformBoardPageOptions { Period = period, Limit = span * 2 + 1, Offset = Math.Max(0, (int)(me.GetNumber("rank") ?? 1) - span - 1) }, ct); }, ct);
    }
    internal sealed class MailImpl : IKitMail
    {
        private readonly KitRuntime _r; private readonly HashSet<string> _seen = new HashSet<string>(); private double _next; private bool _busy;
        internal MailImpl(KitRuntime r) { _r = r; }
        public event Action<KvListEntry>? Arrived;
        private async Task<IReadOnlyList<KvListEntry>> List(CancellationToken ct)
        { var generation = _r.Generation; var ns = _r.Namespace("mail", true); var rows = new List<KvListEntry>(); string? cursor = null; var cursors = new HashSet<string>(); var pages = 0; do { var page = await ns.ListAsync(new KvListOptions { Values = true, Cursor = cursor }, ct); _r.Check(generation); rows.AddRange(page.Entries); cursor = page.NextCursor; if (++pages > 2048 || rows.Count > 100000 || cursor != null && !cursors.Add(cursor)) { throw new KitException("mail", "unavailable"); } if (cursor != null) { await _r.Delay(0, ct); } } while (cursor != null); return rows.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray(); }
        public Task<IReadOnlyList<KvListEntry>> ListAsync(CancellationToken ct = default) => _r.Run("mail", () => List(ct), ct);
        public Task<KvEntry?> ReadAsync(string id, CancellationToken ct = default) => _r.Run("mail", () => _r.Namespace("mail", true).GetEntryAsync(id, ct), ct);
        public Task RemoveAsync(string id, CancellationToken ct = default) => _r.Run("mail", () => _r.Namespace("mail", true).DeleteAsync(id, null, ct), ct);
        public Task<KvEntry> ClaimAsync(string id, CancellationToken ct = default) => _r.Run("mail", async ()

 =>
        { var generation = _r.Generation; var ns = _r.Namespace("mail", true); var letter = await ns.GetEntryAsync(id, ct); _r.Check(generation); if (letter == null) { throw new KitException("mail", "already_claimed"); } try { if (!await ns.TryDeleteAsync(id, new KvDeleteOptions { IfMatch = letter.Version }, ct)) { throw new KitException("mail", "already_claimed"); } } catch (KvStoreException e) when (e.Status == 409) { throw new KitException("mail", "already_claimed"); } _r.Check(generation); return letter; }, ct);
        public Task SendAsync(KitMailSendOptions o, CancellationToken ct = default) => _r.Run("mail", async ()

 =>
        { if (o == null) { throw new ArgumentNullException(nameof(o)); } if (_r.Identity.Token == null) { throw new KitException("mail", "signed_out"); } var col = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = _r.Field("state", "url"), Token = _r.Identity.Token, Transport = _r.Transport }).Collection(_r.Alias("collections", "mail")); var info = await col.InfoAsync(ct); if (!info.IsPlayerMail) { throw new KitException("mail", "server_only"); } await col.Owner(o.To).PutAsync(_r.Identity.UserId + ":" + Auth.AuthClient.NewNonce().Substring(0, 24), o.Value, new KvPutOptions { Ttl = o.Ttl }, ct); }, ct);
        internal void Refresh() => _next = 0;
        internal void Reset() { _seen.Clear(); _busy = false; }
        internal void Tick()


        { if (Arrived == null || _busy || _r.Clock.NowMillis < _next) { return; } _next = _r.Clock.NowMillis + _r.SocialInterval; _busy = true; _ = _r.Run("mail", async () => { try { foreach (var row in await List(CancellationToken.None)) { if (_seen.Add(row.Key)) { Arrived?.Invoke(row); } } } finally { _busy = false; } }); }
    }
    internal sealed class FriendsImpl : IKitFriends
    {
        private readonly KitRuntime _r; private readonly HashSet<string> _requests = new HashSet<string>(); private double _next; private bool _busy;
        internal FriendsImpl(KitRuntime r) { _r = r; }
        public event Action<JsonValue>? Requested;
        public Task<JsonValue> ListAsync(CancellationToken ct = default) => _r.Run("friends", async () =>
        {
            var generation = _r.Generation; var relations = await _r.Platform().Social.FriendsAsync(ct); _r.Check(generation); var rows = relations.GetArrayOrEmpty("friends"); var presence = new Dictionary<string, bool>();
            var ws = new Uri(_r.Field("gateway", "url")); var origin = new UriBuilder(ws) { Scheme = ws.Scheme == "wss" ? "https" : "http", Port = ws.IsDefaultPort ? -1 : ws.Port }.Uri.AbsoluteUri;
            for (var i = 0; i < rows.Count; i += 50)
            {
                var ids = rows.Skip(i).Take(50).Select(x => x.GetString("owner") ?? throw new KitException("friends", "unavailable")).ToArray(); var p = await _r.Platform(origin).PresenceAsync(new PlatformPresenceOptions { ChannelId = _r.Field("gateway", "lobbyChannelId"), UserIds = ids }, ct); _r.Check(generation); foreach (var user in p.GetArrayOrEmpty("users"))
                {
                    presence[user.GetString("userId") ?? ""] = user.GetBool("online") ?? false;
                }
            }


            return Json.Object().Set("friends", JsonValue.Array(rows.Select(row => { var b = Json.Object(); foreach (var m in row.AsObject()) { b.Set(m.Key, m.Value); } b.Set("online", presence.TryGetValue(row.GetString("owner") ?? "", out var online) && online); return b.Build(); }).ToArray())).Build();
        }, ct);
        public Task<JsonValue> RequestsAsync(CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.RequestsAsync(ct), ct);
        public Task RequestAsync(string id, CancellationToken ct = default) => _r.Run("friends", async () => { await _r.Platform().Social.RequestAsync(id, ct); }, ct);
        public Task AcceptAsync(string id, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.AcceptAsync(id, ct), ct);
        public Task DeclineAsync(string id, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.DeclineAsync(id, ct), ct);
        public Task RemoveAsync(string id, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.RemoveAsync(id, ct), ct);
        public Task BlockAsync(string id, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.BlockAsync(id, ct), ct);
        public Task UnblockAsync(string id, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.UnblockAsync(id, ct), ct);
        public Task<JsonValue?> ProfileAsync(CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.ProfileAsync(ct), ct);
        public Task<JsonValue> SetProfileAsync(PlatformProfileOptions o, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.SetProfileAsync(o, ct), ct);
        public Task<JsonValue> ProfilesAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => _r.Run("friends", () => _r.Platform().Social.ProfilesAsync(ids, ct), ct);
        public Task InviteAsync(string id, CancellationToken ct = default) => _r.Run("friends", async () => { var friends = await ListAsync(ct); if (!friends.GetArrayOrEmpty("friends").Any(x => x.GetString("owner") == id && x.GetBool("online") == true)) { throw new KitException("friends", "offline"); } await _r.Room.InviteAsync(id, ct); }, ct);
        internal void Refresh() => _next = 0; internal void Reset() { _requests.Clear(); _busy = false; }


        internal void Tick() { if (Requested == null || _busy || _r.Clock.NowMillis < _next) { return; } _next = _r.Clock.NowMillis + _r.SocialInterval; _busy = true; _ = _r.Run("friends", async () => { try { var generation = _r.Generation; var requests = await _r.Platform().Social.RequestsAsync(); _r.Check(generation); foreach (var request in requests.GetArrayOrEmpty("incoming")) { if (_requests.Add(request.GetString("owner") ?? "")) { Requested?.Invoke(request); } } } finally { _busy = false; } }); }
    }
}
