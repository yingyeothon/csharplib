using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Codec.Samples
{
    /// <summary>Building and reading the frames a gateway channel carries.</summary>
    public static class JsonBasics
    {
        /// <summary>
        /// Builds a frame. <c>Set(key, null)</c> omits the field, because Go's
        /// <c>omitempty</c> means absent and an absent field is not a JSON null —
        /// sending <c>"dir": null</c> is a different frame from sending no dir at all.
        /// </summary>
        public static string BuildPosition(string zone, double x, double y, string? facing)
            => Json.Stringify(Json.Object()
                .Set("type", "pos")
                .Set("zone", zone)
                .Set("x", x)
                .Set("y", y)
                .Set("dir", facing)
                .Build());

        /// <summary>
        /// Reads a frame without throwing. The socket parses whatever a peer chose to
        /// send, so the failing path must cost a return rather than an exception.
        /// </summary>
        public static bool TryReadPosition(string wire, out string zone, out double x, out double y)
        {
            zone = string.Empty;
            x = 0;
            y = 0;

            if (!Json.TryParse(wire, out var frame, out var failure))
            {
                // failure is a code and an offset — never text quoted from the input,
                // because this reaches whatever log writer the consumer installed.
                System.Console.Out.WriteLine("not JSON: " + failure);
                return false;
            }

            zone = frame.GetString("zone") ?? string.Empty;
            x = frame.GetNumber("x") ?? 0;
            y = frame.GetNumber("y") ?? 0;
            return zone.Length > 0;
        }

        /// <summary>
        /// Absent is not null. A C# null means the field was not on the wire;
        /// <see cref="JsonValue.Null"/> means it was there and held JSON null.
        /// </summary>
        public static bool HasFacing(JsonValue frame)
            => frame.TryGetMember("dir", out var dir) && !dir.IsNull;

        /// <summary>Reads a game payload the gateway forwarded without inspecting it.</summary>
        public static IReadOnlyList<JsonValue> ReadItems(JsonValue payload)
            => payload.GetArrayOrEmpty("items");
    }
}
