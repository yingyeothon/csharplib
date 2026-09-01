using NUnit.Framework;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class CloseCodesTests
    {
        [TestCase(4000, CloseDispositionKind.Stop, CloseDispositionKind.Stop)]
        [TestCase(4001, CloseDispositionKind.Stop, CloseDispositionKind.Aborted)]
        [TestCase(4002, CloseDispositionKind.Reconnect, CloseDispositionKind.Reconnect)]
        [TestCase(4003, CloseDispositionKind.ClientBug, CloseDispositionKind.ClientBug)]
        [TestCase(4004, CloseDispositionKind.Stop, CloseDispositionKind.Stop)]
        [TestCase(1000, CloseDispositionKind.Stop, CloseDispositionKind.Finished)]
        [TestCase(1001, CloseDispositionKind.Reconnect, CloseDispositionKind.Reconnect)]
        [TestCase(1003, CloseDispositionKind.ClientBug, CloseDispositionKind.ClientBug)]
        [TestCase(1009, CloseDispositionKind.ClientBug, CloseDispositionKind.ClientBug)]
        [TestCase(1011, CloseDispositionKind.Reconnect, CloseDispositionKind.Reconnect)]
        [TestCase(1006, CloseDispositionKind.Reconnect, CloseDispositionKind.Reconnect)]
        [TestCase(4321, CloseDispositionKind.Reconnect, CloseDispositionKind.Reconnect)]
        public void TheDocumentedTableIsHonoured(int code, CloseDispositionKind lobby, CloseDispositionKind q)
        {
            Assert.That(CloseCodes.Classify(code, GatewayChannelKind.Lobby).Kind, Is.EqualTo(lobby));
            Assert.That(CloseCodes.Classify(code, GatewayChannelKind.Q).Kind, Is.EqualTo(q));
        }

        [Test]
        public void AnAbortedOrFinishedRunNeverReconnects()
        {
            Assert.That(CloseCodes.Classify(4001, GatewayChannelKind.Q).Kind, Is.Not.EqualTo(CloseDispositionKind.Reconnect));
            Assert.That(CloseCodes.Classify(1000, GatewayChannelKind.Q).Kind, Is.Not.EqualTo(CloseDispositionKind.Reconnect));
        }

        [Test]
        public void AnUnknownCodeNamesItselfInTheReason()
        {
            Assert.That(CloseCodes.Classify(4321, GatewayChannelKind.Lobby).Reason, Is.EqualTo("connection lost (4321)"));
        }

        [Test]
        public void TheDocumentedConstantsHoldTheirValues()
        {
            Assert.That(GatewayCloseCode.Replaced, Is.EqualTo(4000));
            Assert.That(GatewayCloseCode.Aborted, Is.EqualTo(4001));
            Assert.That(GatewayCloseCode.Idle, Is.EqualTo(4002));
            Assert.That(GatewayCloseCode.Policy, Is.EqualTo(4003));
            Assert.That(GatewayCloseCode.ChannelGone, Is.EqualTo(4004));
            Assert.That(GatewayCloseCode.Local, Is.EqualTo(4900));
        }
    }
}
