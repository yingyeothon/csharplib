using System;

namespace Yingyeothon.Assets
{
    /// <summary>
    /// The <see cref="AssetClientException.Code"/> values, in the vocabulary the tslib,
    /// csharplib and flutterlib asset clients share. String constants, not an enum.
    /// </summary>
    public static class AssetErrorCodes
    {
        /// <summary>The key is not <c>yak1.</c> + 43 base64url characters of 32 bytes, or 32 raw bytes. Thrown by <see cref="AssetBundleClient.Create"/>.</summary>
        public const string BadKey = "bad_key";

        /// <summary>The CDN answered 403 or 404. <b>A missing object answers 403 on the yyt CDN</b>, so the two are one case.</summary>
        public const string NotFound = "not_found";

        /// <summary>
        /// A length no ciphertext has, a failed segment tag, a wrong key, path or version, or
        /// <c>ReadJsonAsync</c> on bytes that are not UTF-8 JSON. Not retried: a wrong key and a
        /// wrong path fail the same way every time. (A mutable file replaced mid-read on a
        /// host that sends no ETag also lands here.)
        /// </summary>
        public const string AssetCorrupt = "asset_corrupt";

        /// <summary>Any other status, a host that ignores <c>Range</c>, a 206 that is not the range asked for, an object that kept changing, or a plain body larger than any asset.</summary>
        public const string Http = "http";

        /// <summary>No answer (in time), or a body that failed, stalled, ended early or ran past its stated length.</summary>
        public const string Network = "network";
    }

    /// <summary>The one exception the client throws for a bad key, a refused or failed request, or bytes that do not verify.</summary>
    /// <remarks>
    /// The message is <c>"asset {code} ({status})"</c>, sometimes with a fixed SDK phrase
    /// after a colon, and never a key, a URL, a path or a byte of plaintext, so it can be
    /// logged as it is. Local misuse — a malformed <c>BaseUrl</c> or path, a negative
    /// offset, an empty resume ETag — is an <see cref="ArgumentException"/> before any
    /// request; the one after requests is a keyed resume offset past the end of the file,
    /// raised only once the last segment verified. A read of a disposed client is an
    /// <see cref="ObjectDisposedException"/>, and whatever a caller's sink throws passes
    /// through unchanged.
    /// </remarks>
    public sealed class AssetClientException : Exception
    {
        /// <summary>Creates an exception.</summary>
        /// <param name="code">An <see cref="AssetErrorCodes"/> value.</param>
        /// <param name="status">The HTTP status, or 0 when there was no answer to report.</param>
        /// <param name="detail">A fixed SDK phrase that narrows <paramref name="code"/>, or null.</param>
        /// <param name="innerException">The transport failure behind a <see cref="AssetErrorCodes.Network"/>.</param>
        public AssetClientException(string code, int status, string? detail, Exception? innerException)
            : base(
                "asset " + (code ?? throw new ArgumentNullException(nameof(code))) + " (" + status + ")"
                + (detail == null ? string.Empty : ": " + detail),
                innerException)
        {
            Code = code;
            Status = status;
            Detail = detail;
        }

        /// <summary>One of <see cref="AssetErrorCodes"/>.</summary>
        public string Code { get; }

        /// <summary>The HTTP status, or 0 when there was no answer to report.</summary>
        public int Status { get; }

        /// <summary>A fixed SDK phrase that narrows <see cref="Code"/>, or null.</summary>
        public string? Detail { get; }

        internal static AssetClientException Corrupt(int status) => new AssetClientException(AssetErrorCodes.AssetCorrupt, status, null, null);

        internal static AssetClientException HttpError(int status, string? detail = null) => new AssetClientException(AssetErrorCodes.Http, status, detail, null);

        internal static AssetClientException NetworkError(int status, string? detail, Exception? cause = null)
            => new AssetClientException(AssetErrorCodes.Network, status, detail, cause);
    }
}
