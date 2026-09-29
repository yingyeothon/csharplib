using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Assets
{
    /// <summary>A resumable download of one asset straight to a file.</summary>
    public static class AssetFiles
    {
        /// <summary>
        /// Downloads <paramref name="path"/> into the file at <paramref name="destination"/>,
        /// resuming an earlier call's unfinished download of the same object.
        /// </summary>
        /// <remarks>
        /// The plaintext goes to <c>{destination}.part</c> and the object's ETag to
        /// <c>{destination}.part.etag</c>; with a keyed bundle the part only ever holds
        /// verified bytes. A call that finds both resumes from the part's length while the
        /// object still has that ETag, and starts over otherwise. <paramref name="destination"/>
        /// — its folder created if missing — appears only after the last segment verified,
        /// flushed and moved over any file already there; the side files are then gone. A side
        /// file that is a symbolic link is deleted rather than written through.
        /// <b>The file is plaintext on disk</b>, decrypted: put it in the app's private
        /// storage (<c>Application.persistentDataPath</c>). <b>One call per destination at a
        /// time.</b> To give up on a download, or free its space after a full disk, delete
        /// the two side files. Errors are those of <see cref="IAssetBundleClient.DownloadAsync"/>
        /// plus the <see cref="IOException"/>s of writing; the side files then stay for the
        /// next call to resume.
        /// </remarks>
        public static async Task<AssetDownloadResult> DownloadToFileAsync(
            IAssetBundleClient client,
            string path,
            string destination,
            Action<AssetDownloadProgress>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("destination is required", nameof(destination));
            }

            // The asset path is checked before anything on disk is touched. The destination is
            // the caller's: build it from a name you chose, not from a manifest's string.
            AssetPaths.CheckPath(path);
            var part = destination + ".part";
            var etagFile = part + ".etag";

            // A side file that is a link would write the plaintext into its target.
            foreach (var side in new[] { part, etagFile })
            {
                if (File.Exists(side) && (File.GetAttributes(side) & FileAttributes.ReparsePoint) != 0)
                {
                    File.Delete(side);
                }
            }
            var folder = Path.GetDirectoryName(Path.GetFullPath(destination));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            long offset = 0;
            string? savedEtag = null;
            if (File.Exists(part) && File.Exists(etagFile))
            {
                var etag = File.ReadAllText(etagFile, Encoding.UTF8);
                if (etag.Length > 0)
                {
                    savedEtag = etag;
                    offset = new FileInfo(part).Length;
                }
            }

            if (offset == 0 && File.Exists(etagFile))
            {
                File.Delete(etagFile);
            }

            async Task<AssetDownloadResult> Attempt()
            {
                // Not FileMode.Append: an append-only stream cannot be emptied when the object
                // changes and the sink is reset.
                using (var file = new FileStream(part, offset > 0 ? FileMode.OpenOrCreate : FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Seek(0, SeekOrigin.End);
                    var sink = new FileSink(file, () =>
                    {
                        // The part is being emptied for another object: its old ETag must
                        // not survive beside the new bytes.
                        if (File.Exists(etagFile))
                        {
                            File.Delete(etagFile);
                        }

                        savedEtag = null;
                    });
                    var result = await client.DownloadAsync(
                        path,
                        sink,
                        new AssetDownloadOptions
                        {
                            Resume = offset > 0 && savedEtag != null ? new AssetResume(offset, savedEtag) : null,
                            OnProgress = progress =>
                            {
                                // Written after the bytes it vouches for, so a crash in between
                                // leaves a part without its ETag, which the next call discards.
                                if (progress.ETag != null && progress.ETag != savedEtag)
                                {
                                    file.Flush(true);
                                    File.WriteAllText(etagFile, progress.ETag, new UTF8Encoding(false));
                                    savedEtag = progress.ETag;
                                }

                                onProgress?.Invoke(progress);
                            },
                        },
                        cancellationToken);
                    file.Flush(true);
                    return result;
                }
            }

            AssetDownloadResult done;
            try
            {
                done = await Attempt();
            }
            catch (ArgumentOutOfRangeException error) when (offset > 0 && error.ParamName == nameof(AssetDownloadOptions.Resume))
            {
                // A part longer than the file it resumes: nothing to keep. Once more from 0.
                offset = 0;
                savedEtag = null;
                if (File.Exists(etagFile))
                {
                    File.Delete(etagFile);
                }

                done = await Attempt();
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(part, destination);
            if (File.Exists(etagFile))
            {
                File.Delete(etagFile);
            }

            return done;
        }

        private sealed class FileSink : IAssetSink
        {
            private readonly FileStream _file;
            private readonly Action _onReset;

            internal FileSink(FileStream file, Action onReset)
            {
                _file = file;
                _onReset = onReset;
            }

            public Task WriteAsync(ArraySegment<byte> chunk, CancellationToken cancellationToken)
                => _file.WriteAsync(chunk.Array!, chunk.Offset, chunk.Count, cancellationToken);

            public Task ResetAsync(CancellationToken cancellationToken)
            {
                _onReset();
                _file.SetLength(0);
                _file.Position = 0;
                return Task.CompletedTask;
            }
        }
    }
}
