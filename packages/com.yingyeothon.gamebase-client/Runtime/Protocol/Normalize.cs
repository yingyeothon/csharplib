namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Wire-shape normalisation shared by the frame parsers.</summary>
    internal static class Normalize
    {
        /// <summary>
        /// Folds an absent and an empty identifier into null. Go marshals an empty
        /// string either as <c>""</c> or, with <c>omitempty</c>, not at all, and the
        /// gateway uses <c>partyId: ""</c> to mean "you are in no party" — so the two
        /// are the same fact and the SDK must not make callers check both.
        /// </summary>
        internal static string? OptionalId(string? value)
            => string.IsNullOrEmpty(value) ? null : value;
    }
}
