using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client
{
    internal sealed class GatewayGameClientImpl : IGatewayGameClient
    {
        private readonly GatewayGameClientOptions _options;
        private readonly GatewaySocket _socket;
        private readonly ILogger _logger;

        internal GatewayGameClientImpl(GatewayGameClientOptions options)
        {
            _options = options;
            _logger = options.Logger ?? NullLogger.Instance;

            _socket = new GatewaySocket(new GatewaySocketOptions
            {
                Url = options.Url,
                ChannelId = options.ChannelId,
                GameId = options.GameId,
                Token = options.Token,
                Kind = GatewayChannelKind.Q,
                WebSocketFactory = options.WebSocketFactory,
                Backoff = options.Backoff,
                MaxHandshakeFailures = options.MaxHandshakeFailures,
                Logger = _logger,
                Clock = options.Clock ?? SystemClock.Instance,
            });

            _socket.Opened += OnOpened;
            _socket.Frame += OnFrame;
            _socket.Disconnected += e => Disconnected?.Invoke(e);
            _socket.Reconnecting += e => Reconnecting?.Invoke(e);
            _socket.Stopped += OnStopped;
            _socket.ProtocolError += e => ProtocolError?.Invoke(e);
        }

        public GatewayClientState State => _socket.State;

        public event Action? Connected;

        public event Action<JsonValue>? Frame;

        public event Action<ErrorFrame>? Refused;

        public event Action<DisconnectedEvent>? Disconnected;

        public event Action<ReconnectingEvent>? Reconnecting;

        public event Action<GameEndedEvent>? Aborted;

        public event Action<GameEndedEvent>? Finished;

        public event Action<StoppedEvent>? Stopped;

        public event Action<ProtocolErrorEvent>? ProtocolError;

        public void Poll() => _socket.Poll();

        public Task ConnectAsync() => _socket.ConnectAsync();

        public void Close() => _socket.Close();

        public void Dispose() => _socket.Dispose();

        public void Send(JsonValue frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            var type = frame.GetString("type");
            if (type != null && IsReserved(type))
            {
                // The gateway synthesises these itself and uses them to decide which
                // member a connection speaks for, so a client must never send one.
                throw new InvalidOperationException("reserved_type: " + type + " is set by the gateway");
            }

            _socket.Send(frame);
        }

        private static bool IsReserved(string type)
        {
            foreach (var reserved in FrameTypes.ReservedGameFrameTypes)
            {
                if (string.Equals(reserved, type, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void OnOpened(string protocol)
        {
            _logger.Info(
                "game connected",
                Json.Object().Set("channelId", _options.ChannelId).Set("gameId", _options.GameId).Build());
            Connected?.Invoke();
        }

        private void OnFrame(string type, JsonValue raw)
        {
            // Game frames are opaque, so an error frame is recognised structurally
            // rather than by trusting the type alone. The discriminator is a string
            // `code`: the gateway's ErrorFrame marshals `message` with omitempty, so
            // requiring it would hand a refusal to the game as if it were its own
            // data and let the client keep sending into a rising `bad` counter.
            if (string.Equals(type, FrameTypes.Error, StringComparison.Ordinal)
                && raw.GetString("code") != null)
            {
                var error = (ErrorFrame)LobbyFrames.Read(type, raw);
                _logger.Warn(
                    "gateway refused a game message",
                    Json.Object().Set("gameId", _options.GameId).Set("code", error.Code).Build());
                Refused?.Invoke(error);
                return;
            }

            Frame?.Invoke(raw);
        }

        private void OnStopped(StoppedEvent e)
        {
            switch (e.Kind)
            {
                case CloseDispositionKind.Aborted:
                    Aborted?.Invoke(new GameEndedEvent(e.Code, e.Reason));
                    return;
                case CloseDispositionKind.Finished:
                    Finished?.Invoke(new GameEndedEvent(e.Code, e.Reason));
                    return;
                default:
                    Stopped?.Invoke(e);
                    return;
            }
        }
    }
}
