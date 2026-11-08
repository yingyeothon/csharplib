using NUnit.Framework;

namespace Yingyeothon.Tracer.Tests
{
    [TestFixture]
    public class TracerDsnTests
    {
        [Test]
        public void AnHttpsDsnGivesTheKeyTheIdAndTheEnvelopeEndpoint()
        {
            Assert.That(TracerDsn.TryParse(Fixture.Dsn, false, out var dsn), Is.True);
            Assert.That(dsn!.WriteKey, Is.EqualTo(Fixture.WriteKey));
            Assert.That(dsn.ResourceId, Is.EqualTo("trc_fixture01"));
            Assert.That(dsn.EnvelopeUri.AbsoluteUri, Is.EqualTo("https://trace.invalid/api/trc_fixture01/envelope/"));
        }

        [Test]
        public void APortAndAPathPrefixAreKept()
        {
            Assert.That(TracerDsn.TryParse("https://" + Fixture.WriteKey + "@trace.invalid:8443/base/x/trc_abc", false, out var dsn), Is.True);
            Assert.That(dsn!.EnvelopeUri.AbsoluteUri, Is.EqualTo("https://trace.invalid:8443/base/x/api/trc_abc/envelope/"));
        }

        [Test]
        public void SurroundingWhitespaceIsIgnored()
            => Assert.That(TracerDsn.TryParse("  " + Fixture.Dsn + "\n", false, out _), Is.True);

        [TestCase("http://fixturewritekeyfixturewritekey00@127.0.0.1:8095/trc_abc", "http://127.0.0.1:8095/api/trc_abc/envelope/")]
        [TestCase("http://fixturewritekeyfixturewritekey00@localhost:8095/trc_abc", "http://localhost:8095/api/trc_abc/envelope/")]
        [TestCase("http://fixturewritekeyfixturewritekey00@[::1]:8095/trc_abc", "http://[::1]:8095/api/trc_abc/envelope/")]
        [TestCase("http://fixturewritekeyfixturewritekey00@10.0.2.2:8095/trc_abc", "http://10.0.2.2:8095/api/trc_abc/envelope/")]
        public void PlainHttpIsAcceptedOnlyForALoopbackHostWhenAllowed(string text, string envelope)
        {
            Assert.That(TracerDsn.TryParse(text, false, out _), Is.False, "off by default");
            Assert.That(TracerDsn.TryParse(text, true, out var dsn), Is.True);
            Assert.That(dsn!.EnvelopeUri.AbsoluteUri, Is.EqualTo(envelope));
        }

        [TestCase("http://fixturewritekeyfixturewritekey00@trace.invalid/trc_abc")]
        [TestCase("http://fixturewritekeyfixturewritekey00@192.168.0.2/trc_abc")]
        [TestCase("http://fixturewritekeyfixturewritekey00@127.0.0.1.example/trc_abc")]
        public void PlainHttpToAnyOtherHostIsRefusedEvenWhenAllowed(string text)
            => Assert.That(TracerDsn.TryParse(text, true, out _), Is.False);

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not a url")]
        [TestCase("ftp://fixturewritekeyfixturewritekey00@trace.invalid/trc_abc")]
        [TestCase("https://trace.invalid/trc_abc")]
        [TestCase("https://shortkey@trace.invalid/trc_abc")]
        [TestCase("https://fixturewritekey,fixturewritekey00@trace.invalid/trc_abc")]
        [TestCase("https://fixturewritekey%22fixturewritekey00@trace.invalid/trc_abc")]
        [TestCase("https://fixturewritekey%20fixturewritekey00@trace.invalid/trc_abc")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/7")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/trc_")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/trc_ABC")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/trc_abc?x=1")]
        [TestCase("https://fixturewritekeyfixturewritekey00@trace.invalid/trc_abc#f")]
        public void AMalformedDsnIsRefusedWithoutThrowing(string? text)
        {
            Assert.That(TracerDsn.TryParse(text, true, out var dsn), Is.False);
            Assert.That(dsn, Is.Null);
        }

        [Test]
        public void ToStringNeverPrintsTheKey()
        {
            TracerDsn.TryParse(Fixture.Dsn, false, out var dsn);
            Assert.That(dsn!.ToString(), Is.EqualTo("TracerDsn(trc_fixture01)"));
        }
    }
}
