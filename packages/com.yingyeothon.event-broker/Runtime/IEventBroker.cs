using System;
using System.Threading.Tasks;

namespace Yingyeothon.EventBroker
{
    /// <summary>The listener-facing surface of an event broker.</summary>
    /// <remarks>
    /// tslib keys handlers by a name in a TypeScript event map. C# has no mapped
    /// types, and a string-keyed broker would force <c>object</c> payloads and a cast
    /// per dispatch, so the payload type itself is the key here: registering for
    /// <c>TEvent</c> is registering for the event that carries a <c>TEvent</c>.
    /// </remarks>
    public interface IEventListenable
    {
        /// <summary>Listens to an event until it is removed with <c>Off</c>.</summary>
        IEventListenable On<TEvent>(Func<TEvent, Task> handler);

        /// <summary>Listens to an event with a synchronous handler.</summary>
        IEventListenable On<TEvent>(Action<TEvent> handler);

        /// <summary>Listens to an event once; the registration is removed before the handler runs.</summary>
        IEventListenable Once<TEvent>(Func<TEvent, Task> handler);

        /// <summary>Listens to an event once with a synchronous handler.</summary>
        IEventListenable Once<TEvent>(Action<TEvent> handler);

        /// <summary>Removes the first registration whose handler is this exact delegate.</summary>
        IEventListenable Off<TEvent>(Func<TEvent, Task> handler);

        /// <summary>Removes the first registration whose handler is this exact delegate.</summary>
        IEventListenable Off<TEvent>(Action<TEvent> handler);
    }

    /// <summary>A type-safe event broker with asynchronous handlers.</summary>
    public interface IEventBroker : IEventListenable
    {
        /// <summary>
        /// Fires an event into its handlers, in registration order, awaiting each
        /// before the next. Handlers are snapshotted before dispatch, so one
        /// registered during dispatch runs from the next call. A handler that faults
        /// faults this call and the remaining handlers do not run.
        /// </summary>
        /// <returns>Whether at least one handler was registered for this event type.</returns>
        Task<bool> FireAsync<TEvent>(TEvent value);
    }
}
