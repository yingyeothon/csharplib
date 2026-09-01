using System.Collections.Generic;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Frame type names, matching the gateway's own constants.</summary>
    public static class FrameTypes
    {
        public const string Hello = "hello";
        public const string Snapshot = "snapshot";
        public const string Enter = "enter";
        public const string Leave = "leave";
        public const string Pos = "pos";
        public const string Say = "say";
        public const string Event = "event";
        public const string Party = "party";
        public const string PartyCreate = "party.create";
        public const string PartyInvite = "party.invite";
        public const string PartyAccept = "party.accept";
        public const string PartyDecline = "party.decline";
        public const string PartyLeave = "party.leave";
        public const string PartyList = "party.list";
        public const string PartyDeclined = "party.declined";
        public const string Ping = "ping";
        public const string Pong = "pong";
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
        public const string BadMessage = "bad_message";
        public const string CapabilityOff = "capability_off";
        public const string RateLimited = "rate_limited";
        public const string BadScope = "bad_scope";
        public const string BadZone = "bad_zone";
        public const string MoveTooFar = "move_too_far";
        public const string UnknownUser = "unknown_user";
        public const string NoParty = "no_party";
        public const string AlreadyInParty = "already_in_party";
        public const string PartyFull = "party_full";
        public const string NotInvited = "not_invited";
        public const string UnknownParty = "unknown_party";
        public const string NotLeader = "not_leader";
        public const string TooLong = "too_long";
        public const string ReservedType = "reserved_type";
        public const string Unavailable = "unavailable";
    }

    /// <summary>Where a <c>say</c> or <c>event</c> is routed.</summary>
    public enum SayScope
    {
        Zone,
        Party,
        User,
    }

    /// <summary>Wire spellings for <see cref="SayScope"/>.</summary>
    public static class SayScopes
    {
        public const string Zone = "zone";
        public const string Party = "party";
        public const string User = "user";

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
