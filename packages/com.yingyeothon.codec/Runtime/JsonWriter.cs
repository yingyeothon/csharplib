using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Yingyeothon.Codec
{
    /// <summary>Serializes a <see cref="JsonValue"/> to compact JSON text.</summary>
    /// <remarks>
    /// Numbers are always written with <see cref="CultureInfo.InvariantCulture"/>. A
    /// comma-decimal locale would otherwise put <c>1,5</c> on the wire, which the
    /// gateway refuses as <c>bad_message</c> — silently, from the client's side.
    /// </remarks>
    public static class JsonWriter
    {
        [ThreadStatic]
        private static StringBuilder? SharedBuilder;

        /// <summary>Writes <paramref name="value"/> as compact JSON.</summary>
        public static string Write(JsonValue value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), "Use JsonValue.Null for a JSON null.");
            }

            var builder = SharedBuilder;
            if (builder == null)
            {
                builder = new StringBuilder(256);
                SharedBuilder = builder;
            }
            else
            {
                builder.Length = 0;
            }

            WriteValue(builder, value, 0);
            var text = builder.ToString();

            // Keep a modest buffer warm, but do not hold a huge one hostage.
            if (builder.Capacity > 64 * 1024)
            {
                SharedBuilder = null;
            }

            return text;
        }

        private static void WriteValue(StringBuilder builder, JsonValue value, int depth)
        {
            switch (value.Kind)
            {
                case JsonKind.Null:
                    builder.Append("null");
                    return;
                case JsonKind.Bool:
                    builder.Append(value.AsBool() ? "true" : "false");
                    return;
                case JsonKind.Number:
                    builder.Append(FormatNumber(value.AsNumber()));
                    return;
                case JsonKind.String:
                    WriteString(builder, value.AsString());
                    return;
                case JsonKind.Array:
                    RequireDepth(depth);
                    WriteArray(builder, value.AsArray(), depth + 1);
                    return;
                default:
                    RequireDepth(depth);
                    WriteObject(builder, value.AsObject(), depth + 1);
                    return;
            }
        }

        /// <param name="depth">How many containers are already open around this one.</param>
        private static void RequireDepth(int depth)
        {
            // The same bound the parser enforces, counted the same way. A value nested
            // deeper than this cannot be read back by this SDK, so writing it would
            // put a frame on the wire that nothing here can parse; and recursing over
            // a tree a caller built by hand would take the game's main thread down
            // with a StackOverflow, which no catch block can save.
            if (depth >= Json.MaxDepth)
            {
                throw new ArgumentException(
                    "The value is nested deeper than Json.MaxDepth, so it could not be parsed back.", "value");
            }
        }

        private static void WriteArray(StringBuilder builder, IReadOnlyList<JsonValue> items, int depth)
        {
            builder.Append('[');
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteValue(builder, items[i], depth);
            }

            builder.Append(']');
        }

        private static void WriteObject(StringBuilder builder, IReadOnlyList<KeyValuePair<string, JsonValue>> members, int depth)
        {
            builder.Append('{');
            for (var i = 0; i < members.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WriteString(builder, members[i].Key);
                builder.Append(':');
                WriteValue(builder, members[i].Value, depth);
            }

            builder.Append('}');
        }

        internal static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "JSON has no NaN or Infinity.");
            }

            // "R" is shortest-round-trip on this runtime; the plain ToString() on
            // netstandard2.0 is not. An integral value prints without a decimal
            // point, matching JSON.stringify.
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            return text == "-0" ? "0" : text;
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            AppendUnicodeEscape(builder, c);
                        }
                        else if (char.IsSurrogate(c))
                        {
                            // A well-formed pair goes out raw; an unpaired half must
                            // be escaped or it does not survive the UTF-8 encode a
                            // text frame performs, and the peer receives U+FFFD
                            // instead of what the game sent. ES2019 made
                            // JSON.stringify do exactly this.
                            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                builder.Append(c).Append(value[i + 1]);
                                i++;
                            }
                            else
                            {
                                AppendUnicodeEscape(builder, c);
                            }
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char c)
            => builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
    }
}
