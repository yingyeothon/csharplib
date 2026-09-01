using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// String escaping in both directions, for keys as well as values.
    /// </summary>
    /// <remarks>
    /// The writer's job is not only to emit valid JSON but to emit text that
    /// survives a UTF-8 encode, because that is what a WebSocket text frame does to
    /// it. An unpaired surrogate written raw does not survive: it becomes U+FFFD and
    /// the peer receives a different string than the game sent.
    /// </remarks>
    [TestFixture]
    public class JsonStringEscapeTests
    {
        private static string Hex(int code) => code.ToString("x4", CultureInfo.InvariantCulture);

        [Test]
        public void EveryControlCharacterIsEscapedAndReadBackExactly()
        {
            for (var code = 0; code <= 0x1F; code++)
            {
                var original = "a" + (char)code + "b";
                var text = Json.Stringify(JsonValue.Of(original));

                string expected;
                switch (code)
                {
                    case 0x08: expected = "\\b"; break;
                    case 0x09: expected = "\\t"; break;
                    case 0x0A: expected = "\\n"; break;
                    case 0x0C: expected = "\\f"; break;
                    case 0x0D: expected = "\\r"; break;
                    default: expected = "\\u" + Hex(code); break;
                }

                Assert.That(text, Is.EqualTo("\"a" + expected + "b\""), "U+" + code.ToString("X4", CultureInfo.InvariantCulture));
                Assert.That(Json.Parse(text).AsString(), Is.EqualTo(original));
            }
        }

        [Test]
        public void EveryRawControlCharacterInsideAStringIsRefused()
        {
            for (var code = 0; code <= 0x1F; code++)
            {
                var text = "\"a" + (char)code + "b\"";

                Assert.That(Json.TryParse(text, out _, out var failure), Is.False, "U+" + code.ToString("X4", CultureInfo.InvariantCulture));
                Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnescapedControlCharacter));
                Assert.That(failure.Index, Is.EqualTo(2));
            }
        }

        [Test]
        public void DelAndTheLineSeparatorsAreWrittenRaw()
        {
            // JSON.stringify leaves U+007F, U+2028 and U+2029 alone; they are legal
            // in a JSON string. Only a JavaScript *source* file cares about the last
            // two, and the gateway never eval()s a frame.
            Assert.That(Json.Stringify(JsonValue.Of("\u007F")), Is.EqualTo("\"\u007F\""));
            Assert.That(Json.Stringify(JsonValue.Of("\u2028\u2029")), Is.EqualTo("\"\u2028\u2029\""));
            Assert.That(Json.Parse("\"\u2028\"").AsString(), Is.EqualTo("\u2028"));
        }

        [Test]
        public void AValidSurrogatePairIsWrittenRaw()
        {
            var value = JsonValue.Of("\U0001F600");

            Assert.That(Json.Stringify(value), Is.EqualTo("\"\U0001F600\""));
            Assert.That(Json.Parse("\"\\uD83D\\uDE00\""), Is.EqualTo(value));
        }

        // J1: every one of these round-trips to a *different* string today, because
        // the writer emits the unpaired half raw and UTF-8 replaces it with U+FFFD.
        [TestCase(0xD800, "", TestName = "LoneHighSurrogate")]
        [TestCase(0xDC00, "", TestName = "LoneLowSurrogate")]
        [TestCase(0xD800, "A", TestName = "HighSurrogateFollowedByAnAsciiCharacter")]
        [TestCase(0xDBFF, "", TestName = "LastHighSurrogate")]
        [TestCase(0xDFFF, "", TestName = "LastLowSurrogate")]
        public void AnUnpairedSurrogateIsReEscapedSoItSurvivesTheWire(int code, string trailer)
        {
            var original = "x" + (char)code + trailer + "y";
            var value = JsonValue.Of(original);

            var text = Json.Stringify(value);

            Assert.That(text, Is.EqualTo("\"x\\u" + Hex(code) + trailer + "y\""));
            Assert.That(Json.Parse(text), Is.EqualTo(value));

            // The assertion that matters: a text frame is UTF-8 on the wire.
            var encoded = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
            Assert.That(encoded, Is.EqualTo(text));
            Assert.That(Json.Parse(encoded), Is.EqualTo(value));
        }

        [Test]
        public void TwoHighSurrogatesInARowAreBothEscaped()
        {
            var value = JsonValue.Of(new string(new[] { (char)0xD800, (char)0xD801 }));

            Assert.That(Json.Stringify(value), Is.EqualTo("\"\\ud800\\ud801\""));
            Assert.That(Json.Parse(Json.Stringify(value)), Is.EqualTo(value));
        }

        [Test]
        public void ALowSurrogateFollowedByAHighSurrogateIsNotAPair()
        {
            // The halves are in the wrong order, so neither may be written raw.
            var value = JsonValue.Of(new string(new[] { (char)0xDC00, (char)0xD800 }));

            Assert.That(Json.Stringify(value), Is.EqualTo("\"\\udc00\\ud800\""));
            Assert.That(Json.Parse(Json.Stringify(value)), Is.EqualTo(value));
        }

        [Test]
        public void AKeyIsEscapedTheSameWayAValueIs()
        {
            var key = "q\"b\\s\nn\u0001c\u0000z" + (char)0xD800;
            var value = Json.Object().Set(key, "v").Build();

            var text = Json.Stringify(value);

            Assert.That(text, Is.EqualTo("{\"q\\\"b\\\\s\\nn\\u0001c\\u0000z\\ud800\":\"v\"}"));
            Assert.That(Json.Parse(text).GetString(key), Is.EqualTo("v"));
        }

        [Test]
        public void AnEmptyKeyIsALegalKey()
        {
            var value = Json.Parse("{\"\":1}");

            Assert.That(value.GetNumber(""), Is.EqualTo(1d));
            Assert.That(Json.Stringify(value), Is.EqualTo("{\"\":1}"));
        }

        [Test]
        public void ANulCharacterSurvivesInBothAKeyAndAValue()
        {
            var value = Json.Parse("{\"a\\u0000b\":\"c\\u0000d\"}");

            Assert.That(value.GetString("a\0b"), Is.EqualTo("c\0d"));
            Assert.That(Json.Stringify(value), Is.EqualTo("{\"a\\u0000b\":\"c\\u0000d\"}"));
        }

        [Test]
        public void EveryLegalEscapeIsDecoded()
        {
            Assert.That(Json.Parse("\"\\\"\\\\\\/\\b\\f\\n\\r\\t\"").AsString(), Is.EqualTo("\"\\/\b\f\n\r\t"));
            Assert.That(Json.Parse("\"\\u0041\\u00e9\\uD55C\"").AsString(), Is.EqualTo("A\u00e9한"));
            Assert.That(Json.Parse("\"\\uABCD\\uabcd\"").AsString(), Is.EqualTo("\uABCD\uABCD"));
        }

        [Test]
        public void AnEscapedSolidusIsWrittenBackUnescaped()
        {
            // "\/" is legal input but nothing requires it on the way out, and
            // JSON.stringify does not produce it either.
            Assert.That(Json.Stringify(Json.Parse("\"a\\/b\"")), Is.EqualTo("\"a/b\""));
        }

        [TestCase("\"\\x\"", TestName = "BackslashX")]
        [TestCase("\"\\'\"", TestName = "BackslashApostrophe")]
        [TestCase("\"\\a\"", TestName = "BackslashA")]
        [TestCase("\"\\U0041\"", TestName = "BackslashUppercaseU")]
        [TestCase("\"\\ \"", TestName = "BackslashSpace")]
        [TestCase("\"\\0\"", TestName = "BackslashZero")]
        public void AnUnknownEscapeIsRefused(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnknownEscape));
            Assert.That(failure.Index, Is.EqualTo(2));
        }

        [Test]
        public void AnEscapedNewlineIsRefused()
        {
            Assert.That(Json.TryParse("\"\\\n\"", out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnknownEscape));
        }

        [TestCase("\"\\", TestName = "BackslashAtEof")]
        [TestCase("\"\\u", TestName = "BackslashUAtEof")]
        [TestCase("\"\\u1", TestName = "OneHexDigitAtEof")]
        [TestCase("\"\\u12", TestName = "TwoHexDigitsAtEof")]
        [TestCase("\"\\u123", TestName = "ThreeHexDigitsAtEof")]
        public void AnEscapeTruncatedAtEndOfInputIsRefusedWithoutIndexingPastTheBuffer(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(
                failure.Error,
                Is.EqualTo(text.EndsWith("\\") ? JsonParseError.UnexpectedEndOfInput : JsonParseError.TruncatedUnicodeEscape));
        }

        [TestCase("\"\\u12g4\"", 5)]
        [TestCase("\"\\u 123\"", 3)]
        [TestCase("\"\\uD80O\"", 6)]
        [TestCase("\"\\u-123\"", 3)]
        public void InvalidHexInAUnicodeEscapeIsRefused(string text, int index)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.InvalidUnicodeEscape));
            Assert.That(failure.Index, Is.EqualTo(index));
        }

        [TestCase("\"abc", TestName = "AtTopLevel")]
        [TestCase("{\"a\":\"b", TestName = "AsAnObjectValue")]
        [TestCase("{\"a", TestName = "AsAnObjectKey")]
        [TestCase("[\"a", TestName = "AsAnArrayItem")]
        [TestCase("\"", TestName = "OnlyAnOpeningQuote")]
        public void AnUnterminatedStringIsRefused(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnterminatedString));
            Assert.That(failure.Index, Is.EqualTo(text.Length));
        }

        [Test]
        public void ALongStringOfEscapesRoundTrips()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < 20000; i++)
            {
                builder.Append((char)(i % 0x20));
            }

            var value = JsonValue.Of(builder.ToString());

            Assert.That(Json.Parse(Json.Stringify(value)), Is.EqualTo(value));
        }
    }
}
