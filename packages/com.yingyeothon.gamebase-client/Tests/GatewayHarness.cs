using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>
    /// What the lobby and dungeon harnesses share: a fake socket factory, a fake
    /// clock, a capturing log, and the option wiring that points a client at them.
    /// </summary>
    /// <remarks>
    /// The whole suite is deterministic because nothing happens off the test thread:
    /// the fake posts synchronously, and every transition and timer runs inside an
    /// explicit <c>Poll()</c>.
    /// </remarks>
    internal abstract class GatewayHarness
    {
        internal const string Token = "eyJ.secret-token.sig";

        internal FakeWebSocketFactory Factory { get; } = new FakeWebSocketFactory();

        internal FakeClock Clock { get; } = new FakeClock();

        internal CapturingLogWriter Log { get; } = new CapturingLogWriter();

        internal FakeWebSocket Socket => Factory.Latest;

        /// <summary>The client under test, seen through the pump it shares with every other client.</summary>
        protected abstract IGatewayPollable Pollable { get; }

        internal void Poll() => Pollable.Poll();

        /// <summary>Advances the clock and pumps, the way a frame would.</summary>
        internal void Advance(double millis)
        {
            Clock.Advance(millis);
            Pollable.Poll();
        }

        /// <summary>Points the options at this harness's fakes, before the test's own overrides run.</summary>
        protected void Wire(GatewayClientOptions options, string channelId)
        {
            options.Url = "wss://gw.test";
            options.ChannelId = channelId;
            options.Token = Token;
            options.WebSocketFactory = Factory;
            options.Clock = Clock;
            options.Logger = FilteredLogger.Create(new FilteredLoggerOptions
            {
                Severity = LogSeverity.Debug,
                Writer = Log,
            });
            options.Backoff = new BackoffOptions { InitialMs = 500, Jitter = 0, Random = () => 0 };
        }
    }
}
