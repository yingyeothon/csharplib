using System;

namespace Yingyeothon.Codec
{
    /// <summary>Thrown when a string cannot be parsed as JSON.</summary>
    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message, int index)
            : base(message + " (at index " + index + ")")
        {
            Index = index;
        }

        /// <summary>The character offset the parser refused.</summary>
        public int Index { get; }
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

        public JsonKind Expected { get; }

        public JsonKind Actual { get; }
    }
}
