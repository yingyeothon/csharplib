using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>
    /// A lobby client wired to a fake socket, a fake clock and a capturing log.
    /// </summary>
    /// <remarks>
    /// The whole suite is deterministic because nothing happens off the test thread:
    /// the fake posts synchronously, and every transition and timer runs inside an
    /// explicit <c>Poll()</c>.
    /// </remarks>
    internal sealed class LobbyHarness
    {
        internal const string Token = "eyJ.secret-token.sig";

        internal LobbyHarness(Action<GatewayLobbyClientOptions>? configure = null)
        {
            var options = new GatewayLobbyClientOptions
            {
                Url = "wss://gw.test",
                ChannelId = "ch_lobby",
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
            Client = GatewayLobbyClient.Create(options);
        }

        internal FakeWebSocketFactory Factory { get; } = new FakeWebSocketFactory();

        internal FakeClock Clock { get; } = new FakeClock();

        internal CapturingLogWriter Log { get; } = new CapturingLogWriter();

        internal IGatewayLobbyClient Client { get; }

        internal FakeWebSocket Socket => Factory.Latest;

        internal void Poll() => Client.Poll();

        /// <summary>Advances the clock and pumps, the way a frame would.</summary>
        internal void Advance(double millis)
        {
            Clock.Advance(millis);
            Client.Poll();
        }

        /// <summary>Opens the socket, delivers a hello and completes the connect.</summary>
        internal async Task<Hello> ConnectAsync(JsonValue? hello = null)
        {
            var pending = Client.ConnectAsync();
            Socket.ServerOpen();
            Socket.ServerSend(hello ?? Frames.Hello());
            Poll();
            return await pending.ConfigureAwait(false);
        }

        /// <summary>Pumps until a task settles, so a test never waits on a real clock.</summary>
        internal static async Task<T> Settle<T>(Task<T> task, IGatewayPollable pollable)
        {
            for (var i = 0; i < 100 && !task.IsCompleted; i++)
            {
                pollable.Poll();
                await Task.Yield();
            }

            return await task.ConfigureAwait(false);
        }
    }
}
