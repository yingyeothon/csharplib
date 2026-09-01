using System.Collections.Generic;
using NUnit.Framework;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class BackoffTests
    {
        [Test]
        public void DelaysDoubleUpToTheCap()
        {
            var backoff = Backoff.Create(new BackoffOptions
            {
                InitialMs = 500,
                MaxMs = 3000,
                Jitter = 0,
                Random = () => 0,
            });

            var delays = new List<double?>();
            for (var i = 0; i < 4; i++)
            {
                delays.Add(backoff.Next());
            }

            Assert.That(delays, Is.EqualTo(new double?[] { 500, 1000, 2000, 3000 }));
            Assert.That(backoff.Attempts, Is.EqualTo(4));
        }

        [Test]
        public void JitterSpansTheFractionOnBothSides()
        {
            var low = Backoff.Create(new BackoffOptions { InitialMs = 1000, Jitter = 0.2, Random = () => 0 });
            var high = Backoff.Create(new BackoffOptions { InitialMs = 1000, Jitter = 0.2, Random = () => 0.999999 });
            var mid = Backoff.Create(new BackoffOptions { InitialMs = 1000, Jitter = 0.2, Random = () => 0.5 });

            Assert.That(low.Next(), Is.EqualTo(800d));
            Assert.That(high.Next(), Is.EqualTo(1200d).Within(1d));
            Assert.That(mid.Next(), Is.EqualTo(1000d));
        }

        [Test]
        public void ResetRestartsTheSequence()
        {
            var backoff = Backoff.Create(new BackoffOptions { InitialMs = 500, Jitter = 0, Random = () => 0 });

            backoff.Next();
            backoff.Next();
            backoff.Reset();

            Assert.That(backoff.Attempts, Is.Zero);
            Assert.That(backoff.Next(), Is.EqualTo(500d));
        }

        [Test]
        public void MaxAttemptsExhaustsIntoNullAndPinsTheCount()
        {
            var backoff = Backoff.Create(new BackoffOptions
            {
                InitialMs = 500,
                Jitter = 0,
                MaxAttempts = 2,
                Random = () => 0,
            });

            Assert.That(backoff.Next(), Is.EqualTo(500d));
            Assert.That(backoff.Next(), Is.EqualTo(1000d));
            Assert.That(backoff.Next(), Is.Null);
            Assert.That(backoff.Next(), Is.Null);
            Assert.That(backoff.Attempts, Is.EqualTo(2));
        }

        [Test]
        public void TheDefaultScheduleIsUnboundedAndStartsAtHalfASecond()
        {
            var backoff = Backoff.Create();

            var first = backoff.Next();

            Assert.That(first, Is.InRange(400d, 600d));
            for (var i = 0; i < 50; i++)
            {
                Assert.That(backoff.Next(), Is.Not.Null);
            }
        }

        [Test]
        public void TwoDefaultSchedulesDoNotShareARandomSource()
        {
            // Two clients reconnecting after the same gateway restart must not pick
            // the identical delay, or they stampede it together.
            var same = 0;
            for (var i = 0; i < 20; i++)
            {
                if (Backoff.Create().Next() == Backoff.Create().Next())
                {
                    same++;
                }
            }

            Assert.That(same, Is.LessThan(20));
        }
    }
}
