using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>A frame the gateway sent on a lobby channel.</summary>
    public abstract class LobbyServerFrame
    {
        protected LobbyServerFrame(string type, JsonValue raw)
        {
            Type = type;
            Raw = raw;
        }

        /// <summary>The frame's <c>type</c> field, as it arrived.</summary>
        public string Type { get; }

        /// <summary>The frame as received, after party normalisation.</summary>
        public JsonValue Raw { get; }
    }

    /// <summary>Everyone already in the zone the client just entered.</summary>
    public sealed class SnapshotFrame : LobbyServerFrame
    {
        internal SnapshotFrame(string zone, IReadOnlyList<Peer> peers, JsonValue raw)
            : base(FrameTypes.Snapshot, raw)
        {
            Zone = zone;
            Peers = peers;
        }

        /// <summary>The zone this snapshot describes. It replaces whatever the peer map held.</summary>
        public string Zone { get; }

        /// <summary>Every retained peer in the zone, including the receiver's own entry.</summary>
        public IReadOnlyList<Peer> Peers { get; }
    }

    /// <summary>A peer entered the receiver's zone.</summary>
    public sealed class EnterFrame : LobbyServerFrame
    {
        internal EnterFrame(string zone, Peer peer, JsonValue raw)
            : base(FrameTypes.Enter, raw)
        {
            Zone = zone;
            Peer = peer;
        }

        /// <summary>The zone the peer entered.</summary>
        public string Zone { get; }

        /// <summary>The peer that became visible.</summary>
        public Peer Peer { get; }
    }

    /// <summary>A peer left the receiver's zone.</summary>
    public sealed class LeaveFrame : LobbyServerFrame
    {
        internal LeaveFrame(string zone, string userId, JsonValue raw)
            : base(FrameTypes.Leave, raw)
        {
            Zone = zone;
            UserId = userId;
        }

        /// <summary>The zone the peer left.</summary>
        public string Zone { get; }

        /// <summary>The peer that stopped being visible.</summary>
        public string UserId { get; }
    }

    /// <summary>Coalesced positions once per tick; includes the receiver's own entry.</summary>
    public sealed class PosBroadcastFrame : LobbyServerFrame
    {
        internal PosBroadcastFrame(string zone, IReadOnlyList<Peer> peers, JsonValue raw)
            : base(FrameTypes.Pos, raw)
        {
            Zone = zone;
            Peers = peers;
        }

        /// <summary>The zone these positions belong to.</summary>
        public string Zone { get; }

        /// <summary>Only the peers that moved this tick, including the receiver's own entry.</summary>
        public IReadOnlyList<Peer> Peers { get; }
    }

    /// <summary>Chat mirrored to its scope.</summary>
    public sealed class SayBroadcastFrame : LobbyServerFrame
    {
        internal SayBroadcastFrame(string from, string scope, string? to, string text, JsonValue raw)
            : base(FrameTypes.Say, raw)
        {
            From = from;
            Scope = scope;
            To = to;
            Text = text;
        }

        /// <summary>The sender's <c>userId</c>.</summary>
        public string From { get; }

        /// <summary>The wire scope. Unknown values stay readable rather than being dropped.</summary>
        public string Scope { get; }

        /// <summary>The addressee, on a whisper.</summary>
        public string? To { get; }

        /// <summary>The message. Never log it: it is whatever the peer typed.</summary>
        public string Text { get; }
    }

    /// <summary>The opaque game-defined relay; the payload is forwarded unread.</summary>
    public sealed class EventBroadcastFrame : LobbyServerFrame
    {
        internal EventBroadcastFrame(string from, string scope, string? to, string name, JsonValue? payload, JsonValue raw)
            : base(FrameTypes.Event, raw)
        {
            From = from;
            Scope = scope;
            To = to;
            Name = name;
            Payload = payload;
        }

        /// <summary>The sender's <c>userId</c>.</summary>
        public string From { get; }

        /// <summary>The wire scope: <c>zone</c>, <c>party</c> or <c>user</c>.</summary>
        public string Scope { get; }

        /// <summary>The addressee, when the event was sent to one user.</summary>
        public string? To { get; }

        /// <summary>The game-defined event name.</summary>
        public string Name { get; }

        /// <summary>The game's own payload, untouched. Null when the field was absent.</summary>
        public JsonValue? Payload { get; }
    }

    /// <summary>A roster entry. <see cref="Online"/> lets a client grey out a member whose socket dropped.</summary>
    public sealed class PartyMember
    {
        internal PartyMember(string userId, bool online)
        {
            UserId = userId;
            Online = online;
        }

        /// <summary>The member's identity.</summary>
        public string UserId { get; }

        /// <summary>False while the member is disconnected; membership survives a drop.</summary>
        public bool Online { get; }
    }

    /// <summary>
    /// The party snapshot sent on every change and on reconnect.
    /// </summary>
    /// <remarks>
    /// The gateway marshals this with Go <c>omitempty</c>: <c>leaderId</c>,
    /// <c>invited</c> and <c>max</c> are missing on the wire when empty — always after
    /// a leave or dissolve, and <c>invited</c> whenever nobody is pending. They are
    /// filled in here as <c>""</c>, an empty list and <c>0</c> before the frame
    /// reaches a caller, so <c>Invited.Count</c> needs no guard.
    /// </remarks>
    public sealed class PartyFrame : LobbyServerFrame
    {
        internal PartyFrame(
            string? partyId,
            string leaderId,
            IReadOnlyList<PartyMember> members,
            IReadOnlyList<string> invited,
            int max,
            JsonValue raw)
            : base(FrameTypes.Party, raw)
        {
            PartyId = partyId;
            LeaderId = leaderId;
            Members = members;
            Invited = invited;
            Max = max;
        }

        /// <summary>The party, or null when the frame says "you are in no party".</summary>
        public string? PartyId { get; }

        /// <summary>The leader. Normalised to an empty string when the wire omitted it.</summary>
        public string LeaderId { get; }

        /// <summary>The roster.</summary>
        public IReadOnlyList<PartyMember> Members { get; }

        /// <summary>Pending invitations. Normalised to an empty list when the wire omitted it.</summary>
        public IReadOnlyList<string> Invited { get; }

        /// <summary>The channel's party size cap. Normalised to zero when the wire omitted it.</summary>
        public int Max { get; }
    }

    /// <summary>An invite delivered to the invitee.</summary>
    public sealed class PartyInviteFrame : LobbyServerFrame
    {
        internal PartyInviteFrame(string partyId, string from, JsonValue raw)
            : base(FrameTypes.PartyInvite, raw)
        {
            PartyId = partyId;
            From = from;
        }

        /// <summary>The party being offered.</summary>
        public string PartyId { get; }

        /// <summary>Who sent the invitation.</summary>
        public string From { get; }
    }

    /// <summary>Tells the leader an invite was refused.</summary>
    public sealed class PartyDeclinedFrame : LobbyServerFrame
    {
        internal PartyDeclinedFrame(string partyId, string userId, JsonValue raw)
            : base(FrameTypes.PartyDeclined, raw)
        {
            PartyId = partyId;
            UserId = userId;
        }

        /// <summary>The party whose invitation was declined.</summary>
        public string PartyId { get; }

        /// <summary>Who declined it.</summary>
        public string UserId { get; }
    }

    /// <summary>The answer to an application-level ping.</summary>
    public sealed class PongFrame : LobbyServerFrame
    {
        internal PongFrame(JsonValue raw)
            : base(FrameTypes.Pong, raw)
        {
        }
    }

    /// <summary>A typed refusal. Every refusal is a frame, never silence.</summary>
    public sealed class ErrorFrame : LobbyServerFrame
    {
        internal ErrorFrame(string code, string message, JsonValue raw)
            : base(FrameTypes.Error, raw)
        {
            Code = code;
            Message = message;
        }

        /// <summary>A documented refusal code; the set is open, so compare against <see cref="GatewayErrorCode"/> constants.</summary>
        public string Code { get; }

        /// <summary>The gateway's explanation. Never log it: it may quote what the client sent.</summary>
        public string Message { get; }
    }

    /// <summary>A frame this SDK does not model, delivered so a game can still read it.</summary>
    public sealed class UnknownServerFrame : LobbyServerFrame
    {
        internal UnknownServerFrame(string type, JsonValue raw)
            : base(type, raw)
        {
        }
    }
}
