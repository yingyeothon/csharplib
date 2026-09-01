using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Codec.Tests
{
    [TestFixture]
    public class JsonObjectBuilderTests
    {
        [Test]
        public void ANullArgumentOmitsTheFieldEntirely()
        {
            // The gateway marshals with Go's omitempty, so "absent" and "null" are
            // different frames; a `pos` carrying "dir": null is not the same as one
            // that leaves `dir` off.
            var frame = Json.Object()
                .Set("type", "pos")
                .Set("dir", (string?)null)
                .Set("x", (double?)null)
                .Set("live", (bool?)null)
                .Build();

            Assert.That(Json.Stringify(frame), Is.EqualTo("{\"type\":\"pos\"}"));
            Assert.That(frame.TryGetMember("dir", out _), Is.False);
        }

        [Test]
        public void SetNullWritesAnExplicitJsonNull()
        {
            var frame = Json.Object().Set("type", "x").SetNull("payload").Build();

            Assert.That(Json.Stringify(frame), Is.EqualTo("{\"type\":\"x\",\"payload\":null}"));
            Assert.That(frame.TryGetMember("payload", out var payload), Is.True);
            Assert.That(payload.IsNull, Is.True);
        }

        [Test]
        public void FieldsKeepTheOrderTheyWereSetIn()
        {
            var frame = Json.Object().Set("b", 1d).Set("a", 2d).Build();

            Assert.That(Json.Stringify(frame), Is.EqualTo("{\"b\":1,\"a\":2}"));
        }

        [Test]
        public void AnOpaquePayloadSurvivesUntouched()
        {
            // `event.payload` and every q frame are json.RawMessage on the gateway
            // side: the SDK must not reshape them.
            var payload = Json.Parse("{\"nested\":{\"list\":[1,\"two\",null,{\"deep\":true}]}}");
            var frame = Json.Object().Set("type", "event").Set("payload", payload).Build();

            var reparsed = Json.Parse(Json.Stringify(frame));

            Assert.That(reparsed.GetMemberOrNull("payload"), Is.EqualTo(payload));
        }
    }
}
