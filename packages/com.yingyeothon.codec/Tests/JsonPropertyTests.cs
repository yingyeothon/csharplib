using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// Properties that must hold for every value, checked over a generated corpus.
    /// </summary>
    /// <remarks>
    /// The seed is fixed. A test that produces a different corpus on every run makes
    /// a failure unreproducible, and this repository has no ambient state anywhere
    /// else either. Widen the corpus by adding a seed, not by reading the clock.
    /// </remarks>
    [TestFixture]
    public class JsonPropertyTests
    {
        private const int MaxNesting = 6;

        private static readonly char[] StringAlphabet =
        {
            'a', 'b', 'c', 'd', 'e', 'f', ' ', '"', '\\', '/',
            '\n', '\t', '\u0000', '\u0001', '\u001F', '\u007F',
            '\u00E9', '\uD55C', '\u2028', '\uFEFF',
            '\uD83D', '\uDE00',          // a pair, when they land in that order
            '\uD800', '\uDBFF', '\uDFFF' // and unpaired halves when they do not
        };

        private static string NextString(Random random)
        {
            var length = random.Next(0, 12);
            var builder = new StringBuilder(length);
            for (var i = 0; i < length; i++)
            {
                builder.Append(StringAlphabet[random.Next(StringAlphabet.Length)]);
            }

            return builder.ToString();
        }

        private static double NextNumber(Random random)
        {
            switch (random.Next(6))
            {
                case 0: return 0d;
                case 1: return random.Next(-1000, 1000);
                case 2: return random.NextDouble();
                case 3: return -random.NextDouble() * 1e9;
                case 4: return random.NextDouble() * 1e-15;
                default: return random.Next(-1000, 1000) + random.NextDouble();
            }
        }

        private static JsonValue NextValue(Random random, int depth)
        {
            switch (random.Next(depth >= MaxNesting ? 4 : 6))
            {
                case 0:
                    return JsonValue.Null;
                case 1:
                    return JsonValue.Of(random.Next(2) == 0);
                case 2:
                    return JsonValue.Of(NextNumber(random));
                case 3:
                    return JsonValue.Of(NextString(random));
                case 4:
                    {
                        var items = new List<JsonValue>();
                        var count = random.Next(0, 5);
                        for (var i = 0; i < count; i++)
                        {
                            items.Add(NextValue(random, depth + 1));
                        }

                        return JsonValue.Array(items);
                    }

                default:
                    {
                        var members = new List<KeyValuePair<string, JsonValue>>();
                        var count = random.Next(0, 5);
                        for (var i = 0; i < count; i++)
                        {
                            members.Add(new KeyValuePair<string, JsonValue>(
                                NextString(random) + i.ToString(CultureInfo.InvariantCulture),
                                NextValue(random, depth + 1)));
                        }

                        return JsonValue.Object(members);
                    }
            }
        }

        private static IEnumerable<JsonValue> Corpus(int seed, int count)
        {
            var random = new Random(seed);
            for (var i = 0; i < count; i++)
            {
                yield return NextValue(random, 0);
            }
        }

        [TestCase(1)]
        [TestCase(20260901)]
        public void AValueSurvivesAWriteAndReadUnchanged(int seed)
        {
            foreach (var value in Corpus(seed, 250))
            {
                var text = Json.Stringify(value);

                Assert.That(Json.Parse(text), Is.EqualTo(value));
                Assert.That(Json.Parse(text).GetHashCode(), Is.EqualTo(value.GetHashCode()));
            }
        }

        [TestCase(2)]
        [TestCase(20260902)]
        public void WritingIsIdempotent(int seed)
        {
            foreach (var value in Corpus(seed, 250))
            {
                var text = Json.Stringify(value);

                Assert.That(Json.Stringify(Json.Parse(text)), Is.EqualTo(text));
            }
        }

        [TestCase(3)]
        [TestCase(20260903)]
        public void EveryWrittenDocumentSurvivesAUtf8Encode(int seed)
        {
            // A WebSocket text frame is UTF-8 bytes. Anything the writer emits that
            // cannot be encoded and decoded back to itself is data the peer will not
            // receive - which is precisely what an unpaired surrogate written raw is.
            foreach (var value in Corpus(seed, 250))
            {
                var text = Json.Stringify(value);
                var wire = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));

                Assert.That(wire, Is.EqualTo(text));
                Assert.That(Json.Parse(wire), Is.EqualTo(value));
            }
        }

        [Test]
        public void ReorderingAnObjectsMembersChangesNeitherEqualityNorTheHash()
        {
            var compared = 0;
            foreach (var value in Corpus(4, 250))
            {
                if (value.Kind != JsonKind.Object || value.AsObject().Count < 2)
                {
                    continue;
                }

                var reversed = new List<KeyValuePair<string, JsonValue>>(value.AsObject());
                reversed.Reverse();
                var shuffled = JsonValue.Object(reversed);

                Assert.That(shuffled, Is.EqualTo(value));
                Assert.That(value, Is.EqualTo(shuffled));
                Assert.That(shuffled.GetHashCode(), Is.EqualTo(value.GetHashCode()));
                compared++;
            }

            // A loop that never ran would pass every assertion inside it.
            Assert.That(compared, Is.GreaterThan(10));
        }

        [Test]
        public void EveryGeneratedDocumentIsAcceptedByTheStrictParser()
        {
            // The writer must not be able to emit something this parser refuses.
            foreach (var value in Corpus(5, 250))
            {
                var text = Json.Stringify(value);

                Assert.That(Json.TryParse(text, out _, out var failure), Is.True, failure.ToString());
            }
        }
    }
}
