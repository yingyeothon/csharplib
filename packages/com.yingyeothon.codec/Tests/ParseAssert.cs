using NUnit.Framework;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>Assertions over the non-throwing parser, shared by the grammar suites.</summary>
    internal static class ParseAssert
    {
        /// <summary>Asserts that the text is refused and hands back the failure for further assertions.</summary>
        internal static JsonParseFailure Refused(string text)
        {
            Assert.That(Json.TryParse(text, out _, out var failure), Is.False, "expected the " + text.Length + "-char input to be refused");
            Assert.That(failure.IsFailure, Is.True);
            return failure;
        }

        /// <summary>Asserts that the text is refused with exactly this reason at exactly this offset.</summary>
        internal static void Refused(string text, JsonParseError error, int index)
        {
            var failure = Refused(text);

            Assert.That(failure.Error, Is.EqualTo(error));
            Assert.That(failure.Index, Is.EqualTo(index));
        }
    }
}
