using System.Diagnostics;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>A monotonic millisecond clock. Injected so timeouts are testable.</summary>
    public interface IClock
    {
        /// <summary>A monotonic reading in milliseconds. Only differences between readings are meaningful.</summary>
        double NowMillis { get; }
    }

    /// <summary>The default clock, backed by a monotonic <see cref="Stopwatch"/>.</summary>
    /// <remarks>
    /// Deliberately not wall-clock: a device clock that jumps backwards (NTP, a user
    /// changing the time, a phone resuming) would otherwise stall a reconnect
    /// indefinitely.
    /// </remarks>
    public sealed class SystemClock : IClock
    {
        /// <summary>The shared monotonic clock every client uses unless a test injects its own.</summary>
        public static readonly IClock Instance = new SystemClock();

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private SystemClock()
        {
        }

        public double NowMillis => _stopwatch.Elapsed.TotalMilliseconds;
    }
}
