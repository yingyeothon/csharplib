# Yingyeothon.EventBroker

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.event-broker/README.md`](../../packages/com.yingyeothon.event-broker/README.md).

## Contents

- [`EventBroker`](#static-class-eventbroker)
- [`IEventBroker`](#interface-ieventbroker)
- [`IEventListenable`](#interface-ieventlistenable)

## static class EventBroker

Creates `IEventBroker` instances.

| Member | Summary |
| --- | --- |
| `Create() : IEventBroker` | Creates a broker. It is not thread-safe; drive it from one thread. |

## interface IEventBroker

A type-safe event broker with asynchronous handlers.

| Member | Summary |
| --- | --- |
| `FireAsync(TEvent) : Task<Boolean>` | Fires an event into its handlers, in registration order, awaiting each before the next. Handlers are snapshotted before dispatch, so one registered during dispatch runs from the next call. A handler that faults faults this call and the remaining handlers do not run. |

## interface IEventListenable

The listener-facing surface of an event broker.

| Member | Summary |
| --- | --- |
| `Off(Action<TEvent>) : IEventListenable` | Removes the first registration whose handler is this exact delegate. |
| `Off(Func<TEvent, Task>) : IEventListenable` | Removes the first registration whose handler is this exact delegate. |
| `On(Action<TEvent>) : IEventListenable` | Listens to an event with a synchronous handler. |
| `On(Func<TEvent, Task>) : IEventListenable` | Listens to an event until it is removed with `Off` . |
| `Once(Action<TEvent>) : IEventListenable` | Listens to an event once with a synchronous handler. |
| `Once(Func<TEvent, Task>) : IEventListenable` | Listens to an event once; the registration is removed before the handler runs. |
