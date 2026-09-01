using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;
using HelloFrame = Yingyeothon.Gamebase.Client.Hello;

namespace Yingyeothon.Gamebase.Client
{
    internal sealed class GatewayLobbyClientImpl : IGatewayLobbyClient, IPartyCommands
    {
        private readonly GatewayLobbyClientOptions _options;
        private readonly GatewaySocket _socket;
        private readonly ILogger _logger;

        private MapFetcher? _mapFetcher;
        private IPeerMap _peers;

        internal GatewayLobbyClientImpl(GatewayLobbyClientOptions options)
        {
            _options = options;
            _logger = options.Logger ?? NullLogger.Instance;
            _peers = PeerMap.Create(new PeerMapOptions { SelfUserId = string.Empty });

            _socket = new GatewaySocket(new GatewaySocketOptions
            {
                Url = options.Url,
                ChannelId = options.ChannelId,
                Token = options.Token,
                Kind = GatewayChannelKind.Lobby,
                WebSocketFactory = options.WebSocketFactory,
                Backoff = options.Backoff,
                HelloTimeoutMillis = options.HelloTimeoutMillis,
                MaxHandshakeFailures = options.MaxHandshakeFailures,
                Logger = _logger,
                Clock = options.Clock ?? SystemClock.Instance,
            });

            _socket.Frame += OnFrame;
            _socket.Disconnected += OnDisconnected;
            _socket.Reconnecting += e => Reconnecting?.Invoke(e);
            _socket.Stopped += e => Stopped?.Invoke(e);
            _socket.ProtocolError += e => ProtocolError?.Invoke(e);
        }

        public GatewayClientState State => _socket.State;

        public Hello? Hello { get; private set; }

        public Capabilities? Capabilities => Hello?.Capabilities;

        public string? PartyId { get; private set; }

        public PartyFrame? Roster { get; private set; }

        public IPeerMap Peers => _peers;

        public IPartyCommands Party => this;

        public event Action<Hello>? Connected;

        public event Action<DisconnectedEvent>? Disconnected;

        public event Action<ReconnectingEvent>? Reconnecting;

        public event Action<StoppedEvent>? Stopped;

        public event Action<SnapshotFrame>? Snapshot;

        public event Action<Peer>? PeerEnter;

        public event Action<string>? PeerLeave;

        public event Action<IReadOnlyList<Peer>>? PeerMove;

        public event Action<SayBroadcastFrame>? Said;

        public event Action<EventBroadcastFrame>? EventReceived;

        public event Action<PartyFrame>? PartyChanged;

        public event Action<PartyInviteFrame>? PartyInvited;

        public event Action<PartyDeclinedFrame>? PartyDeclined;

        public event Action? Pong;

        public event Action<ErrorFrame>? Refused;

        public event Action<ProtocolErrorEvent>? ProtocolError;

        public event Action<LobbyServerFrame>? Frame;

        public void Poll() => _socket.Poll();

        /// <remarks>
        /// Deliberately not an <c>async</c> method. An <c>await</c> here adds a state
        /// machine between the socket's settlement and the caller's task, and Unity's
        /// Mono does not run that continuation inline: with
        /// <c>ConfigureAwait(false)</c> the caller resumed on a thread-pool thread,
        /// which is precisely where <c>Send()</c> and every other entry point are
        /// illegal. Settling our own source from a synchronous continuation keeps the
        /// task completed on the pump thread, and leaves the resumption context the
        /// caller's own choice — Unity's synchronization context puts them back on the
        /// main thread, a console host resumes inline. Found by running the suite
        /// inside the editor; no dotnet-hosted test could see it.
        /// </remarks>
        public Task<Hello> ConnectAsync()
        {
            var source = new TaskCompletionSource<Hello>();
            _socket.ConnectAsync().ContinueWith(
                connect =>
                {
                    if (connect.IsFaulted)
                    {
                        source.TrySetException(connect.Exception!.InnerExceptions);
                    }
                    else if (connect.IsCanceled)
                    {
                        source.TrySetCanceled();
                    }
                    else
                    {
                        // The socket only settles a connect after the hello handler has
                        // run, so this is never null here.
                        source.TrySetResult(Hello!);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return source.Task;
        }

        public void Close() => _socket.Close();

        public void Dispose() => _socket.Dispose();

        public Task<JsonValue> MapAsync(CancellationToken cancellationToken = default)
        {
            var hello = Hello;
            if (hello == null)
            {
                var source = new TaskCompletionSource<JsonValue>();
                source.SetException(new InvalidOperationException("MapAsync() needs hello first"));
                return source.Task;
            }

            _mapFetcher ??= new MapFetcher(_options.HttpFetcher ?? HttpFetcher.Default, _logger);
            return _mapFetcher.FetchAsync(hello.MapUrl, cancellationToken);
        }

        // ---- senders --------------------------------------------------------

        public void Pos(string zone, double x, double y, string? dir = null)
        {
            RequireCapability(Capabilities?.Pos, "pos");
            if (dir != null && LobbyFrameWriter.IsDirTooLong(dir))
            {
                throw new ArgumentException(
                    "dir must be at most " + LobbyFrameWriter.MaxDirBytes + " bytes", nameof(dir));
            }

            _socket.Send(LobbyFrameWriter.Pos(zone, x, y, dir));
        }

        public void Say(SayScope scope, string text, string? to = null)
        {
            RequireSayScope(scope);
            _socket.Send(LobbyFrameWriter.Say(scope, to, text));
        }

        public void Event(SayScope scope, string name, JsonValue? payload, string? to = null)
        {
            // Only the `event` capability, never the say list: the gateway's
            // handleEventLocked checks Capabilities.Event and then routes the scope,
            // and it never calls AllowsSay. A channel that restricts chat to `zone`
            // still routes a party event, so guarding on the say list here refuses a
            // frame the gateway would have delivered.
            RequireCapability(Capabilities?.Event, "event");
            _socket.Send(LobbyFrameWriter.Event(scope, to, name, payload));
        }

        public void Ping() => _socket.Send(LobbyFrameWriter.TypeOnly(FrameTypes.Ping));

        public void Send(JsonValue frame) => _socket.Send(frame);

        void IPartyCommands.Create()
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.TypeOnly(FrameTypes.PartyCreate));
        }

        void IPartyCommands.Invite(string userId)
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.PartyInvite(userId));
        }

