using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.EventBroker.Tests
{
    public sealed class DataEvent
    {
        public DataEvent(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    public sealed class OtherEvent
    {
    }

    [TestFixture]
    public class EventBrokerTests
    {
        [Test]
        public async Task FireReportsWhetherAnyHandlerWasRegistered()
        {
            var broker = EventBroker.Create();

            Assert.That(await broker.FireAsync(new DataEvent("a")), Is.False);

            broker.On<DataEvent>(_ => { });

            Assert.That(await broker.FireAsync(new DataEvent("a")), Is.True);
        }

        [Test]
        public async Task HandlersRunInRegistrationOrder()
        {
            var order = new List<string>();
            var broker = EventBroker.Create();
            broker.On<DataEvent>(e => order.Add("first:" + e.Text));
            broker.On<DataEvent>(e => order.Add("second:" + e.Text));

            await broker.FireAsync(new DataEvent("x"));

            Assert.That(order, Is.EqualTo(new[] { "first:x", "second:x" }));
        }

        [Test]
        public async Task AsynchronousHandlersAreAwaitedOneAtATime()
        {
            var order = new List<string>();
            var broker = EventBroker.Create();
            broker.On<DataEvent>(async _ =>
            {
                order.Add("first:enter");
                await Task.Yield();
                order.Add("first:leave");
            });
            broker.On<DataEvent>(_ =>
            {
                order.Add("second");
                return Task.CompletedTask;
            });

            await broker.FireAsync(new DataEvent("x"));

            Assert.That(order, Is.EqualTo(new[] { "first:enter", "first:leave", "second" }));
        }

        [Test]
        public async Task EventTypesAreIndependent()
        {
            var seen = new List<string>();
            var broker = EventBroker.Create();
            broker.On<DataEvent>(_ => seen.Add("data"));
            broker.On<OtherEvent>(_ => seen.Add("other"));

            await broker.FireAsync(new OtherEvent());

            Assert.That(seen, Is.EqualTo(new[] { "other" }));
        }

        [Test]
        public async Task OnceRunsOnlyOnceAndIsRemovedEvenWhenItThrows()
        {
            var calls = 0;
            var broker = EventBroker.Create();
            broker.Once<DataEvent>(_ =>
            {
                calls++;
                throw new InvalidOperationException("boom");
            });

            Assert.ThrowsAsync<InvalidOperationException>(async () => await broker.FireAsync(new DataEvent("x")));
            Assert.That(await broker.FireAsync(new DataEvent("x")), Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task OffBeforeAFireStopsAOnceHandlerFromEverRunning()
        {
            var calls = 0;
            Action<DataEvent> handler = _ => calls++;
            var broker = EventBroker.Create();
            broker.Once(handler);
            broker.Off(handler);

            Assert.That(await broker.FireAsync(new DataEvent("x")), Is.False);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public async Task OffRemovesOnlyTheFirstMatchingRegistration()
        {
            var calls = 0;
            Action<DataEvent> handler = _ => calls++;
            var broker = EventBroker.Create();
            broker.On(handler).On(handler).Off(handler);

            await broker.FireAsync(new DataEvent("x"));

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void OffIsANoOpForAnUnknownHandlerOrEventType()
        {
            var broker = EventBroker.Create();

            Assert.DoesNotThrow(() => broker.Off<DataEvent>(_ => { }));
            Assert.DoesNotThrow(() => broker.Off<OtherEvent>(_ => Task.CompletedTask));
        }

        [Test]
        public async Task AHandlerAddedDuringDispatchWaitsForTheNextFire()
        {
            var seen = new List<string>();
            var broker = EventBroker.Create();
            broker.On<DataEvent>(_ =>
            {
                seen.Add("first");
                broker.On<DataEvent>(__ => seen.Add("late"));
            });

            await broker.FireAsync(new DataEvent("x"));

            Assert.That(seen, Is.EqualTo(new[] { "first" }));

            await broker.FireAsync(new DataEvent("y"));

            // The second fire snapshots [first, late], so the late handler runs
            // exactly once here and the first one registers yet another.
            Assert.That(seen, Is.EqualTo(new[] { "first", "first", "late" }));
        }

        [Test]
        public async Task RemovingAHandlerDuringDispatchLeavesTheCurrentDispatchUnchanged()
        {
            var seen = new List<string>();
            var broker = EventBroker.Create();
            Action<DataEvent> second = _ => seen.Add("second");
            broker.On<DataEvent>(_ =>
            {
                seen.Add("first");
                broker.Off(second);
            });
            broker.On(second);

            await broker.FireAsync(new DataEvent("x"));

            Assert.That(seen, Is.EqualTo(new[] { "first", "second" }));

            seen.Clear();
            await broker.FireAsync(new DataEvent("y"));

            Assert.That(seen, Is.EqualTo(new[] { "first" }));
        }

        [Test]
        public void AThrowingHandlerFaultsTheFireAndSkipsTheRest()
        {
            var seen = new List<string>();
            var broker = EventBroker.Create();
            broker.On<DataEvent>(_ => seen.Add("before"));
            broker.On<DataEvent>(_ => throw new InvalidOperationException("boom"));
            broker.On<DataEvent>(_ => seen.Add("after"));

            var error = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await broker.FireAsync(new DataEvent("x")));

            Assert.That(error!.Message, Is.EqualTo("boom"));
            Assert.That(seen, Is.EqualTo(new[] { "before" }));
        }

        [Test]
        public void AFaultedAsyncHandlerFaultsTheFireToo()
        {
            var broker = EventBroker.Create();
            broker.On<DataEvent>(_ => Task.FromException(new TimeoutException("late")));

            Assert.ThrowsAsync<TimeoutException>(async () => await broker.FireAsync(new DataEvent("x")));
        }

        [Test]
        public void RegistrationIsFluentAndReturnsTheSameBroker()
        {
            var broker = EventBroker.Create();

            IEventListenable returned = broker.On<DataEvent>(_ => { });

            Assert.That(returned, Is.SameAs(broker));
            Assert.That(broker.Once<DataEvent>(_ => { }), Is.SameAs(broker));
            Assert.That(broker.Off<DataEvent>(_ => { }), Is.SameAs(broker));
        }

        [Test]
        public void RegisteringANullHandlerIsRefused()
        {
            var broker = EventBroker.Create();

            Assert.Throws<ArgumentNullException>(() => broker.On((Action<DataEvent>)null!));
            Assert.Throws<ArgumentNullException>(() => broker.On((Func<DataEvent, Task>)null!));
            Assert.Throws<ArgumentNullException>(() => broker.Off((Action<DataEvent>)null!));
        }

        // ---- no thread but the caller's --------------------------------------------------

        /// <summary>
        /// A context the test pumps itself, counting every post that arrives from another
        /// thread: dotnet has a thread pool to finish work the caller never pumps, a WebGL
        /// player has none, so work that leaves the caller's thread is exactly what hangs there.
        /// </summary>
        private sealed class PumpedContext : SynchronizationContext
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback, object?)> _queue =
                new System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback, object?)>();

            private readonly int _thread = Environment.CurrentManagedThreadId;

            internal int ForeignPosts;

            public override void Post(SendOrPostCallback d, object? state)
            {
                if (Environment.CurrentManagedThreadId != _thread)
                {
                    Interlocked.Increment(ref ForeignPosts);
                }

                _queue.Enqueue((d, state));
            }

            internal void Pump()
            {
                while (_queue.TryDequeue(out var item))
                {
                    item.Item1(item.Item2);
                }
            }
        }

        private static void WithContext(PumpedContext context, Action body)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                body();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        /// <remarks>
        /// With <c>ConfigureAwait(false)</c> the fire loop went on to the next handler on
        /// the thread pool either way — the runtime does not inline such a continuation
        /// under a synchronization context — so off Unity's main thread on a native player,
        /// and never at all in a WebGL player, which has no pool.
        /// </remarks>
        [TestCase(false)]
        [TestCase(true)]
        public void AHandlerThatFinishesLaterIsFollowedOnTheCallersContext(bool runContinuationsAsynchronously)
        {
            var broker = EventBroker.Create();
            var first = new TaskCompletionSource<bool>(
                runContinuationsAsynchronously ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
            var secondRanOn = 0;
            broker.On<DataEvent>(_ => first.Task);
            broker.On<DataEvent>(_ => { secondRanOn = Environment.CurrentManagedThreadId; });
            var context = new PumpedContext();
            var caller = Environment.CurrentManagedThreadId;

            WithContext(context, () =>
            {
                var fire = broker.FireAsync(new DataEvent("a"));
                first.SetResult(true);
                for (var i = 0; i < 200 && !fire.IsCompleted; i++)
                {
                    context.Pump();
                    Thread.Sleep(1); // time for a thread pool, if the loop went to one
                }

                Assert.That(fire.IsCompleted, Is.True);
                Assert.That(fire.Result, Is.True);
            });

            Assert.That(secondRanOn, Is.EqualTo(caller));
            Assert.That(context.ForeignPosts, Is.EqualTo(0));
        }
    }
}
