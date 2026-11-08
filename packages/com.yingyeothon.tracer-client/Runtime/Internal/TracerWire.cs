using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer
{
    /// <summary>One envelope item: its header fields without <c>length</c>, and its payload.</summary>
    internal sealed class WireItem
    {
        internal WireItem(JsonValue header, byte[] payload)
        {
            Header = header;
            Payload = payload;
        }

        internal JsonValue Header { get; }

        internal byte[] Payload { get; }
    }

    /// <summary>The envelope format and the JSON shapes tracer stores; pure functions.</summary>
    internal static class TracerWire
    {
        internal const string LogContentType = "application/vnd.sentry.items.log+json";

        /// <summary>Room left for an item header line and newlines when sizing an envelope.</summary>
        internal const int ItemOverhead = 512;

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal static JsonValue Sdk { get; } = Json.Object()
            .Set("name", "yyt.csharp")
            .Set("version", TracerClient.SdkVersion)
            .Build();

        internal static string Hex(Func<double> random, int bytes)
        {
            var b = new StringBuilder(bytes * 2);
            for (var i = 0; i < bytes; i++)
            {
                var v = (int)(random() * 256);
                v = v < 0 ? 0 : v > 255 ? 255 : v;
                b.Append(v.ToString("x2", CultureInfo.InvariantCulture));
            }

            return b.ToString();
        }

        /// <summary>RFC 3339 in UTC with milliseconds, as event and breadcrumb timestamps are written.</summary>
        internal static string Rfc3339(DateTimeOffset t)
            => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        /// <summary>Float seconds, as log entry timestamps are written.</summary>
        internal static double EpochSeconds(DateTimeOffset t) => t.ToUnixTimeMilliseconds() / 1000.0;

        internal static string EventLevel(TracerLevel level) => level switch
        {
            TracerLevel.Debug => "debug",
            TracerLevel.Info => "info",
            TracerLevel.Warning => "warning",
            TracerLevel.Error => "error",
            _ => "fatal",
        };

        internal static string LogLevel(TracerLevel level) => level == TracerLevel.Warning ? "warn" : EventLevel(level);

        /// <summary>The OpenTelemetry severity number of a level.</summary>
        internal static int SeverityNumber(TracerLevel level) => level switch
        {
            TracerLevel.Debug => 5,
            TracerLevel.Info => 9,
            TracerLevel.Warning => 13,
            TracerLevel.Error => 17,
            _ => 21,
        };

        internal static int Utf8Length(string s) => Utf8.GetByteCount(s);

        /// <summary>
        /// The length tracer's Go re-marshalling gives <paramref name="json"/>: Go escapes
        /// <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c> as six bytes and U+2028/U+2029 as six, so
        /// a size checked against the ingest's 16 KiB attribute cap counts them that way.
        /// </summary>
        internal static int GoJsonLength(string json)
        {
            var extra = 0;
            foreach (var c in json)
            {
                if (c == '<' || c == '>' || c == '&')
                {
                    extra += 5;
                }
                else if (c == '\u2028' || c == '\u2029')
                {
                    extra += 3;
                }
            }

            return Utf8.GetByteCount(json) + extra;
        }

        /// <summary><paramref name="s"/> cut to at most <paramref name="maxBytes"/> UTF-8 bytes, never inside a surrogate pair.</summary>
        internal static string Clip(string? s, int maxBytes)
        {
            if (s == null)
            {
                return string.Empty;
            }

            if (s.Length * 3 <= maxBytes || Utf8.GetByteCount(s) <= maxBytes)
            {
                return s;
            }

            var bytes = 0;
            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                int width;
                int chars;
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    width = 4;
                    chars = 2;
                }
                else
                {
                    width = c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                    chars = 1;
                }

                if (bytes + width > maxBytes)
                {
                    break;
                }

                bytes += width;
                i += chars;
            }

            return s.Substring(0, i);
        }

        internal static byte[] JsonBytes(JsonValue value) => Utf8.GetBytes(Json.Stringify(value));

        internal static byte[] Text(string s) => Utf8.GetBytes(s);

        /// <summary>
        /// A JSON-safe copy of a caller's value: strings clipped, keys clipped to 200 bytes,
        /// nesting stopped at depth 8, containers at 200 entries and the whole copy at 2000
        /// values. Never throws.
        /// </summary>
        internal static JsonValue Safe(JsonValue value)
        {
            var budget = 2000;
            return Safe(value, 0, ref budget);
        }

        private static JsonValue Safe(JsonValue value, int depth, ref int budget)
        {
            if (--budget < 0)
            {
                return JsonValue.Of("…");
            }

            switch (value.Kind)
            {
                case JsonKind.String:
                    return JsonValue.Of(Clip(value.AsString(), TracerLimits.MaxTextBytes));
                case JsonKind.Array:
                    if (depth >= 8)
                    {
                        return JsonValue.Of("…");
                    }

                    var items = new List<JsonValue>();
                    foreach (var item in value.AsArray())
                    {
                        if (items.Count == 200 || budget <= 0)
                        {
                            break;
                        }

                        items.Add(Safe(item, depth + 1, ref budget));
                    }

                    return JsonValue.Array(items);
                case JsonKind.Object:
                    if (depth >= 8)
                    {
                        return JsonValue.Of("…");
                    }

                    var members = new List<KeyValuePair<string, JsonValue>>();
                    foreach (var member in value.AsObject())
                    {
                        if (members.Count == 200 || budget <= 0)
                        {
                            break;
                        }

                        members.Add(new KeyValuePair<string, JsonValue>(Clip(member.Key, 200), Safe(member.Value, depth + 1, ref budget)));
                    }

                    return JsonValue.Object(members);
                default:
                    return value;
            }
        }

        /// <summary>A typed log attribute <c>{"value", "type"}</c>; null for a JSON null.</summary>
        internal static JsonValue? Typed(JsonValue value)
        {
            switch (value.Kind)
            {
                case JsonKind.Null:
                    return null;
                case JsonKind.Bool:
                    return Attr(value, "boolean");
                case JsonKind.Number:
                    var n = value.AsNumber();
                    return Attr(value, Math.Floor(n) == n && Math.Abs(n) <= 9007199254740991d ? "integer" : "double");
                case JsonKind.String:
                    return Attr(JsonValue.Of(Clip(value.AsString(), TracerLimits.MaxTextBytes)), "string");
                default:
                    return Attr(JsonValue.Of(Clip(Json.Stringify(Safe(value)), TracerLimits.MaxTextBytes)), "string");
            }
        }

        internal static JsonValue Attr(JsonValue value, string type)
            => Json.Object().Set("value", value).Set("type", type).Build();

        /// <summary>The envelope: a header line, then per item a header line with an explicit <c>length</c>, the payload and a newline.</summary>
        internal static byte[] Envelope(JsonValue header, IReadOnlyList<WireItem> items)
        {
            using (var out_ = new MemoryStream())
            {
                Write(out_, JsonBytes(header));
                out_.WriteByte(0x0A);
                foreach (var item in items)
                {
                    var members = new List<KeyValuePair<string, JsonValue>>(item.Header.AsObject())
                    {
                        new KeyValuePair<string, JsonValue>("length", JsonValue.Of(item.Payload.Length)),
                    };
                    Write(out_, JsonBytes(JsonValue.Object(members)));
                    out_.WriteByte(0x0A);
                    Write(out_, item.Payload);
                    out_.WriteByte(0x0A);
                }

                return out_.ToArray();
            }
        }

        private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

        /// <summary>Gzip of <paramref name="body"/>; throws where the runtime has no zlib.</summary>
        internal static byte[] Gzip(byte[] body)
        {
            using (var out_ = new MemoryStream())
            {
                using (var gz = new GZipStream(out_, CompressionLevel.Fastest, true))
                {
                    gz.Write(body, 0, body.Length);
                }

                return out_.ToArray();
            }
        }

        /// <summary>
        /// Frames of a .NET, Mono or Unity stack trace text, oldest first (the Sentry order),
        /// the innermost 200 kept. A line in no known shape is skipped.
        /// </summary>
        internal static List<JsonValue> Frames(string? stackTrace)
        {
            var frames = new List<JsonValue>();
            if (string.IsNullOrEmpty(stackTrace))
            {
                return frames;
            }

            foreach (var raw in stackTrace!.Split('\n'))
            {
                var frame = Frame(raw.Trim());
                if (frame != null)
                {
                    frames.Add(frame);
                    if (frames.Count == 200)
                    {
                        break;
                    }
                }
            }

            frames.Reverse();
            return frames;
        }

        /// <summary>
        /// One frame: <c>at Ns.Type.Method(args) in /path/File.cs:line 42</c> (.NET),
        /// <c>at Ns.Type.Method (args) [0x00012] in /path/File.cs:42</c> (Mono), or
        /// <c>Ns.Type:Method(args) (at Assets/File.cs:42)</c> (Unity's log callback).
        /// </summary>
        private static JsonValue? Frame(string line)
        {
            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal))
            {
                return null;
            }

            string function;
            string? file = null;
            int? lineNo = null;
            var wrapper = false;
            if (line.StartsWith("at ", StringComparison.Ordinal))
            {
                var rest = line.Substring(3);
                // Mono's "(wrapper managed-to-native) Type.Method (args)": runtime glue.
                if (rest.StartsWith("(wrapper ", StringComparison.Ordinal))
                {
                    var close = rest.IndexOf(") ", StringComparison.Ordinal);
                    if (close < 0)
                    {
                        return null;
                    }

                    rest = rest.Substring(close + 2);
                    wrapper = true;
                }

                var inAt = rest.LastIndexOf(" in ", StringComparison.Ordinal);
                var head = inAt < 0 ? rest : rest.Substring(0, inAt);
                if (inAt >= 0)
                {
                    SplitLocation(rest.Substring(inAt + 4), out file, out lineNo);
                }

                function = FunctionName(head);
            }
            else
            {
                var at = line.LastIndexOf(" (at ", StringComparison.Ordinal);
                if (at < 0 || !line.EndsWith(")", StringComparison.Ordinal))
                {
                    return null;
                }

                SplitLocation(line.Substring(at + 5, line.Length - at - 6), out file, out lineNo);
                function = FunctionName(line.Substring(0, at)).Replace(':', '.');
            }

            if (function.Length == 0)
            {
                return null;
            }

            // A file IL2CPP or Mono could not resolve is "<hash>": no use to a reader.
            if (file != null && (file.Length == 0 || file.StartsWith("<", StringComparison.Ordinal)))
            {
                file = null;
                lineNo = null;
            }

            var builder = Json.Object();
            if (file != null)
            {
                builder.Set("filename", Clip(ShortPath(file), 300));
            }

            builder.Set("function", Clip(function, 300));
            if (lineNo.HasValue)
            {
                builder.Set("lineno", JsonValue.Of(lineNo.Value));
            }

            // No abs_path: a Mono or .NET build-machine path names the developer's home.
            builder.Set("in_app", !wrapper && InApp(function));
            builder.Set("platform", "csharp");
            return builder.Build();
        }

        /// <summary>The method without its parameter list and Mono's IL offset.</summary>
        private static string FunctionName(string head)
        {
            var bracket = head.IndexOf(" [0x", StringComparison.Ordinal);
            if (bracket >= 0)
            {
                head = head.Substring(0, bracket);
            }

            var paren = head.IndexOf('(');
            if (paren > 0)
            {
                head = head.Substring(0, paren);
            }

            return head.Trim();
        }

        private static void SplitLocation(string location, out string? file, out int? lineNo)
        {
            location = location.Trim();
            file = location;
            lineNo = null;
            var colon = location.LastIndexOf(':');
            if (colon <= 0)
            {
                return;
            }

            var number = location.Substring(colon + 1).Trim();
            if (number.StartsWith("line ", StringComparison.Ordinal))
            {
                number = number.Substring(5);
            }

            if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                file = location.Substring(0, colon);
                lineNo = n == 0 ? (int?)null : n;
            }
        }

        /// <summary>
        /// The path a reader can find in the game's repository: from <c>Assets/</c> or
        /// <c>Packages/</c> on, else the file name. The read API shows <c>filename</c>, and
        /// tracer groups by it.
        /// </summary>
        private static string ShortPath(string path)
        {
            var p = path.Replace('\\', '/');
            foreach (var root in new[] { "Assets/", "Packages/" })
            {
                var at = p.StartsWith(root, StringComparison.Ordinal) ? 0 : p.LastIndexOf("/" + root, StringComparison.Ordinal);
                if (at >= 0)
                {
                    return p.Substring(at == 0 ? 0 : at + 1);
                }
            }

            var slash = p.LastIndexOf('/');
            return slash < 0 ? p : p.Substring(slash + 1);
        }

        private static bool InApp(string function)
            => !function.StartsWith("System.", StringComparison.Ordinal)
                && !function.StartsWith("UnityEngine.", StringComparison.Ordinal)
                && !function.StartsWith("UnityEditor.", StringComparison.Ordinal)
                && !function.StartsWith("Mono.", StringComparison.Ordinal)
                && !function.StartsWith("Yingyeothon.Tracer.", StringComparison.Ordinal);
    }
}
