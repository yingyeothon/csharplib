using System;
using System.Collections.Generic;

namespace Yingyeothon.Codec
{
    /// <summary>Entry points for reading and writing JSON.</summary>
    public static class Json
    {
        /// <summary>Parses JSON text, throwing <see cref="JsonParseException"/> on malformed input.</summary>
        public static JsonValue Parse(string text) => JsonParser.Parse(text);

        /// <summary>
        /// Parses JSON text without throwing. The gateway socket decides what to do
        /// with a malformed frame per frame, and building an exception on that path
        /// would cost more than the frame itself.
        /// </summary>
        public static bool TryParse(string text, out JsonValue value)
        {
            try
            {
                value = JsonParser.Parse(text);
                return true;
            }
            catch (JsonParseException)
            {
                value = JsonValue.Null;
                return false;
            }
            catch (ArgumentNullException)
            {
                value = JsonValue.Null;
                return false;
            }
        }

        /// <summary>Serializes a value as compact JSON.</summary>
        public static string Stringify(JsonValue value) => JsonWriter.Write(value);

        /// <summary>Starts building an object whose absent fields are simply omitted.</summary>
        public static JsonObjectBuilder Object() => new JsonObjectBuilder();

        /// <summary>Builds an array from a parameter list.</summary>
        public static JsonValue Array(params JsonValue[] items) => JsonValue.Array(items);

        /// <summary>Builds an array of strings.</summary>
        public static JsonValue ArrayOfStrings(IEnumerable<string> items)
        {
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
