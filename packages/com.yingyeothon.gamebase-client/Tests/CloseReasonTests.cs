using System;
using System.Text;
using NUnit.Framework;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class CloseReasonTests
    {
        private static IWebSocket Create(CollectingSink sink)
            => WebSocketTransport.Default.Create(new WebSocketCreateContext(
                "ws://127.0.0.1:1/",
                new[] { "bearer", "t" },
                sink));

        [Test]
        public void ALongCloseReasonIsTruncatedWithoutSplittingASurrogatePair()
        {
            // CloseOutputAsync throws above 123 UTF-8 bytes, and a cut that leaves a
            // lone surrogate encodes as U+FFFD, which reads as corruption rather than
            // as a truncation.
            var sink = new CollectingSink();
            using var socket = Create(sink);

            // 62 emoji = 62 surrogate pairs = 248 bytes; the cut lands mid-pair.
            var reason = string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 62));
            socket.Close(GatewayCloseCode.Local, reason);

            var posted = sink.TryDequeue(out var closed);

            Assert.That(posted, Is.True);
            Assert.That(Encoding.UTF8.GetByteCount(closed.Reason), Is.LessThanOrEqualTo(123));
            Assert.That(closed.Reason.Length, Is.GreaterThan(0));
            Assert.That(char.IsHighSurrogate(closed.Reason[closed.Reason.Length - 1]), Is.False);
        }

        [Test]
        public void TheFirstLocalCloseWinsBothItsCodeAndItsReason()
        {
            var sink = new CollectingSink();
            using var socket = Create(sink);

            socket.Close(GatewayCloseCode.Local, "unexpected subprotocol");
            socket.Close(1000, "client closed");

            Assert.That(sink.TryDequeue(out var closed), Is.True);
            Assert.That(closed.Code, Is.EqualTo(GatewayCloseCode.Local));
            Assert.That(closed.Reason, Is.EqualTo("unexpected subprotocol"));
        }

        [Test]
        public void AShortReasonIsLeftAlone()
        {
            var sink = new CollectingSink();
            using var socket = Create(sink);

            socket.Close(GatewayCloseCode.Local, "hello timeout");

            Assert.That(sink.TryDequeue(out var closed), Is.True);
            Assert.That(closed.Reason, Is.EqualTo("hello timeout"));
        }
    }
}
