using System;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>Options for <see cref="Backoff.Create(BackoffOptions)"/>.</summary>
    public sealed class BackoffOptions
    {
        /// <summary>Delay before the first retry.</summary>
        public double InitialMs { get; set; } = 500;

        /// <summary>Upper bound on any delay.</summary>
        public double MaxMs { get; set; } = 15000;

        /// <summary>Multiplier applied per attempt.</summary>
        public double Factor { get; set; } = 2;

        /// <summary>Fraction of the delay randomised on both sides.</summary>
        public double Jitter { get; set; } = 0.2;

        /// <summary>Give up after this many consecutive attempts; null is unbounded.</summary>
        public int? MaxAttempts { get; set; }

        /// <summary>Random source in [0, 1). Injectable so a test can pin the jitter.</summary>
        public Func<double>? Random { get; set; }
    }

    /// <summary>An exponential backoff schedule with jitter.</summary>
    public interface IBackoff
    {
        /// <summary>Consecutive attempts since the last <see cref="Reset"/>.</summary>
        int Attempts { get; }

        /// <summary>The next delay in milliseconds, or null once the attempts are exhausted.</summary>
        double? Next();

        void Reset();
    }

    /// <summary>Creates <see cref="IBackoff"/> schedules.</summary>
    public static class Backoff
    {
        public static IBackoff Create() => Create(new BackoffOptions());

        public static IBackoff Create(BackoffOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new BackoffImpl(options);
        }

        private sealed class BackoffImpl : IBackoff
        {
            private readonly double _initialMs;
            private readonly double _maxMs;
            private readonly double _factor;
            private readonly double _jitter;
            private readonly int? _maxAttempts;
            private readonly Func<double> _random;

            internal BackoffImpl(BackoffOptions options)
            {
                _initialMs = options.InitialMs;
                _maxMs = options.MaxMs;
                _factor = options.Factor;
                _jitter = options.Jitter;
                _maxAttempts = options.MaxAttempts;

                if (options.Random != null)
                {
                    _random = options.Random;
                }
                else
                {
                    // Per instance, never a shared static: two clients reconnecting
                    // after the same gateway restart must not pick the same delay.
                    var random = new Random();
                    _random = () => random.NextDouble();
                }
            }

            public int Attempts { get; private set; }

            public double? Next()
            {
                if (_maxAttempts.HasValue && Attempts >= _maxAttempts.Value)
                {
                    return null;
                }

                var baseDelay = Math.Min(_maxMs, _initialMs * Math.Pow(_factor, Attempts));
                Attempts++;
                var spread = baseDelay * _jitter;
                return Math.Round(baseDelay - spread + (_random() * spread * 2));
            }

            public void Reset() => Attempts = 0;
        }
    }
}
