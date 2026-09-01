using System.Threading.Tasks;
using Yingyeothon.EventBroker;

namespace Yingyeothon.EventBroker.Samples
{
    /// <summary>A player died. The payload type is the key, so a typo is a compile error.</summary>
    public sealed class PlayerDied
    {
        public PlayerDied(string userId)
        {
            UserId = userId;
        }

        /// <summary>Who died.</summary>
        public string UserId { get; }
    }

    /// <summary>The run ended.</summary>
    public sealed class RunEnded
    {
        public RunEnded(bool cleared)
        {
            Cleared = cleared;
        }

        /// <summary>Whether the party cleared it.</summary>
        public bool Cleared { get; }
    }

    /// <summary>Registering handlers and firing events.</summary>
    public static class TypedEvents
    {
        /// <summary>
        /// Handlers run in registration order, each awaited before the next, over a
        /// snapshot taken at fire time — so one registered during dispatch runs from
        /// the next fire, not this one.
        /// </summary>
        public static IEventBroker Wire(System.Action<string> onDeath, System.Func<RunEnded, Task> onEnd)
        {
            var broker = EventBroker.Create();

            // On/Once/Off return IEventListenable so they chain, which means the
            // broker itself is what you keep: only it can fire.
            broker.On<PlayerDied>(e => onDeath(e.UserId))
                  // A Once registration is removed BEFORE its handler runs, so it is
                  // gone even if the handler throws.
                  .Once<RunEnded>(onEnd);

            return broker;
        }

        /// <summary>Answers false when nothing was registered for that type.</summary>
        public static Task<bool> AnnounceDeathAsync(IEventBroker broker, string userId)
            => broker.FireAsync(new PlayerDied(userId));
    }
}
