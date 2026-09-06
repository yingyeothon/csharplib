using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.KvStore.Tests
{
    /// <summary>
    /// A scripted transport: every call is recorded for the test to assert on, and
    /// answered by the next scripted reply. No reply scripted is a test defect, not
    /// a 200.
    /// </summary>
    internal sealed class FakeTransport : IHttpTransport
    {
        private readonly Queue<HttpReply> _replies = new Queue<HttpReply>();

        internal List<HttpCall> Calls { get; } = new List<HttpCall>();

        /// <summary>Thrown instead of answering, to stand in for a socket failure.</summary>
        internal Exception? Throws { get; set; }

        /// <summary>Never completes, so a timeout or a cancellation has something to interrupt.</summary>
        internal bool Hangs { get; set; }

        internal HttpCall LastCall => Calls[Calls.Count - 1];

        internal FakeTransport Reply(int status, string body = "", string? etag = null, string? expiresAt = null)
        {
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Cache-Control", "no-store"),
            };
            if (etag != null)
            {
                // Lower-case on purpose: the server writes it that way and the lookup
                // must not care.
                headers.Add(new KeyValuePair<string, string>("etag", etag));
            }

            if (expiresAt != null)
            {
                headers.Add(new KeyValuePair<string, string>("x-kv-expires-at", expiresAt));
            }

            _replies.Enqueue(new HttpReply(status, headers, body));
            return this;
        }

        internal FakeTransport Error(int status, string code, string? reason = null, string? currentJson = null)
        {
            var details = reason != null
                ? ",\"details\":{\"reason\":\"" + reason + "\"}"
                : currentJson != null ? ",\"details\":{\"current\":" + currentJson + "}" : string.Empty;
            return Reply(status, "{\"error\":{\"code\":\"" + code + "\",\"message\":\"refused\"" + details + "}}");
        }

        public Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken)
        {
            Calls.Add(call);
            if (Throws != null)
            {
                throw Throws;
            }

            if (Hangs)
            {
                var pending = new TaskCompletionSource<HttpReply>();
                cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return pending.Task;
            }

            if (_replies.Count == 0)
            {
                throw new InvalidOperationException("no reply scripted for " + call.Method);
            }

            return Task.FromResult(_replies.Dequeue());
        }
    }

    internal static class Calls
    {
        internal static string? Header(this HttpCall call, string name)
        {
            foreach (var header in call.Headers)
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
