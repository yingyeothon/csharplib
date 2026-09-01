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

        public string ChannelId { get; set; } = string.Empty;

        /// <summary>
        /// The channel JWT. It rides in the subprotocol list, never in the URL, and
        /// is never logged.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>Defaults to <see cref="WebSocketTransport.Default"/>; required on Unity WebGL.</summary>
        public IWebSocketFactory? WebSocketFactory { get; set; }

        public BackoffOptions? Backoff { get; set; }

        /// <summary>Consecutive closes before open that end the session.</summary>
        public int MaxHandshakeFailures { get; set; } = 5;

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
        void Create();

        void Invite(string userId);

        void Accept(string partyId);

        void Decline(string partyId);

        void Leave();

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
        GatewayClientState State { get; }

        /// <summary>The latest <c>hello</c>, once connected.</summary>
        Hello? Hello { get; }

        Capabilities? Capabilities { get; }

        /// <summary>Current party, from <c>hello.partyId</c> or the latest roster.</summary>
        string? PartyId { get; }

        /// <summary>The latest roster frame, if any.</summary>
        PartyFrame? Roster { get; }

        IPeerMap Peers { get; }

        /// <summary>Completes with <c>hello</c>. Fails if the connection stops before that.</summary>
        Task<Hello> ConnectAsync();

        void Close();

        /// <summary>Fetches <c>hello.mapUrl</c>, cached per URL.</summary>
        Task<JsonValue> MapAsync(CancellationToken cancellationToken = default);

        void Pos(string zone, double x, double y, string? dir = null);

        void Say(SayScope scope, string text, string? to = null);

        void Event(SayScope scope, string name, JsonValue? payload, string? to = null);

        IPartyCommands Party { get; }

        void Ping();

        /// <summary>Escape hatch for a frame the helpers do not cover.</summary>
        void Send(JsonValue frame);

        /// <summary><c>hello</c> arrived; fires again after every successful reconnect.</summary>
        event Action<Hello> Connected;

        event Action<DisconnectedEvent> Disconnected;

        event Action<ReconnectingEvent> Reconnecting;

        event Action<StoppedEvent> Stopped;

        event Action<SnapshotFrame> Snapshot;

        event Action<Peer> PeerEnter;

        event Action<string> PeerLeave;

        event Action<IReadOnlyList<Peer>> PeerMove;

        /// <summary>Chat arrived. Named <c>Said</c> because <c>Say</c> is the sender.</summary>
        event Action<SayBroadcastFrame> Said;

        /// <summary>A game event arrived. Named for the same reason as <see cref="Said"/>.</summary>
        event Action<EventBroadcastFrame> EventReceived;

        /// <summary>A roster snapshot arrived.</summary>
        event Action<PartyFrame> PartyChanged;

        event Action<PartyInviteFrame> PartyInvited;

        event Action<PartyDeclinedFrame> PartyDeclined;

        event Action Pong;

        /// <summary>The gateway refused something this client sent.</summary>
        event Action<ErrorFrame> Refused;

        event Action<ProtocolErrorEvent> ProtocolError;

        /// <summary>Every frame after <c>hello</c>, before any SDK handling. Rosters are already normalised.</summary>
        event Action<LobbyServerFrame> Frame;
    }

    /// <summary>Creates lobby clients.</summary>
    public static class GatewayLobbyClient
    {
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
