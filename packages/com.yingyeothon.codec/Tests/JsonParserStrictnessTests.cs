using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// The grammar the parser accepts, asserted from the outside: every shape that
    /// must be refused, and every shape that must not be.
    /// </summary>
    /// <remarks>
    /// Strictness here is not pedantry. The gateway is a Go service using
    /// <c>encoding/json</c>; anything this parser accepts that Go refuses is a
    /// divergence the SDK would paper over, and anything it refuses that Go accepts
    /// is a frame the game silently drops.
    /// </remarks>
    [TestFixture]
    public class JsonParserStrictnessTests
    {
        private static JsonParseFailure Refused(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False, "expected '" + text.Length + "' chars of input to be refused");
            Assert.That(failure.IsFailure, Is.True);
            return failure;
        }

        [TestCase("[", JsonParseError.UnexpectedEndOfInput, 1)]
        [TestCase("{", JsonParseError.ExpectedKey, 1)]
        [TestCase("]", JsonParseError.ExpectedValue, 0)]
        [TestCase("}", JsonParseError.ExpectedValue, 0)]
        [TestCase("[}", JsonParseError.ExpectedValue, 1)]
        [TestCase("{]", JsonParseError.ExpectedKey, 1)]
        [TestCase("[1,", JsonParseError.UnexpectedEndOfInput, 3)]
        [TestCase("[1}", JsonParseError.ExpectedCommaOrEnd, 2)]
        [TestCase("[[]", JsonParseError.ExpectedCommaOrEnd, 3)]
        [TestCase("{\"a\":1", JsonParseError.ExpectedCommaOrEnd, 6)]
        [TestCase("{\"a\":1]", JsonParseError.ExpectedCommaOrEnd, 6)]
        [TestCase("{\"a\"}", JsonParseError.ExpectedColon, 4)]
        [TestCase("{\"a\":}", JsonParseError.ExpectedValue, 5)]
        [TestCase("{\"a\" 1}", JsonParseError.ExpectedColon, 5)]
        [TestCase("{\"a\"::1}", JsonParseError.ExpectedValue, 5)]
        [TestCase("{:1}", JsonParseError.ExpectedKey, 1)]
        [TestCase("{,}", JsonParseError.ExpectedKey, 1)]
        [TestCase("[,1]", JsonParseError.ExpectedValue, 1)]
        [TestCase("[1,,2]", JsonParseError.ExpectedValue, 3)]
        [TestCase("[1,]", JsonParseError.ExpectedValue, 3)]
        [TestCase("{\"a\":1,}", JsonParseError.ExpectedKey, 7)]
        [TestCase(",", JsonParseError.ExpectedValue, 0)]
        [TestCase(":", JsonParseError.ExpectedValue, 0)]
        public void AMalformedStructureIsRefusedWithItsReasonAndPosition(string text, JsonParseError error, int index)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(error));
            Assert.That(failure.Index, Is.EqualTo(index));
        }

        [TestCase("{1:2}")]
        [TestCase("{true:1}")]
        [TestCase("{null:1}")]
        [TestCase("{[]:1}")]
        public void AnObjectKeyMustBeAQuotedString(string text)
        {
            Assert.That(Refused(text).Error, Is.EqualTo(JsonParseError.ExpectedKey));
        }

        [TestCase("{} {}", 3)]
        [TestCase("1 2", 2)]
        [TestCase("null null", 5)]
        [TestCase("\"a\"\"b\"", 3)]
        [TestCase("[]x", 2)]
        [TestCase("truex", 4)]
        [TestCase("nullx", 4)]
        [TestCase("01", 1)]
        public void ContentAfterTheTopLevelValueIsRefused(string text, int index)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.TrailingContent));
            Assert.That(failure.Index, Is.EqualTo(index));
        }

        [TestCase("tru")]
        [TestCase("nul")]
        [TestCase("fals")]
        [TestCase("[tru]")]
        [TestCase("[fals]")]
        public void ATruncatedLiteralIsRefused(string text)
        {
            Assert.That(Refused(text).Error, Is.EqualTo(JsonParseError.ExpectedLiteral));
        }

        [TestCase("True")]
        [TestCase("TRUE")]
        [TestCase("False")]
        [TestCase("Null")]
        [TestCase("undefined")]
        public void ALiteralIsCaseSensitive(string text)
        {
            Assert.That(Refused(text).Error, Is.EqualTo(JsonParseError.ExpectedValue));
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t\r\n  ")]
        public void EmptyOrWhitespaceOnlyInputIsRefused(string text)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.UnexpectedEndOfInput));
            Assert.That(failure.Index, Is.EqualTo(text.Length));
        }

        [Test]
        public void AByteOrderMarkIsNotWhitespace()
        {
            // RFC 8259 does not allow a BOM and JSON.parse refuses one. A frame that
            // arrives with one is a bug on the sender's side, not something to
            // silently absorb.
            Assert.That(Refused("\uFEFF").Error, Is.EqualTo(JsonParseError.ExpectedValue));
            Assert.That(Refused("\uFEFF{}").Error, Is.EqualTo(JsonParseError.ExpectedValue));
        }

        [TestCase(0x0B, TestName = "VerticalTab")]
        [TestCase(0x0C, TestName = "FormFeed")]
        [TestCase(0x00A0, TestName = "NoBreakSpace")]
        [TestCase(0x2028, TestName = "LineSeparator")]
        [TestCase(0xFEFF, TestName = "ByteOrderMark")]
        public void OnlyTheFourJsonWhitespaceCharactersAreWhitespace(int code)
        {
            var failure = Refused("[" + (char)code + "1]");

            Assert.That(failure.Error, Is.EqualTo(JsonParseError.ExpectedValue));
            Assert.That(failure.Index, Is.EqualTo(1));
        }

        [Test]
        public void SpaceTabCarriageReturnAndLineFeedAreAcceptedEverywhere()
        {
            var value = Json.Parse(" \t\r\n{ \t\"a\"\r\n:\n[ 1 , 2 ]\t, \"b\" : { } , \"c\" : [ ] } \t\r\n");

            Assert.That(value.GetArrayOrEmpty("a").Count, Is.EqualTo(2));
            Assert.That(value.GetMemberOrNull("b")!.AsObject(), Is.Empty);
            Assert.That(value.GetArrayOrEmpty("c"), Is.Empty);
        }

        [TestCase("null")]
        [TestCase("true")]
        [TestCase("false")]
        [TestCase("0")]
        [TestCase("\"\"")]
        [TestCase("[]")]
        [TestCase("{}")]
        public void EveryBareValueIsALegalDocument(string text)
        {
            Assert.That(Json.TryParse(text, out var value, out var failure), Is.True);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.None));
            Assert.That(Json.Stringify(value), Is.EqualTo(text));
        }

        [Test]
        public void AParserCarriesNoStateFromOneDocumentToTheNext()
        {
            // The parser now holds a failure in a field instead of throwing. A field
            // that outlived a call would poison every later frame on the socket, and
            // the socket parses one per tick.
            for (var round = 0; round < 3; round++)
            {
                Assert.That(Json.TryParse("{\"a\":", out _, out var bad), Is.False);
                Assert.That(bad.Error, Is.EqualTo(JsonParseError.UnexpectedEndOfInput));

                Assert.That(Json.TryParse("{\"a\":1}", out var good, out var none), Is.True);
                Assert.That(none.Error, Is.EqualTo(JsonParseError.None));
                Assert.That(good.GetNumber("a"), Is.EqualTo(1d));
            }
        }

        [Test]
        public void OnlyTheFirstReasonIsReportedWhenADocumentIsWrongInSeveralPlaces()
        {
            // The reason has to name where the document stopped making sense, not
            // wherever the unwinding happened to end.
            Assert.That(Refused("[1,\"\\x\",2e]").Error, Is.EqualTo(JsonParseError.UnknownEscape));
            Assert.That(Refused("{\"a\" 1, \"b\"}").Error, Is.EqualTo(JsonParseError.ExpectedColon));
        }

        [Test]
        public void KeysAreComparedByOrdinalNotByCulture()
        {
            // StringComparer.Ordinal, never the current culture: a Turkish locale
            // folds "i" and "I", and the gateway's keys are ASCII identifiers whose
            // identity must not depend on the player's device settings.
            var previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");

                var value = Json.Parse("{\"id\":1,\"ID\":2,\"\u0130d\":3}");

                Assert.That(value.AsObject().Count, Is.EqualTo(3));
                Assert.That(value.GetNumber("id"), Is.EqualTo(1d));
                Assert.That(value.GetNumber("ID"), Is.EqualTo(2d));
                Assert.That(value.GetNumber("\u0130d"), Is.EqualTo(3d));
                Assert.That(Json.Parse("{\"id\":1}"), Is.Not.EqualTo(Json.Parse("{\"ID\":1}")));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void NestedContainersKeepTheirShape()
        {
            var text = "{\"a\":[[],[{}],[{\"b\":[1,{\"c\":null}]}]],\"d\":{}}";

            Assert.That(Json.Stringify(Json.Parse(text)), Is.EqualTo(text));
        }
    }
}
