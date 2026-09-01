using System;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Where a client's connection currently is.</summary>
    public enum GatewayClientState
    {
        /// <summary>Created, but <c>ConnectAsync</c> has not been called.</summary>
        Idle,
        /// <summary>A socket is opening, or the lobby is waiting for <c>hello</c>.</summary>
        Connecting,
        /// <summary>Usable. On a lobby channel that means <c>hello</c> has arrived.</summary>
        Connected,
        /// <summary>The connection dropped and a retry is scheduled.</summary>
        Reconnecting,
        /// <summary>Terminal. A client that reached this cannot be reissued.</summary>
        Closed,
    }

    /// <summary>The connection dropped.</summary>
    public readonly struct DisconnectedEvent
    {
        public DisconnectedEvent(int code, string reason, bool willReconnect)
        {
            Code = code;
            Reason = reason;
            WillReconnect = willReconnect;
        }

        /// <summary>The WebSocket close code. See <see cref="GatewayCloseCode"/>.</summary>
        public int Code { get; }

        /// <summary>The close reason as the SDK classified it. Never the peer's own text.</summary>
        public string Reason { get; }

        /// <summary>Whether a reconnect is already scheduled.</summary>
        public bool WillReconnect { get; }
    }

    /// <summary>A reconnect is scheduled.</summary>
    public readonly struct ReconnectingEvent
    {
        public ReconnectingEvent(int attempt, double delayMillis)
        {
            Attempt = attempt;
            DelayMillis = delayMillis;
        }

        /// <summary>Which consecutive retry this is, counting from one.</summary>
        public int Attempt { get; }

        /// <summary>How long the client will wait before opening the next socket.</summary>
        public double DelayMillis { get; }
    }

    /// <summary>The connection ended for good; no reconnect will follow.</summary>
    public readonly struct StoppedEvent
    {
        public StoppedEvent(CloseDispositionKind kind, string reason, int code)
        {
            Kind = kind;
            Reason = reason;
            Code = code;
        }

        /// <summary>Why the connection will not be retried.</summary>
        public CloseDispositionKind Kind { get; }

        /// <summary>Why it stopped, as the SDK classified it.</summary>
        public string Reason { get; }

        /// <summary>The close code that ended it.</summary>
        public int Code { get; }
    }

    /// <summary>A frame the SDK could not read.</summary>
    public readonly struct ProtocolErrorEvent
    {
        public ProtocolErrorEvent(string message)
        {
            Message = message;
        }

        /// <summary>A refusal code and an offset, or a capped frame type. Never quotes the frame.</summary>
        public string Message { get; }
    }

    /// <summary>A dungeon run ended, either aborted or finished.</summary>
    public readonly struct GameEndedEvent
    {
        public GameEndedEvent(int code, string reason)
        {
            Code = code;
            Reason = reason;
        }

        /// <summary>The close code: 1000 for a normal finish, 4001 for an abort.</summary>
        public int Code { get; }

        /// <summary>How the run ended, as the SDK classified it.</summary>
        public string Reason { get; }
    }

    /// <summary>Something that must be pumped from the caller's own thread.</summary>
    /// <remarks>
    /// In Unity this is <c>MonoBehaviour.Update()</c>. Nothing observable happens
    /// without it: frames stay queued, timeouts do not fire, and a scheduled
    /// reconnect does not open. Call it unconditionally, before any pause check.
    /// </remarks>
    public interface IGatewayPollable
    {
        /// <summary>Drains what arrived, advances the timers, and raises the handlers — on the calling thread.</summary>
        void Poll();
    }

    /// <summary>The connection ended before it became usable.</summary>
    public sealed class GatewayStoppedException : Exception
    {
        public GatewayStoppedException(string message)
            : base(message)
        {
        }
    }
}
