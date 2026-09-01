using System;
using System.Collections.Generic;

namespace Yingyeothon.Codec
{
    /// <summary>Entry points for reading and writing JSON.</summary>
    public static class Json
    {
        /// <summary>
        /// The longest document <see cref="Parse(string)"/> and
        /// <see cref="TryParse(string, out JsonValue)"/> accept, in characters.
        /// </summary>
        /// <remarks>
        /// Sized for frames, with room to spare: the gateway caps its own outbound
        /// frames far below this. A document that is legitimately larger — a
        /// downloaded map asset, say — is not a frame, and its reader says so by
        /// calling <see cref="ParseBig"/> with the limit it is willing to pay for.
        /// </remarks>
        public const int MaxLength = 1024 * 1024;

        /// <summary>The largest limit <see cref="ParseBig"/> will accept.</summary>
        public const int MaxBigLength = 64 * 1024 * 1024;

        /// <summary>How many nested arrays or objects a document may contain.</summary>
        public const int MaxDepth = JsonParser.MaxDepth;

        /// <summary>Parses JSON text, throwing <see cref="JsonParseException"/> on malformed input.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            return Parsed(JsonParser.TryParse(text, MaxLength, out var value, out var failure), value, failure);
        }

        /// <summary>
        /// Parses a document that is deliberately larger than a frame, up to
        /// <paramref name="maxLength"/> characters.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Parse(string)"/> rather than a bigger default,
        /// so the cost of a multi-megabyte document is something a caller opts into
        /// at the call site instead of something every frame pays for.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxLength"/> is below 1 or above <see cref="MaxBigLength"/>.
        /// </exception>
        public static JsonValue ParseBig(string text, int maxLength)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            CheckLimit(maxLength);
            return Parsed(JsonParser.TryParse(text, maxLength, out var value, out var failure), value, failure);
        }

        /// <summary>
        /// Parses JSON text without throwing, for a caller that only wants to know
        /// whether it worked.
        /// </summary>
        public static bool TryParse(string text, out JsonValue value)
            => JsonParser.TryParse(text, MaxLength, out value, out _);

        /// <summary>
        /// Parses JSON text without throwing, reporting why it was refused.
        /// </summary>
        /// <remarks>
        /// The gateway socket decides what to do with a malformed frame per frame.
        /// This path allocates no exception, and <paramref name="failure"/> carries a
        /// code and an offset — never anything copied out of the document, which is
        /// wire data and must not reach a log.
        /// </remarks>
        public static bool TryParse(string text, out JsonValue value, out JsonParseFailure failure)
            => JsonParser.TryParse(text, MaxLength, out value, out failure);

        /// <summary>Non-throwing <see cref="ParseBig"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="maxLength"/> is below 1 or above <see cref="MaxBigLength"/>.
        /// </exception>
        public static bool TryParseBig(string text, int maxLength, out JsonValue value, out JsonParseFailure failure)
        {
            CheckLimit(maxLength);
            return JsonParser.TryParse(text, maxLength, out value, out failure);
        }

        private static JsonValue Parsed(bool ok, JsonValue value, JsonParseFailure failure)
            => ok ? value : throw new JsonParseException(failure);

        private static void CheckLimit(int maxLength)
        {
            if (maxLength < 1 || maxLength > MaxBigLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxLength), "The limit must be between 1 and Json.MaxBigLength.");
            }
        }

        /// <summary>Serializes a value as compact JSON.</summary>
        public static string Stringify(JsonValue value) => JsonWriter.Write(value);

        /// <summary>Starts building an object whose absent fields are simply omitted.</summary>
        public static JsonObjectBuilder Object() => new JsonObjectBuilder();

        /// <summary>Builds an array from a parameter list.</summary>
        public static JsonValue Array(params JsonValue[] items) => JsonValue.Array(items);

        /// <summary>Builds an array of strings. A null item is refused, as everywhere else.</summary>
        public static JsonValue ArrayOfStrings(IEnumerable<string> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var values = new List<JsonValue>();
            foreach (var item in items)
            {
                values.Add(JsonValue.Of(item));
            }

            return JsonValue.Array(values);
        }
    }

    /// <summary>
    /// Builds a JSON object where a null argument means "leave the field off the
    /// wire", which is how Go's <c>omitempty</c> marshals an empty value. Writing
    /// <c>null</c> explicitly is a separate, deliberate call.
    /// </summary>
    public sealed class JsonObjectBuilder
    {
        private readonly List<KeyValuePair<string, JsonValue>> _members =
            new List<KeyValuePair<string, JsonValue>>();

        /// <summary>Adds a member. A null <paramref name="value"/> omits the field entirely.</summary>
        public JsonObjectBuilder Set(string key, JsonValue? value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (value != null)
            {
                _members.Add(new KeyValuePair<string, JsonValue>(key, value));
            }

            return this;
        }

        /// <summary>Adds a string member. A null <paramref name="value"/> omits the field.</summary>
        public JsonObjectBuilder Set(string key, string? value)
            => Set(key, value == null ? null : JsonValue.Of(value));

        /// <summary>Adds a number member. A null <paramref name="value"/> omits the field.</summary>
        public JsonObjectBuilder Set(string key, double? value)
            => Set(key, value == null ? null : JsonValue.Of(value.Value));

        /// <summary>Adds a bool member. A null <paramref name="value"/> omits the field.</summary>
        public JsonObjectBuilder Set(string key, bool? value)
            => Set(key, value == null ? null : JsonValue.Of(value.Value));

        /// <summary>Adds an explicit JSON <c>null</c>, which is not the same as omitting the field.</summary>
        public JsonObjectBuilder SetNull(string key) => Set(key, JsonValue.Null);

        /// <summary>Produces the object.</summary>
        public JsonValue Build() => JsonValue.Object(_members);
    }
}
