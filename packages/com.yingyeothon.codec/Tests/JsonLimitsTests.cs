using System;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// The two bounds that keep a hostile or merely enormous document from taking
    /// the game's main thread down with it: nesting depth and input length.
    /// </summary>
    [TestFixture]
    public class JsonLimitsTests
    {
        private const int MaxDepth = 64;

        /// <summary><paramref name="depth"/> nested arrays, the innermost one empty.</summary>
        private static string NestArrays(int depth) => new string('[', depth) + new string(']', depth);

        /// <summary><paramref name="depth"/> nested objects, the innermost one empty.</summary>
        private static string NestObjects(int depth)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < depth - 1; i++)
            {
                builder.Append("{\"a\":");
            }

            return builder.Append("{}").Append(new string('}', depth - 1)).ToString();
        }

        [Test]
        public void NestingExactlyAtTheDepthLimitIsAccepted()
        {
            Assert.That(Json.TryParse(NestArrays(MaxDepth), out _, out var arrays), Is.True, arrays.ToString());
            Assert.That(Json.TryParse(NestObjects(MaxDepth), out _, out var objects), Is.True, objects.ToString());
        }

        [Test]
        public void OneLevelPastTheDepthLimitIsRefused()
        {
            Assert.That(Json.TryParse(NestArrays(MaxDepth + 1), out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.DepthExceeded));
            Assert.That(failure.Index, Is.EqualTo(MaxDepth));

            Assert.That(Json.TryParse(NestObjects(MaxDepth + 1), out _, out var objects), Is.False);
            Assert.That(objects.Error, Is.EqualTo(JsonParseError.DepthExceeded));
        }

        [Test]
        public void AnAlternatingNestIsCountedTheSameWay()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < MaxDepth + 1; i++)
            {
                builder.Append(i % 2 == 0 ? "[" : "{\"a\":");
            }

            Assert.That(Json.TryParse(builder.ToString(), out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.DepthExceeded));
        }

        [Test]
        public void AbsurdNestingIsRefusedWithoutOverflowingTheStack()
        {
            var deep = new string('[', 100_000) + new string(']', 100_000);

            Assert.That(Json.TryParse(deep, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.DepthExceeded));
        }

        [Test]
        public void TheWriterRefusesWhatTheParserWouldRefuseRatherThanOverflowingTheStack()
        {
            // A value tree is not always something that came off the wire: a caller
            // can build one, and JsonValue puts no bound on how deep. Writing it
            // recurses, and a StackOverflow is not catchable.
            JsonValue legal = JsonValue.Array(new JsonValue[0]);
            for (var i = 1; i < MaxDepth; i++)
            {
                legal = JsonValue.ArrayOf(legal);
            }

            Assert.That(Json.Stringify(legal), Is.EqualTo(NestArrays(MaxDepth)));

            var tooDeep = JsonValue.ArrayOf(legal);
            Assert.Throws<ArgumentException>(() => Json.Stringify(tooDeep));

            var absurd = JsonValue.Array(new JsonValue[0]);
            for (var i = 0; i < 100_000; i++)
            {
                absurd = JsonValue.ArrayOf(absurd);
            }

            Assert.Throws<ArgumentException>(() => Json.Stringify(absurd));
        }

        [Test]
        public void ALeafInsideTheDeepestLegalContainerIsStillWritable()
        {
            // The bound counts containers, not values. Getting this wrong refuses a
            // perfectly legal document at exactly the limit.
            JsonValue value = JsonValue.ArrayOf(JsonValue.Of("leaf"));
            for (var i = 1; i < MaxDepth; i++)
            {
                value = JsonValue.ArrayOf(value);
            }

            var text = Json.Stringify(value);

            Assert.That(text, Is.EqualTo(new string('[', MaxDepth) + "\"leaf\"" + new string(']', MaxDepth)));
            Assert.That(Json.Parse(text), Is.EqualTo(value));
        }

        [Test]
        public void TheDefaultAndCeilingLengthsArePinned()
        {
            // These are part of the contract: a consumer sizes its receive buffer
            // against them, so a change here is a change to the SDK's promises.
            Assert.That(Json.MaxLength, Is.EqualTo(1024 * 1024));
            Assert.That(Json.MaxBigLength, Is.EqualTo(64 * 1024 * 1024));
            Assert.That(Json.MaxDepth, Is.EqualTo(MaxDepth));
        }

        [Test]
        public void InputExactlyAtTheDefaultLimitIsAccepted()
        {
            var text = "\"" + new string('a', Json.MaxLength - 2) + "\"";

            Assert.That(text.Length, Is.EqualTo(Json.MaxLength));
            Assert.That(Json.TryParse(text, out var value, out var failure), Is.True, failure.ToString());
            Assert.That(value.AsString().Length, Is.EqualTo(Json.MaxLength - 2));
        }

        [Test]
        public void InputOneCharacterPastTheDefaultLimitIsRefused()
        {
            var text = "\"" + new string('a', Json.MaxLength - 1) + "\"";

            Assert.That(text.Length, Is.EqualTo(Json.MaxLength + 1));
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.InputTooLong));
        }

        [Test]
        public void TheLengthCheckHappensBeforeAnyScanning()
        {
            // Both limits are broken here. If the reported reason were DepthExceeded
            // the parser would have walked a megabyte of hostile input first, which
            // is exactly what the cap exists to prevent.
            var text = new string('[', Json.MaxLength + 1);

            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.InputTooLong));
        }

        [Test]
        public void AHugeArrayIsRefusedByDefaultAndAcceptedOnlyWhenTheCallerOptsIn()
        {
            const int Items = 600_000;
            var builder = new StringBuilder("[");
            for (var i = 0; i < Items; i++)
            {
                builder.Append(i == 0 ? "1" : ",1");
            }

            var text = builder.Append(']').ToString();
            Assert.That(text.Length, Is.GreaterThan(Json.MaxLength));

            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.InputTooLong));

            Assert.That(Json.TryParseBig(text, 8 * 1024 * 1024, out var value, out var big), Is.True, big.ToString());
            Assert.That(value.AsArray().Count, Is.EqualTo(Items));
        }

        [Test]
        public void ParseBigStillEnforcesTheLimitItWasGiven()
        {
            var text = "[" + new string('1', 100) + "]";

            Assert.That(Json.TryParseBig(text, text.Length, out _, out var atLimit), Is.True, atLimit.ToString());
            Assert.That(Json.TryParseBig(text, text.Length - 1, out _, out var past), Is.False);
            Assert.That(past.Error, Is.EqualTo(JsonParseError.InputTooLong));
            Assert.That(Assert.Throws<JsonParseException>(() => Json.ParseBig(text, 1))!.Error,
                Is.EqualTo(JsonParseError.InputTooLong));
        }

        [TestCase(-1)]
        [TestCase(0)]
        public void ParseBigRefusesANonsensicalLimit(int maxLength)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.ParseBig("1", maxLength));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.TryParseBig("1", maxLength, out _, out _));
        }

        [Test]
        public void ParseBigRefusesALimitAboveTheCeiling()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.ParseBig("1", Json.MaxBigLength + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Json.TryParseBig("1", int.MaxValue, out _, out _));
        }

        [Test]
        public void ParseBigIsOtherwiseTheSameParser()
        {
            Assert.That(Json.ParseBig("{\"a\":[1,null]}", Json.MaxBigLength), Is.EqualTo(Json.Parse("{\"a\":[1,null]}")));
            Assert.That(Json.TryParseBig("{", Json.MaxBigLength, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.ExpectedKey));
        }
    }
}
