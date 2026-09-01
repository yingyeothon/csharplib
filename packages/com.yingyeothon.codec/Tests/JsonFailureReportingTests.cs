using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// What a caller learns when a document is refused.
    /// </summary>
    /// <remarks>
    /// A boolean is not enough: the gateway socket parses every inbound frame, and
    /// "the frame is not JSON" is not a diagnosis anyone can act on in the field. It
    /// is also not enough to put the offending text in the message — a refusal is
    /// reported through <c>ProtocolError</c> into whatever log writer the consumer
    /// installed, and a frame body is never allowed there (<c>rules/security.md</c>).
    /// So the reason is a code and an offset, and nothing else.
    /// </remarks>
    [TestFixture]
    public class JsonFailureReportingTests
    {
        /// <summary>One input per failure code. The exhaustiveness test guards it.</summary>
        private static readonly IReadOnlyList<KeyValuePair<JsonParseError, string>> Samples =
            new List<KeyValuePair<JsonParseError, string>>
            {
                new KeyValuePair<JsonParseError, string>(JsonParseError.InputTooLong, new string('a', Json.MaxLength + 1)),
                new KeyValuePair<JsonParseError, string>(JsonParseError.DepthExceeded, new string('[', 65)),
                new KeyValuePair<JsonParseError, string>(JsonParseError.UnexpectedEndOfInput, ""),
                new KeyValuePair<JsonParseError, string>(JsonParseError.TrailingContent, "1 2"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedValue, "]"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedLiteral, "tru"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedKey, "{1:1}"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedColon, "{\"a\"}"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedCommaOrEnd, "[1}"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.UnterminatedString, "\"a"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.UnescapedControlCharacter, "\"a\u0001\""),
                new KeyValuePair<JsonParseError, string>(JsonParseError.UnknownEscape, "\"\\x\""),
                new KeyValuePair<JsonParseError, string>(JsonParseError.TruncatedUnicodeEscape, "\"\\u12"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.InvalidUnicodeEscape, "\"\\u12g4\""),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedFractionDigit, "1."),
                new KeyValuePair<JsonParseError, string>(JsonParseError.ExpectedExponentDigit, "1e"),
                new KeyValuePair<JsonParseError, string>(JsonParseError.NumberOutOfRange, "1e400"),
            };

        [Test]
        public void EveryFailureCodeIsReachableFromSomeInput()
        {
            // Adding a code without an input that produces it is how a reason nobody
            // can trigger — or worse, one nobody can tell apart — gets in.
            var covered = new HashSet<JsonParseError>();
            foreach (var sample in Samples)
            {
                Assert.That(Json.TryParse(sample.Value, out _, out var failure), Is.False, sample.Key.ToString());
                Assert.That(failure.Error, Is.EqualTo(sample.Key));
                covered.Add(sample.Key);
            }

            foreach (JsonParseError code in Enum.GetValues(typeof(JsonParseError)))
            {
                if (code == JsonParseError.None || code == JsonParseError.NullInput)
                {
                    continue;
                }

                Assert.That(covered, Does.Contain(code), "no sample input produces " + code);
            }
        }

        [Test]
        public void ParseAndTryParseAgreeOnTheReason()
        {
            foreach (var sample in Samples)
            {
                Json.TryParse(sample.Value, out _, out var failure);
                var error = Assert.Throws<JsonParseException>(() => Json.Parse(sample.Value));

                Assert.That(error!.Error, Is.EqualTo(failure.Error));
                Assert.That(error.Index, Is.EqualTo(failure.Index));
                Assert.That(error.Failure, Is.EqualTo(failure));
            }
        }

        [Test]
        public void AFailureMessageIsBuiltFromTheCodeAndTheOffsetAndNothingElse()
        {
            // Equality with a template computed from (code, index) is the whole
            // guarantee: if the message is exactly this, no byte of the input can be
            // inside it.
            foreach (var sample in Samples)
            {
                var error = Assert.Throws<JsonParseException>(() => Json.Parse(sample.Value));

                Assert.That(error!.Message, Is.EqualTo("Malformed JSON: " + error.Failure));
                Assert.That(error.Failure.ToString(), Is.EqualTo(error.Error + " at " + error.Index));
            }
        }

        [Test]
        public void ARefusedFrameNeverPutsItsContentInTheReason()
        {
            const string Token = "S3CRET-TOKEN";

            var error = Assert.Throws<JsonParseException>(() => Json.Parse("{\"authorization\":\"bearer " + Token + "\""));

            // Positive control: Does.Not.Contain passes against an empty string too,
            // so assert the message really does say something first.
            Assert.That(error!.Message, Does.Contain("ExpectedCommaOrEnd"));
            Assert.That(error.Message, Does.Contain(error.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(error.Message, Does.Not.Contain(Token));
            Assert.That(error.Message, Does.Not.Contain("bearer"));
            Assert.That(error.Message, Does.Not.Contain("authorization"));

            var number = Assert.Throws<JsonParseException>(() => Json.Parse("[123456789e400]"));
            Assert.That(number!.Message, Does.Contain("NumberOutOfRange"));
            Assert.That(number.Message, Does.Not.Contain("123456789"));

            var escape = Assert.Throws<JsonParseException>(() => Json.Parse("\"\\Qsecret\""));
            Assert.That(escape!.Message, Does.Contain("UnknownEscape"));
            Assert.That(escape.Message, Does.Not.Contain("Q"));
        }

        [Test]
        public void ANullDocumentIsAFailureForTryParseAndAnArgumentErrorForParse()
        {
            // A null reference is a bug in the caller, not a malformed frame, so the
            // throwing entry point says so. TryParse is the "I am handed whatever the
            // socket produced" path and never throws.
            Assert.That(Json.TryParse(null!, out var value, out var failure), Is.False);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.NullInput));
            Assert.That(failure.Index, Is.EqualTo(0));
            Assert.That(value, Is.SameAs(JsonValue.Null));

            Assert.Throws<ArgumentNullException>(() => Json.Parse(null!));
            Assert.Throws<ArgumentNullException>(() => JsonCodec.Decode(null!));
        }

        [Test]
        public void ASuccessfulParseReportsNoFailure()
        {
            Assert.That(Json.TryParse("{\"type\":\"pong\"}", out var value, out var failure), Is.True);
            Assert.That(failure.Error, Is.EqualTo(JsonParseError.None));
            Assert.That(failure.IsFailure, Is.False);
            Assert.That(failure.Index, Is.EqualTo(0));
            Assert.That(value.GetString("type"), Is.EqualTo("pong"));
        }

        [Test]
        public void AFailedParseLeavesTheOutValueAtTheJsonNullSentinel()
        {
            Assert.That(Json.TryParse("{", out var value, out _), Is.False);
            Assert.That(value, Is.SameAs(JsonValue.Null));

            Assert.That(Json.TryParse("{", out var legacy), Is.False);
            Assert.That(legacy, Is.SameAs(JsonValue.Null));
        }

        [Test]
        public void TheCodecSurfacesTheSameReason()
        {
            var error = Assert.Throws<JsonParseException>(() => JsonCodec.Decode("{\"a\":}"));

            Assert.That(error!.Error, Is.EqualTo(JsonParseError.ExpectedValue));
            Assert.That(error.Index, Is.EqualTo(5));
        }

        [Test]
        public void TwoFailuresAreEqualWhenTheirCodeAndOffsetMatch()
        {
            var left = new JsonParseFailure(JsonParseError.ExpectedColon, 4);
            var right = new JsonParseFailure(JsonParseError.ExpectedColon, 4);
            var other = new JsonParseFailure(JsonParseError.ExpectedColon, 5);

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
            Assert.That(left, Is.Not.EqualTo(other));
            Assert.That(default(JsonParseFailure).Error, Is.EqualTo(JsonParseError.None));
        }
    }
}
