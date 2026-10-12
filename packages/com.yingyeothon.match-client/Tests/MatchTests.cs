using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.KvStore;
using Yingyeothon.Platform;

namespace Yingyeothon.Match.Tests
{
    [TestFixture]
    public class MatchTests
    {
        private const string Matched = "{\"matchId\":\"match_fixture\",\"partial\":false,\"result\":null,\"members\":[{\"userId\":\"a\"},{\"userId\":\"b\"}],\"future\":true}";
        private sealed class Transport : IHttpTransport
        {
            internal HttpCall? Call; internal int Status = 200; internal string Body = "{\"state\":\"waiting\",\"position\":1,\"waited\":0}";
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct) { Call = call; return Task.FromResult(new HttpReply(Status, Array.Empty<KeyValuePair<string, string>>(), Body)); }
        }
        [Test]
        public void TicketCommandsPreserveShapeAndWaitForPoll()
        {
            var t = new Transport(); using var c = DeferredMatchClient.Create(new DeferredMatchClientOptions { ChannelId = "match_fixture", Http = new PlatformClientOptions { BaseUrl = "https://example.test", Token = "fixture", Transport = t } });
            var task = c.SubmitAsync(); Assert.That(task.IsCompleted, Is.False); c.Poll(); Assert.That(task.Result.State, Is.EqualTo(MatchTicketState.Waiting));
            Assert.That(t.Call!.Url.AbsolutePath, Is.EqualTo("/m/match_fixture/ticket"));
            t.Body = "{\"state\":\"proposed\",\"matchId\":\"m\",\"acceptBy\":123,\"accepted\":true,\"partial\":false}";
            task = c.AcceptAsync(); c.Poll(); Assert.That(task.Result.State, Is.EqualTo(MatchTicketState.Proposed));
            task = c.DeclineAsync(); c.Poll(); Assert.That(task.Result.State, Is.EqualTo(MatchTicketState.Proposed));
            t.Status = 204; var cancel = c.CancelAsync(); c.Poll(); Assert.That(cancel.IsCompletedSuccessfully, Is.True);
            t.Status = 404; var status = c.StatusAsync(); c.Poll(); Assert.That(status.Result, Is.Null);
            t.Status = 429; t.Body = "{\"error\":{\"code\":\"rate_limited\",\"details\":{\"reason\":\"cooldown\",\"retryAfter\":5}}}";
            task = c.SubmitAsync(); c.Poll(); Assert.That(((PlatformException)task.Exception!.InnerException!).RetryAfterMillis, Is.EqualTo(5000));
        }
        [TestCase("{\"state\":\"confirmed\",\"matchId\":\"m\",\"partial\":false,\"result\":null,\"members\":[]}", MatchTicketState.Confirmed)]
        [TestCase("{\"state\":\"expired\",\"reason\":\"accept\"}", MatchTicketState.Expired)]
        [TestCase("{\"state\":\"declined\",\"matchId\":\"m\"}", MatchTicketState.Declined)]
        [TestCase("{\"state\":\"failed\",\"reason\":\"closed\"}", MatchTicketState.Failed)]
        public void TerminalStatesAreReadable(string json, MatchTicketState state) => Assert.That(new MatchTicket(Json.Parse(json)).State, Is.EqualTo(state));
        [TestCase("{}")]
        [TestCase("{\"state\":\"waiting\",\"position\":1.5,\"waited\":0}")]
        [TestCase("{\"state\":\"proposed\",\"matchId\":\"m\",\"acceptBy\":1}")]
        [TestCase("{\"state\":\"expired\",\"reason\":\"unknown\"}")]
        [TestCase("{\"state\":\"failed\",\"reason\":\"unknown\"}")]
        public void MalformedTicketsFailSafely(string json) => Assert.That(() => new MatchTicket(Json.Parse(json)), Throws.TypeOf<PlatformException>());
        private sealed class Clock : IClock { public double NowMillis { get; set; } }
        private sealed class Socket : IWebSocket, IWebSocketFactory
        {
            internal WebSocketCreateContext? Context; internal int Disposals;
            public IWebSocket Create(WebSocketCreateContext context) { Context = context; return this; }
            public void Start() => Context!.Sink.Post(SocketEvent.Opened(this, "bearer"));
            public void SendText(string text) { }
            public void Close(int code, string reason) => Context!.Sink.Post(SocketEvent.Closed(this, code, reason));
            public void Dispose() => Disposals++;
            internal void Message(string text) => Context!.Sink.Post(SocketEvent.Message(this, text));
        }
        private static ILiveMatchClient Live(Socket socket, Clock? clock = null) => LiveMatchClient.Create(new LiveMatchClientOptions { Url = "wss://example.test", ChannelId = "match_fixture", Token = "fixture", WebSocketFactory = socket, Clock = clock });
        [Test]
        public void LiveMatchedKeepsMembersFutureFieldsAndRetiresSocket()
        {
            var s = new Socket(); using var c = Live(s); var task = c.FindAsync();
            Assert.That(c.FindAsync(), Is.SameAs(task));
            s.Message("{\"type\":\"waiting\"}"); s.Message("{\"type\":\"matched\"," + Matched.Substring(1));
            Assert.That(task.IsCompleted, Is.False); c.Poll();
            Assert.That(task.Result.Members.Count, Is.EqualTo(2)); Assert.That(task.Result.Raw.GetBool("future"), Is.True);
            Assert.That(s.Disposals, Is.EqualTo(1));
            Assert.That(s.Context!.Url, Does.Not.Contain("fixture?token")); Assert.That(s.Context.SubProtocols[1], Is.EqualTo("fixture"));
        }
        [TestCase("bad", "bad_body")]
        [TestCase("{\"type\":\"matched\"}", "bad_body")]
        [TestCase("{\"type\":\"failed\"}", "unavailable")]
        [TestCase("{\"type\":\"replaced\"}", "cancelled")]
        public void LiveFailureIsPumped(string frame, string code)
        {
            var s = new Socket(); using var c = Live(s); var task = c.FindAsync(); s.Message(frame); c.Poll();
            Assert.That(((PlatformException)task.Exception!.InnerException!).Code, Is.EqualTo(code));
            Assert.That(s.Disposals, Is.EqualTo(1));
        }
        [Test]
        public void CancellationTimeoutAndDisposeSettleOnlyAtPoll()
        {
            var s = new Socket(); var clock = new Clock(); using var c = Live(s, clock); using var cancel = new CancellationTokenSource();
            var task = c.FindAsync(cancel.Token); cancel.Cancel(); Assert.That(task.IsCompleted, Is.False); c.Poll(); Assert.That(task.IsFaulted, Is.True);
            task = c.FindAsync(); clock.NowMillis = 120000; c.Poll(); Assert.That(((PlatformException)task.Exception!.InnerException!).Code, Is.EqualTo("timeout"));
            task = c.FindAsync(); c.Dispose(); c.Poll(); Assert.That(task.IsFaulted, Is.True);
        }
    }
}
