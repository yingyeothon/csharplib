using System;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Codec.Tests
{
    [TestFixture]
    public class JsonCodecTests
    {
        [Test]
        public void EncodeAndDecodeRoundTripThroughTheInterface()
        {
            ICodec<string> codec = JsonCodec.Instance;
            var frame = Json.Object()
                .Set("type", "pos")
                .Set("zone", "town")
                .Set("x", 1.5)
                .Build();

            var wire = codec.Encode(frame);

            Assert.That(wire, Is.EqualTo("{\"type\":\"pos\",\"zone\":\"town\",\"x\":1.5}"));
            Assert.That(codec.Decode(wire), Is.EqualTo(frame));
        }

        [Test]
        public void EncodingANullReferenceIsRefused()
        {
            // tslib's jsonCodec answers `encode(undefined)` with the literal string
            // "undefined", which its own decode then refuses. C# has no undefined,
            // so this is rejected at the call instead of on the wire.
            Assert.Throws<ArgumentNullException>(() => JsonCodec.Encode(null!));
            Assert.Throws<ArgumentNullException>(() => JsonCodec.Decode(null!));
        }

        [Test]
        public void DecodingMalformedTextThrows()
        {
            var error = Assert.Throws<JsonParseException>(() => JsonCodec.Decode("{"));

            Assert.That(error!.Index, Is.GreaterThanOrEqualTo(0));
        }
    }
}
