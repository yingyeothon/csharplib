using System;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Where a client's connection currently is.</summary>
    public enum GatewayClientState
    {
        Idle,
        Connecting,
        Connected,
        Reconnecting,
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

        public int Code { get; }

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

        public int Attempt { get; }

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

        public CloseDispositionKind Kind { get; }

        public string Reason { get; }

        public int Code { get; }
    }

    /// <summary>A frame the SDK could not read.</summary>
    public readonly struct ProtocolErrorEvent
    {
        public ProtocolErrorEvent(string message)
        {
            Message = message;
        }

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

        public int Code { get; }

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
