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

        /// <summary>The peer's identity.</summary>
        public string UserId { get; }

        /// <summary>Position on the map's x axis. A <c>double</c>, because the wire is Go <c>float64</c>.</summary>
        public double X { get; }

        /// <summary>Position on the map's y axis.</summary>
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

        /// <summary>
        /// The peer as the latest <c>pos</c> states it. <c>dir</c> REPLACES rather
        /// than merges: the gateway rebuilds the whole peer from each inbound frame
        /// (<c>Peer{UserID, X, Y, Dir: in.Dir}</c>) and marshals it with
        /// <c>dir,omitempty</c>, so an omitted <c>dir</c> is the authoritative "this
        /// peer has no facing", not "unchanged". Carrying the old value forward
        /// leaves a peer facing a direction it has cleared, with no later frame able
        /// to correct it.
        /// </summary>
        internal Peer WithPosition(double x, double y, string? dir)
            => new Peer(UserId, x, y, dir);
    }
}
