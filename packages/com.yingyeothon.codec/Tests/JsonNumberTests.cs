using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// The number grammar, the number model and the exact bytes a number puts on
    /// the wire.
    /// </summary>
    /// <remarks>
    /// The output format is pinned character for character on purpose. It is valid
    /// JSON but it is not byte-identical to <c>JSON.stringify</c>, and a future
    /// "cleanup" that changes it changes what every gateway and every replay log
    /// sees. If one of these has to change, changing it must be a decision.
    /// </remarks>
    [TestFixture]
    public class JsonNumberTests
    {
        private static JsonParseFailure Refused(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            return failure;
        }

        [TestCase("+1")]
        [TestCase(".5")]
        [TestCase("-.5")]
        [TestCase("--1")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        public void SomethingThatIsNotANumberIsNotAValue(string text)
        {
            Assert.That(Refused(text).Error, Is.EqualTo(JsonParseError.ExpectedValue));
        }

        [Test]
        public void ABareMinusSignRunsOutOfInput()
        {
            var failure = Refused("-");

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnexpectedEndOfInput));
            Assert.That(failure.Index, Is.EqualTo(1));
        }

        [TestCase("01")]
        [TestCase("-01")]
        [TestCase("007")]
        [TestCase("0x10")]
        [TestCase("1_000")]
        [TestCase("1d")]
        [TestCase("1f")]
        [TestCase("1e1e1")]
        public void ALeadingZeroOrATrailingSuffixLeavesUnparsedInput(string text)
        {
            // The number itself is legal as far as it goes; what refuses these is
            // the "nothing but whitespace after the value" rule.
            Assert.That(Refused(text).Error, Is.EqualTo(JsonParseError.TrailingContent));
        }

        [TestCase("1.", 2)]
        [TestCase("1..2", 2)]
        [TestCase("1.e5", 2)]
        [TestCase("-0.", 3)]
        public void ADecimalPointNeedsADigitAfterIt(string text, int index)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.ExpectedFractionDigit));
            Assert.That(failure.Index, Is.EqualTo(index));
        }

        [TestCase("1e", 2)]
        [TestCase("1E", 2)]
        [TestCase("1e+", 3)]
        [TestCase("1e-", 3)]
        [TestCase("1.5E", 4)]
        public void AnExponentNeedsADigit(string text, int index)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.ExpectedExponentDigit));
            Assert.That(failure.Index, Is.EqualTo(index));
        }

        [TestCase("0", 0d)]
        [TestCase("0e0", 0d)]
        [TestCase("0E0", 0d)]
        [TestCase("1e+0", 1d)]
        [TestCase("1E-0", 1d)]
        [TestCase("1E2", 100d)]
        [TestCase("1e-7", 1e-7)]
        [TestCase("-1.25", -1.25)]
        [TestCase("1.7976931348623157e308", double.MaxValue)]
        [TestCase("5e-324", double.Epsilon)]
        [TestCase("1e-320", 1e-320)]
        public void ALegalNumberIsRead(string text, double expected)
        {
            Assert.That(Json.Parse(text).AsNumber(), Is.EqualTo(expected));
        }

        [TestCase("1e400")]
        [TestCase("-1e400")]
        [TestCase("1e999999")]
        public void ANumberTooLargeForADoubleIsRefusedRatherThanBecomingInfinity(string text)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.NumberOutOfRange));
            Assert.That(failure.Index, Is.EqualTo(0));
        }

        [Test]
        public void AnAbsurdlyLongNumericLiteralIsRefusedRatherThanBecomingInfinity()
        {
            Assert.That(Refused(new string('9', 10000)).Error, Is.EqualTo(JsonParseError.NumberOutOfRange));
            Assert.That(Refused("1e" + new string('9', 10000)).Error, Is.EqualTo(JsonParseError.NumberOutOfRange));
        }

        [Test]
        public void ANumberTooSmallForADoubleUnderflowsToZero()
        {
            // JSON.parse("1e-400") is 0 as well. Underflow is not an error; only
            // overflow is, because overflow would put Infinity in the value tree and
            // Infinity has no JSON spelling to write back.
            Assert.That(Json.Parse("1e-400").AsNumber(), Is.EqualTo(0d));
            Assert.That(double.IsNegative(Json.Parse("-1e-400").AsNumber()), Is.True);
        }

        [TestCase("1e-320", "1E-320")]
        [TestCase("1E2", "100")]
        [TestCase("-0", "0")]
        [TestCase("0.1", "0.1")]
        [TestCase("1.5", "1.5")]
        [TestCase("-1.25", "-1.25")]
        [TestCase("1e21", "1E+21")]
        [TestCase("1e-7", "1E-07")]
        [TestCase("5e-324", "5E-324")]
        [TestCase("1.7976931348623157e308", "1.7976931348623157E+308")]
        public void TheWireFormatOfANumberIsPinned(string text, string expected)
        {
            var written = Json.Stringify(Json.Parse(text));

            Assert.That(written, Is.EqualTo(expected));

            // A pin that drifted into an unparseable literal would be worse than no
            // pin, so assert the pinned text is still a number this parser reads back
            // to the same double.
            Assert.That(Json.Parse(written).AsNumber(), Is.EqualTo(Json.Parse(text).AsNumber()));
        }

        [Test]
        public void NumbersAreDoublesAndLoseIntegerPrecisionPastFiftyThreeBits()
        {
            // A documented limit, not a bug: the wire's integers (tick, max, code)
            // are small. If a field ever needs more than 2^53, it has to arrive as a
            // string, and this test is what says so out loud.
            Assert.That(Json.Parse("9007199254740992").AsNumber(), Is.EqualTo(9007199254740992d));
            Assert.That(Json.Parse("9007199254740993").AsNumber(), Is.EqualTo(9007199254740992d));
            Assert.That(Json.Stringify(Json.Parse("9007199254740993")), Is.EqualTo("9007199254740992"));
            Assert.That(Json.Stringify(Json.Parse("12345678901234567890")), Is.EqualTo("1.2345678901234567E+19"));
        }

        [Test]
        public void NegativeZeroIsIndistinguishableFromZeroOnTheWire()
        {
            var negative = Json.Parse("-0");

            Assert.That(double.IsNegative(negative.AsNumber()), Is.True);
            Assert.That(Json.Stringify(negative), Is.EqualTo("0"));
            Assert.That(negative, Is.EqualTo(JsonValue.Of(0d)));
            Assert.That(negative.GetHashCode(), Is.EqualTo(JsonValue.Of(0d).GetHashCode()));
            Assert.That(negative.AsInt32(), Is.EqualTo(0));
        }

        [TestCase("tr-TR")]
        [TestCase("de-DE")]
        [TestCase("fr-FR")]
        public void EveryNumericConversionIgnoresTheCurrentCulture(string cultureName)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);

                Assert.That(Json.Stringify(JsonValue.Of(1.5)), Is.EqualTo("1.5"));
                Assert.That(Json.Stringify(JsonValue.Of(-1234.5)), Is.EqualTo("-1234.5"));
                Assert.That(Json.Stringify(JsonValue.Of(1e21)), Is.EqualTo("1E+21"));
                Assert.That(Json.Parse("-1234.5").AsNumber(), Is.EqualTo(-1234.5));
                Assert.That(Json.Parse("1e-7").AsNumber(), Is.EqualTo(1e-7));
                Assert.That(Json.Parse("-2147483648").AsInt32(), Is.EqualTo(int.MinValue));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void JsonHasNoSpellingForNaNOrInfinitySoTheValueIsRefused(double value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JsonValue.Of(value));
        }
    }
}
