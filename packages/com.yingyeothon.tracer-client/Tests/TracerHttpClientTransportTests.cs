using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.Tracer.Tests
{
    /// <summary>The real transport, and a whole client over it, against a local <see cref="HttpListener"/>.</summary>
    [TestFixture]
    [Category("Integration")]
    public class TracerHttpClientTransportTests
    {
        private HttpListener? _listener;
        private string _origin = string.Empty;
        private Func<HttpListenerRequest, (int status, Dictionary<string, string> headers)>? _answer;

        [SetUp]
        public void Start()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var port = 20000 + new Random().Next(20000);
                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                try
                {
                    listener.Start();
                }
                catch (HttpListenerException)
                {
                    continue;
                }

                _listener = listener;
                _origin = "http://127.0.0.1:" + port;
                _ = ServeAsync(listener);
                return;
            }

            Assert.Ignore("no free port for HttpListener");
        }

        [TearDown]
        public void Stop()
        {
            _listener?.Stop();
            _listener?.Close();
        }

        private async Task ServeAsync(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var (status, headers) = _answer!(context.Request);
                context.Response.StatusCode = status;
                foreach (var header in headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                try
                {
                    var body = System.Text.Encoding.UTF8.GetBytes("{\"id\":\"\"}");
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // The client went away.
                }
            }
        }

        private static byte[] Read(HttpListenerRequest request)
        {
            using (var buffer = new MemoryStream())
            {
                request.InputStream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        [Test]
        public async Task SendsTheHeadersAndBodyAndReadsStatusAndRetryAfter()
        {
            string? contentType = null, encoding = null, auth = null;
            byte[]? body = null;
            _answer = request =>
            {
                contentType = request.ContentType;
                encoding = request.Headers["Content-Encoding"];
                auth = request.Headers["X-Sentry-Auth"];
                body = Read(request);
                return (429, new Dictionary<string, string> { ["Retry-After"] = "300" });
            };

            var reply = await TracerHttpClientTransport.Default.SendAsync(
                new TracerHttpRequest(
                    new Uri(_origin + "/api/trc_x/envelope/"),
                    new[]
                    {
                        new KeyValuePair<string, string>("Content-Type", "application/x-sentry-envelope"),
                        new KeyValuePair<string, string>("X-Sentry-Auth", "Sentry sentry_key=k"),
                        new KeyValuePair<string, string>("Content-Encoding", "gzip"),
                    },
                    new byte[] { 1, 2, 3 },
                    TimeSpan.FromSeconds(5)),
                CancellationToken.None);

            Assert.That(contentType, Is.EqualTo("application/x-sentry-envelope"));
            Assert.That(encoding, Is.EqualTo("gzip"));
            Assert.That(auth, Is.EqualTo("Sentry sentry_key=k"));
            Assert.That(body, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(reply.Status, Is.EqualTo(429));
            Assert.That(reply.RetryAfter, Is.EqualTo("300"));
        }

        [Test]
        public async Task ARedirectIsNotFollowed()
        {
            var hits = 0;
            _answer = _ =>
            {
                hits++;
                return (307, new Dictionary<string, string> { ["Location"] = _origin + "/elsewhere" });
            };

            var reply = await TracerHttpClientTransport.Default.SendAsync(
                new TracerHttpRequest(new Uri(_origin + "/api/trc_x/envelope/"), new KeyValuePair<string, string>[0], new byte[1], TimeSpan.FromSeconds(5)),
                CancellationToken.None);

            Assert.That(reply.Status, Is.EqualTo(307));
            Assert.That(hits, Is.EqualTo(1));
        }

        [Test]
        public async Task AClientDeliversAReportOverHttp()
        {
            var bodies = new List<byte[]>();
            _answer = request =>
            {
                bodies.Add(FakeTransport.Gunzip(Read(request)));
                return (200, new Dictionary<string, string>());
            };
            var client = TracerClient.Create(new TracerOptions
            {
                Dsn = "http://" + Fixture.WriteKey + "@" + _origin.Substring("http://".Length) + "/trc_fixture01",
                AllowHttpLoopback = true,
            });
            Assert.That(client.State, Is.EqualTo(TracerState.Active));
            client.Report("over http");
            var flush = client.FlushAsync();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!flush.IsCompleted && DateTime.UtcNow < deadline)
            {
                client.Poll();
                await Task.Delay(10);
            }

            Assert.That(flush.IsCompleted, Is.True);
            Assert.That(client.Stats.EnvelopesSent, Is.EqualTo(1));
            Assert.That(ParsedEnvelope.Parse(bodies[0]).Items[0].Json.GetString("message"), Is.EqualTo("over http"));
            client.Dispose();
        }

        [Test]
        public void ACallerOwnedClientIsUsedAsItIs()
        {
            Assert.That(() => TracerHttpClientTransport.Create(null!), Throws.ArgumentNullException);
            Assert.That(TracerHttpClientTransport.Create(new HttpClient()).ToString(), Is.EqualTo("TracerHttpClientTransport"));
            Assert.That(TracerHttpClientTransport.Default.ToString(), Is.EqualTo("TracerHttpClientTransport.Default"));
        }
    }
}
