using System;
using System.Globalization;

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
        /// consumer's log writer, so it is capped and every character that can break or
        /// reorder a log line is replaced before it can become a log-volume or
        /// log-injection vector: C0 and C1 controls (NEL included), format characters
        /// (the bidi overrides and isolates, zero-width ones), U+2028 and U+2029, and any
        /// surrogate that is not half of a whole pair inside the window — a lone one has no
        /// UTF-8 form, and the cut must not split an emoji.
        /// </summary>
        internal static string Diagnostic(string value)
        {
            const int max = 32;
            var length = Math.Min(value.Length, max);
            var buffer = new char[length];
            for (var i = 0; i < length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(value[i + 1]))
                {
                    buffer[i] = c;
                    buffer[i + 1] = value[i + 1];
                    i++;
                    continue;
                }

                buffer[i] = Unsafe(c) ? '?' : c;
            }

            return length < value.Length ? new string(buffer) + "\u2026" : new string(buffer);
        }

        private static bool Unsafe(char c)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                case UnicodeCategory.Surrogate:
                    return true;
                default:
                    return false;
            }
        }
    }
}
