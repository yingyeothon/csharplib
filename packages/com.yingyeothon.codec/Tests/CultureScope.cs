using System;
using System.Globalization;
using System.Threading;

namespace Yingyeothon.Codec.Tests
{
    /// <summary>
    /// Swaps the current culture for the length of a test and restores it on dispose,
    /// so a comma-decimal or dotless-i locale can be tried without leaking into the
    /// next test.
    /// </summary>
    internal sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        internal CultureScope(string cultureName)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureName);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _previous;
            Thread.CurrentThread.CurrentCulture = _previous;
        }
    }
}
