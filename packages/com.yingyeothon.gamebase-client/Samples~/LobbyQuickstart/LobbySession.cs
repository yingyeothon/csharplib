using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yingyeothon.Gamebase.Client.Samples
{
    /// <summary>
    /// A lobby connection with every event wired up, engine-free so it compiles and is
    /// testable outside Unity. <c>LobbyQuickstart</c> is the MonoBehaviour over it.
    /// </summary>
    public sealed class LobbySession : IDisposable
    {
        private readonly IGatewayLobbyClient _client;

        public LobbySession(string url, string channelId, string channelJwt)
        {
            _client = GatewayLobbyClient.Create(new GatewayLobbyClientOptions
            {
                Url = url,
                ChannelId = channelId,
                Token = channelJwt,
            });

            // Fires on the first hello AND after every reconnect, so announcing the
            // position here re-establishes the player in the zone both times.
            _client.Connected += hello =>
            {
                Zone = hello.Zone;
                Connected?.Invoke(hello);
            };

            _client.PeerEnter += peer => PeerEntered?.Invoke(peer);
            _client.PeerLeave += userId => PeerLeft?.Invoke(userId);
            _client.PeerMove += peers => PeersMoved?.Invoke(peers);
            _client.Said += frame => ChatArrived?.Invoke(frame);
            _client.Disconnected += e => Dropped?.Invoke(e);
            _client.Stopped += e => Ended?.Invoke(e);
        }

        /// <summary>The zone the last <c>hello</c> named, before any move of our own.</summary>
        public string Zone { get; private set; } = string.Empty;

        public event Action<Hello>? Connected;

        public event Action<Peer>? PeerEntered;

        public event Action<string>? PeerLeft;

        public event Action<IReadOnlyList<Peer>>? PeersMoved;

        public event Action<SayBroadcastFrame>? ChatArrived;

        public event Action<DisconnectedEvent>? Dropped;

        public event Action<StoppedEvent>? Ended;

        /// <summary>Completes on the gateway's <c>hello</c>, not on the socket opening.</summary>
        public Task<Hello> ConnectAsync() => _client.ConnectAsync();

        /// <summary>
        /// Drives everything: received frames, timeouts and reconnect delays. Call it
        /// every frame, unconditionally. A client that is never polled does nothing.
        /// </summary>
        public void Poll() => _client.Poll();

        /// <summary>
        /// Announces a position. Until the first call the player has no zone at all,
        /// so nobody sees them and no snapshot arrives.
        /// </summary>
        public void Move(double x, double y, string? facing = null) => _client.Pos(Zone, x, y, facing);

        /// <summary>Sends zone chat. Validate the length first: the gateway caps it at 1024 bytes.</summary>
        public void SayToZone(string text) => _client.Say(SayScope.Zone, text);

        public void Dispose() => _client.Dispose();
    }
}
