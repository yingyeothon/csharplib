namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Application close codes the gateway uses.</summary>
    public static class GatewayCloseCode
    {
        /// <summary>A newer socket of the same user replaced this one. Do not reconnect.</summary>
        public const int Replaced = 4000;

        /// <summary>q only: the actor stopped consuming; the run is aborted, not finished.</summary>
        public const int Aborted = 4001;

        /// <summary>No pong within the idle window.</summary>
        public const int Idle = 4002;

        /// <summary>Too many refused messages on one socket; a client bug.</summary>
        public const int Policy = 4003;

        /// <summary>The channel expired or was disabled.</summary>
        public const int ChannelGone = 4004;

        /// <summary>
        /// The code this SDK closes with when it ends a socket itself. A client may
        /// only send 1000 or 3000-4999, and this one is not used by the gateway.
        /// </summary>
        public const int Local = 4900;
    }
}
