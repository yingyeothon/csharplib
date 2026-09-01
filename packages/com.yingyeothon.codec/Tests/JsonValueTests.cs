using System.Collections.Generic;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Codec.Tests
{
    [TestFixture]
    public class JsonValueTests
    {
        [Test]
        public void AbsentIsNotTheSameAsJsonNull()
        {
            var value = Json.Parse("{\"present\":null}");

            Assert.That(value.TryGetMember("present", out var present), Is.True);
            Assert.That(present.Kind, Is.EqualTo(JsonKind.Null));
            Assert.That(value.GetMemberOrNull("present"), Is.Not.Null);

            Assert.That(value.TryGetMember("absent", out _), Is.False);
            Assert.That(value.GetMemberOrNull("absent"), Is.Null);
        }

        [Test]
        public void TypedGettersFoldNullAndAbsentTogether()
        {
            var value = Json.Parse("{\"s\":null,\"n\":null,\"b\":null,\"a\":null}");

            Assert.That(value.GetString("s"), Is.Null);
            Assert.That(value.GetString("missing"), Is.Null);
            Assert.That(value.GetNumber("n"), Is.Null);
            Assert.That(value.GetBool("b"), Is.Null);
            Assert.That(value.GetArrayOrEmpty("a"), Is.Empty);
        }

        [Test]
        public void ObjectMembersKeepWireOrder()
        {
            var value = Json.Parse("{\"z\":1,\"a\":2,\"m\":3}");

            var keys = new List<string>();
            foreach (var member in value.AsObject())
            {
                keys.Add(member.Key);
            }

            Assert.That(keys, Is.EqualTo(new[] { "z", "a", "m" }));
        }

        [Test]
        public void ARepeatedKeyReplacesInPlace()
        {
            var value = Json.Parse("{\"a\":1,\"b\":2,\"a\":3}");

            Assert.That(value.AsObject().Count, Is.EqualTo(2));
            Assert.That(value.AsObject()[0].Key, Is.EqualTo("a"));
            Assert.That(value.GetNumber("a"), Is.EqualTo(3d));
        }

        [Test]
        public void ReadingTheWrongKindThrows()
        {
            var value = JsonValue.Of("text");

            var error = Assert.Throws<JsonKindException>(() => value.AsNumber());
            Assert.That(error!.Expected, Is.EqualTo(JsonKind.Number));
            Assert.That(error.Actual, Is.EqualTo(JsonKind.String));
        }

        [Test]
        public void EqualityIsStructuralAndIgnoresMemberOrder()
        {
            Assert.That(Json.Parse("{\"a\":1,\"b\":[1,2]}"), Is.EqualTo(Json.Parse("{\"b\":[1,2],\"a\":1}")));
            Assert.That(Json.Parse("{\"a\":1}"), Is.Not.EqualTo(Json.Parse("{\"a\":2}")));
            Assert.That(Json.Parse("[1,2]"), Is.Not.EqualTo(Json.Parse("[2,1]")));
            Assert.That(JsonValue.Null, Is.Not.EqualTo(JsonValue.Of(false)));
        }

        [Test]
        public void AsInt32RefusesAFractionalNumber()
        {
            Assert.That(JsonValue.Of(200d).AsInt32(), Is.EqualTo(200));
            Assert.Throws<JsonKindException>(() => JsonValue.Of(1.5).AsInt32());
        }

        [Test]
        public void ArrayAndObjectRefuseNullReferences()
        {
            Assert.Throws<System.ArgumentException>(() => JsonValue.ArrayOf(JsonValue.Of(1d), null!));
            Assert.Throws<System.ArgumentNullException>(() => JsonValue.Of((string)null!));
        }
    }
}
