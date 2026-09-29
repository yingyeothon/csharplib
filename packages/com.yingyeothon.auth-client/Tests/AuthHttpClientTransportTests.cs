using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.Auth.Tests
{
    /// <summary>The real transport against a local <see cref="HttpListener"/>, plain HTTP.</summary>
    [TestFixture]
    [Category("Integration")]
    public class AuthHttpClientTransportTests
    {
        private HttpListener? _listener;
        private string _origin = string.Empty;
        private Func<HttpListenerRequest, (int status, byte[] body, Dictionary<string, string> headers)>? _answer;

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

                var (status, body, headers) = _answer!(context.Request);
                context.Response.StatusCode = status;
                foreach (var header in headers)
                {
                    context.Response.Headers[header.Key] = header.Value;
                }

                try
                {
                    context.Response.ContentLength64 = body.Length;
                    await context.Response.OutputStream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    context.Response.Close();
                }
                catch (Exception)
                {
                    // The client stopped reading an oversize body; that is the point.
                }
            }
        }

        private AuthHttpRequest Request(string method, string path, string? body = null, params KeyValuePair<string, string>[] headers)
            => new AuthHttpRequest(method, new Uri(_origin + path), headers, body, TimeSpan.FromSeconds(5));

        [Test]
        public async Task SendsTheBodyAndHeadersAndReadsTheReplyBack()
        {
            string? method = null, contentType = null, authorization = null, body = null;
            _answer = request =>
            {
                method = request.HttpMethod;
                contentType = request.ContentType;
                authorization = request.Headers["Authorization"];
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = reader.ReadToEnd();
                }

                return (200, Encoding.UTF8.GetBytes("{\"jwt\":\"j\"}"), new Dictionary<string, string>());
            };

            var reply = await AuthHttpClientTransport.Default.SendAsync(
                Request(
                    "POST",
                    "/c/auth_1/token",
                    "{\"provider\":\"github\"}",
                    new KeyValuePair<string, string>("Content-Type", "application/json"),
                    new KeyValuePair<string, string>("Authorization", "Bearer x")),
                CancellationToken.None);

            Assert.That(method, Is.EqualTo("POST"));
            Assert.That(contentType, Does.StartWith("application/json"));
            Assert.That(authorization, Is.EqualTo("Bearer x"));
            Assert.That(body, Is.EqualTo("{\"provider\":\"github\"}"));
            Assert.That(reply.Status, Is.EqualTo(200));
            Assert.That(reply.Body, Is.EqualTo("{\"jwt\":\"j\"}"));
        }

        [Test]
        public async Task AFailureStatusIsAReplyNotAThrow()
        {
            _answer = _ => (401, Encoding.UTF8.GetBytes("{\"error\":\"no\"}"), new Dictionary<string, string>());

            var reply = await AuthHttpClientTransport.Default.SendAsync(Request("GET", "/c/auth_1/verify"), CancellationToken.None);

            Assert.That(reply.Status, Is.EqualTo(401));
        }

        [Test]
        public async Task ARedirectIsNotFollowed()
        {
            var hits = 0;
            _answer = _ =>
            {
                hits++;
                return (307, new byte[0], new Dictionary<string, string> { ["Location"] = _origin + "/elsewhere" });
            };

            var reply = await AuthHttpClientTransport.Default.SendAsync(Request("POST", "/c/auth_1/token", "{}"), CancellationToken.None);

            Assert.That(reply.Status, Is.EqualTo(307));
            Assert.That(hits, Is.EqualTo(1));
        }

        [Test]
        public async Task AReplyOverTheCapIsRefusedAsItStreams()
        {
            _answer = _ => (200, new byte[1024 * 1024 + 1], new Dictionary<string, string>());

            try
            {
                await AuthHttpClientTransport.Default.SendAsync(Request("GET", "/c/auth_1/.well-known/config"), CancellationToken.None);
                Assert.Fail("expected IOException");
            }
            catch (IOException error)
            {
                Assert.That(error.Message, Does.Not.Contain("127.0.0.1"));
            }
        }

        [Test]
        public async Task TheWholeClientSurfacesAnOversizeReplyAsNetwork()
        {
            _answer = _ => (200, new byte[1024 * 1024 + 1], new Dictionary<string, string>());
            var client = AuthClient.Create(new AuthClientOptions { BaseUrl = _origin, ChannelId = "auth_1" });

            var error = await Fails.WithAuthException(() => client.FetchConfigAsync());

            Assert.That(error.Code, Is.EqualTo(AuthErrorCodes.Network));
        }

        [Test]
        public void ACallerOwnedClientIsUsedAsItIs()
        {
            Assert.That(() => AuthHttpClientTransport.Create(null!), Throws.ArgumentNullException);
            Assert.That(AuthHttpClientTransport.Create(new HttpClient()).ToString(), Is.EqualTo("AuthHttpClientTransport"));
            Assert.That(AuthHttpClientTransport.Default.ToString(), Is.EqualTo("AuthHttpClientTransport.Default"));
        }
    }
}
