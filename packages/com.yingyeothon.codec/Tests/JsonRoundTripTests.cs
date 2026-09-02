using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Codec.Tests
{
    [TestFixture]
    public class JsonRoundTripTests
    {
        [TestCase("null")]
        [TestCase("true")]
        [TestCase("false")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("1e-7")]
        [TestCase("\"\"")]
        [TestCase("\"hi\"")]
        [TestCase("[]")]
        [TestCase("{}")]
        [TestCase("[1,2,3]")]
        [TestCase("{\"a\":1,\"b\":[true,null,\"x\"]}")]
        public void ParseAndStringifyRoundTrip(string text)
        {
            var value = Json.Parse(text);

            Assert.That(Json.Parse(Json.Stringify(value)), Is.EqualTo(value));
        }

        [Test]
        public void AnIntegralNumberIsWrittenWithoutADecimalPoint()
        {
            Assert.That(Json.Stringify(JsonValue.Of(1d)), Is.EqualTo("1"));
            Assert.That(Json.Stringify(JsonValue.Of(200d)), Is.EqualTo("200"));
            Assert.That(Json.Stringify(JsonValue.Of(-0d)), Is.EqualTo("0"));
            Assert.That(Json.Stringify(JsonValue.Of(1.5)), Is.EqualTo("1.5"));
        }

        [TestCase("tr-TR")]
        [TestCase("de-DE")]
        public void NumbersAreWrittenAndReadInvariantOfTheCurrentCulture(string cultureName)
        {
            // A comma-decimal locale would put "1,5" on the wire, which the
            // gateway drops as bad_message without telling the client.
            using var culture = new CultureScope(cultureName);

            Assert.That(Json.Stringify(JsonValue.Of(1.5)), Is.EqualTo("1.5"));
            Assert.That(Json.Parse("1.5").AsNumber(), Is.EqualTo(1.5));
        }

        [Test]
        public void StringsEscapeControlCharactersAndQuotes()
        {
            var text = Json.Stringify(JsonValue.Of("a\"b\\c\nd\te\u0001f"));

            Assert.That(text, Is.EqualTo("\"a\\\"b\\\\c\\nd\\te\\u0001f\""));
            Assert.That(Json.Parse(text).AsString(), Is.EqualTo("a\"b\\c\nd\te\u0001f"));
        }

        [Test]
        public void UnicodeEscapesIncludingSurrogatePairsAreDecoded()
        {
            Assert.That(Json.Parse("\"\\uD55C\"").AsString(), Is.EqualTo("한"));
            Assert.That(Json.Parse("\"\\uD83D\\uDE00\"").AsString(), Is.EqualTo("\U0001F600"));
            Assert.That(Json.Parse("\"\\u0041\\/\\b\\f\"").AsString(), Is.EqualTo("A/\b\f"));
        }

        [Test]
        public void MultiByteTextSurvivesTheRoundTrip()
        {
            var value = JsonValue.Of("한글 🎮 テスト");

            Assert.That(Json.Parse(Json.Stringify(value)).AsString(), Is.EqualTo("한글 🎮 テスト"));
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase("{")]
        [TestCase("[1,]")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{a:1}")]
        [TestCase("{'a':1}")]
        [TestCase("01")]
        [TestCase("+1")]
        [TestCase(".5")]
        [TestCase("1.")]
        [TestCase("1e")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("undefined")]
        [TestCase("nul")]
        [TestCase("\"unterminated")]
        [TestCase("\"bad \\x escape\"")]
        [TestCase("\"raw \u0001 control\"")]
        [TestCase("{} trailing")]
        [TestCase("1 2")]
        [TestCase("// comment")]
        public void MalformedInputIsRefused(string text)
        {
            Assert.Throws<JsonParseException>(() => Json.Parse(text));
            Assert.That(Json.TryParse(text, out _), Is.False);
        }

        [Test]
        public void NestingDeeperThanTheLimitIsRefusedWithoutOverflowingTheStack()
        {
            var deep = new string('[', 100_000) + new string(']', 100_000);

            Assert.That(Json.TryParse(deep, out _), Is.False);
        }

        [Test]
        public void TryParseReportsSuccessAndTheValue()
        {
            Assert.That(Json.TryParse("{\"type\":\"pong\"}", out var value), Is.True);
            Assert.That(value.GetString("type"), Is.EqualTo("pong"));
        }

        [Test]
        public void AnOutOfRangeNumberIsRefusedRatherThanBecomingInfinity()
        {
            Assert.Throws<JsonParseException>(() => Json.Parse("1e400"));
        }

        [Test]
        public void WhitespaceBetweenTokensIsIgnored()
        {
            var value = Json.Parse(" {\n \"a\" : [ 1 , 2 ] \t} ");

            Assert.That(value.GetArrayOrEmpty("a").Count, Is.EqualTo(2));
        }
    }
}
