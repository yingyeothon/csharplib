namespace Yingyeothon.Logger
{
    /// <summary>
    /// A log level, and — as a logger's own severity — the threshold below which
    /// nothing is written. <see cref="None"/> ranks above every level, so a logger
    /// set to it writes nothing at all.
    /// </summary>
    public enum LogSeverity
    {
        /// <summary>Detail for diagnosis. Never a payload, a token or a frame body, even here.</summary>
        Debug = 100,
        /// <summary>Normal operation worth recording.</summary>
        Info = 500,
        /// <summary>Something recoverable went wrong.</summary>
        Warn = 700,
        /// <summary>Something failed.</summary>
        Error = 900,
        /// <summary>Above every level, so a logger set to it writes nothing.</summary>
        None = int.MaxValue,
    }
}
