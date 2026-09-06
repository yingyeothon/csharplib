using NUnit.Framework;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvRulesTests
    {
        [TestCase("a", true)]
        [TestCase("settings", true)]
        [TestCase("A.b_c:d-9", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase(".hidden", false)]
        [TestCase("-dash", false)]
        [TestCase("a/b", false)]
        [TestCase("a b", false)]
        [TestCase("a@b", false)]
        [TestCase("한글", false)]
        public void KeyGrammar(string? key, bool ok)
            => Assert.That(KvRules.IsKey(key), Is.EqualTo(ok));

        [Test]
        public void KeyLengthBoundary()
        {
            Assert.That(KvRules.IsKey("k" + new string('x', 127)), Is.True, "128 is the longest");
            Assert.That(KvRules.IsKey("k" + new string('x', 128)), Is.False, "129 is over");
        }

        [TestCase("announcements", true)]
        [TestCase("my.col_1-x", true)]
        [TestCase("kv_01h5xk4r7z3b2m9q8w6e5t4y3n", true)]
        [TestCase("KV_01H5XK4R7Z3B2M9Q8W6E5T4Y3N", false, Description = "id-shaped once lower-cased")]
        [TestCase("kv_01h5xk4r7z3b2m9q8w6e5t4y3nx", true, Description = "one character too long to be an id is a name")]
        [TestCase("kv_notices", true, Description = "a kv_ prefix alone is a name the store looks up")]
        [TestCase("a:b", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("_lead", false)]
        [TestCase("a/b", false)]
        public void CollectionRefGrammar(string? reference, bool ok)
            => Assert.That(KvRules.IsCollectionRef(reference), Is.EqualTo(ok));

        [Test]
        public void CollectionNameLengthBoundary()
        {
            Assert.That(KvRules.IsCollectionRef(new string('n', 64)), Is.True);
            Assert.That(KvRules.IsCollectionRef(new string('n', 65)), Is.False);
        }

        [TestCase("kv_01h5xk4r7z3b2m9q8w6e5t4y3n", true)]
        [TestCase("kv_01H5XK4R7Z3B2M9Q8W6E5T4Y3N", false, Description = "an id is lower-case; upper-case is a name the server refuses")]
        [TestCase("kv_short", false)]
        [TestCase("doc_01h5xk4r7z3b2m9q8w6e5t4y3n", false)]
        [TestCase(null, false)]
        public void CollectionIdShape(string? reference, bool ok)
            => Assert.That(KvRules.IsCollectionId(reference), Is.EqualTo(ok));

        [TestCase("me", true)]
        [TestCase("0123456789abcdef0123456789abcdef", true)]
        [TestCase("party:abc-DEF_9", true)]
        [TestCase("guild:x", true)]
        [TestCase("0123456789ABCDEF0123456789abcdef", false)]
        [TestCase("0123456789abcdef0123456789abcde", false)]
        [TestCase("Party:abc", false)]
        [TestCase("toolongkind:abc", false)]
        [TestCase("party:", false)]
        [TestCase(":abc", false)]
        [TestCase("party:a/b", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OwnerIdGrammar(string? owner, bool ok)
            => Assert.That(KvRules.IsOwnerId(owner), Is.EqualTo(ok));

        [Test]
        public void GroupOwnerIdLengthBoundary()
        {
            Assert.That(KvRules.IsOwnerId("guild:" + new string('a', 48)), Is.True);
            Assert.That(KvRules.IsOwnerId("guild:" + new string('a', 49)), Is.False);
            Assert.That(KvRules.IsOwnerId(new string('k', 8) + ":a"), Is.True);
            Assert.That(KvRules.IsOwnerId(new string('k', 9) + ":a"), Is.False);
        }

        [Test]
        public void ConstantsAreTheServers()
        {
            Assert.That(KvRules.MaxValueBytes, Is.EqualTo(16384));
            Assert.That(KvRules.MaxTtlSeconds, Is.EqualTo(31622400));
            Assert.That(KvRules.MaxListLimit, Is.EqualTo(100));
            Assert.That(KvRules.MaxSafeInteger, Is.EqualTo(9007199254740991L));
        }
    }
}
