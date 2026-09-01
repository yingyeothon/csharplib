using System;
using System.Threading.Tasks;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>A dungeon client wired to a fake socket, a fake clock and a capturing log.</summary>
    internal sealed class GameHarness
    {
        internal const string Token = "eyJ.secret-token.sig";

        internal GameHarness(Action<GatewayGameClientOptions>? configure = null)
        {
            var options = new GatewayGameClientOptions
            {
                Url = "wss://gw.test",
                ChannelId = "q_dungeon",
                GameId = "g_1",
                Token = Token,
                WebSocketFactory = Factory,
                Clock = Clock,
                Logger = FilteredLogger.Create(new FilteredLoggerOptions
                {
                    Severity = LogSeverity.Debug,
                    Writer = Log,
                }),
                Backoff = new BackoffOptions { InitialMs = 500, Jitter = 0, Random = () => 0 },
            };
            configure?.Invoke(options);
            Client = GatewayGameClient.Create(options);
        }

        internal FakeWebSocketFactory Factory { get; } = new FakeWebSocketFactory();

        internal FakeClock Clock { get; } = new FakeClock();

        internal CapturingLogWriter Log { get; } = new CapturingLogWriter();

        internal IGatewayGameClient Client { get; }

        internal FakeWebSocket Socket => Factory.Latest;

        internal void Poll() => Client.Poll();

        internal void Advance(double millis)
        {
            Clock.Advance(millis);
            Client.Poll();
        }

        /// <summary>A q channel is ready on open; there is no hello handshake.</summary>
        internal async Task ConnectAsync()
        {
            var pending = Client.ConnectAsync();
            Socket.ServerOpen();
            Poll();
            await pending.ConfigureAwait(false);
        }
    }
}
