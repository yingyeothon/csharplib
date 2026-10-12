using System;
using System.Threading;
namespace Yingyeothon.Platform
{
    internal sealed class OwnerGate
    {
        private int _thread;
        private int _depth;
        internal IDisposable Enter()
        {
            var thread = Thread.CurrentThread.ManagedThreadId;
            var prior = Interlocked.CompareExchange(ref _thread, thread, 0);
            if (prior != 0 && prior != thread)
            {
                throw new InvalidOperationException("Concurrent owner calls are forbidden");


            }

            _depth++; return new Lease(this);
        }
        private sealed class Lease : IDisposable
        {
            private OwnerGate? _gate;
            internal Lease(OwnerGate gate) { _gate = gate; }
            public void Dispose()
            {
                var gate = _gate; _gate = null; if (gate != null && --gate._depth == 0)
                {
                    Volatile.Write(ref gate._thread, 0);
                }
            }
        }
    }
}
