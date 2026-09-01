using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>
    /// The channel's capability object, forwarded verbatim in <c>hello</c>.
    /// </summary>
    /// <remarks>
    /// Every field is nullable and null means "unrestricted", not "disabled". Only an
    /// explicit <c>false</c> disables a capability, and only a present, non-null
    /// <c>say</c> list restricts scopes — the gateway marshals a nil <c>[]string</c>
    /// as JSON <c>null</c> (the Go field carries no <c>omitempty</c>), so folding null
    /// into an empty list would refuse every chat message.
    /// </remarks>
    public sealed class Capabilities
    {
        public Capabilities(bool? pos, IReadOnlyList<string>? say, bool? party, bool? channelEvent, bool? debug)
        {
            Pos = pos;
            Say = say;
            Party = party;
            Event = channelEvent;
            Debug = debug;
        }

        public bool? Pos { get; }

        /// <summary>Allowed say/event scopes, or null when the channel restricts none.</summary>
        public IReadOnlyList<string>? Say { get; }

        public bool? Party { get; }

        public bool? Event { get; }

        public bool? Debug { get; }

        internal static Capabilities FromJson(JsonValue? value)
        {
            if (value == null || value.Kind != JsonKind.Object)
            {
                return new Capabilities(null, null, null, null, null);
            }

            IReadOnlyList<string>? say = null;
            if (value.TryGetMember("say", out var sayValue) && sayValue.Kind == JsonKind.Array)
            {
                var scopes = new List<string>();
                foreach (var item in sayValue.AsArray())
                {
                    if (item.Kind == JsonKind.String)
                    {
                        scopes.Add(item.AsString());
                    }
                }

                say = scopes;
            }

            return new Capabilities(
                value.GetBool("pos"),
                say,
                value.GetBool("party"),
                value.GetBool("event"),
                value.GetBool("debug"));
        }

        /// <summary>Whether this scope may be used, given what the channel allows.</summary>
        public bool AllowsScope(SayScope scope)
        {
            if (Say == null)
            {
                return true;
            }

            var wire = SayScopes.ToWire(scope);
            foreach (var allowed in Say)
            {
                if (string.Equals(allowed, wire, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
