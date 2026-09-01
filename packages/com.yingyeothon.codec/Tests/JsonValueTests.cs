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
        public void AsInt32RefusesAFractionalOrOutOfRangeNumber()
        {
            // "Expected a JSON Number but the value is a Number" is not a diagnosis.
            // A fractional value is a *range* problem, not a kind problem, and the
            // two have different fixes for whoever reads the log.
            Assert.That(JsonValue.Of(200d).AsInt32(), Is.EqualTo(200));
            Assert.That(JsonValue.Of((double)int.MaxValue).AsInt32(), Is.EqualTo(int.MaxValue));
            Assert.That(JsonValue.Of((double)int.MinValue).AsInt32(), Is.EqualTo(int.MinValue));
            Assert.That(JsonValue.Of(-0d).AsInt32(), Is.EqualTo(0));

            Assert.Throws<JsonNumberException>(() => JsonValue.Of(1.5).AsInt32());
            Assert.Throws<JsonNumberException>(() => JsonValue.Of(int.MaxValue + 1d).AsInt32());
            Assert.Throws<JsonNumberException>(() => JsonValue.Of(int.MinValue - 1d).AsInt32());
            Assert.Throws<JsonNumberException>(() => JsonValue.Of(2147483647.5).AsInt32());
            Assert.Throws<JsonNumberException>(() => JsonValue.Of(1e300).AsInt32());
        }

        [Test]
        public void AKindErrorStaysAKindErrorAndNeitherCarriesTheValue()
        {
            Assert.Throws<JsonKindException>(() => JsonValue.Of("7").AsInt32());
            Assert.Throws<JsonKindException>(() => JsonValue.Null.AsInt32());

            var error = Assert.Throws<JsonNumberException>(() => JsonValue.Of(1.5).AsInt32());

            // Positive control first: the message has to say something at all.
            Assert.That(error!.Message, Does.Contain("integer"));
            Assert.That(error.Message, Does.Not.Contain("1.5"));
        }

        [Test]
        public void MemberOrderDoesNotAffectEqualityAtAnyDepth()
        {
            var left = Json.Parse("{\"a\":{\"x\":[1,{\"p\":true,\"q\":null}],\"y\":2},\"b\":3}");
            var right = Json.Parse("{\"b\":3,\"a\":{\"y\":2,\"x\":[1,{\"q\":null,\"p\":true}]}}");

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void ObjectsWithTheSameKeysButDifferentValuesAreNotEqual()
        {
            Assert.That(Json.Parse("{\"a\":1,\"b\":2}"), Is.Not.EqualTo(Json.Parse("{\"a\":1,\"b\":3}")));
            Assert.That(Json.Parse("{\"a\":1}"), Is.Not.EqualTo(Json.Parse("{\"a\":1,\"b\":2}")));
            Assert.That(Json.Parse("{\"a\":1}"), Is.Not.EqualTo(Json.Parse("{\"b\":1}")));
            Assert.That(Json.Parse("{}"), Is.Not.EqualTo(Json.Parse("[]")));
            Assert.That(Json.Parse("{}"), Is.EqualTo(Json.Parse("{}")));
            Assert.That(Json.Parse("[]"), Is.EqualTo(Json.Parse("[]")));
        }

        [Test]
        public void ADuplicateKeyCollapsesBeforeEqualitySeesIt()
        {
            Assert.That(Json.Parse("{\"a\":1,\"a\":2}"), Is.EqualTo(Json.Parse("{\"a\":2}")));
            Assert.That(Json.Parse("{\"a\":1,\"a\":2}").GetHashCode(), Is.EqualTo(Json.Parse("{\"a\":2}").GetHashCode()));
        }

        [Test]
        public void EqualityIsReflexiveSymmetricAndSurvivesBoxing()
        {
            var value = Json.Parse("{\"a\":[1,\"x\",null]}");
            object boxed = Json.Parse("{\"a\":[1,\"x\",null]}");

            // Held in a local rather than written inline: `x.Equals(null)` teaches the
            // compiler's null analysis that `x` may be null for the rest of the block.
            JsonValue? absent = null;

            Assert.That(value.Equals(value), Is.True);
            Assert.That(value.Equals(boxed), Is.True);
            Assert.That(value.Equals("not a json value"), Is.False);
            Assert.That(value.Equals(absent), Is.False);
        }

        [TestCase("null")]
        [TestCase("true")]
        [TestCase("1")]
        [TestCase("\"s\"")]
        [TestCase("[1]")]
        public void FieldAccessOnANonObjectAnswersAbsentRatherThanThrowing(string text)
        {
            var value = Json.Parse(text);

            Assert.That(value.TryGetMember("a", out var member), Is.False);
            Assert.That(member, Is.SameAs(JsonValue.Null));
            Assert.That(value.GetMemberOrNull("a"), Is.Null);
            Assert.That(value.GetString("a"), Is.Null);
            Assert.That(value.GetNumber("a"), Is.Null);
            Assert.That(value.GetBool("a"), Is.Null);
            Assert.That(value.GetArrayOrEmpty("a"), Is.Empty);
        }

        [Test]
        public void ATypedGetterOnAMemberOfTheWrongKindAnswersNull()
        {
            var value = Json.Parse("{\"n\":1,\"s\":\"x\",\"b\":true,\"a\":[1]}");

            Assert.That(value.GetString("n"), Is.Null);
            Assert.That(value.GetNumber("s"), Is.Null);
            Assert.That(value.GetBool("a"), Is.Null);
            Assert.That(value.GetArrayOrEmpty("b"), Is.Empty);
            Assert.That(value.TryGetMember("n", out _), Is.True);
        }

        [Test]
        public void ANullKeyIsAbsentRatherThanACrash()
        {
            Assert.That(Json.Parse("{\"a\":1}").TryGetMember(null!, out _), Is.False);
        }

        [Test]
        public void AValueDoesNotChangeWhenTheListItWasBuiltFromDoes()
        {
            var items = new List<JsonValue> { JsonValue.Of(1d) };
            var array = JsonValue.Array(items);
            var members = new List<KeyValuePair<string, JsonValue>>
            {
                new KeyValuePair<string, JsonValue>("a", JsonValue.Of(1d))
            };
            var obj = JsonValue.Object(members);

            items.Add(JsonValue.Of(2d));
            members.Add(new KeyValuePair<string, JsonValue>("b", JsonValue.Of(2d)));

            Assert.That(Json.Stringify(array), Is.EqualTo("[1]"));
            Assert.That(Json.Stringify(obj), Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void ArrayOfStringsRefusesANullArgumentOrElement()
        {
            Assert.Throws<System.ArgumentNullException>(() => Json.ArrayOfStrings(null!));
            Assert.Throws<System.ArgumentNullException>(() => Json.ArrayOfStrings(new string?[] { "a", null }!));
            Assert.That(Json.Stringify(Json.ArrayOfStrings(new[] { "a", "b" })), Is.EqualTo("[\"a\",\"b\"]"));
        }

        [Test]
        public void ABuilderKeepsWhatItAlreadyHasWhenItIsBuiltTwice()
        {
            // Build() snapshots; it does not reset. Pinned because "reuse the builder"
            // is the kind of thing a caller tries once and then relies on.
            var builder = Json.Object().Set("a", 1d);
            var first = builder.Build();
            builder.Set("b", 2d);
            var second = builder.Build();

            Assert.That(Json.Stringify(first), Is.EqualTo("{\"a\":1}"));
            Assert.That(Json.Stringify(second), Is.EqualTo("{\"a\":1,\"b\":2}"));
        }

        [Test]
        public void ABuilderRefusesANullKey()
        {
            Assert.Throws<System.ArgumentNullException>(() => Json.Object().Set(null!, "v"));
            Assert.Throws<System.ArgumentNullException>(() => Json.Object().SetNull(null!));
        }

        [Test]
        public void AnIntOverloadIsTheSameNumberAsItsDouble()
        {
            Assert.That(JsonValue.Of(5), Is.EqualTo(JsonValue.Of(5d)));
            Assert.That(Json.Stringify(JsonValue.Of(int.MinValue)), Is.EqualTo("-2147483648"));
            Assert.That(JsonValue.Of(int.MaxValue).AsInt32(), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ABuilderThatSetsTheSameKeyTwiceKeepsTheLastValueInTheFirstPlace()
        {
            // The builder appends; JsonValue.Object is what collapses. Same rule the
            // parser applies to a duplicate key on the wire, so a frame built locally
            // and the same frame read back agree.
            var frame = Json.Object().Set("a", 1d).Set("b", 2d).Set("a", 3d).Build();

            Assert.That(Json.Stringify(frame), Is.EqualTo("{\"a\":3,\"b\":2}"));
        }

        [Test]
        public void ArrayAndObjectRefuseNullReferences()
        {
            Assert.Throws<System.ArgumentException>(() => JsonValue.ArrayOf(JsonValue.Of(1d), null!));
            Assert.Throws<System.ArgumentNullException>(() => JsonValue.Of((string)null!));
        }
    }
}
