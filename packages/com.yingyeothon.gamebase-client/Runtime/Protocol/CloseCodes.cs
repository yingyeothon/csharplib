namespace Yingyeothon.Gamebase.Client
{
    /// <summary>What a client should do about a close.</summary>
    public enum CloseDispositionKind
    {
        Reconnect,
        Stop,
        Aborted,
        Finished,
        ClientBug,
    }

    /// <summary>A close code's meaning for this channel kind.</summary>
    public readonly struct CloseDisposition
    {
        public CloseDisposition(CloseDispositionKind kind, string reason)
        {
            Kind = kind;
            Reason = reason;
        }

        public CloseDispositionKind Kind { get; }

        public string Reason { get; }
    }

    /// <summary>Maps a close code to what the client should do.</summary>
    public static class CloseCodes
    {
        /// <summary>
        /// Every code the gateway documents is listed; anything else is treated as a
        /// transient network failure and retried with backoff.
        /// </summary>
        public static CloseDisposition Classify(int code, GatewayChannelKind kind)
        {
            switch (code)
            {
                case GatewayCloseCode.Replaced:
                    return new CloseDisposition(CloseDispositionKind.Stop, "replaced by a newer connection");
                case GatewayCloseCode.Aborted:
                    return kind == GatewayChannelKind.Q
                        ? new CloseDisposition(CloseDispositionKind.Aborted, "the game actor stopped responding")
                        : new CloseDisposition(CloseDispositionKind.Stop, "aborted");
                case GatewayCloseCode.Idle:
                    return new CloseDisposition(CloseDispositionKind.Reconnect, "idle timeout");
                case GatewayCloseCode.Policy:
                    return new CloseDisposition(CloseDispositionKind.ClientBug, "too many refused messages");
                case GatewayCloseCode.ChannelGone:
                    return new CloseDisposition(CloseDispositionKind.Stop, "channel expired or disabled");
                case 1000:
                    return kind == GatewayChannelKind.Q
                        ? new CloseDisposition(CloseDispositionKind.Finished, "the game dropped the connection")
                        : new CloseDisposition(CloseDispositionKind.Stop, "closed normally");
                case 1001:
                    return new CloseDisposition(CloseDispositionKind.Reconnect, "gateway restarting");
                case 1003:
                    return new CloseDisposition(CloseDispositionKind.ClientBug, "binary frame sent");
                case 1009:
                    return new CloseDisposition(CloseDispositionKind.ClientBug, "frame too large");
                case 1011:
                    return new CloseDisposition(CloseDispositionKind.Reconnect, "gateway failed to enter the game");
                default:
                    return new CloseDisposition(CloseDispositionKind.Reconnect, "connection lost (" + code + ")");
            }
        }
    }
}
