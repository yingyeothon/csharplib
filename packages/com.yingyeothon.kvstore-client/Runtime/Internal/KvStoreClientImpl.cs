using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.KvStore
{
    /// <summary>The route kinds a log line names. Never the path: a key is on it.</summary>
    internal static class KvRoutes
    {
        internal const string Meta = "meta";
        internal const string Entries = "entries";
        internal const string Entry = "entry";
        internal const string Incr = "incr";
    }

    internal sealed class KvStoreClientImpl : IKvStoreClient
    {
        private readonly string _origin;
        private readonly string _authorization;
        private readonly IHttpTransport _transport;
        private readonly ILogger _logger;
        private readonly TimeSpan _timeout;

        internal KvStoreClientImpl(Uri baseUrl, string token, IHttpTransport transport, ILogger logger, TimeSpan timeout)
        {
            _origin = baseUrl.AbsoluteUri.TrimEnd('/');
            // Assembled once, and held only here: no field, no log line and no
            // exception ever sees the token again.
            _authorization = "Bearer " + token;
            _transport = transport;
            _logger = logger;
            _timeout = timeout;
        }

        public IKvCollection Collection(string nameOrId)
        {
            if (!KvRules.IsCollectionRef(nameOrId))
            {
                throw new ArgumentException(
                    "nameOrId is neither a kv_ collection id nor a name the console accepts", nameof(nameOrId));
            }

            return new KvCollectionImpl(this, nameOrId);
        }

        /// <summary>
        /// Sends one request with the credential attached and returns whatever the
        /// store answered. A throw from the transport, or a timeout, becomes a
        /// <see cref="KvStoreException"/>; the caller's own cancellation passes through.
        /// </summary>
        internal async Task<HttpReply> SendAsync(
            string method,
            string route,
            string pathAndQuery,
            string? body,
            List<KeyValuePair<string, string>>? headers,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var all = new List<KeyValuePair<string, string>>(3 + (headers?.Count ?? 0))
            {
                new KeyValuePair<string, string>("Authorization", _authorization),
            };
            if (body != null)
            {
                all.Add(new KeyValuePair<string, string>("Content-Type", "application/json"));
            }

            if (headers != null)
            {
                all.AddRange(headers);
            }

            var call = new HttpCall(method, new Uri(_origin + pathAndQuery, UriKind.Absolute), all, body, _timeout);

            HttpReply reply;
            using (var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bound.CancelAfter(_timeout);
                try
                {
                    reply = await _transport.SendAsync(call, bound.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException error)
                {
                    // Ours fired, or the transport's own timeout did: either way no
                    // reply came in time and the caller did not ask to stop.
                    Failed(method, route, KvErrorCodes.Timeout);
                    throw new KvStoreException(0, KvErrorCodes.Timeout, null, null, false, error);
                }
                catch (Exception error)
                {
                    Failed(method, route, KvErrorCodes.Network);
                    throw new KvStoreException(0, KvErrorCodes.Network, null, null, false, error);
                }
            }

            if (reply == null)
            {
                Failed(method, route, KvErrorCodes.Network);
                throw new KvStoreException(0, KvErrorCodes.Network);
            }

            if (_logger.IsEnabled(LogSeverity.Debug))
            {
                _logger.Debug(
                    "kv request",
                    Json.Object()
                        .Set("method", method)
                        .Set("route", route)
                        .Set("status", (double)reply.Status)
                        .Set("bytes", (double)Encoding.UTF8.GetByteCount(reply.Body))
                        .Build());
            }

            // Checked before anything reads it: a transport without a buffer cap of
            // its own would otherwise hand a parser whatever a proxy chose to send.
            if (reply.Body.Length > KvReplies.MaxBodyChars)
            {
                throw new KvStoreException(
                    reply.Status, KvReplies.IsSuccess(reply) ? KvErrorCodes.BadBody : KvErrorCodes.Http);
            }

            return reply;
        }

        private void Failed(string method, string route, string code)
        {
            if (_logger.IsEnabled(LogSeverity.Warn))
            {
                _logger.Warn(
                    "kv request failed",
                    Json.Object().Set("method", method).Set("route", route).Set("code", code).Build());
            }
        }
    }
}
