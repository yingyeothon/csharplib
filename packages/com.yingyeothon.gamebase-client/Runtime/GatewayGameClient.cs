using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Options for <see cref="GatewayGameClient.Create"/>.</summary>
    public sealed class GatewayGameClientOptions : GatewayClientOptions
    {
        /// <summary>The run to join; the caller must be in its start event's members.</summary>
        public string GameId { get; set; } = string.Empty;
    }

    /// <summary>
    /// A client for the gateway's dungeon (<c>q</c>) channel.
    /// </summary>
    /// <remarks>
    /// The gateway defines no outbound vocabulary here — every frame belongs to the
    /// game — so this is a typed passthrough whose only protocol knowledge is the
    /// connect sequence, the reserved inbound types, and the difference between an
    /// aborted run and a finished one. Neither of those reconnects; a retry needs a
    /// fresh <c>GameId</c>.
    /// </remarks>
    public interface IGatewayGameClient : IGatewayPollable, IDisposable
    {
        /// <summary>Where this client's connection currently is.</summary>
        GatewayClientState State { get; }

        /// <summary>Completes once the socket is open with the bearer subprotocol echoed.</summary>
        Task ConnectAsync();

        /// <summary>Closes the connection. No reconnect follows.</summary>
        void Close();

        /// <summary>Sends a game frame. <c>enter</c> and <c>leave</c> are refused locally.</summary>
        void Send(JsonValue frame);

        /// <summary>
        /// The socket is open and the gateway has pushed <c>enter</c> to the actor.
        /// Fires again after a reconnect; the game answers with its own snapshot.
        /// </summary>
        event Action Connected;

        /// <summary>Every game-defined frame, verbatim.</summary>
        event Action<JsonValue> Frame;

        /// <summary>A gateway refusal.</summary>
        event Action<ErrorFrame> Refused;

        /// <summary>The connection dropped. Fires before every reconnect and before every stop.</summary>
        event Action<DisconnectedEvent> Disconnected;

        /// <summary>A retry is scheduled, with its attempt number and delay.</summary>
        event Action<ReconnectingEvent> Reconnecting;

        /// <summary>Close 4001: the actor died. Retry only with a new <c>GameId</c>.</summary>
        event Action<GameEndedEvent> Aborted;

        /// <summary>Close 1000: the game dropped this connection after ending normally.</summary>
        event Action<GameEndedEvent> Finished;

        /// <summary>Any other terminal close.</summary>
        event Action<StoppedEvent> Stopped;

        /// <summary>A frame arrived that this SDK could not read.</summary>
        event Action<ProtocolErrorEvent> ProtocolError;
    }

    /// <summary>Creates dungeon clients.</summary>
    public static class GatewayGameClient
    {
        /// <summary>Creates a dungeon client. Options are copied, so later edits to them change nothing.</summary>
        public static IGatewayGameClient Create(GatewayGameClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new GatewayGameClientImpl(options);
        }
    }
}
