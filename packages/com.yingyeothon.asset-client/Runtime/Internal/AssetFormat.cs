namespace Yingyeothon.Assets
{
    /// <summary>
    /// The <c>yyt-enc v1</c> layout: a 40-byte header, then 64 KiB ciphertext segments
    /// each ending in a 32-byte HMAC-SHA256 tag. Pure arithmetic, no IO and no crypto, so
    /// every offset rule is testable on its own. The normative text is
    /// <c>docs/asset-encryption.md</c> in the service repository.
    /// </summary>
    internal static class AssetFormat
    {
        /// <summary><c>0x28</c> ‖ salt (32) ‖ noncePrefix (7).</summary>
        internal const int HeaderLength = 40;

        /// <summary>The first byte of every ciphertext: the header length.</summary>
        internal const byte HeaderByte = 0x28;

        internal const int SaltLength = 32;

        internal const int NoncePrefixLength = 7;

        /// <summary>Ciphertext segment size, tag included.</summary>
        internal const int SegmentSize = 65536;

        internal const int TagLength = 32;

        /// <summary>Plaintext a full first segment holds: 65,536 − 40 − 32.</summary>
        internal const int FirstPlain = SegmentSize - HeaderLength - TagLength;

        /// <summary>Plaintext every full later segment holds: 65,536 − 32.</summary>
        internal const int LaterPlain = SegmentSize - TagLength;

        /// <summary>An empty file: the header and one empty segment's tag.</summary>
        internal const long MinCiphertext = HeaderLength + TagLength;

        /// <summary>The platform's 256 MiB file ceiling plus the format's overhead (4,099 segments): 268,566,664 bytes.</summary>
        internal const long MaxCiphertext = 256L * 1024 * 1024 + HeaderLength + TagLength * 4099L;

        /// <summary>The segment count a ciphertext length implies, or -1 when no plaintext encrypts to that length.</summary>
        internal static int SegmentsOf(long total)
        {
            if (total < MinCiphertext || total > MaxCiphertext)
            {
                return -1;
            }

            if (total <= SegmentSize)
            {
                return 1;
            }

            var n = 1 + (int)((total - SegmentSize + SegmentSize - 1) / SegmentSize);

            // The last segment must hold at least one plaintext byte and its tag.
            return total - (long)SegmentSize * (n - 1) < TagLength + 1 ? -1 : n;
        }

        internal static long PlaintextLengthOf(long total, int segments) => total - HeaderLength - (long)TagLength * segments;

        /// <summary>Ciphertext offset where segment <paramref name="i"/> starts.</summary>
        internal static long CipherStart(int i) => i == 0 ? HeaderLength : (long)SegmentSize * i;

        /// <summary>Plaintext offset where segment <paramref name="i"/> starts.</summary>
        internal static long PlainStart(int i) => i == 0 ? 0 : FirstPlain + (long)LaterPlain * (i - 1);

        /// <summary>
        /// The segment holding plaintext offset <paramref name="p"/>. An offset past any file
        /// the format allows is clamped first, so a caller's huge window cannot overflow the
        /// segment number into a negative range.
        /// </summary>
        internal static int SegmentOf(long p)
        {
            p = System.Math.Min(p, MaxCiphertext);
            return p < FirstPlain ? 0 : 1 + (int)((p - FirstPlain) / LaterPlain);
        }

        /// <summary>
        /// Ciphertext range <c>[start, end)</c> of segment <paramref name="i"/> in a file of
        /// <paramref name="total"/> bytes; with a negative total, the nominal extent of a full
        /// segment, which is what a request can ask for before the length is known.
        /// </summary>
        internal static void SegmentExtent(int i, long total, out long start, out long end)
        {
            start = CipherStart(i);
            var full = start + (i == 0 ? SegmentSize - HeaderLength : SegmentSize);
            end = total < 0 || full < total ? full : total;
        }

        /// <summary><c>IV_i = noncePrefix ‖ u32be(i) ‖ last ‖ 0x00000000</c>.</summary>
        internal static byte[] SegmentIv(byte[] noncePrefix, int i, bool last)
        {
            var iv = new byte[16];
            System.Buffer.BlockCopy(noncePrefix, 0, iv, 0, NoncePrefixLength);
            iv[7] = (byte)(i >> 24);
            iv[8] = (byte)(i >> 16);
            iv[9] = (byte)(i >> 8);
            iv[10] = (byte)i;
            iv[11] = last ? (byte)1 : (byte)0;
            return iv;
        }
    }
}
