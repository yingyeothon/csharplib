using System;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Assets
{
    /// <summary>Where <see cref="IAssetBundleClient.DownloadAsync"/> puts the plaintext, one verified piece at a time.</summary>
    public interface IAssetSink
    {
        /// <summary>
        /// The next plaintext bytes, in order; awaited before the next piece. The segment's
        /// array may be reused by the client after the task completes: copy it to keep it.
        /// </summary>
        Task WriteAsync(ArraySegment<byte> chunk, CancellationToken cancellationToken);

        /// <summary>
        /// The object changed since the bytes already written (or since the resume): drop
        /// everything, the download starts again from byte 0.
        /// </summary>
        Task ResetAsync(CancellationToken cancellationToken);
    }

    /// <summary>Where an earlier download stopped.</summary>
    public sealed class AssetResume
    {
        /// <summary>Creates a resume point: a non-negative byte count and the non-empty ETag an earlier <see cref="AssetDownloadProgress"/> reported.</summary>
        public AssetResume(long offset, string etag)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "a resume offset must not be negative");
            }

            if (string.IsNullOrEmpty(etag))
            {
                throw new ArgumentException("a resume ETag must not be empty", nameof(etag));
            }

            Offset = offset;
            ETag = etag;
        }

        /// <summary>Plaintext bytes the sink already holds from an earlier download.</summary>
        public long Offset { get; }

        /// <summary>The ETag that earlier download reported; another object starts over.</summary>
        public string ETag { get; }
    }

    /// <summary>Reported after every piece a download writes, and once for a download that wrote nothing.</summary>
    public sealed class AssetDownloadProgress
    {
        public AssetDownloadProgress(long written, long? total, string? etag)
        {
            Written = written;
            Total = total;
            ETag = etag;
        }

        /// <summary>Plaintext bytes the sink holds, a resumed offset included.</summary>
        public long Written { get; }

        /// <summary>
        /// The file's plaintext length; null only for a plain file whose length the client
        /// cannot trust — a compressed or unsized body, and in CORS-safe mode any whole-file answer.
        /// </summary>
        public long? Total { get; }

        /// <summary>The object's ETag; keep it with <see cref="Written"/> to resume later.</summary>
        public string? ETag { get; }
    }

    /// <summary>What a finished download wrote.</summary>
    public sealed class AssetDownloadResult
    {
        public AssetDownloadResult(long bytes, string? etag)
        {
            Bytes = bytes;
            ETag = etag;
        }

        /// <summary>The file's plaintext length, now all in the sink.</summary>
        public long Bytes { get; }

        /// <summary>The object's ETag, when the host named one.</summary>
        public string? ETag { get; }
    }

    /// <summary>Options for one read.</summary>
    public sealed class AssetReadOptions
    {
        /// <summary>
        /// Sends <c>Cache-Control: no-cache</c> — never in CORS-safe mode, where the header needs a
        /// preflight the CDN refuses. A mutable file is served <c>no-cache</c> already.
        /// </summary>
        public bool NoCache { get; set; }
    }

    /// <summary>Options for <see cref="IAssetBundleClient.DownloadAsync"/>.</summary>
    public sealed class AssetDownloadOptions
    {
        /// <summary>Where an earlier download stopped, or null for a fresh one.</summary>
        public AssetResume? Resume { get; set; }

        /// <summary>Called after every piece, on the thread the download continues on. Whatever it throws ends the download and reaches the caller.</summary>
        public Action<AssetDownloadProgress>? OnProgress { get; set; }

        /// <summary>As <see cref="AssetReadOptions.NoCache"/>.</summary>
        public bool NoCache { get; set; }
    }
}
