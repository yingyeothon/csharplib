using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Options shared by both gateway clients.</summary>
    public abstract class GatewayClientOptions
    {
        /// <summary>Gateway origin, e.g. <c>wss://gw.yyt.life</c>. The query string is added by the SDK.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>The channel to join, as the console shows it: <c>lobby_…</c> or <c>q_…</c>.</summary>
        public string ChannelId { get; set; } = string.Empty;

        /// <summary>
        /// The channel JWT. It rides in the subprotocol list, never in the URL, and
        /// is never logged.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Defaults to <see cref="WebSocketTransport.Default"/>; required on Unity WebGL.</summary>
        public IWebSocketFactory? WebSocketFactory { get; set; }

        /// <summary>Reconnect schedule; defaults to 500 ms, doubling, capped at 15 s, with 20% jitter.</summary>
        public BackoffOptions? Backoff { get; set; }

        /// <summary>Consecutive closes before open that end the session.</summary>
        public int MaxHandshakeFailures { get; set; } = 5;

        /// <summary>Defaults to a logger that discards everything. This SDK never logs the token.</summary>
        public ILogger? Logger { get; set; }

        /// <summary>Defaults to a monotonic system clock; a test injects its own.</summary>
        public IClock? Clock { get; set; }
    }

    /// <summary>Options for <see cref="GatewayLobbyClient.Create"/>.</summary>
    public sealed class GatewayLobbyClientOptions : GatewayClientOptions
    {
        /// <summary>Used by <c>MapAsync</c>; defaults to <see cref="HttpFetcher.Default"/>.</summary>
        public IHttpFetcher? HttpFetcher { get; set; }

        /// <summary>How long to wait for <c>hello</c> after the socket opens.</summary>
        public double HelloTimeoutMillis { get; set; } = 10000;
    }

    /// <summary>The party commands a lobby channel offers.</summary>
    public interface IPartyCommands
    {
        /// <summary>Creates a party with the caller as its leader.</summary>
        void Create();

        /// <summary>Invites a user. The leader's command.</summary>
        void Invite(string userId);

        /// <summary>Accepts a pending invitation.</summary>
        void Accept(string partyId);

        /// <summary>Declines a pending invitation.</summary>
        void Decline(string partyId);

        /// <summary>Leaves the current party. Leadership passes to the next member.</summary>
        void Leave();

        /// <summary>Asks for the current roster; it arrives as <c>PartyChanged</c>.</summary>
        void List();
    }

    /// <summary>
    /// A client for the gateway's lobby channel.
    /// </summary>
    /// <remarks>
    /// Nothing is observed until <see cref="IGatewayPollable.Poll"/> runs, so call it
    /// every frame from the thread that created the client. Receive handling and the
    /// timers both live there, which is what keeps every handler on Unity's main
    /// thread without a synchronisation context.
    /// </remarks>
    public interface IGatewayLobbyClient : IGatewayPollable, IDisposable
    {
        /// <summary>Where this client's connection currently is.</summary>
        GatewayClientState State { get; }

        /// <summary>The latest <c>hello</c>, once connected.</summary>
        Hello? Hello { get; }

        /// <summary>What the channel enables, from <c>hello</c>. Null on a field means unrestricted.</summary>
        Capabilities? Capabilities { get; }

        /// <summary>Current party, from <c>hello.partyId</c> or the latest roster.</summary>
        string? PartyId { get; }

        /// <summary>The latest roster frame, if any.</summary>
        PartyFrame? Roster { get; }

        /// <summary>The peers visible in the current zone, with the receiver's own entry filtered out.</summary>
        IPeerMap Peers { get; }

        /// <summary>Completes with <c>hello</c>. Fails if the connection stops before that.</summary>
        Task<Hello> ConnectAsync();

        /// <summary>Closes the connection. No reconnect follows.</summary>
        void Close();

        /// <summary>Fetches <c>hello.mapUrl</c>, cached per URL.</summary>
        Task<JsonValue> MapAsync(CancellationToken cancellationToken = default);

        /// <summary>Announces a position. Until the first call the player has no zone at all.</summary>
        void Pos(string zone, double x, double y, string? dir = null);

        /// <summary>Sends chat. The gateway refuses text that is empty or over 1024 bytes.</summary>
        void Say(SayScope scope, string text, string? to = null);

        /// <summary>Sends a game-defined event the gateway routes by scope but never reads.</summary>
        void Event(SayScope scope, string name, JsonValue? payload, string? to = null);

        /// <summary>The party commands, when the channel enables them.</summary>
        IPartyCommands Party { get; }

        /// <summary>Sends an application-level liveness probe; the answer arrives as <c>Pong</c>.</summary>
        void Ping();

        /// <summary>Escape hatch for a frame the helpers do not cover.</summary>
        void Send(JsonValue frame);

        /// <summary><c>hello</c> arrived; fires again after every successful reconnect.</summary>
        event Action<Hello> Connected;

        /// <summary>The connection dropped. Fires before every reconnect and before every stop.</summary>
        event Action<DisconnectedEvent> Disconnected;

        /// <summary>A retry is scheduled, with its attempt number and delay.</summary>
        event Action<ReconnectingEvent> Reconnecting;

        /// <summary>Terminal: no further attempt will be made.</summary>
        event Action<StoppedEvent> Stopped;

        /// <summary>The zone was replaced wholesale, which is how a zone change starts.</summary>
        event Action<SnapshotFrame> Snapshot;

        /// <summary>A peer became visible in the current zone.</summary>
        event Action<Peer> PeerEnter;

        /// <summary>A peer stopped being visible; the argument is its <c>userId</c>.</summary>
        event Action<string> PeerLeave;

        /// <summary>Known peers moved. The receiver's own entry is already filtered out.</summary>
        event Action<IReadOnlyList<Peer>> PeerMove;

        /// <summary>Chat arrived. Named <c>Said</c> because <c>Say</c> is the sender.</summary>
        event Action<SayBroadcastFrame> Said;

        /// <summary>A game event arrived. Named for the same reason as <see cref="Said"/>.</summary>
        event Action<EventBroadcastFrame> EventReceived;

        /// <summary>A roster snapshot arrived.</summary>
        event Action<PartyFrame> PartyChanged;

        /// <summary>Someone invited this player to a party.</summary>
        event Action<PartyInviteFrame> PartyInvited;

        /// <summary>Someone declined an invitation to this player's party.</summary>
        event Action<PartyDeclinedFrame> PartyDeclined;

        /// <summary>The gateway answered a ping.</summary>
        event Action Pong;

        /// <summary>The gateway refused something this client sent.</summary>
        event Action<ErrorFrame> Refused;

        /// <summary>A frame arrived that this SDK could not read.</summary>
        event Action<ProtocolErrorEvent> ProtocolError;

        /// <summary>Every frame after <c>hello</c>, before any SDK handling. Rosters are already normalised.</summary>
        event Action<LobbyServerFrame> Frame;
    }

    /// <summary>Creates lobby clients.</summary>
    public static class GatewayLobbyClient
    {
        /// <summary>Creates a lobby client. Options are copied, so later edits to them change nothing.</summary>
        public static IGatewayLobbyClient Create(GatewayLobbyClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new GatewayLobbyClientImpl(options);
        }
    }
}
