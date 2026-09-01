using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Samples
{
    /// <summary>How a dungeon run ended. The distinction is the point of the client.</summary>
    public enum DungeonOutcome
    {
        /// <summary>Still running.</summary>
        Running,

        /// <summary>Close 1000: the game ended normally and dropped the connection.</summary>
        Finished,

        /// <summary>Close 4001: the actor died. A retry needs a new game id.</summary>
        Aborted,

        /// <summary>Any other terminal close.</summary>
        Stopped,
    }

    /// <summary>
    /// A dungeon connection. Everything on a <c>q</c> socket is the game's own schema,
    /// so this is a passthrough with one job: telling a finished run from an aborted one.
    /// </summary>
    public sealed class DungeonRun : IDisposable
    {
        private readonly IGatewayGameClient _client;

        public DungeonRun(string url, string channelId, string gameId, string channelJwt)
        {
            _client = GatewayGameClient.Create(new GatewayGameClientOptions
            {
                Url = url,
                ChannelId = channelId,
                GameId = gameId,
                Token = channelJwt,
            });

            _client.Frame += frame => FrameArrived?.Invoke(frame);
            _client.Refused += error => Refused?.Invoke(error.Code);
            _client.Finished += _ => End(DungeonOutcome.Finished);
            _client.Aborted += _ => End(DungeonOutcome.Aborted);
            _client.Stopped += _ => End(DungeonOutcome.Stopped);
        }

        /// <summary>Where the run stands. Anything but <see cref="DungeonOutcome.Running"/> is terminal.</summary>
        public DungeonOutcome Outcome { get; private set; } = DungeonOutcome.Running;

        /// <summary>Every frame the actor sent, verbatim. It has no required shape.</summary>
        public event Action<JsonValue>? FrameArrived;

        /// <summary>The gateway refused something this client sent; the argument is the code.</summary>
        public event Action<string>? Refused;

        /// <summary>The run ended. After <see cref="DungeonOutcome.Aborted"/>, retry with a NEW game id.</summary>
        public event Action<DungeonOutcome>? Ended;

        /// <summary>Completes when the socket opens. A <c>q</c> channel has no <c>hello</c> handshake.</summary>
        public Task ConnectAsync() => _client.ConnectAsync();

        public void Poll() => _client.Poll();

        /// <summary>Sends a game frame. <c>enter</c> and <c>leave</c> are refused locally.</summary>
        public void Send(JsonValue frame) => _client.Send(frame);

        /// <summary>Sends a frame carrying only a type, which is the common case.</summary>
        public void Send(string type) => _client.Send(Json.Object().Set("type", type).Build());

        public void Dispose() => _client.Dispose();

        private void End(DungeonOutcome outcome)
        {
            if (Outcome != DungeonOutcome.Running)
            {
                return;
            }

            Outcome = outcome;
            Ended?.Invoke(outcome);
        }
    }
}
