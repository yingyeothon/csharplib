using System.Text;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Builds the client-to-gateway lobby frames.</summary>
    /// <remarks>
    /// Every optional field goes through <see cref="JsonObjectBuilder.Set(string, string)"/>
    /// with a null, which omits the key entirely rather than writing <c>null</c> —
    /// the gateway reads these with Go structs, where an absent field and a null one
    /// are not the same frame.
    /// </remarks>
    internal static class LobbyFrameWriter
    {
        /// <summary>The gateway refuses a <c>dir</c> longer than this many bytes as <c>bad_message</c>.</summary>
        internal const int MaxDirBytes = 16;

        internal static JsonValue Pos(string zone, double x, double y, string? dir)
            => Json.Object()
                .Set("type", FrameTypes.Pos)
                .Set("zone", zone)
                .Set("x", x)
                .Set("y", y)
                .Set("dir", dir)
                .Build();

        internal static JsonValue Say(SayScope scope, string? to, string text)
            => Json.Object()
                .Set("type", FrameTypes.Say)
                .Set("scope", SayScopes.ToWire(scope))
                .Set("to", to)
                .Set("text", text)
                .Build();

        internal static JsonValue Event(SayScope scope, string? to, string name, JsonValue? payload)
            => Json.Object()
                .Set("type", FrameTypes.Event)
                .Set("scope", SayScopes.ToWire(scope))
                .Set("to", to)
                .Set("name", name)
                .Set("payload", payload)
                .Build();

        internal static JsonValue TypeOnly(string type)
            => Json.Object().Set("type", type).Build();

        internal static JsonValue PartyInvite(string userId)
            => Json.Object().Set("type", FrameTypes.PartyInvite).Set("userId", userId).Build();

        internal static JsonValue PartyAccept(string partyId)
            => Json.Object().Set("type", FrameTypes.PartyAccept).Set("partyId", partyId).Build();

        internal static JsonValue PartyDecline(string partyId)
            => Json.Object().Set("type", FrameTypes.PartyDecline).Set("partyId", partyId).Build();

        /// <summary>
        /// The gateway measures <c>dir</c> in bytes, not characters, so a three-byte
        /// Hangul syllable spends three of the sixteen.
        /// </summary>
        internal static bool IsDirTooLong(string dir) => Encoding.UTF8.GetByteCount(dir) > MaxDirBytes;
    }
}
