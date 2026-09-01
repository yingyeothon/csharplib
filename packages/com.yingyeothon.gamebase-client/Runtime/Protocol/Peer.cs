using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>One retained position.</summary>
    public sealed class Peer
    {
        public Peer(string userId, double x, double y, string? dir)
        {
            UserId = userId;
            X = x;
            Y = y;
            Dir = dir;
        }

        public string UserId { get; }

        public double X { get; }

        public double Y { get; }

        /// <summary>
        /// The game's own facing token, an opaque string of at most 16 bytes
        /// (<c>"n"</c>, <c>"left"</c>, ...), or null when the game has no facing.
        /// </summary>
        /// <remarks>
        /// It is a <em>string</em> on the wire. A numeric <c>dir</c> makes the gateway
        /// refuse the whole frame as <c>bad_message</c> and the position is dropped,
        /// which is why this SDK offers no numeric overload anywhere.
        /// </remarks>
        public string? Dir { get; }

        internal static Peer FromJson(JsonValue value)
            => new Peer(
                value.GetString("userId") ?? string.Empty,
                value.GetNumber("x") ?? 0,
                value.GetNumber("y") ?? 0,
                value.GetString("dir"));

        internal Peer WithPosition(double x, double y, string? dir)
            => new Peer(UserId, x, y, dir ?? Dir);
    }
}
