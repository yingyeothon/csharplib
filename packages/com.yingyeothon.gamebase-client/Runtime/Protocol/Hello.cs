using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>
    /// The first frame on a lobby channel; nothing is "connected" before it. The
    /// client holds no configuration and learns everything here.
    /// </summary>
    public sealed class Hello
    {
        public Hello(
            string userId,
            string connectionId,
            int tick,
            string mapUrl,
            string zone,
            string? partyId,
            Capabilities capabilities,
            JsonValue raw)
        {
            UserId = userId;
            ConnectionId = connectionId;
            Tick = tick;
            MapUrl = mapUrl;
            Zone = zone;
            PartyId = partyId;
            Capabilities = capabilities;
            Raw = raw;
        }

        /// <summary>This player's identity, the same value as the token's <c>sub</c>.</summary>
        public string UserId { get; }

        /// <summary>This socket. A reconnect gets a new one, and only the gateway may set it.</summary>
        public string ConnectionId { get; }

        /// <summary>Position flush interval in milliseconds (the channel's <c>flushIntervalMs</c>).</summary>
        public int Tick { get; }

        /// <summary>Immutable, public map asset. A new map version is a new URL.</summary>
        public string MapUrl { get; }

        /// <summary>The zone the game should start in; the player has no zone until the first <c>pos</c>.</summary>
        public string Zone { get; }

        /// <summary>Set when the gateway already knows this player's party; null otherwise.</summary>
        public string? PartyId { get; }

        /// <summary>What the channel enables. A null field means unrestricted, not disabled.</summary>
        public Capabilities Capabilities { get; }

        /// <summary>The frame as received, so a field this SDK does not model is still reachable.</summary>
        public JsonValue Raw { get; }

        internal static Hello FromJson(JsonValue frame)
        {
            return new Hello(
                frame.GetString("userId") ?? string.Empty,
                frame.GetString("connectionId") ?? string.Empty,
                (int)(frame.GetNumber("tick") ?? 0),
                frame.GetString("mapUrl") ?? string.Empty,
                frame.GetString("zone") ?? string.Empty,
                Normalize.OptionalId(frame.GetString("partyId")),
                Capabilities.FromJson(frame.GetMemberOrNull("capabilities")),
                frame);
        }
    }
}
