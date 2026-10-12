using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Platform
{
    /// <summary>Time and authenticated platform routes. Every pending task requires Poll.</summary>
    public interface IPlatformClient : IPollable
    {
        /// <summary>Platform UTC time, epoch milliseconds, without a bearer.</summary>
        Task<long> TimeAsync(CancellationToken cancellationToken = default);
        /// <summary>Addresses a board without making a request.</summary>
        IPlatformBoard Board(string nameOrId);
        /// <summary>Player profiles and relations.</summary>
        IPlatformSocial Social { get; }
        /// <summary>Gateway presence batch, at most fifty player ids.</summary>
        Task<JsonValue> PresenceAsync(PlatformPresenceOptions options, CancellationToken cancellationToken = default);
    }
    /// <summary>Presence route input; the client base must be the gateway HTTP base.</summary>
    public sealed class PlatformPresenceOptions
    {
        /// <summary>Lobby channel id.</summary>
        public string ChannelId { get; set; } = "";
        /// <summary>One to fifty player ids.</summary>
        public IReadOnlyList<string> UserIds { get; set; } = Array.Empty<string>();
    }
    /// <summary>Leaderboard wire operations. JSON results preserve future fields.</summary>
    public interface IPlatformBoard
    {
        /// <summary>Board configuration and configured periods.</summary>
        Task<JsonValue> InfoAsync(CancellationToken cancellationToken = default);
        /// <summary>Submits an integer score; returns submitted and stored periods, not ranks.</summary>
        Task<JsonValue> SubmitAsync(PlatformScoreOptions options, CancellationToken cancellationToken = default);
        /// <summary>Reads a page of ranked entries.</summary>
        Task<JsonValue> TopAsync(PlatformBoardPageOptions options, CancellationToken cancellationToken = default);
        /// <summary>Own score and rank, or null when absent.</summary>
        Task<JsonValue?> MeAsync(string period, CancellationToken cancellationToken = default);
    }
    /// <summary>Score input.</summary>
    public sealed class PlatformScoreOptions
    {
        /// <summary>Safe integer score.</summary>
        public long Score { get; set; }
        /// <summary>Optional metadata, at most 1024 UTF-8 bytes.</summary>
        public string? Meta { get; set; }
    }
    /// <summary>Leaderboard page input.</summary>
    public sealed class PlatformBoardPageOptions
    {
        /// <summary>alltime, daily or weekly.</summary>
        public string Period { get; set; } = "alltime";
        /// <summary>One to one hundred entries.</summary>
        public int Limit { get; set; } = 50;
        /// <summary>Zero to one thousand entries skipped.</summary>
        public int Offset { get; set; }
    }
    /// <summary>Profile edit.</summary>
    public sealed class PlatformProfileOptions
    {
        /// <summary>Nonempty display name.</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>Optional avatar; null clears it.</summary>
        public string? Avatar { get; set; }
    }
    /// <summary>Authenticated social routes; returned JSON uses the service field names.</summary>
    public interface IPlatformSocial
    {
        /// <summary>Own profile, or null when absent.</summary>
        Task<JsonValue?> ProfileAsync(CancellationToken cancellationToken = default);
        /// <summary>Sets the current player's profile.</summary>
        Task<JsonValue> SetProfileAsync(PlatformProfileOptions options, CancellationToken cancellationToken = default);
        /// <summary>Profiles of one to fifty players.</summary>
        Task<JsonValue> ProfilesAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default);
        /// <summary>Friend relations.</summary>
        Task<JsonValue> FriendsAsync(CancellationToken cancellationToken = default);
        /// <summary>Incoming and outgoing requests.</summary>
        Task<JsonValue> RequestsAsync(CancellationToken cancellationToken = default);
        /// <summary>Requests a relation.</summary>
        Task<JsonValue> RequestAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Accepts an incoming request.</summary>
        Task AcceptAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Declines an incoming request.</summary>
        Task DeclineAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Removes a relation.</summary>
        Task RemoveAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Blocks a player.</summary>
        Task BlockAsync(string userId, CancellationToken cancellationToken = default);
        /// <summary>Unblocks a player.</summary>
        Task UnblockAsync(string userId, CancellationToken cancellationToken = default);
    }
    /// <summary>Platform client factory.</summary>
    public static class PlatformClient
    {
        /// <summary>Creates time, board and social clients over a shared pumped requester.</summary>
        public static IPlatformClient Create(PlatformClientOptions options) => new PlatformClientImpl(PlatformHttpClient.Create(options));
    }
    internal sealed class PlatformClientImpl : IPlatformClient
    {
        private readonly IPlatformHttpClient _http;
        internal PlatformClientImpl(IPlatformHttpClient http) { _http = http; Social = new SocialImpl(this); }
        public IPlatformSocial Social { get; }
        public void Poll() => _http.Poll();
        public void Dispose() => _http.Dispose();
        internal Task<JsonValue> Request(string path, string method, JsonValue? body, CancellationToken ct, bool auth = true)
            => _http.RequestAsync(new PlatformRequestOptions { Path = path, Method = method, Body = body, Authenticated = auth },
                value => value != null && value.Kind == JsonKind.Object ? value : throw new PlatformException(200, "bad_body"), ct);
        internal Task<JsonValue?> Empty(string path, string method, CancellationToken ct) => _http.RequestAsync(new PlatformRequestOptions { Path = path, Method = method }, ct);
        internal Task<JsonValue?> Missing(string path, CancellationToken ct) => _http.RequestAsync(new PlatformRequestOptions { Path = path, MissingAsNull = true }, ct);
        public Task<long> TimeAsync(CancellationToken cancellationToken = default)
            => _http.RequestAsync(new PlatformRequestOptions { Path = "/time", Authenticated = false }, value =>
            {
                var milliseconds = value?.GetNumber("epochMs");
                if (!milliseconds.HasValue || milliseconds <= 0 || milliseconds > 253402300799999L || milliseconds != Math.Floor(milliseconds.Value))
                {
                    throw new PlatformException(200, "bad_body");
                }

                return (long)milliseconds.Value;
            }, cancellationToken);
        public IPlatformBoard Board(string nameOrId) => new BoardImpl(this, "/lb/" + PlatformHttpClient.Segment(nameOrId));
        public Task<JsonValue> PresenceAsync(PlatformPresenceOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return Request("/presence?channel=" + PlatformHttpClient.Query(options.ChannelId) + "&users=" + Batch(options.UserIds), "GET", null, cancellationToken);
        }
        private static string Player(string id)
        {
            if (id == null || id.Length != 32 || id.Any(c => !(c >= 'a' && c <= 'f') && !(c >= '0' && c <= '9')))
            {
                throw new ArgumentException("Expected a player id", nameof(id));
            }

            return id;
        }
        private static string Batch(IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count < 1 || ids.Count > 50)
            {
                throw new ArgumentException("Expected 1 to 50 players", nameof(ids));
            }

            return PlatformHttpClient.Query(string.Join(",", ids.Select(Player).Distinct()));
        }
        private sealed class SocialImpl : IPlatformSocial
        {
            private readonly PlatformClientImpl _c;
            internal SocialImpl(PlatformClientImpl c) { _c = c; }
            public Task<JsonValue?> ProfileAsync(CancellationToken ct = default) => _c.Missing("/social/me/profile", ct);
            public Task<JsonValue> SetProfileAsync(PlatformProfileOptions options, CancellationToken ct = default)
            {
                if (options == null || string.IsNullOrEmpty(options.DisplayName))
                {
                    throw new ArgumentException("Expected a profile", nameof(options));
                }

                return _c.Request("/social/me/profile", "PUT", Json.Object().Set("displayName", options.DisplayName).Set("avatar", options.Avatar == null ? JsonValue.Null : JsonValue.Of(options.Avatar)).Build(), ct);
            }
            public Task<JsonValue> ProfilesAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => _c.Request("/social/profiles?ids=" + Batch(ids), "GET", null, ct);
            public Task<JsonValue> FriendsAsync(CancellationToken ct = default) => _c.Request("/social/friends", "GET", null, ct);
            public Task<JsonValue> RequestsAsync(CancellationToken ct = default) => _c.Request("/social/requests", "GET", null, ct);
            public Task<JsonValue> RequestAsync(string id, CancellationToken ct = default) => _c.Request("/social/requests", "POST", Json.Object().Set("to", Player(id)).Build(), ct);
            public Task AcceptAsync(string id, CancellationToken ct = default) => _c.Empty("/social/requests/" + Player(id) + "/accept", "POST", ct);
            public Task DeclineAsync(string id, CancellationToken ct = default) => _c.Empty("/social/requests/" + Player(id) + "/decline", "POST", ct);
            public Task RemoveAsync(string id, CancellationToken ct = default) => _c.Empty("/social/friends/" + Player(id), "DELETE", ct);
            public Task BlockAsync(string id, CancellationToken ct = default) => _c.Empty("/social/blocks/" + Player(id), "PUT", ct);
            public Task UnblockAsync(string id, CancellationToken ct = default) => _c.Empty("/social/blocks/" + Player(id), "DELETE", ct);
        }
        private sealed class BoardImpl : IPlatformBoard
        {
            private readonly PlatformClientImpl _c; private readonly string _path;
            internal BoardImpl(PlatformClientImpl c, string path) { _c = c; _path = path; }
            private static string Period(string period)
            { if (period != "alltime" && period != "daily" && period != "weekly") { throw new ArgumentException("Invalid period", nameof(period)); } return period; }
            public Task<JsonValue> InfoAsync(CancellationToken ct = default) => _c.Request(_path, "GET", null, ct);
            public Task<JsonValue> SubmitAsync(PlatformScoreOptions options, CancellationToken ct = default)
            {
                if (options == null || Math.Abs((double)options.Score) > 9007199254740991d || (options.Meta != null && Encoding.UTF8.GetByteCount(options.Meta) > 1024))
                {
                    throw new ArgumentException("Invalid score or metadata", nameof(options));
                }

                return _c.Request(_path + "/scores/me", "PUT", Json.Object().Set("score", (double)options.Score).Set("meta", options.Meta).Build(), ct);
            }
            public Task<JsonValue> TopAsync(PlatformBoardPageOptions options, CancellationToken ct = default)
            {
                if (options == null || options.Limit < 1 || options.Limit > 100 || options.Offset < 0 || options.Offset > 1000)
                {
                    throw new ArgumentException("Invalid page", nameof(options));
                }

                return _c.Request(_path + "/top?period=" + Period(options.Period) + "&limit=" + options.Limit.ToString(CultureInfo.InvariantCulture) + "&offset=" + options.Offset.ToString(CultureInfo.InvariantCulture), "GET", null, ct);
            }
            public Task<JsonValue?> MeAsync(string period, CancellationToken ct = default) => _c.Missing(_path + "/scores/me?period=" + Period(period), ct);
        }
    }
}
