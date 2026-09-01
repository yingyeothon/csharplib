namespace Yingyeothon.Logger
{
    /// <summary>
    /// A log level, and — as a logger's own severity — the threshold below which
    /// nothing is written. <see cref="None"/> ranks above every level, so a logger
    /// set to it writes nothing at all.
    /// </summary>
    public enum LogSeverity
    {
        Debug = 100,
        Info = 500,
        Warn = 700,
        Error = 900,
        None = int.MaxValue,
    }
}
