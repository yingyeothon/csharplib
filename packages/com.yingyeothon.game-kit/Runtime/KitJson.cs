using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Yingyeothon.Codec;

namespace Yingyeothon.GameKit
{
    /// <summary>Canonical JSON and SHA-256 for the shared turns contract.</summary>
    public static class KitJson
    {
        /// <summary>RFC 8785 JSON for immutable codec values; invalid Unicode is refused.</summary>
        public static string Canonicalize(JsonValue value)
        { if (value == null) { throw new ArgumentNullException(nameof(value)); } return Encode(value, 0); }
        private static string Encode(JsonValue value, int depth)
        {
            if (depth > 128)
            {
                throw new ArgumentException("Canonical JSON nesting is too deep");
            }

            switch (value.Kind)
            {
                case JsonKind.Null: return "null";
                case JsonKind.Bool: return value.AsBool() ? "true" : "false";
                case JsonKind.String: return String(value.AsString());
                case JsonKind.Number: return Number(value.AsNumber());
                case JsonKind.Array: return "[" + string.Join(",", value.AsArray().Select(x => Encode(x, depth + 1))) + "]";
                default: return "{" + string.Join(",", value.AsObject().OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => String(x.Key) + ":" + Encode(x.Value, depth + 1))) + "}";
            }
        }
        private static string String(string value)
        {
            for (var i = 0; i < value.Length; i++)


            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                    {
                        throw new ArgumentException("Invalid Unicode");
                    }
                }
                else if (char.IsLowSurrogate(value[i]))
                {
                    throw new ArgumentException("Invalid Unicode");
                }
            }
            return Json.Stringify(JsonValue.Of(value));
        }
        private static string Number(double n)
        {
            if (n == 0)
            {
                return "0";


            }
            // Select the shortest precision that round-trips on the target BCL.
            var text = n.ToString("G17", CultureInfo.InvariantCulture);
            for (var p = 1; p <= 17; p++) { var candidate = n.ToString("G" + p, CultureInfo.InvariantCulture); if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed == n) { text = candidate; break; } }


            var negative = text[0] == '-'; if (negative)
            {
                text = text.Substring(1);
            }

            var split = text.Split('E', 'e'); var mantissa = split[0]; var exponent = split.Length == 2 ? int.Parse(split[1], CultureInfo.InvariantCulture) : 0;
            var dot = mantissa.IndexOf('.'); var position = (dot < 0 ? mantissa.Length : dot) + exponent; var digits = mantissa.Replace(".", "");
            while (digits.Length > 1 && digits[0] == '0') { digits = digits.Substring(1); position--; }
            while (digits.Length > 1 && digits[digits.Length - 1] == '0')
            {
                digits = digits.Substring(0, digits.Length - 1);
            }

            string result;
            if (position > 0 && position <= 21)
            {
                result = position >= digits.Length ? digits + new string('0', position - digits.Length) : digits.Substring(0, position) + "." + digits.Substring(position);
            }
            else if (position <= 0 && position > -6)
            {
                result = "0." + new string('0', -position) + digits;
            }
            else { var e = position - 1; result = digits.Substring(0, 1) + (digits.Length > 1 ? "." + digits.Substring(1) : "") + "e" + (e >= 0 ? "+" : "") + e.ToString(CultureInfo.InvariantCulture); }
            return negative ? "-" + result : result;
        }
        /// <summary>Lowercase hex SHA-256 of UTF-8 canonical JSON.</summary>
        public static string Hash(JsonValue value)
        { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonicalize(value))).Select(x => x.ToString("x2", CultureInfo.InvariantCulture))); }
    }
}
