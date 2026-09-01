using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Turns a decoded gateway frame into a typed <see cref="LobbyServerFrame"/>.</summary>
    /// <remarks>
    /// Hand-written on purpose: no attributes, no reflection, nothing for IL2CPP's
    /// managed stripper to remove. It is also short enough to diff against the
    /// gateway's <c>protocol.go</c> by eye, which is how the wire stays correct.
    /// </remarks>
    public static class LobbyFrames
    {
        /// <summary>Reads a decoded lobby frame, taking its kind from the <c>type</c> field.</summary>
        public static LobbyServerFrame Read(JsonValue frame)
        {
            if (frame == null)
            {
                throw new System.ArgumentNullException(nameof(frame));
            }

            return Read(frame.GetString("type") ?? string.Empty, frame);
        }

        internal static LobbyServerFrame Read(string type, JsonValue frame)
        {
            switch (type)
            {
                case FrameTypes.Snapshot:
                    return new SnapshotFrame(
                        frame.GetString("zone") ?? string.Empty,
                        ReadPeers(frame),
                        frame);

                case FrameTypes.Enter:
                    return new EnterFrame(
                        frame.GetString("zone") ?? string.Empty,
                        Peer.FromJson(frame),
                        frame);

                case FrameTypes.Leave:
                    return new LeaveFrame(
                        frame.GetString("zone") ?? string.Empty,
                        frame.GetString("userId") ?? string.Empty,
                        frame);

                case FrameTypes.Pos:
                    return new PosBroadcastFrame(
                        frame.GetString("zone") ?? string.Empty,
                        ReadPeers(frame),
                        frame);

                case FrameTypes.Say:
                    return new SayBroadcastFrame(
                        frame.GetString("from") ?? string.Empty,
                        frame.GetString("scope") ?? string.Empty,
                        Normalize.OptionalId(frame.GetString("to")),
                        frame.GetString("text") ?? string.Empty,
                        frame);

                case FrameTypes.Event:
                    return new EventBroadcastFrame(
                        frame.GetString("from") ?? string.Empty,
                        frame.GetString("scope") ?? string.Empty,
                        Normalize.OptionalId(frame.GetString("to")),
                        frame.GetString("name") ?? string.Empty,
                        frame.GetMemberOrNull("payload"),
                        frame);

                case FrameTypes.Party:
                    return ReadParty(frame);

                case FrameTypes.PartyInvite:
                    return new PartyInviteFrame(
                        frame.GetString("partyId") ?? string.Empty,
                        frame.GetString("from") ?? string.Empty,
                        frame);

                case FrameTypes.PartyDeclined:
                    return new PartyDeclinedFrame(
                        frame.GetString("partyId") ?? string.Empty,
                        frame.GetString("userId") ?? string.Empty,
                        frame);

                case FrameTypes.Pong:
                    return new PongFrame(frame);

                case FrameTypes.Error:
                    return new ErrorFrame(
                        frame.GetString("code") ?? string.Empty,
                        frame.GetString("message") ?? string.Empty,
                        frame);

                default:
                    return new UnknownServerFrame(type, frame);
            }
        }

        private static IReadOnlyList<Peer> ReadPeers(JsonValue frame)
        {
            var peers = new List<Peer>();
            foreach (var item in frame.GetArrayOrEmpty("peers"))
            {
                if (item.Kind == JsonKind.Object)
                {
                    peers.Add(Peer.FromJson(item));
                }
            }

            return peers;
        }

        private static PartyFrame ReadParty(JsonValue frame)
        {
            var members = new List<PartyMember>();
            foreach (var item in frame.GetArrayOrEmpty("members"))
            {
                if (item.Kind == JsonKind.Object)
                {
                    members.Add(new PartyMember(
                        item.GetString("userId") ?? string.Empty,
                        item.GetBool("online") ?? false));
                }
            }

            var invited = new List<string>();
            foreach (var item in frame.GetArrayOrEmpty("invited"))
            {
                if (item.Kind == JsonKind.String)
                {
                    invited.Add(item.AsString());
                }
            }

            // leaderId / invited / max are omitempty on the wire and simply absent
            // when empty; the defaults here are what makes the public type honest.
            return new PartyFrame(
                Normalize.OptionalId(frame.GetString("partyId")),
                frame.GetString("leaderId") ?? string.Empty,
                members,
                invited,
                (int)(frame.GetNumber("max") ?? 0),
                frame);
        }
    }
}
