using System;
using NUnit.Framework;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class GatewayUrlTests
    {
        [Test]
        public void AppendsTheChannelToABareOrigin()
        {
            Assert.That(GatewayUrl.Build("wss://gw.test", "ch_lobby"), Is.EqualTo("wss://gw.test/?channel=ch_lobby"));
            Assert.That(GatewayUrl.Build("wss://gw.test/", "ch_lobby"), Is.EqualTo("wss://gw.test/?channel=ch_lobby"));
        }

        [Test]
        public void AddsTheGameIdOnlyWhenGiven()
        {
            Assert.That(
                GatewayUrl.Build("wss://gw.test", "q_dungeon", "g_1"),
                Is.EqualTo("wss://gw.test/?channel=q_dungeon&gameId=g_1"));
            Assert.That(GatewayUrl.Build("wss://gw.test", "q_dungeon"), Does.Not.Contain("gameId"));
        }

        [Test]
        public void KeepsAQueryStringTheCallerAlreadyPutOnTheUrl()
        {
            Assert.That(
                GatewayUrl.Build("wss://gw.test/path?a=1", "ch_lobby"),
                Is.EqualTo("wss://gw.test/path?a=1&channel=ch_lobby"));
        }

        [Test]
        public void ReplacesAnExistingChannelRatherThanDuplicatingIt()
        {
            // The gateway reads one `channel`; a duplicate would silently connect to
            // whichever it happened to pick.
            var url = GatewayUrl.Build("wss://gw.test/?channel=stale&a=1", "ch_lobby");

            Assert.That(url, Is.EqualTo("wss://gw.test/?channel=ch_lobby&a=1"));
        }

        [Test]
        public void EscapesValuesThatNeedIt()
        {
            Assert.That(GatewayUrl.Build("wss://gw.test", "a b&c=d"), Does.Contain("channel=a%20b%26c%3Dd"));
        }

        [Test]
        public void RefusesNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => GatewayUrl.Build(null!, "c"));
            Assert.Throws<ArgumentNullException>(() => GatewayUrl.Build("wss://gw.test", null!));
        }
    }
}
