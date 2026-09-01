using System;

namespace Yingyeothon.Codec
{
    /// <summary>The JSON text codec.</summary>
    /// <remarks>
    /// Differs from tslib's <c>jsonCodec</c> on one point, deliberately: TypeScript's
    /// <c>encode(undefined)</c> returns the literal string <c>"undefined"</c>, which
    /// is not valid JSON and which its own <c>decode</c> then refuses. C# has no
    /// <c>undefined</c>, so a null reference is rejected outright instead.
    /// </remarks>
    public static class JsonCodec
    {
        /// <summary>The shared, stateless codec instance.</summary>
        public static readonly ICodec<string> Instance = new JsonStringCodec();

        /// <summary>Serializes a value as compact JSON.</summary>
        public static string Encode(JsonValue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), "Use JsonValue.Null for a JSON null.");
            }

            return JsonWriter.Write(value);
        }

        /// <summary>Parses JSON text, throwing <see cref="JsonParseException"/> on malformed input.</summary>
        public static JsonValue Decode(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return JsonParser.Parse(text);
        }

        private sealed class JsonStringCodec : ICodec<string>
        {
            public string Encode(JsonValue value) => JsonCodec.Encode(value);

            public JsonValue Decode(string wire) => JsonCodec.Decode(wire);
        }
    }
}
