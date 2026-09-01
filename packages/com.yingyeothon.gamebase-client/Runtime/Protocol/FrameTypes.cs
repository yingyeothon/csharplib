using System.Collections.Generic;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Frame type names, matching the gateway's own constants.</summary>
    public static class FrameTypes
    {
        /// <summary>The gateway's first frame on a lobby channel. Nothing is connected before it.</summary>
        public const string Hello = "hello";
        /// <summary>Every retained peer in a zone, sent to a newcomer instead of one <c>enter</c> each.</summary>
        public const string Snapshot = "snapshot";
        /// <summary>A peer became visible in the zone. Synthesised by the gateway.</summary>
        public const string Enter = "enter";
        /// <summary>A peer stopped being visible in the zone. Synthesised by the gateway.</summary>
        public const string Leave = "leave";
        /// <summary>A position: outbound one player's, inbound a batch coalesced per tick.</summary>
        public const string Pos = "pos";
        /// <summary>Chat, routed by scope.</summary>
        public const string Say = "say";
        /// <summary>A game-defined event, routed by scope. The gateway never reads its payload.</summary>
        public const string Event = "event";
        /// <summary>A party roster snapshot.</summary>
        public const string Party = "party";
        /// <summary>Create a party with the sender as its leader.</summary>
        public const string PartyCreate = "party.create";
        /// <summary>Outbound, invite a user; inbound, an invitation arrived.</summary>
        public const string PartyInvite = "party.invite";
        /// <summary>Accept an invitation.</summary>
        public const string PartyAccept = "party.accept";
        /// <summary>Decline an invitation.</summary>
        public const string PartyDecline = "party.decline";
        /// <summary>Leave the current party.</summary>
        public const string PartyLeave = "party.leave";
        /// <summary>Ask for the current roster.</summary>
        public const string PartyList = "party.list";
        /// <summary>Someone declined an invitation to the sender's party.</summary>
        public const string PartyDeclined = "party.declined";
        /// <summary>An application-level liveness probe.</summary>
        public const string Ping = "ping";
        /// <summary>The gateway's answer to a ping.</summary>
        public const string Pong = "pong";
        /// <summary>A refusal of something the client sent.</summary>
        public const string Error = "error";

        /// <summary>
        /// Types the gateway synthesises itself and refuses from a client. It decides
        /// which member a connection speaks for, so a client must never send one.
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedGameFrameTypes = new[] { Enter, Leave };
    }

    /// <summary>Documented gateway refusal codes. The set is open; do not close it into an enum.</summary>
    public static class GatewayErrorCode
    {
        /// <summary>The frame did not parse, a field had the wrong type, or an event <c>name</c> was outside 1..64 bytes. A numeric <c>dir</c> or a comma-decimal number reaches the gateway this way.</summary>
        public const string BadMessage = "bad_message";
        /// <summary>The channel disables that command.</summary>
        public const string CapabilityOff = "capability_off";
        /// <summary>Over the channel's per-connection message rate.</summary>
        public const string RateLimited = "rate_limited";
        /// <summary>The scope is not one this channel allows.</summary>
        public const string BadScope = "bad_scope";
        /// <summary>The zone name is malformed or over 64 bytes.</summary>
        public const string BadZone = "bad_zone";
        /// <summary>The position moved further in one frame than the channel's <c>maxMoveDelta</c>.</summary>
        public const string MoveTooFar = "move_too_far";
        /// <summary>No such user on this channel.</summary>
        public const string UnknownUser = "unknown_user";
        /// <summary>The command needs a party and the sender is in none.</summary>
        public const string NoParty = "no_party";
        /// <summary>The sender is already in a party.</summary>
        public const string AlreadyInParty = "already_in_party";
        /// <summary>The party is at the channel's <c>partySizeMax</c>.</summary>
        public const string PartyFull = "party_full";
        /// <summary>Accepting a party the sender was not invited to.</summary>
        public const string NotInvited = "not_invited";
        /// <summary>No such party.</summary>
        public const string UnknownParty = "unknown_party";
        /// <summary>The command is the party leader's to make.</summary>
        public const string NotLeader = "not_leader";
        /// <summary>A field is over its byte cap: <c>text</c> 1024 or <c>payload</c> 8192. An out-of-range <c>name</c> is <c>bad_message</c> instead.</summary>
        public const string TooLong = "too_long";
        /// <summary>A <c>q</c> frame used <c>enter</c> or <c>leave</c>, which are the gateway's own.</summary>
        public const string ReservedType = "reserved_type";
        /// <summary>The gateway could not serve the request right now.</summary>
        public const string Unavailable = "unavailable";
    }

    /// <summary>Where a <c>say</c> or <c>event</c> is routed.</summary>
    public enum SayScope
    {
        /// <summary>Everyone in the sender's current zone.</summary>
        Zone,
        /// <summary>The sender's party.</summary>
        Party,
        /// <summary>One user, named by the <c>to</c> argument, across zones.</summary>
        User,
    }

    /// <summary>Wire spellings for <see cref="SayScope"/>.</summary>
    public static class SayScopes
    {
        /// <summary>Everyone in the sender's current zone.</summary>
        public const string Zone = "zone";
        /// <summary>The sender's party.</summary>
        public const string Party = "party";
        /// <summary>One user, named by <c>to</c>, across zones.</summary>
        public const string User = "user";

        /// <summary>The wire spelling of a scope.</summary>
        public static string ToWire(SayScope scope)
        {
            switch (scope)
            {
                case SayScope.Party:
                    return Party;
                case SayScope.User:
                    return User;
                default:
                    return Zone;
            }
        }

        /// <summary>Reads a wire scope back, answering false for one this SDK does not know.</summary>
        public static bool TryParse(string? wire, out SayScope scope)
        {
            switch (wire)
            {
                case Zone:
                    scope = SayScope.Zone;
                    return true;
                case Party:
                    scope = SayScope.Party;
                    return true;
                case User:
                    scope = SayScope.User;
                    return true;
                default:
                    scope = SayScope.Zone;
                    return false;
            }
        }
    }
}
