using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.KvStore.Tests
{
    /// <summary>
    /// The real transport against a local <see cref="HttpListener"/>. Plain HTTP only —
    /// Mono's listener cannot accept a WebSocket, but a request it can.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    public class HttpClientTransportTests
    {
        private HttpListener? _listener;
        private string _origin = string.Empty;
        private Func<HttpListenerRequest, (int status, string body, Dictionary<string, string> headers)>? _answer;
        private Task? _serving;

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
                _serving = ServeAsync(listener);
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

                var (status, body, headers) = _answer!(context.Request);
                context.Response.StatusCode = status;
                foreach (var header in headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }

        [Test]
        public async Task SendsMethodHeadersAndBodyAndReadsTheReplyBack()
        {
            string? method = null, authorization = null, contentType = null, ifMatch = null, body = null, path = null;
            _answer = request =>
            {
                method = request.HttpMethod;
                authorization = request.Headers["Authorization"];
                contentType = request.ContentType;
                ifMatch = request.Headers["If-Match"];
                path = request.Url!.PathAndQuery;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }

                return (201, string.Empty, new Dictionary<string, string> { ["ETag"] = "\"1\"", ["X-KV-Expires-At"] = "1700000000" });
            };

            var reply = await HttpClientTransport.Default.SendAsync(
                new HttpCall(
                    "PUT",
                    new Uri(_origin + "/kv/profile/u/me/entries/a%20key?ttl=60"),
                    new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("Authorization", "Bearer " + KvHarness.Token),
                        new KeyValuePair<string, string>("Content-Type", "application/json"),
                        new KeyValuePair<string, string>("If-Match", "\"3\""),
                    },
                    "{\"v\":1}",
                    TimeSpan.FromSeconds(5)),
                CancellationToken.None);

            Assert.That(method, Is.EqualTo("PUT"));
            Assert.That(path, Is.EqualTo("/kv/profile/u/me/entries/a%20key?ttl=60"), "escapes survive the wire");
            Assert.That(authorization, Is.EqualTo("Bearer " + KvHarness.Token));
            Assert.That(contentType, Does.StartWith("application/json"));
            Assert.That(ifMatch, Is.EqualTo("\"3\""));
            Assert.That(body, Is.EqualTo("{\"v\":1}"));
            Assert.That(reply.Status, Is.EqualTo(201));
            Assert.That(reply.Body, Is.Empty);
            Assert.That(KvHeaderOf(reply, "etag"), Is.EqualTo("\"1\""));
            Assert.That(KvHeaderOf(reply, "x-kv-expires-at"), Is.EqualTo("1700000000"));
        }

        [Test]
        public async Task AFailureStatusIsAReplyNotAThrow()
        {
            _answer = _ => (409, "{\"error\":{\"code\":\"conflict\"}}", new Dictionary<string, string>());

            var reply = await HttpClientTransport.Default.SendAsync(
                new HttpCall("GET", new Uri(_origin + "/kv/c/entries/k"), KvReplyHeaders.None, null, TimeSpan.FromSeconds(5)),
                CancellationToken.None);

            Assert.That(reply.Status, Is.EqualTo(409));
            Assert.That(reply.Body, Is.EqualTo("{\"error\":{\"code\":\"conflict\"}}"));
        }

        [Test]
        public async Task ARedirectIsNotFollowed()
        {
            var hits = 0;
            _answer = _ =>
            {
                hits++;
                return (302, string.Empty, new Dictionary<string, string> { ["Location"] = _origin + "/elsewhere" });
            };

            var reply = await HttpClientTransport.Default.SendAsync(
                new HttpCall("GET", new Uri(_origin + "/kv/c/entries/k"), KvReplyHeaders.None, null, TimeSpan.FromSeconds(5)),
                CancellationToken.None);

            Assert.That(reply.Status, Is.EqualTo(302));
            Assert.That(hits, Is.EqualTo(1));
        }

        [Test]
        public async Task TheWholeClientRoundTripsThroughTheRealTransport()
        {
            string? authorization = null;
            _answer = request =>
            {
                authorization = request.Headers["Authorization"];
                return (200, "{\"volume\":0.5}", new Dictionary<string, string> { ["ETag"] = "\"2\"" });
            };
            var client = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = _origin, Token = KvHarness.Token });

            var entry = await client.Collection("profile").Mine.GetEntryAsync("settings");

            Assert.That(authorization, Is.EqualTo("Bearer " + KvHarness.Token));
            Assert.That(entry!.Version, Is.EqualTo(2));
            Assert.That(entry.Value.GetNumber("volume"), Is.EqualTo(0.5));
        }

        [Test]
        public void ACallerOwnedClientIsUsedAsItIs()
        {
            Assert.That(() => HttpClientTransport.Create(null!), Throws.ArgumentNullException);
            Assert.That(HttpClientTransport.Create(new HttpClient()), Is.Not.Null);
            Assert.That(HttpClientTransport.Create(new HttpClient()).ToString(), Is.EqualTo("HttpClientTransport"));
            Assert.That(HttpClientTransport.Default.ToString(), Is.EqualTo("HttpClientTransport.Default"));
        }

        [Test]
        public async Task ANullCallIsRefused()
        {
            try
            {
                await HttpClientTransport.Default.SendAsync(null!, CancellationToken.None);
                Assert.Fail("expected ArgumentNullException");
            }
            catch (ArgumentNullException)
            {
            }
        }

        [Test]
        public async Task AHostThatDoesNotAnswerIsANetworkFailureThroughTheClient()
        {
            _listener!.Stop();
            var client = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = _origin, Token = KvHarness.Token });

            var error = await Fails.WithKvStoreException(() => client.Collection("profile").GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.Network));
            Assert.That(error.InnerException, Is.InstanceOf<HttpRequestException>());
        }

        private static string? KvHeaderOf(HttpReply reply, string name)
        {
            foreach (var header in reply.Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }
    }
}
