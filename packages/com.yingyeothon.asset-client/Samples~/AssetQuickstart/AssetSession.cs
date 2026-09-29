#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Assets.Samples
{
    /// <summary>
    /// The manifest pattern over one live bundle: a mutable <c>manifest.json</c> naming
    /// immutable files by content, each read whole, by range, or downloaded to disk.
    /// Engine-free, so it runs in a console host too.
    /// </summary>
    public sealed class AssetSession : IDisposable
    {
        private readonly IAssetBundleClient _bundle;

        /// <param name="baseUrl"><c>https://d.yyt.life/assets/{bundleId}/</c>; <c>yyt asset files &lt;bundle&gt;</c> prints it.</param>
        /// <param name="key">The <c>yak1.…</c> key of an encrypted bundle, or null for a plain one. It ships inside the app; never log it.</param>
        /// <param name="transport">Null outside WebGL; <c>AssetUnityWebRequestTransport.Instance</c> on it.</param>
        public AssetSession(string baseUrl, string? key, IAssetTransport? transport = null)
        {
            _bundle = AssetBundleClient.Create(new AssetBundleClientOptions
            {
                BaseUrl = baseUrl,
                Key = key,
                Transport = transport,
            });
        }

        /// <summary>The manifest revalidates on every read; the files it names never change.</summary>
        public Task<JsonValue> ManifestAsync(CancellationToken cancellationToken = default)
            => _bundle.ReadJsonAsync("manifest.json", null, cancellationToken);

        /// <summary>A small file, whole and verified.</summary>
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default)
            => _bundle.ReadAsync(path, null, cancellationToken);

        /// <summary>Only the segments a window covers are fetched.</summary>
        public Task<byte[]> ReadRangeAsync(string path, long start, long end, CancellationToken cancellationToken = default)
            => _bundle.ReadRangeAsync(path, start, end, null, cancellationToken);

        /// <summary>
        /// A large file to disk, resuming where an earlier call stopped. The file is plaintext:
        /// put it under <c>Application.persistentDataPath</c>.
        /// </summary>
        public Task<AssetDownloadResult> DownloadAsync(string path, string destination, Action<AssetDownloadProgress>? onProgress = null, CancellationToken cancellationToken = default)
            => AssetFiles.DownloadToFileAsync(_bundle, path, destination, onProgress, cancellationToken);

        /// <summary>Zeroes the key. Call it when the app is done with the bundle, not from a screen that merely shows it.</summary>
        public void Dispose() => _bundle.Dispose();
    }
}
