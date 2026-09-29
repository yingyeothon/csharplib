using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Auth.Tests
{
    /// <summary>Answers each request from a queue and records what was sent.</summary>
    internal sealed class FakeAuthTransport : IAuthTransport
    {
        private readonly Queue<Func<AuthHttpRequest, CancellationToken, Task<AuthHttpResponse>>> _answers =
            new Queue<Func<AuthHttpRequest, CancellationToken, Task<AuthHttpResponse>>>();

        internal List<AuthHttpRequest> Sent { get; } = new List<AuthHttpRequest>();

        internal FakeAuthTransport Reply(int status, string body)
        {
            _answers.Enqueue((_, __) => Task.FromResult(new AuthHttpResponse(status, body)));
            return this;
        }

        internal FakeAuthTransport Throw(Exception error)
        {
            _answers.Enqueue((_, __) => Task.FromException<AuthHttpResponse>(error));
            return this;
        }

        internal FakeAuthTransport Hang()
        {
            _answers.Enqueue(async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("unreachable");
            });
            return this;
        }

        public Task<AuthHttpResponse> SendAsync(AuthHttpRequest request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return _answers.Dequeue()(request, cancellationToken);
        }
    }
}
