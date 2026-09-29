using System;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Assets
{
    /// <summary>
    /// A reader for one asset bundle on the yyt CDN. With a key it decrypts <c>yyt-enc v1</c>
    /// ciphertext, verifying every 64 KiB segment before releasing a byte of it; without one
    /// it reads a plain bundle through the same calls.
    /// </summary>
    /// <remarks>
    /// A <c>path</c> is the file's object key below the bundle: segments separated by
    /// <c>/</c>, no leading slash, no empty, <c>.</c> or <c>..</c> segment, no backslash, no
    /// control character, no lone surrogate — an <see cref="ArgumentException"/> before any
    /// request otherwise. Every failure the CDN, the network or the bytes cause is an
    /// <see cref="AssetClientException"/>. Tasks resume on the caller's synchronization
    /// context. <see cref="IDisposable.Dispose"/> zeroes the key and the derived keys; a
    /// read in flight then stops at its next piece with an <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public interface IAssetBundleClient : IDisposable
    {
        /// <summary>The whole file, verified before any byte is returned.</summary>
        Task<byte[]> ReadAsync(string path, AssetReadOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// The whole file as UTF-8 JSON (a leading byte-order mark dropped); anything else, or a
        /// document over the codec's limits (64 Mi characters, 64 levels), is <c>asset_corrupt</c>.
        /// </summary>
        Task<JsonValue> ReadJsonAsync(string path, AssetReadOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Plaintext bytes <c>[start, end)</c>, clamped to the file; <paramref name="end"/> null
        /// reads to the end, and <c>end &lt;= start</c> returns empty without a request. An
        /// encrypted file fetches only the segments the window covers.
        /// </summary>
        Task<byte[]> ReadRangeAsync(string path, long start, long? end, AssetReadOptions? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Streams the file into <paramref name="sink"/>, each encrypted segment verified before
        /// a byte of it is written, and continues from <see cref="AssetDownloadOptions.Resume"/>
        /// while the object still has that ETag (otherwise the sink is reset and it starts from
        /// byte 0). Whatever the sink or the progress callback throws ends the download and
        /// reaches the caller unchanged. A keyed resume offset past the end of the file is an
        /// <see cref="ArgumentOutOfRangeException"/>, raised only once the last segment verified.
        /// </summary>
        Task<AssetDownloadResult> DownloadAsync(string path, IAssetSink sink, AssetDownloadOptions? options = null, CancellationToken cancellationToken = default);
    }
}
