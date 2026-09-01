using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Yingyeothon.EventBroker
{
    /// <summary>Creates <see cref="IEventBroker"/> instances.</summary>
    /// <remarks>A broker is not thread-safe; drive it from one thread, as tslib does.</remarks>
    public static class EventBroker
    {
        /// <summary>Creates a broker. It is not thread-safe; drive it from one thread.</summary>
        public static IEventBroker Create() => new EventBrokerImpl();

        private sealed class Registration<TEvent>
        {
            internal Registration(Func<TEvent, Task> handler, object identity, bool once)
            {
                Handler = handler;
                Identity = identity;
                Once = once;
            }

            internal Func<TEvent, Task> Handler { get; }

            /// <summary>
            /// The delegate the caller passed. A synchronous handler is wrapped for
            /// storage, so <c>Off</c> has to match on what was handed in, not on the
            /// wrapper.
            /// </summary>
            internal object Identity { get; }

            internal bool Once { get; }
        }

        private sealed class EventBrokerImpl : IEventBroker
        {
            private readonly Dictionary<Type, object> _byType = new Dictionary<Type, object>();

            public IEventListenable On<TEvent>(Func<TEvent, Task> handler) => Add(handler, handler, false);

            public IEventListenable On<TEvent>(Action<TEvent> handler) => Add(Wrap(handler), handler, false);

            public IEventListenable Once<TEvent>(Func<TEvent, Task> handler) => Add(handler, handler, true);

            public IEventListenable Once<TEvent>(Action<TEvent> handler) => Add(Wrap(handler), handler, true);

            public IEventListenable Off<TEvent>(Func<TEvent, Task> handler) => Remove<TEvent>(handler);

            public IEventListenable Off<TEvent>(Action<TEvent> handler) => Remove<TEvent>(handler);

            public async Task<bool> FireAsync<TEvent>(TEvent value)
            {
                if (!_byType.TryGetValue(typeof(TEvent), out var stored))
                {
                    return false;
                }

                var live = (List<Registration<TEvent>>)stored;
                if (live.Count == 0)
                {
                    return false;
                }

                // Iterate a snapshot so a handler registered during dispatch waits for
                // the next fire, while `once` removal and `Off` still mutate the live
                // list that the next fire will snapshot.
                var snapshot = live.ToArray();
                foreach (var registration in snapshot)
                {
                    if (registration.Once)
                    {
                        // Removed before the call, so a throwing handler is still gone.
                        live.Remove(registration);
                    }

                    await registration.Handler(value).ConfigureAwait(false);
                }

                return true;
            }

            private static Func<TEvent, Task> Wrap<TEvent>(Action<TEvent> handler)
            {
                if (handler == null)
                {
                    throw new ArgumentNullException(nameof(handler));
                }

                return value =>
                {
                    handler(value);
                    return Task.CompletedTask;
                };
            }

            private IEventListenable Add<TEvent>(Func<TEvent, Task> handler, object identity, bool once)
            {
                if (handler == null)
                {
                    throw new ArgumentNullException(nameof(handler));
                }

                if (!_byType.TryGetValue(typeof(TEvent), out var stored))
                {
                    stored = new List<Registration<TEvent>>();
                    _byType[typeof(TEvent)] = stored;
                }

                ((List<Registration<TEvent>>)stored).Add(new Registration<TEvent>(handler, identity, once));
                return this;
            }

            private IEventListenable Remove<TEvent>(object identity)
            {
                if (identity == null)
                {
                    throw new ArgumentNullException(nameof(identity));
                }

                if (!_byType.TryGetValue(typeof(TEvent), out var stored))
                {
                    return this;
                }

                var live = (List<Registration<TEvent>>)stored;
                for (var i = 0; i < live.Count; i++)
                {
                    if (Equals(live[i].Identity, identity))
                    {
                        live.RemoveAt(i);
                        return this;
                    }
                }

                return this;
            }
        }
    }
}
