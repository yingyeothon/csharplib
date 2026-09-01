using System;
using System.Globalization;

namespace Yingyeothon.Codec
{
    /// <summary>Why a document was refused. Part of the public contract: these names reach logs.</summary>
    /// <remarks>
    /// A code, never a quotation. A refusal is reported through the gateway's
    /// <c>ProtocolError</c> into whatever log writer the consumer installed, and a
    /// frame body is never allowed there (see <c>rules/security.md</c>), so nothing
    /// derived from the input may appear in a failure.
    /// </remarks>
    public enum JsonParseError
    {
        /// <summary>No failure.</summary>
        None = 0,

        /// <summary>The text was a null reference.</summary>
        NullInput,

        /// <summary>The text was longer than the limit the caller allowed.</summary>
        InputTooLong,

        /// <summary>More nested arrays or objects than <c>Json.MaxDepth</c> allows.</summary>
        DepthExceeded,

        /// <summary>The text ended in the middle of a value.</summary>
        UnexpectedEndOfInput,

        /// <summary>Something other than whitespace followed the top-level value.</summary>
        TrailingContent,

        /// <summary>A value was expected and the character there cannot begin one.</summary>
        ExpectedValue,

        /// <summary><c>true</c>, <c>false</c> or <c>null</c> was started but not spelled out.</summary>
        ExpectedLiteral,

        /// <summary>An object member did not begin with a quoted key.</summary>
        ExpectedKey,

        /// <summary>An object key was not followed by <c>:</c>.</summary>
        ExpectedColon,

        /// <summary>An array or object element was not followed by <c>,</c> or its closing bracket.</summary>
        ExpectedCommaOrEnd,

        /// <summary>The text ended before a string's closing quote.</summary>
        UnterminatedString,

        /// <summary>A character below U+0020 appeared in a string without an escape.</summary>
        UnescapedControlCharacter,

        /// <summary>A backslash was followed by something JSON does not escape.</summary>
        UnknownEscape,

        /// <summary>The text ended inside a <c>\u</c> escape.</summary>
        TruncatedUnicodeEscape,

        /// <summary>A <c>\u</c> escape contained something that is not a hex digit.</summary>
        InvalidUnicodeEscape,

        /// <summary>A decimal point was not followed by a digit.</summary>
        ExpectedFractionDigit,

        /// <summary>An exponent marker was not followed by a digit.</summary>
        ExpectedExponentDigit,

        /// <summary>The number does not fit in a <see cref="double"/>.</summary>
        NumberOutOfRange,
    }

    /// <summary>A parse failure: a reason and the offset it was found at.</summary>
    /// <remarks>
    /// A value type, so reporting a malformed frame allocates nothing. The gateway
    /// socket parses every inbound frame; a peer sending garbage must not be able to
    /// make the client allocate an exception per frame.
    /// </remarks>
    public readonly struct JsonParseFailure : IEquatable<JsonParseFailure>
    {
        /// <summary>Creates a failure.</summary>
        public JsonParseFailure(JsonParseError error, int index)
        {
            Error = error;
            Index = index;
        }

        /// <summary>Why the document was refused.</summary>
        public JsonParseError Error { get; }

        /// <summary>
        /// The character offset the parser refused, or 0 when the input was rejected
        /// before any scanning (a null reference, or text over the length limit).
        /// </summary>
        public int Index { get; }

        /// <summary>Whether this describes a failure at all.</summary>
        public bool IsFailure => Error != JsonParseError.None;

        /// <inheritdoc />
        public bool Equals(JsonParseFailure other) => Error == other.Error && Index == other.Index;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is JsonParseFailure other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked(((int)Error * 397) ^ Index);

        public static bool operator ==(JsonParseFailure left, JsonParseFailure right) => left.Equals(right);

        public static bool operator !=(JsonParseFailure left, JsonParseFailure right) => !left.Equals(right);

        /// <summary>The reason and the offset, and nothing from the document itself.</summary>
        public override string ToString() => Error + " at " + Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Thrown when a string cannot be parsed as JSON.</summary>
    public sealed class JsonParseException : Exception
    {
        /// <summary>Creates an exception describing <paramref name="failure"/>.</summary>
        public JsonParseException(JsonParseFailure failure)
            : base("Malformed JSON: " + failure)
        {
            Failure = failure;
        }

        /// <summary>The reason and offset, in the form the non-throwing path reports.</summary>
        public JsonParseFailure Failure { get; }

        /// <summary>Why the document was refused.</summary>
        public JsonParseError Error => Failure.Error;

        /// <summary>The character offset the parser refused.</summary>
        public int Index => Failure.Index;
    }

    /// <summary>Thrown when a <see cref="JsonValue"/> is read as the wrong kind.</summary>
    public sealed class JsonKindException : Exception
    {
        public JsonKindException(JsonKind expected, JsonKind actual)
            : base("Expected a JSON " + expected + " but the value is a " + actual + ".")
        {
            Expected = expected;
            Actual = actual;
        }

        /// <summary>The kind the accessor required.</summary>
        public JsonKind Expected { get; }

        /// <summary>The kind the value actually held.</summary>
        public JsonKind Actual { get; }
    }

    /// <summary>
    /// Thrown when a JSON number is the right kind but the wrong shape for the
    /// requested conversion — fractional, or outside the target's range.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="JsonKindException"/> on purpose: reporting a
    /// fractional value as "expected a Number but the value is a Number" tells the
    /// reader nothing, and the two have different fixes. The message never carries
    /// the value, which is wire data.
    /// </remarks>
    public sealed class JsonNumberException : Exception
    {
        /// <summary>Creates an exception with a message that must not quote the value.</summary>
        public JsonNumberException(string message)
            : base(message)
        {
        }
    }
}
