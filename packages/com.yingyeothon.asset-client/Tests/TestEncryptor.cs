using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yingyeothon.Assets.Tests
{
    /// <summary>
    /// An independent <c>yyt-enc v1</c> encryptor, written from the service repository's
    /// <c>docs/asset-encryption.md</c> rather than from the runtime, so the tests can make
    /// ciphertexts of any size and any path. The conformance vectors are what prove it agrees
    /// with the Go encryptor. It keeps to what Unity's .NET Standard 2.1 profile has — no
    /// <c>HKDF</c> class, no <c>Aes.EncryptEcb</c> — because the editor compiles these tests too.
    /// </summary>
    internal static class TestEncryptor
    {
        private const int FirstPlain = 65464;
        private const int LaterPlain = 65504;

        internal static byte[] Encrypt(byte[] key, string ad, byte[] plaintext)
        {
            var adBytes = Encoding.UTF8.GetBytes(ad);
            var kDet = Hkdf(key, new byte[32], Encoding.UTF8.GetBytes("yyt-enc v1 det"), 32);
            byte[] d;
            using (var mac = new HMACSHA256(kDet))
            {
                var input = new MemoryStream();
                input.Write(U32(adBytes.Length));
                input.Write(adBytes);
                input.Write(plaintext);
                d = mac.ComputeHash(input.ToArray());
            }

            var saltPrefix = Hkdf(d, new byte[32], Encoding.UTF8.GetBytes("yyt-enc v1 header"), 39);
            var salt = saltPrefix.AsSpan(0, 32).ToArray();
            var noncePrefix = saltPrefix.AsSpan(32).ToArray();
            var km = Hkdf(key, salt, adBytes, 64);
            var kEnc = km.AsSpan(0, 32).ToArray();
            var kMac = km.AsSpan(32).ToArray();

            var n = plaintext.Length <= FirstPlain ? 1 : 1 + (plaintext.Length - FirstPlain + LaterPlain - 1) / LaterPlain;
            var output = new MemoryStream();
            output.WriteByte(0x28);
            output.Write(salt);
            output.Write(noncePrefix);
            var at = 0;
            for (var i = 0; i < n; i++)
            {
                var size = Math.Min(i == 0 ? FirstPlain : LaterPlain, plaintext.Length - at);
                var iv = new byte[16];
                noncePrefix.CopyTo(iv, 0);
                U32(i).CopyTo(iv, 7);
                iv[11] = i == n - 1 ? (byte)1 : (byte)0;
                var body = Ctr(kEnc, iv, plaintext.AsSpan(at, size).ToArray());
                at += size;
                byte[] tag;
                using (var mac = new HMACSHA256(kMac))
                {
                    var signed = new byte[iv.Length + body.Length];
                    iv.CopyTo(signed, 0);
                    body.CopyTo(signed, iv.Length);
                    tag = mac.ComputeHash(signed);
                }

                output.Write(body);
                output.Write(tag);
            }

            return output.ToArray();
        }

        /// <summary>RFC 5869: T(1) = HMAC(PRK, info ‖ 1), T(i) = HMAC(PRK, T(i-1) ‖ info ‖ i).</summary>
        private static byte[] Hkdf(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            byte[] prk;
            using (var extract = new HMACSHA256(salt))
            {
                prk = extract.ComputeHash(ikm);
            }

            var output = new MemoryStream();
            var previous = new byte[0];
            using (var expand = new HMACSHA256(prk))
            {
                for (var i = 1; output.Length < length; i++)
                {
                    var block = new MemoryStream();
                    block.Write(previous, 0, previous.Length);
                    block.Write(info, 0, info.Length);
                    block.WriteByte((byte)i);
                    previous = expand.ComputeHash(block.ToArray());
                    output.Write(previous, 0, previous.Length);
                }
            }

            return output.ToArray().AsSpan(0, length).ToArray();
        }

        private static byte[] Ctr(byte[] key, byte[] iv, byte[] input)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;
            using var encryptor = aes.CreateEncryptor();
            var output = new byte[input.Length];
            var counter = (byte[])iv.Clone();
            var stream = new byte[16];
            for (var at = 0; at < input.Length; at += 16)
            {
                encryptor.TransformBlock(counter, 0, 16, stream, 0);
                for (var b = 0; b < 16 && at + b < input.Length; b++)
                {
                    output[at + b] = (byte)(input[at + b] ^ stream[b]);
                }

                var value = (uint)((counter[12] << 24) | (counter[13] << 16) | (counter[14] << 8) | counter[15]) + 1;
                counter[12] = (byte)(value >> 24);
                counter[13] = (byte)(value >> 16);
                counter[14] = (byte)(value >> 8);
                counter[15] = (byte)value;
            }

            return output;
        }

        private static byte[] U32(int n) => new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n };

        /// <summary>Deterministic bytes that do not repeat with a short period.</summary>
        internal static byte[] Pattern(int length, int seed = 7)
        {
            var output = new byte[length];
            for (var i = 0; i < length; i++)
            {
                output[i] = (byte)((i * 31 + seed + (i >> 8)) & 0xff);
            }

            return output;
        }

        /// <summary>32 key bytes and their <c>yak1.</c> text form.</summary>
        internal static (byte[] Bytes, string Text) Key(int fill)
        {
            var bytes = new byte[32];
            for (var i = 0; i < 32; i++)
            {
                bytes[i] = (byte)((i * 13 + fill) & 0xff);
            }

            return (bytes, "yak1." + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        }
    }
}
