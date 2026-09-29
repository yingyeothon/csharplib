using System;
using System.Security.Cryptography;
using System.Text;

namespace Yingyeothon.Assets
{
    /// <summary>
    /// The bundle key and what it derives, on primitives netstandard2.0 has: HKDF-SHA256
    /// built on <see cref="HMACSHA256"/> (the <c>HKDF</c> class is .NET 5+), and AES-256-CTR
    /// built on an ECB encryptor of the counter block (there is no CTR mode).
    /// </summary>
    internal sealed class BundleCrypto : IDisposable
    {
        private byte[]? _key;

        internal BundleCrypto(byte[] key)
        {
            _key = key;
        }

        internal bool IsDisposed => _key == null;

        /// <summary>
        /// Checks the header byte and the total length and derives the segment keys. Never
        /// recomputes the plaintext digest: a decryptor cannot tell a derived salt from a
        /// random one, and does not need to.
        /// </summary>
        internal SegmentDecryptor Open(byte[] header, long total, string ad)
        {
            var key = _key ?? throw new ObjectDisposedException(nameof(IAssetBundleClient));
            var segments = AssetFormat.SegmentsOf(total);
            if (segments < 0 || header.Length != AssetFormat.HeaderLength || header[0] != AssetFormat.HeaderByte)
            {
                throw AssetClientException.Corrupt(0);
            }

            var salt = new byte[AssetFormat.SaltLength];
            Buffer.BlockCopy(header, 1, salt, 0, salt.Length);
            var noncePrefix = new byte[AssetFormat.NoncePrefixLength];
            Buffer.BlockCopy(header, 1 + AssetFormat.SaltLength, noncePrefix, 0, noncePrefix.Length);

            var okm = Hkdf(key, salt, Encoding.UTF8.GetBytes(ad), 64);
            var encKey = new byte[32];
            var macKey = new byte[32];
            Buffer.BlockCopy(okm, 0, encKey, 0, 32);
            Buffer.BlockCopy(okm, 32, macKey, 0, 32);
            Array.Clear(okm, 0, okm.Length);
            return new SegmentDecryptor(this, total, segments, noncePrefix, encKey, macKey);
        }

        public void Dispose()
        {
            var key = _key;
            _key = null;
            if (key != null)
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        /// <summary>RFC 5869 with SHA-256: extract with <paramref name="salt"/>, expand with <paramref name="info"/>.</summary>
        internal static byte[] Hkdf(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            byte[] prk;
            using (var extract = new HMACSHA256(salt))
            {
                prk = extract.ComputeHash(ikm);
            }

            var output = new byte[length];
            try
            {
                using (var expand = new HMACSHA256(prk))
                {
                    var previous = new byte[0];
                    var block = new byte[32 + info.Length + 1];
                    var at = 0;
                    for (byte counter = 1; at < length; counter++)
                    {
                        var input = previous.Length + info.Length + 1;
                        Buffer.BlockCopy(previous, 0, block, 0, previous.Length);
                        Buffer.BlockCopy(info, 0, block, previous.Length, info.Length);
                        block[input - 1] = counter;
                        var t = expand.ComputeHash(block, 0, input);
                        Array.Clear(previous, 0, previous.Length);
                        previous = t;
                        var take = Math.Min(t.Length, length - at);
                        Buffer.BlockCopy(t, 0, output, at, take);
                        at += take;
                    }

                    Array.Clear(previous, 0, previous.Length);
                    Array.Clear(block, 0, block.Length);
                }
            }
            finally
            {
                Array.Clear(prk, 0, prk.Length);
            }

            return output;
        }
    }

    /// <summary>One file's verified view: its segment count and each segment's plaintext.</summary>
    internal sealed class SegmentDecryptor : IDisposable
    {
        private readonly BundleCrypto _owner;
        private readonly byte[] _noncePrefix;
        private readonly byte[] _encKey;
        private readonly byte[] _macKey;

        internal SegmentDecryptor(BundleCrypto owner, long total, int segments, byte[] noncePrefix, byte[] encKey, byte[] macKey)
        {
            _owner = owner;
            Total = total;
            Segments = segments;
            PlaintextLength = AssetFormat.PlaintextLengthOf(total, segments);
            _noncePrefix = noncePrefix;
            _encKey = encKey;
            _macKey = macKey;
        }

        internal long Total { get; }

        internal int Segments { get; }

        internal long PlaintextLength { get; }

        /// <summary>
        /// Verifies segment <paramref name="i"/> — exactly the ciphertext bytes of its extent,
        /// tag included — and only then decrypts it. A mismatch is <c>asset_corrupt</c>.
        /// </summary>
        internal byte[] Open(int i, byte[] segment, int offset, int count)
        {
            // A read already in flight when the client was disposed stops at its next
            // segment instead of decrypting on with the derived keys.
            if (_owner.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(IAssetBundleClient));
            }

            AssetFormat.SegmentExtent(i, Total, out var start, out var end);
            if (i < 0 || i >= Segments || count != end - start)
            {
                throw AssetClientException.Corrupt(0);
            }

            var iv = AssetFormat.SegmentIv(_noncePrefix, i, i == Segments - 1);
            var bodyLength = count - AssetFormat.TagLength;
            byte[] tag;
            using (var mac = new HMACSHA256(_macKey))
            {
                mac.TransformBlock(iv, 0, iv.Length, null, 0);
                mac.TransformFinalBlock(segment, offset, bodyLength);
                tag = mac.Hash;
            }

            // Every byte compared, whatever the first difference; nothing of the segment is
            // decrypted before this holds. netstandard2.0 has no FixedTimeEquals.
            var diff = 0;
            for (var b = 0; b < AssetFormat.TagLength; b++)
            {
                diff |= tag[b] ^ segment[offset + bodyLength + b];
            }

            if (diff != 0)
            {
                throw AssetClientException.Corrupt(0);
            }

            var plain = new byte[bodyLength];
            Ctr(iv, segment, offset, bodyLength, plain);
            return plain;
        }

        /// <summary>
        /// AES-256-CTR: the keystream is the ECB encryption of the IV, whose last four bytes
        /// count blocks big-endian from 0. A segment is at most 4,096 blocks, so the counter
        /// never carries into the <c>last</c> byte.
        /// </summary>
        private void Ctr(byte[] iv, byte[] input, int offset, int count, byte[] output)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                aes.Key = _encKey;
                using (var encryptor = aes.CreateEncryptor())
                {
                    var counter = (byte[])iv.Clone();
                    var stream = new byte[16];
                    for (var at = 0; at < count; at += 16)
                    {
                        encryptor.TransformBlock(counter, 0, 16, stream, 0);
                        var n = Math.Min(16, count - at);
                        for (var b = 0; b < n; b++)
                        {
                            output[at + b] = (byte)(input[offset + at + b] ^ stream[b]);
                        }

                        for (var c = 15; c >= 12; c--)
                        {
                            if (++counter[c] != 0)
                            {
                                break;
                            }
                        }
                    }

                    Array.Clear(stream, 0, stream.Length);
                }
            }
        }

        public void Dispose()
        {
            Array.Clear(_encKey, 0, _encKey.Length);
            Array.Clear(_macKey, 0, _macKey.Length);
        }
    }
}
