using System;

namespace Yingyeothon.Assets
{
    /// <summary>The bundle key's text form, <c>yak1.</c> + 43 base64url characters.</summary>
    internal static class AssetKey
    {
        private const string Prefix = "yak1.";
        private const int TextLength = 43;
        private const int KeyBytes = 32;
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        /// <summary>
        /// A private copy of the 32 key bytes, from the text form (decoding to 32 bytes and
        /// re-encoding to the same text) or from 32 raw bytes. Anything else is
        /// <c>bad_key</c>, and the exception never quotes what it was given.
        /// </summary>
        internal static byte[] Parse(string? text, byte[]? bytes)
        {
            if (bytes != null)
            {
                if (text != null || bytes.Length != KeyBytes)
                {
                    throw BadKey();
                }

                var copy = new byte[KeyBytes];
                Buffer.BlockCopy(bytes, 0, copy, 0, KeyBytes);
                return copy;
            }

            if (text == null || text.Length != Prefix.Length + TextLength
                || !text.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw BadKey();
            }

            var body = text.Substring(Prefix.Length);
            var raw = Decode(body);
            if (raw == null)
            {
                throw BadKey();
            }

            // Only 16 of the 64 characters can end a canonical 32-byte encoding; the other
            // 48 decode to the same bytes as one of them, and must not be keys.
            if (raw.Length != KeyBytes || !string.Equals(Encode(raw), body, StringComparison.Ordinal))
            {
                Array.Clear(raw, 0, raw.Length);
                throw BadKey();
            }

            return raw;
        }

        private static AssetClientException BadKey() => new AssetClientException(AssetErrorCodes.BadKey, 0, null, null);

        /// <summary>Lenient on purpose: the re-encode comparison is what makes it strict.</summary>
        private static byte[]? Decode(string text)
        {
            var output = new byte[text.Length * 6 / 8];
            var bits = 0;
            var value = 0;
            var at = 0;
            foreach (var c in text)
            {
                var digit = c < 128 ? Alphabet.IndexOf(c) : -1;
                if (digit < 0)
                {
                    Array.Clear(output, 0, output.Length);
                    return null;
                }

                value = ((value << 6) | digit) & 0xffffff;
                bits += 6;
                if (bits >= 8)
                {
                    bits -= 8;
                    output[at++] = (byte)((value >> bits) & 0xff);
                }
            }

            return output;
        }

        private static string Encode(byte[] bytes)
        {
            var output = new char[(bytes.Length * 8 + 5) / 6];
            var o = 0;
            var i = 0;
            for (; i + 3 <= bytes.Length; i += 3)
            {
                var n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
                output[o++] = Alphabet[(n >> 18) & 63];
                output[o++] = Alphabet[(n >> 12) & 63];
                output[o++] = Alphabet[(n >> 6) & 63];
                output[o++] = Alphabet[n & 63];
            }

            var rest = bytes.Length - i;
            if (rest == 1)
            {
                var n = bytes[i] << 16;
                output[o++] = Alphabet[(n >> 18) & 63];
                output[o++] = Alphabet[(n >> 12) & 63];
            }
            else if (rest == 2)
            {
                var n = (bytes[i] << 16) | (bytes[i + 1] << 8);
                output[o++] = Alphabet[(n >> 18) & 63];
                output[o++] = Alphabet[(n >> 12) & 63];
                output[o++] = Alphabet[(n >> 6) & 63];
            }

            var text = new string(output, 0, o);
            Array.Clear(output, 0, output.Length);
            return text;
        }
    }
}