        void IPartyCommands.Accept(string partyId)
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.PartyAccept(partyId));
        }

        void IPartyCommands.Decline(string partyId)
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.PartyDecline(partyId));
        }

        void IPartyCommands.Leave()
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.TypeOnly(FrameTypes.PartyLeave));
        }

        void IPartyCommands.List()
        {
            RequireCapability(Capabilities?.Party, "party");
            _socket.Send(LobbyFrameWriter.TypeOnly(FrameTypes.PartyList));
        }

        /// <summary>
        /// Only an explicit <c>false</c> disables a capability. An absent field means
        /// the channel does not restrict it, so the send goes out and the gateway
        /// decides.
        /// </summary>
        private static void RequireCapability(bool? enabled, string name)
        {
            if (enabled == false)
            {
                throw new InvalidOperationException("capability_off: " + name + " is disabled on this channel");
            }
        }

        /// <summary>
        /// Gates <c>say</c>, and only <c>say</c> — the say list is the gateway's chat
        /// ACL, not a general scope ACL.
        /// </summary>
        private void RequireSayScope(SayScope scope)
        {
            var capabilities = Capabilities;
            if (capabilities != null && !capabilities.AllowsScope(scope))
            {
                throw new InvalidOperationException(
                    "capability_off: say scope " + SayScopes.ToWire(scope) + " is disabled");
            }
        }

        // ---- inbound --------------------------------------------------------

        private void OnFrame(string type, JsonValue raw)
        {
            if (string.Equals(type, FrameTypes.Hello, StringComparison.Ordinal))
            {
                OnHello(HelloFrame.FromJson(raw));
                return;
            }

            var frame = LobbyFrames.Read(type, raw);
            Frame?.Invoke(frame);

            switch (frame)
            {
                case SnapshotFrame snapshot:
                    ApplyPeerFrame(snapshot);
                    return;
                case EnterFrame enter:
                    ApplyPeerFrame(enter);
                    return;
                case LeaveFrame leave:
                    ApplyPeerFrame(leave);
                    return;
                case PosBroadcastFrame pos:
                    ApplyPeerFrame(pos);
                    return;
                case SayBroadcastFrame say:
                    Said?.Invoke(say);
                    return;
                case EventBroadcastFrame gameEvent:
                    EventReceived?.Invoke(gameEvent);
                    return;
                case PartyFrame party:
                    Roster = party;
                    PartyId = party.PartyId;
                    PartyChanged?.Invoke(party);
                    return;
                case PartyInviteFrame invite:
                    PartyInvited?.Invoke(invite);
                    return;
                case PartyDeclinedFrame declined:
                    PartyDeclined?.Invoke(declined);
                    return;
                case PongFrame _:
                    Pong?.Invoke();
                    return;
                case ErrorFrame error:
                    // The code is the routing fact; the message may quote what the
                    // client sent, so only the code is logged.
                    _logger.Warn(
                        "gateway refused a lobby message",
                        Json.Object().Set("channelId", _options.ChannelId).Set("code", error.Code).Build());
                    Refused?.Invoke(error);
                    return;
                default:
                    ProtocolError?.Invoke(new ProtocolErrorEvent("unknown frame type " + Normalize.Diagnostic(frame.Type)));
                    return;
            }
        }

        private void OnHello(Hello hello)
        {
            Hello = hello;
            PartyId = hello.PartyId;

            // A roster from before the outage may be stale; the gateway re-sends
            // `party` after `hello` whenever it still knows the party.
            Roster = null;
            _peers = PeerMap.Create(new PeerMapOptions { SelfUserId = hello.UserId });

            _logger.Info(
                "lobby connected",
                Json.Object()
                    .Set("channelId", _options.ChannelId)
                    .Set("userId", hello.UserId)
                    .Set("tick", (double)hello.Tick)
                    .Set("zone", hello.Zone)
                    .Build());

            Connected?.Invoke(hello);
        }

        private void OnDisconnected(DisconnectedEvent e)
        {
            _peers.Reset();
            Disconnected?.Invoke(e);
        }

        private void ApplyPeerFrame(LobbyServerFrame frame)
        {
            var change = _peers.Apply(frame);
            if (change == null)
            {
                return;
            }

            switch (change.Kind)
            {
                case PeerChangeKind.Snapshot:
                    Snapshot?.Invoke((SnapshotFrame)frame);
                    return;
                case PeerChangeKind.Enter:
                    PeerEnter?.Invoke(change.Peers[0]);
                    return;
                case PeerChangeKind.Leave:
                    PeerLeave?.Invoke(change.UserId!);
                    return;
                default:
                    PeerMove?.Invoke(change.Peers);
                    return;
            }
        }
    }
}
