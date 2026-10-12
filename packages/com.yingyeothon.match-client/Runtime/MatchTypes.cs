using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.Platform;

namespace Yingyeothon.Match
{
    /// <summary>Members and callback result shared by live and deferred confirmation.</summary>
    public sealed class MatchResult
    {
        /// <summary>Parses required fields and retains the original JSON including future fields.</summary>
        public MatchResult(JsonValue value)
        {
            Raw = value ?? throw new ArgumentNullException(nameof(value));
            MatchId = value.GetString("matchId") ?? throw new PlatformException(200, "bad_body");
            Partial = value.GetBool("partial") ?? throw new PlatformException(200, "bad_body");
            if (MatchId.Length == 0 || !value.TryGetMember("result", out var result) || !value.TryGetMember("members", out var members) || members.Kind != JsonKind.Array)
            {
                throw new PlatformException(200, "bad_body");
            }

            Result = result;
            var ids = new List<string>();
            foreach (var member in members.AsArray())
            {
                var id = member.GetString("userId");
                if (string.IsNullOrEmpty(id))
                {
                    throw new PlatformException(200, "bad_body");
                }

                ids.Add(id!);
            }
            Members = ids.AsReadOnly();
        }
        /// <summary>Match identifier.</summary>
        public string MatchId { get; }
        /// <summary>Whether timeout formed a short group.</summary>
        public bool Partial { get; }
        /// <summary>Callback JSON, including JSON null.</summary>
        public JsonValue Result { get; }
        /// <summary>Authenticated member user ids in ticket order.</summary>
        public IReadOnlyList<string> Members { get; }
        /// <summary>Original matched or confirmed object.</summary>
        public JsonValue Raw { get; }
    }
    /// <summary>Deferred lifecycle states.</summary>
    public enum MatchTicketState { Waiting, Proposed, Confirmed, Expired, Declined, Failed }
    /// <summary>Validated deferred ticket, preserving future fields.</summary>
    public sealed class MatchTicket
    {
        /// <summary>Reads a ticket without inventing a terminal result.</summary>
        public MatchTicket(JsonValue value)
        {
            Raw = value ?? throw new ArgumentNullException(nameof(value));
            var state = value.GetString("state");
            switch (state)
            {
                case "waiting": State = MatchTicketState.Waiting; Integer(value, "position"); Integer(value, "waited"); break;
                case "proposed": State = MatchTicketState.Proposed; Text(value, "matchId"); Integer(value, "acceptBy"); if (value.GetBool("accepted") == null || value.GetBool("partial") == null) { throw new PlatformException(200, "bad_body"); } break;
                case "confirmed": State = MatchTicketState.Confirmed; Result = new MatchResult(value); break;
                case "expired": State = MatchTicketState.Expired; if (value.GetString("reason") != "wait" && value.GetString("reason") != "accept") { throw new PlatformException(200, "bad_body"); } break;
                case "declined": State = MatchTicketState.Declined; Text(value, "matchId"); break;
                case "failed": State = MatchTicketState.Failed; if (value.GetString("reason") != "callback" && value.GetString("reason") != "closed") { throw new PlatformException(200, "bad_body"); } break;
                default: throw new PlatformException(200, "bad_body");
            }
        }
        private static void Text(JsonValue v, string key)
        {
            if (v.GetString(key) == null)
            {
                throw new PlatformException(200, "bad_body");
            }
        }
        private static void Integer(JsonValue v, string key)
        {
            var n = v.GetNumber(key); if (n == null || n < 0 || n > 9007199254740991d || n != Math.Floor(n.Value))
            {
                throw new PlatformException(200, "bad_body");
            }
        }
        /// <summary>Current ticket state.</summary>
        public MatchTicketState State { get; }
        /// <summary>Confirmed match only.</summary>
        public MatchResult? Result { get; }
        /// <summary>Full ticket JSON.</summary>
        public JsonValue Raw { get; }
    }
    /// <summary>Deferred HTTP options.</summary>
    public sealed class DeferredMatchClientOptions
    {
        /// <summary>Match HTTP API base and player token.</summary>
        public PlatformClientOptions Http { get; set; } = new PlatformClientOptions();
        /// <summary>Match channel id.</summary>
        public string ChannelId { get; set; } = "";
    }
    /// <summary>Ticket wire API. Polling and retries belong to the kit.</summary>
    public interface IDeferredMatchClient : IPollable
    {
        /// <summary>Submits a ticket.</summary>
        Task<MatchTicket> SubmitAsync(CancellationToken cancellationToken = default);
        /// <summary>Reads the caller's ticket, null on 404.</summary>
        Task<MatchTicket?> StatusAsync(CancellationToken cancellationToken = default);
        /// <summary>Deletes a ticket, declining an open proposal.</summary>
        Task CancelAsync(CancellationToken cancellationToken = default);
        /// <summary>Accepts a proposal; confirmation follows asynchronously.</summary>
        Task<MatchTicket> AcceptAsync(CancellationToken cancellationToken = default);
        /// <summary>Declines a proposal.</summary>
        Task<MatchTicket> DeclineAsync(CancellationToken cancellationToken = default);
    }
    /// <summary>Live socket configuration.</summary>
    public sealed class LiveMatchClientOptions
    {
        /// <summary>Match WebSocket origin without query, fragment or userinfo.</summary>
        public string Url { get; set; } = "";
        /// <summary>Match channel id.</summary>
        public string ChannelId { get; set; } = "";
        /// <summary>Player JWT, passed only in bearer subprotocols.</summary>
        public string Token { get; set; } = "";
        /// <summary>Socket factory; required for WebGL.</summary>
        public IWebSocketFactory? WebSocketFactory { get; set; }
        /// <summary>Monotonic clock.</summary>
        public IClock? Clock { get; set; }
        /// <summary>Maximum find duration in milliseconds, default 120000.</summary>
        public double TimeoutMillis { get; set; } = 120000;
    }
    /// <summary>Live matchmaking socket, pumped on the owner thread.</summary>
    public interface ILiveMatchClient : IPollable
    {
        /// <summary>Starts or observes the current find.</summary>
        Task<MatchResult> FindAsync(CancellationToken cancellationToken = default);
        /// <summary>Queues cancellation; the task settles at the next Poll.</summary>
        void Cancel();
    }
}
