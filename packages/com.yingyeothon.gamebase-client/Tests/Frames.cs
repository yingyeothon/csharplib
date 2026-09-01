using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>Builds gateway frames the way the gateway would put them on the wire.</summary>
    internal static class Frames
    {
        internal static JsonValue Peer(string userId, double x, double y, string? dir = null)
            => Json.Object().Set("userId", userId).Set("x", x).Set("y", y).Set("dir", dir).Build();

        internal static JsonValue Snapshot(string zone, params JsonValue[] peers)
            => Json.Object().Set("type", "snapshot").Set("zone", zone).Set("peers", Json.Array(peers)).Build();

        internal static JsonValue Pos(string zone, params JsonValue[] peers)
            => Json.Object().Set("type", "pos").Set("zone", zone).Set("peers", Json.Array(peers)).Build();

        internal static JsonValue Enter(string zone, string userId, double x, double y, string? dir = null)
            => Json.Object()
                .Set("type", "enter")
                .Set("zone", zone)
                .Set("userId", userId)
                .Set("x", x)
                .Set("y", y)
                .Set("dir", dir)
                .Build();

        internal static JsonValue Leave(string zone, string userId)
            => Json.Object().Set("type", "leave").Set("zone", zone).Set("userId", userId).Build();

        internal static JsonValue Hello(
            string userId = "alice",
            string? partyId = null,
            IReadOnlyList<string>? say = null,
            bool? pos = true,
            bool? party = true,
            bool? channelEvent = true,
            string zone = "town")
        {
            var capabilities = Json.Object()
                .Set("pos", pos)
                .Set("say", say == null ? null : Json.ArrayOfStrings(say))
                .Set("party", party)
                .Set("event", channelEvent)
                .Build();

            return Json.Object()
                .Set("type", "hello")
                .Set("userId", userId)
                .Set("connectionId", "gw1:abc")
                .Set("tick", 200d)
                .Set("mapUrl", "https://cdn/map/v1.json")
                .Set("zone", zone)
                .Set("partyId", partyId)
                .Set("capabilities", capabilities)
                .Build();
        }

        internal static LobbyServerFrame Read(JsonValue frame) => LobbyFrames.Read(frame);
    }
}
