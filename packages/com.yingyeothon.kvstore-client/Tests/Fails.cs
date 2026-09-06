using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.KvStore.Tests
{
    /// <summary>
    /// Awaits a call that must throw, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <c>Assert.ThrowsAsync</c> waits synchronously, and inside the Unity editor the
    /// waiting thread is the main thread — the one every <c>await</c> in the client
    /// posts its continuation back to. A path that really yields (a timer, a socket)
    /// therefore deadlocks there and passes under dotnet, so a test over such a path
    /// is <c>async Task</c> and uses this instead.
    /// </remarks>
    internal static class Fails
    {
        internal static async Task<KvStoreException> WithKvStoreException(Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (KvStoreException error)
            {
                return error;
            }

            Assert.Fail("expected a KvStoreException");
            return null!;
        }

        internal static async Task<OperationCanceledException> WithCancellation(Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (OperationCanceledException error)
            {
                return error;
            }

            Assert.Fail("expected an OperationCanceledException");
            return null!;
        }
    }
}
