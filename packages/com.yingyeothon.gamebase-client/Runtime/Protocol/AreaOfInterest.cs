using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>
    /// The channel's view rule, as <c>hello</c> carries it once the gateway has applied its
    /// defaults. <c>maxPeers</c> is always present; <c>range</c> only when the channel
    /// defines an area-of-interest box, so a channel without one sends
    /// <c>{ "maxPeers": 64 }</c> alone.
    /// </summary>
    /// <remarks>
    /// A game that renders the peers it is told about needs nothing from this: the cut
    /// already arrives as <c>enter</c> and <c>leave</c>. It is here for a game that
    /// wants to size a view or explain why a distant player is not shown.
    /// </remarks>
    public sealed class AreaOfInterest
    {
        public AreaOfInterest(double? range, int maxPeers)
        {
            Range = range;
            MaxPeers = maxPeers;
        }

        /// <summary>
        /// Half-width of the box in tiles, measured as Chebyshev distance from your last
        /// <c>pos</c>. Null means the whole zone: the gateway omits a zero range, and a
        /// non-positive one reads as null too.
        /// </summary>
        public double? Range { get; }

        /// <summary>
        /// The hard cap on peers in view; beyond it the gateway sends a <c>leave</c> for the
        /// farthest. The console allows 1 to 256. A missing, non-numeric or non-positive
        /// value reads as the gateway's own default, 64, and anything above 256 as 256, so a
        /// hostile value cannot size an array in the game.
        /// </summary>
        public int MaxPeers { get; }

        internal static AreaOfInterest? FromJson(JsonValue? value)
        {
            if (value == null || value.Kind != JsonKind.Object)
            {
                return null;
            }

            // Compared as doubles before the cast: an out-of-range double cast to int is
            // unspecified, and differs between runtimes.
            var maxPeers = value.GetNumber("maxPeers") ?? DefaultMaxPeers;
            maxPeers = maxPeers < 1 ? DefaultMaxPeers : maxPeers > MaxMaxPeers ? MaxMaxPeers : maxPeers;
            var range = value.GetNumber("range");
            return new AreaOfInterest(range > 0 ? range : null, (int)maxPeers);
        }

        private const double DefaultMaxPeers = 64;
        private const double MaxMaxPeers = 256;
    }
}
