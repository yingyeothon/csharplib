using System;

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

        /// <summary>
        /// Renders a peer-chosen string for a diagnostic message. A frame's
        /// <c>type</c> is whatever the peer put there, and these messages reach a
        /// consumer's log writer, so it is capped and stripped of control characters
        /// before it can become a log-volume or log-injection vector.
        /// </summary>
        internal static string Diagnostic(string value)
        {
            const int max = 32;
            var length = Math.Min(value.Length, max);
            var buffer = new char[length];
            for (var i = 0; i < length; i++)
            {
                var c = value[i];
                buffer[i] = c < ' ' || c == '\u007f' ? '?' : c;
            }

            return length < value.Length ? new string(buffer) + "\u2026" : new string(buffer);
        }
    }
}
