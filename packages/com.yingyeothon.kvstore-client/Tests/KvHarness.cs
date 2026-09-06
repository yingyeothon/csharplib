using System;
using Yingyeothon.Logger;

namespace Yingyeothon.KvStore.Tests
{
    /// <summary>A client over a scripted transport and a capturing logger.</summary>
    internal sealed class KvHarness
    {
        /// <summary>Three dot-separated words shaped like a JWT that are not one; the "never logged" tests search for it.</summary>
        internal const string Token = "eyJ.secret-token.sig";

        internal const string BaseUrl = "https://doc-dev.yyt.life";

        internal KvHarness(Action<KvStoreClientOptions>? configure = null)
        {
            Transport = new FakeTransport();
            Log = new CapturingLogWriter();
            var options = new KvStoreClientOptions
            {
                BaseUrl = BaseUrl,
                Token = Token,
                Transport = Transport,
                Logger = FilteredLogger.Create(new FilteredLoggerOptions { Severity = LogSeverity.Debug, Writer = Log }),
            };
            configure?.Invoke(options);
            Client = KvStoreClient.Create(options);
        }

        internal FakeTransport Transport { get; }

        internal CapturingLogWriter Log { get; }

        internal IKvStoreClient Client { get; }

        internal IKvCollection Profile => Client.Collection("profile");
    }
}
