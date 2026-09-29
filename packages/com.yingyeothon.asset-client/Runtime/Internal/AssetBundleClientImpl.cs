using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Assets
{
    /// <summary>An attempt that saw the object change; the caller starts over.</summary>
    internal sealed class ObjectChangedException : Exception
    {
        internal ObjectChangedException(int status)
            : base("asset object changed")
        {
            Status = status;
        }

        internal int Status { get; }
    }

    internal sealed class AssetBundleClientImpl : IAssetBundleClient
    {
        /// <summary>How often a read starts over because the object changed under it.</summary>
        private const int MaxRestarts = 3;

        private static readonly byte[] Empty = new byte[0];

        private readonly BundleBase _base;
        private readonly bool _encrypted;
        private readonly bool _corsSafe;
        private readonly ILogger _logger;
        private readonly Requester _requester;
        private readonly BundleCrypto? _crypto;
        private volatile bool _disposed;

        internal AssetBundleClientImpl(BundleBase bundle, byte[]? key, bool corsSafe, IAssetTransport transport, ILogger logger, TimeSpan responseTimeout, TimeSpan bodyIdleTimeout)
        {
            _base = bundle;
            _encrypted = key != null;
            _corsSafe = corsSafe;
            _logger = logger;
            _crypto = key == null ? null : new BundleCrypto(key);
            _requester = new Requester(transport, logger, responseTimeout, bodyIdleTimeout, () => _disposed);
        }

        // ---- public calls -------------------------------------------------------

        public Task<byte[]> ReadAsync(string path, AssetReadOptions? options = null, CancellationToken cancellationToken = default)
            => ReadWholeAsync(FileOf(path), options?.NoCache ?? false, cancellationToken);

        public async Task<JsonValue> ReadJsonAsync(string path, AssetReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            var bytes = await ReadWholeAsync(FileOf(path), options?.NoCache ?? false, cancellationToken);
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw AssetClientException.Corrupt(0);
            }

            if (text.Length > 0 && text[0] == '﻿')
            {
                text = text.Substring(1);
            }

            // Not attached as the inner exception: a parse error may describe the plaintext.
            if (text.Length > Json.MaxBigLength)
            {
                throw AssetClientException.Corrupt(0);
            }

            try
            {
                return Json.ParseBig(text, Json.MaxBigLength);
            }
            catch (JsonParseException)
            {
                throw AssetClientException.Corrupt(0);
            }
        }

        public Task<byte[]> ReadRangeAsync(string path, long start, long? end, AssetReadOptions? options = null, CancellationToken cancellationToken = default)
        {
            var file = FileOf(path);
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "asset range start must not be negative");
            }

            if (end.HasValue && end.Value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(end), "asset range end must not be negative");
            }

            if (end.HasValue && end.Value <= start)
            {
                return Task.FromResult(Empty);
            }

            var noCache = options?.NoCache ?? false;
            return _encrypted
                ? ReadEncryptedRangeAsync(file, start, end, noCache, cancellationToken)
                : ReadPlainRangeAsync(file, start, end, noCache, cancellationToken);
        }

        public Task<AssetDownloadResult> DownloadAsync(string path, IAssetSink sink, AssetDownloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            var file = FileOf(path);
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            options ??= new AssetDownloadOptions();
            return _encrypted
                ? DownloadEncryptedAsync(file, sink, options, cancellationToken)
                : DownloadPlainAsync(file, sink, options, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = true;
            _crypto?.Dispose();
        }

        // ---- plumbing -------------------------------------------------------------

        private sealed class FileRef
        {
            internal FileRef(string path, string url, string ad)
            {
                Path = path;
                Url = url;
                Ad = ad;
            }

            internal string Path { get; }

            internal string Url { get; }

            /// <summary>The associated data: the object key below the bundle.</summary>
            internal string Ad { get; }
        }

        private FileRef FileOf(string path)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(IAssetBundleClient));
            }

            var checkedPath = AssetPaths.CheckPath(path);
            return new FileRef(checkedPath, AssetPaths.FileUrl(_base, checkedPath), (_base.AdPrefix ?? string.Empty) + checkedPath);
        }

        private Task<Answer> SendAsync(FileRef file, RequestSpec spec, CancellationToken cancellationToken)
            => _requester.SendAsync(file.Url, file.Path, spec, cancellationToken);

        /// <summary>The request itself failed — no status — as opposed to a body that failed.</summary>
        private static bool IsTransportRejection(AssetClientException error)
            => error.Code == AssetErrorCodes.Network && error.Status == 0 && error.Detail == null;

        private async Task<T> RestartingAsync<T>(FileRef file, Func<Task<T>> attempt, Func<Task> onChanged)
        {
            for (var restart = 0; ; restart++)
            {
                try
                {
                    return await attempt();
                }
                catch (ObjectChangedException changed)
                {
                    // Never reset a caller's sink for a client disposed meanwhile.
                    if (_disposed)
                    {
                        throw new ObjectDisposedException(nameof(IAssetBundleClient));
                    }

                    if (restart >= MaxRestarts)
                    {
                        throw AssetClientException.HttpError(changed.Status, "the object kept changing");
                    }

                    _logger.Info(
                        "asset changed during a read; starting over",
                        Json.Object().Set("path", Requester.Diagnostic(file.Path)).Set("restart", (double)(restart + 1)).Build());
                    await onChanged();
                }
            }
        }

        private BundleCrypto Crypto()
        {
            if (_crypto == null || _crypto.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(IAssetBundleClient));
            }

            return _crypto;
        }

        // ---- whole-file read ------------------------------------------------------

        private async Task<byte[]> ReadWholeAsync(FileRef file, bool noCache, CancellationToken cancellationToken)
        {
            var answer = await SendAsync(file, new RequestSpec("GET", RequestKind.Whole, noCache: noCache && !_corsSafe), cancellationToken);
            try
            {
                if (answer.Status != 200)
                {
                    throw AssetClientException.HttpError(answer.Status);
                }

                if (!_encrypted)
                {
                    return await answer.Body.RestAsync(
                        AssetFormat.MaxCiphertext,
                        () => AssetClientException.HttpError(answer.Status, "larger than any asset"));
                }

                // Refuse a length no ciphertext has before holding any of it, and stop
                // reading an unsized body once it has passed the largest one.
                if (!answer.Encoded && answer.Length.HasValue && AssetFormat.SegmentsOf(answer.Length.Value) < 0)
                {
                    throw AssetClientException.Corrupt(answer.Status);
                }

                var ciphertext = await answer.Body.RestAsync(AssetFormat.MaxCiphertext, () => AssetClientException.Corrupt(answer.Status));
                var total = ciphertext.LongLength;
                var header = new byte[Math.Min(AssetFormat.HeaderLength, ciphertext.Length)];
                Buffer.BlockCopy(ciphertext, 0, header, 0, header.Length);
                using (var decryptor = Crypto().Open(header, total, file.Ad))
                {
                    var output = new byte[decryptor.PlaintextLength];
                    for (var i = 0; i < decryptor.Segments; i++)
                    {
                        AssetFormat.SegmentExtent(i, total, out var start, out var end);
                        var plain = decryptor.Open(i, ciphertext, (int)start, (int)(end - start));
                        Buffer.BlockCopy(plain, 0, output, (int)AssetFormat.PlainStart(i), plain.Length);
                    }

                    return output;
                }
            }
            finally
            {
                answer.Body.Dispose();
            }
        }

        // ---- encrypted, opened at a window -----------------------------------------

        private sealed class Plan
        {
            internal Plan(long start, long end, int first, int last)
            {
                Start = start;
                End = end;
                First = first;
                Last = last;
            }

            /// <summary>The clamped plaintext window.</summary>
            internal long Start { get; }

            internal long End { get; }

            internal int First { get; }

            internal int Last { get; }
        }

        private sealed class Opened
        {
            internal Opened(SegmentDecryptor decryptor, string? etag, Plan plan, BodyReader body)
            {
                Decryptor = decryptor;
                ETag = etag;
                Plan = plan;
                Body = body;
            }

            internal SegmentDecryptor Decryptor { get; }

            internal string? ETag { get; }

            internal Plan Plan { get; }

            /// <summary>Positioned at segment <see cref="Plan.First"/>.</summary>
            internal BodyReader Body { get; }
        }

        /// <summary>Which segments a window needs, once the length is known.</summary>
        private static Plan PlanWindow(long total, long start, long? end)
        {
            var segments = AssetFormat.SegmentsOf(total);
            if (segments < 0)
            {
                throw AssetClientException.Corrupt(0);
            }

            var plainLength = AssetFormat.PlaintextLengthOf(total, segments);
            var s = Math.Min(start, plainLength);
            var e = Math.Min(end ?? plainLength, plainLength);

            // The length came from an unauthenticated header, so a window with nothing in
            // it — an empty file, a window past the end, a finished resume — still fetches
            // and verifies the last segment: its `last` flag and its exact extent are what
            // prove the length, the key and the path. Nothing of it is released.
            var empty = s >= e;
            return new Plan(s, e, empty ? segments - 1 : AssetFormat.SegmentOf(s), empty ? segments - 1 : AssetFormat.SegmentOf(e - 1));
        }

        /// <summary>
        /// Opens plaintext <c>[start, end)</c> of an encrypted file: the length and the ETag
        /// first, then one ranged request from the first segment the window touches to the
        /// end of the last. Every body this opened and did not hand back is disposed, on
        /// success and on failure alike, so a refused answer never holds its connection.
        /// </summary>
        private async Task<Opened> OpenEncryptedAsync(FileRef file, long start, long? end, string? resumeEtag, bool noCache, CancellationToken cancellationToken)
        {
            var bodies = new List<BodyReader>();
            BodyReader? kept = null;

            async Task<Answer> Send(RequestSpec spec)
            {
                var answer = await SendAsync(file, spec, cancellationToken);
                bodies.Add(answer.Body);
                return answer;
            }

            try
            {
                var opened = await OpenEncryptedWithAsync(Send, file, start, end, resumeEtag, noCache && !_corsSafe);
                kept = opened.Body;
                return opened;
            }
            finally
            {
                foreach (var body in bodies)
                {
                    if (!ReferenceEquals(body, kept))
                    {
                        body.Dispose();
                    }
                }
            }
        }

        private async Task<Opened> OpenEncryptedWithAsync(Func<RequestSpec, Task<Answer>> send, FileRef file, long start, long? end, string? resumeEtag, bool noCache)
        {
            if (!_corsSafe)
            {
                return await OpenConditionalAsync(send, file, start, end, resumeEtag, noCache);
            }

            // Content-Range is unreadable and If-Range would need a preflight the CDN
            // refuses, so the length and the identity come from a HEAD.
            var head = await send(new RequestSpec("HEAD", RequestKind.Head));
            if (head.Status != 200)
            {
                throw AssetClientException.HttpError(head.Status);
            }

            if (head.Encoded || !head.Length.HasValue)
            {
                throw AssetClientException.HttpError(head.Status, "no usable Content-Length");
            }

            if (resumeEtag != null && head.ETag != resumeEtag)
            {
                throw new ObjectChangedException(head.Status);
            }

            var total = head.Length.Value;
            try
            {
                return await OpenSafelistedAsync(send, file, start, end, total, head.ETag);
            }
            catch (AssetClientException error) when (IsTransportRejection(error))
            {
                // The HEAD went through and a request that differs from it only by Range
                // did not: a browser that still preflights Range, which the CDN refuses.
                // The whole file still verifies segment by segment.
                _logger.Warn("asset ranged request refused; reading the whole file", Json.Object().Set("path", Requester.Diagnostic(file.Path)).Build());
                return await OpenWholeAsync(send, file, start, end, total, head.ETag);
            }
        }

        /// <summary>Outside a browser: Content-Range for the length, If-Range for identity.</summary>
        private async Task<Opened> OpenConditionalAsync(Func<RequestSpec, Task<Answer>> send, FileRef file, long start, long? end, string? resumeEtag, bool noCache)
        {
            var first = AssetFormat.SegmentOf(start);
            var ifRange = Answer.IsStrong(resumeEtag) ? resumeEtag : null;

            // The header and the first segments arrive in one request when the window
            // starts in segment 0; the host clamps a range past the end.
            long? to;
            if (first != 0)
            {
                to = AssetFormat.HeaderLength - 1;
            }
            else if (!end.HasValue)
            {
                to = null;
            }
            else
            {
                AssetFormat.SegmentExtent(AssetFormat.SegmentOf(Math.Max(end.Value - 1, 0)), -1, out _, out var extentEnd);
                to = extentEnd - 1;
            }

            var answer = await send(new RequestSpec("GET", first == 0 ? RequestKind.Segments : RequestKind.Header, 0, to, ifRange, noCache));
            var checkedRange = CheckRanged(answer, 0, to, null, resumeEtag, ifRange != null, false);
            var total = checkedRange.Total ?? throw AssetClientException.HttpError(answer.Status, "no total length");
            var header = await answer.Body.ReadExactlyAsync(Math.Min(AssetFormat.HeaderLength, total));
            var plan = PlanWindow(total, start, end);
            var decryptor = Crypto().Open(header, total, file.Ad);
            if (first == 0 && plan.First == 0)
            {
                return new Opened(decryptor, checkedRange.ETag, plan, answer.Body);
            }

            return await OpenSegmentsOrReleaseAsync(send, decryptor, plan, total, checkedRange.ETag, noCache);
        }

        /// <summary>In a browser: only Range crosses, and each answer's ETag is compared.</summary>
        private async Task<Opened> OpenSafelistedAsync(Func<RequestSpec, Task<Answer>> send, FileRef file, long start, long? end, long total, string? etag)
        {
            var plan = PlanWindow(total, start, end);
            long to;
            if (plan.First == 0)
            {
                AssetFormat.SegmentExtent(plan.Last, total, out _, out var lastEnd);
                to = lastEnd - 1;
            }
            else
            {
                to = AssetFormat.HeaderLength - 1;
            }

            var answer = await send(new RequestSpec("GET", plan.First == 0 ? RequestKind.Segments : RequestKind.Header, 0, to));
            CheckRanged(answer, 0, to, total, etag, false, true);
            var header = await answer.Body.ReadExactlyAsync(Math.Min(AssetFormat.HeaderLength, total));
            var decryptor = Crypto().Open(header, total, file.Ad);
            if (plan.First == 0)
            {
                return new Opened(decryptor, etag, plan, answer.Body);
            }

            return await OpenSegmentsOrReleaseAsync(send, decryptor, plan, total, etag, false);
        }

        /// <summary>The segments request, zeroing the segment keys if it does not open.</summary>
        private async Task<Opened> OpenSegmentsOrReleaseAsync(Func<RequestSpec, Task<Answer>> send, SegmentDecryptor decryptor, Plan plan, long total, string? etag, bool noCache)
        {
            try
            {
                return await OpenSegmentsAsync(send, decryptor, plan, total, etag, noCache);
            }
            catch
            {
                decryptor.Dispose();
                throw;
            }
        }

        /// <summary>The ranged request for segments <c>first … last</c>, after the header.</summary>
        private async Task<Opened> OpenSegmentsAsync(Func<RequestSpec, Task<Answer>> send, SegmentDecryptor decryptor, Plan plan, long total, string? etag, bool noCache)
        {
            AssetFormat.SegmentExtent(plan.First, total, out var from, out _);
            AssetFormat.SegmentExtent(plan.Last, total, out _, out var lastEnd);
            var to = lastEnd - 1;
            var ifRange = !_corsSafe && Answer.IsStrong(etag) ? etag : null;
            var answer = await send(new RequestSpec("GET", RequestKind.Segments, from, to, ifRange, noCache));
            CheckRanged(answer, from, to, total, etag, ifRange != null, _corsSafe);
            return new Opened(decryptor, etag, plan, answer.Body);
        }

        /// <summary>
        /// The fallback when Range cannot be sent: one plain GET, the segments before the
        /// window read and dropped unreleased, then the window verified as usual.
        /// </summary>
        private async Task<Opened> OpenWholeAsync(Func<RequestSpec, Task<Answer>> send, FileRef file, long start, long? end, long total, string? etag)
        {
            var answer = await send(new RequestSpec("GET", RequestKind.Whole));
            if (answer.Status != 200)
            {
                throw AssetClientException.HttpError(answer.Status);
            }

            if (etag != null && answer.ETag != etag)
            {
                throw new ObjectChangedException(answer.Status);
            }

            var plan = PlanWindow(total, start, end);
            var header = await answer.Body.ReadExactlyAsync(Math.Min(AssetFormat.HeaderLength, total));
            var decryptor = Crypto().Open(header, total, file.Ad);
            try
            {
                AssetFormat.SegmentExtent(plan.First, total, out var target, out _);
                for (long at = AssetFormat.HeaderLength; at < target;)
                {
                    var step = Math.Min(AssetFormat.SegmentSize, target - at);
                    await answer.Body.ReadExactlyAsync(step);
                    at += step;
                }
            }
            catch
            {
                decryptor.Dispose();
                throw;
            }

            return new Opened(decryptor, etag, plan, answer.Body);
        }

        private readonly struct Checked
        {
            internal Checked(long? total, string? etag)
            {
                Total = total;
                ETag = etag;
            }

            internal long? Total { get; }

            internal string? ETag { get; }
        }

        /// <summary>
        /// Accepts a ranged answer, or says why not. A 200 to a request that carried
        /// If-Range, another ETag or another total length means the object changed (start
        /// over); a 200 otherwise means the host ignores Range.
        /// </summary>
        private static Checked CheckRanged(Answer answer, long from, long? to, long? total, string? etag, bool ifRangeSent, bool corsSafe)
        {
            var status = answer.Status;
            if (status == 200)
            {
                if (ifRangeSent || (etag != null && answer.ETag != etag))
                {
                    throw new ObjectChangedException(status);
                }

                throw AssetClientException.HttpError(status, "the host ignores Range");
            }

            if (status == 416)
            {
                // The range was computed from a length the object no longer has; with
                // nothing known yet, the object is empty, which no ciphertext is.
                if (total.HasValue)
                {
                    throw new ObjectChangedException(status);
                }

                throw AssetClientException.Corrupt(status);
            }

            // Without If-Range only the answer's own ETag says which object this is; an
            // answer that names none cannot be spliced onto one that did.
            if (etag != null && answer.ETag != etag && (answer.ETag != null || !ifRangeSent))
            {
                throw new ObjectChangedException(status);
            }

            var range = answer.Range;
            if (range == null)
            {
                if (!corsSafe)
                {
                    throw AssetClientException.HttpError(status, "no Content-Range");
                }

                return new Checked(total, answer.ETag ?? etag);
            }

            if (!range.Total.HasValue)
            {
                throw AssetClientException.HttpError(status, "no total length in Content-Range");
            }

            if (total.HasValue && range.Total.Value != total.Value)
            {
                throw new ObjectChangedException(status);
            }

            var last = Math.Min(to ?? range.Total.Value - 1, range.Total.Value - 1);
            if (range.Start != from || range.End != last)
            {
                throw AssetClientException.HttpError(status, "Content-Range is not the range asked for");
            }

            return new Checked(range.Total, answer.ETag ?? etag);
        }

        /// <summary>Verifies and emits the opened window segment by segment: one segment in memory, no byte before its tag verified.</summary>
        private static async Task EmitSegmentsAsync(Opened opened, Func<ArraySegment<byte>, Task> emit)
        {
            var decryptor = opened.Decryptor;
            try
            {
                for (var i = opened.Plan.First; i <= opened.Plan.Last; i++)
                {
                    AssetFormat.SegmentExtent(i, decryptor.Total, out var start, out var end);
                    var ciphertext = await opened.Body.ReadExactlyAsync(end - start);
                    var plain = decryptor.Open(i, ciphertext, 0, ciphertext.Length);
                    var at = AssetFormat.PlainStart(i);
                    var lo = Math.Max(opened.Plan.Start - at, 0);
                    var hi = Math.Min(opened.Plan.End - at, plain.Length);
                    if (lo < hi)
                    {
                        await emit(new ArraySegment<byte>(plain, (int)lo, (int)(hi - lo)));
                    }
                }
            }
            finally
            {
                opened.Body.Dispose();
                decryptor.Dispose();
            }
        }

        private async Task<byte[]> ReadEncryptedRangeAsync(FileRef file, long start, long? end, bool noCache, CancellationToken cancellationToken)
        {
            var chunks = new List<ArraySegment<byte>>();
            return await RestartingAsync(
                file,
                async () =>
                {
                    var opened = await OpenEncryptedAsync(file, start, end, null, noCache, cancellationToken);
                    await EmitSegmentsAsync(opened, chunk =>
                    {
                        chunks.Add(chunk);
                        return Task.CompletedTask;
                    });
                    return Concat(chunks);
                },
                () =>
                {
                    chunks.Clear();
                    return Task.CompletedTask;
                });
        }

        private static byte[] Concat(List<ArraySegment<byte>> chunks)
        {
            long size = 0;
            foreach (var chunk in chunks)
            {
                size += chunk.Count;
            }

            var output = new byte[size];
            long at = 0;
            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk.Array!, chunk.Offset, output, (int)at, chunk.Count);
                at += chunk.Count;
            }

            return output;
        }

        // ---- plain range ------------------------------------------------------------

        private async Task<byte[]> ReadPlainRangeAsync(FileRef file, long start, long? end, bool noCache, CancellationToken cancellationToken)
        {
            var to = end.HasValue ? end.Value - 1 : (long?)null;
            Answer answer;
            try
            {
                answer = await SendAsync(file, new RequestSpec("GET", RequestKind.Range, start, to, noCache: noCache && !_corsSafe), cancellationToken);
            }
            catch (AssetClientException error) when (_corsSafe && IsTransportRejection(error))
            {
                _logger.Warn("asset ranged request refused; reading the whole file", Json.Object().Set("path", Requester.Diagnostic(file.Path)).Build());
                answer = await SendAsync(file, new RequestSpec("GET", RequestKind.Whole), cancellationToken);
            }

            try
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(IAssetBundleClient));
                }

                if (answer.Status == 416)
                {
                    return Empty;
                }

                var tooLarge = (Func<AssetClientException>)(() => AssetClientException.HttpError(answer.Status, "larger than any asset"));
                if (answer.Status == 200)
                {
                    // Range was ignored or never sent: this is the whole file.
                    var whole = await answer.Body.RestAsync(AssetFormat.MaxCiphertext, tooLarge);
                    return Slice(whole, start, end ?? whole.LongLength);
                }

                var range = answer.Range;
                if (range == null && !_corsSafe)
                {
                    throw AssetClientException.HttpError(answer.Status, "no Content-Range");
                }

                if (range != null
                    && (range.Start != start
                        || (range.Total.HasValue && range.End != Math.Min(to ?? range.Total.Value - 1, range.Total.Value - 1))))
                {
                    throw AssetClientException.HttpError(answer.Status, "Content-Range is not the range asked for");
                }

                var bytes = await answer.Body.RestAsync(AssetFormat.MaxCiphertext, tooLarge);
                long? expected = range == null ? (long?)null : range.End - range.Start + 1;
                if (expected.HasValue && bytes.LongLength < expected.Value)
                {
                    throw AssetClientException.NetworkError(answer.Status, "the body ended early");
                }

                var keep = Math.Min(bytes.LongLength, Math.Min(expected ?? long.MaxValue, end.HasValue ? end.Value - start : long.MaxValue));
                return Slice(bytes, 0, keep);
            }
            finally
            {
                answer.Body.Dispose();
            }
        }

        private static byte[] Slice(byte[] bytes, long start, long end)
        {
            var s = Math.Min(start, bytes.LongLength);
            var e = Math.Min(Math.Max(end, s), bytes.LongLength);
            if (s == 0 && e == bytes.LongLength)
            {
                return bytes;
            }

            var output = new byte[e - s];
            Buffer.BlockCopy(bytes, (int)s, output, 0, output.Length);
            return output;
        }

        // ---- downloads ----------------------------------------------------------------

        private async Task<AssetDownloadResult> DownloadEncryptedAsync(FileRef file, IAssetSink sink, AssetDownloadOptions options, CancellationToken cancellationToken)
        {
            var written = options.Resume?.Offset ?? 0;
            var resumeEtag = options.Resume?.ETag;
            return await RestartingAsync(
                file,
                async () =>
                {
                    var opened = await OpenEncryptedAsync(file, written, null, resumeEtag, options.NoCache, cancellationToken);
                    var total = opened.Decryptor.PlaintextLength;
                    var overshot = written > total;
                    var reported = false;
                    void Report()
                    {
                        reported = true;
                        options.OnProgress?.Invoke(new AssetDownloadProgress(written, total, opened.ETag));
                    }

                    await EmitSegmentsAsync(opened, async chunk =>
                    {
                        await sink.WriteAsync(chunk, cancellationToken);
                        written += chunk.Count;
                        Report();
                    });

                    // Only now is the length authenticated (the last segment verified), so
                    // only now can an offset past it be the caller's mistake.
                    if (overshot)
                    {
                        throw new ArgumentOutOfRangeException(nameof(AssetDownloadOptions.Resume), "asset resume offset is past the end of the file");
                    }

                    // An empty file, or a resume that was already complete: nothing was
                    // written, and the caller still hears that the download is whole.
                    if (!reported)
                    {
                        Report();
                    }

                    return new AssetDownloadResult(total, opened.ETag);
                },
                async () =>
                {
                    if (written > 0)
                    {
                        await sink.ResetAsync(cancellationToken);
                    }

                    written = 0;
                    resumeEtag = null;
                });
        }

        private async Task<AssetDownloadResult> DownloadPlainAsync(FileRef file, IAssetSink sink, AssetDownloadOptions options, CancellationToken cancellationToken)
        {
            var written = options.Resume?.Offset ?? 0;
            var resumeEtag = options.Resume?.ETag;
            var noCache = options.NoCache && !_corsSafe;

            Task<Answer> Whole() => SendAsync(file, new RequestSpec("GET", RequestKind.Whole, noCache: noCache), cancellationToken);

            async Task<AssetDownloadResult> Attempt()
            {
                var resuming = written > 0 && resumeEtag != null;
                var ifRange = resuming && !_corsSafe && Answer.IsStrong(resumeEtag) ? resumeEtag : null;
                Answer answer;
                if (!resuming)
                {
                    answer = await Whole();
                }
                else
                {
                    try
                    {
                        answer = await SendAsync(file, new RequestSpec("GET", RequestKind.Range, written, null, ifRange, noCache), cancellationToken);
                    }
                    catch (AssetClientException error) when (_corsSafe && IsTransportRejection(error))
                    {
                        _logger.Warn("asset ranged request refused; reading the whole file", Json.Object().Set("path", Requester.Diagnostic(file.Path)).Build());
                        answer = await Whole();
                    }
                }

                try
                {
                    return await ConsumeAsync(answer, ifRange != null);
                }
                finally
                {
                    answer.Body.Dispose();
                }
            }

            async Task<AssetDownloadResult> ConsumeAsync(Answer answer, bool ifRangeSent)
            {
                long? total;
                if (answer.Status == 416)
                {
                    // Nothing at or after the offset. Under a matching If-Range whose stated
                    // length is the offset, the earlier download was complete; anything
                    // else is another object, read afresh.
                    if (ifRangeSent && answer.UnsatisfiedTotal == written)
                    {
                        options.OnProgress?.Invoke(new AssetDownloadProgress(written, written, resumeEtag));
                        return new AssetDownloadResult(written, resumeEtag);
                    }

                    throw new ObjectChangedException(answer.Status);
                }

                if (answer.Status == 206)
                {
                    // Without If-Range only the answer's own ETag names the object.
                    if (answer.ETag != resumeEtag && (answer.ETag != null || !ifRangeSent))
                    {
                        throw new ObjectChangedException(answer.Status);
                    }

                    var range = answer.Range;
                    if (range == null && !_corsSafe)
                    {
                        throw AssetClientException.HttpError(answer.Status, "no Content-Range");
                    }

                    if (range != null && range.Start != written)
                    {
                        throw AssetClientException.HttpError(answer.Status, "Content-Range is not the range asked for");
                    }

                    total = range?.Total ?? (answer.Length.HasValue && !answer.Encoded ? written + answer.Length.Value : (long?)null);
                }
                else
                {
                    // A 200 is the whole file: under If-Range the object changed, and
                    // without it the host ignored Range.
                    if (written > 0)
                    {
                        await sink.ResetAsync(cancellationToken);
                    }

                    written = 0;

                    // A browser cannot see Content-Encoding cross-origin, so there a
                    // Content-Length may be the compressed size: no total at all.
                    total = answer.Encoded || _corsSafe ? null : answer.Length;
                }

                // After a 200 the resume's ETag describes the old object, never these bytes.
                var etag = answer.Status == 200 ? answer.ETag : answer.ETag ?? resumeEtag;
                var reported = false;
                for (; ; )
                {
                    var next = await answer.Body.NextAsync();
                    if (!next.HasValue)
                    {
                        break;
                    }

                    var chunk = next.Value;
                    if (total.HasValue && written + chunk.Count > total.Value)
                    {
                        throw AssetClientException.NetworkError(answer.Status, "the body ran past its stated length");
                    }

                    // An unsized body is still bounded: no asset the platform accepts is larger.
                    if (written + chunk.Count > AssetFormat.MaxCiphertext)
                    {
                        throw AssetClientException.HttpError(answer.Status, "larger than any asset");
                    }

                    await sink.WriteAsync(chunk, cancellationToken);
                    written += chunk.Count;
                    reported = true;
                    options.OnProgress?.Invoke(new AssetDownloadProgress(written, total, etag));
                }

                if (total.HasValue && written != total.Value)
                {
                    throw AssetClientException.NetworkError(answer.Status, "the body ended early");
                }

                if (!reported)
                {
                    options.OnProgress?.Invoke(new AssetDownloadProgress(written, total, etag));
                }

                return new AssetDownloadResult(written, etag);
            }

            return await RestartingAsync(file, Attempt, async () =>
            {
                if (written > 0)
                {
                    await sink.ResetAsync(cancellationToken);
                }

                written = 0;
                resumeEtag = null;
            });
        }
    }
}
