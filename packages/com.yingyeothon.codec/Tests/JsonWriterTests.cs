using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>Writer concerns that are not about a single value's text.</summary>
    [TestFixture]
    public class JsonWriterTests
    {
        [Test]
        public void ANullReferenceIsNotAJsonNull()
        {
            Assert.Throws<ArgumentNullException>(() => Json.Stringify(null!));
            Assert.Throws<ArgumentNullException>(() => JsonWriter.Write(null!));
            Assert.That(Json.Stringify(JsonValue.Null), Is.EqualTo("null"));
        }

        [Test]
        public void TheSharedBuilderIsPerThreadSoConcurrentWritersDoNotInterleave()
        {
            // The writer keeps a [ThreadStatic] StringBuilder. If that ever becomes a
            // plain static, this is the test that notices, and it notices as garbled
            // output rather than as a crash.
            const int Writers = 8;
            const int Rounds = 200;
            var failures = new List<string>();
            var barrier = new Barrier(Writers);

            Parallel.For(0, Writers, index =>
            {
                var value = Json.Object()
                    .Set("writer", (double)index)
                    .Set("payload", JsonValue.Of(new string((char)('a' + index), 512)))
                    .Build();
                var expected = Json.Stringify(value);

                barrier.SignalAndWait();
                for (var round = 0; round < Rounds; round++)
                {
                    var actual = Json.Stringify(value);
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        lock (failures)
                        {
                            failures.Add("writer " + index + " round " + round);
                        }

                        return;
                    }
                }
            });

            Assert.That(failures, Is.Empty);
        }

        [Test]
        public void AHugeDocumentDoesNotLeaveTheNextWriteHoldingItsBuffer()
        {
            // The writer drops its cached builder once it has grown past 64 KiB. The
            // write after that one is the one that would break if the drop were wrong.
            var items = new List<JsonValue>();
            for (var i = 0; i < 40000; i++)
            {
                items.Add(JsonValue.Of("0123456789"));
            }

            var huge = Json.Stringify(JsonValue.Array(items));
            Assert.That(huge.Length, Is.GreaterThan(64 * 1024));

            Assert.That(Json.Stringify(Json.Object().Set("type", "pong").Build()), Is.EqualTo("{\"type\":\"pong\"}"));
            Assert.That(Json.Parse(huge).AsArray().Count, Is.EqualTo(40000));
        }

        [Test]
        public void ToStringIsTheSameTextAsStringify()
        {
            var value = Json.Parse("{\"a\":[1,\"x\",null,true]}");

            Assert.That(value.ToString(), Is.EqualTo(Json.Stringify(value)));
        }
    }
}
