using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Yingyeothon.Codec
{
    /// <summary>
    /// A strict, allocation-modest recursive-descent JSON parser that never throws.
    /// </summary>
    /// <remarks>
    /// Strict per RFC 8259: no trailing commas, no comments, no unquoted keys, no
    /// single quotes, no <c>NaN</c>/<c>Infinity</c>, no leading zeros or a leading
    /// <c>+</c>, no byte-order mark, and nothing but whitespace after the top-level
    /// value. Nesting and length are bounded so a hostile frame cannot overflow the
    /// stack of a game's main thread or make it allocate without limit.
    ///
    /// The core reports a <see cref="JsonParseFailure"/> instead of throwing, for two
    /// reasons. The gateway socket parses every inbound frame, so a peer sending
    /// garbage must not be able to charge the client an exception per frame; and a
    /// message built from the input would carry a frame body into the consumer's log
    /// writer, which <c>rules/security.md</c> forbids. <see cref="Json.Parse"/> is a
    /// thin throwing wrapper for the callers that want one.
    /// </remarks>
    internal sealed class JsonParser
    {
        internal const int MaxDepth = 64;

        private readonly string _text;
        private int _at;
        private JsonParseError _error;
        private int _errorAt;

        private JsonParser(string text)
        {
            _text = text;
        }

        /// <summary>
        /// Parses <paramref name="text"/>, which may be null. Returns false and a
        /// reason rather than throwing, and allocates nothing on the failing path
        /// beyond what it had already built.
        /// </summary>
        internal static bool TryParse(string? text, int maxLength, out JsonValue value, out JsonParseFailure failure)
        {
            if (text == null)
            {
                value = JsonValue.Null;
                failure = new JsonParseFailure(JsonParseError.NullInput, 0);
                return false;
            }

            // Before any scanning: the whole point of the cap is that an enormous
            // document costs one comparison, not a walk.
            if (text.Length > maxLength)
            {
                value = JsonValue.Null;
                failure = new JsonParseFailure(JsonParseError.InputTooLong, 0);
                return false;
            }

            var parser = new JsonParser(text);
            parser.SkipWhitespace();
            var parsed = parser.ReadValue(0);
            if (parsed != null)
            {
                parser.SkipWhitespace();
                if (parser._at != text.Length)
                {
                    parser.Fail(JsonParseError.TrailingContent);
                }
            }

            if (parser._error != JsonParseError.None)
            {
                value = JsonValue.Null;
                failure = new JsonParseFailure(parser._error, parser._errorAt);
                return false;
            }

            value = parsed!;
            failure = default;
            return true;
        }

        /// <summary>Records the first failure and answers the "failed" sentinel.</summary>
        private JsonValue? Fail(JsonParseError error) => FailAt(error, _at);

        private JsonValue? FailAt(JsonParseError error, int index)
        {
            // First failure wins: everything above unwinds by returning null, and a
            // later, less specific reason must not overwrite the real one.
            if (_error == JsonParseError.None)
            {
                _error = error;
                _errorAt = index;
            }

            return null;
        }

        /// <param name="depth">How many containers are already open around this value.</param>
        private JsonValue? ReadValue(int depth)
        {
            if (_at >= _text.Length)
            {
                return Fail(JsonParseError.UnexpectedEndOfInput);
            }

            switch (_text[_at])
            {
                case '{':
                    return depth >= MaxDepth ? Fail(JsonParseError.DepthExceeded) : ReadObject(depth + 1);
                case '[':
                    return depth >= MaxDepth ? Fail(JsonParseError.DepthExceeded) : ReadArray(depth + 1);
                case '"':
                    {
                        var text = ReadString();
                        return text == null ? null : JsonValue.Of(text);
                    }

                case 't':
                    return Expect("true") ? JsonValue.Of(true) : null;
                case 'f':
                    return Expect("false") ? JsonValue.Of(false) : null;
                case 'n':
                    return Expect("null") ? JsonValue.Null : null;
                default:
                    return TryReadNumber(out var number) ? JsonValue.Of(number) : null;
            }
        }

        private JsonValue? ReadObject(int depth)
        {
            _at++; // '{'
            var members = new List<KeyValuePair<string, JsonValue>>();
            SkipWhitespace();
            if (Peek() == '}')
            {
                _at++;
                return JsonValue.Object(members);
            }

            // Every path out of this loop either returns or fails, and `continue`
            // only runs after a ',' was consumed, so a malformed document cannot
            // spin here.
            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    return Fail(JsonParseError.ExpectedKey);
                }

                var key = ReadString();
                if (key == null)
                {
                    return null;
                }

                SkipWhitespace();
                if (Peek() != ':')
                {
                    return Fail(JsonParseError.ExpectedColon);
                }

                _at++;
                SkipWhitespace();
                var item = ReadValue(depth);
                if (item == null)
                {
                    return null;
                }

                members.Add(new KeyValuePair<string, JsonValue>(key, item));
                SkipWhitespace();
                var c = Peek();
                if (c == ',')
                {
                    _at++;
                    continue;
                }

                if (c == '}')
                {
                    _at++;
                    return JsonValue.Object(members);
                }

                return Fail(JsonParseError.ExpectedCommaOrEnd);
            }
        }

        private JsonValue? ReadArray(int depth)
        {
            _at++; // '['
            var items = new List<JsonValue>();
            SkipWhitespace();
            if (Peek() == ']')
            {
                _at++;
                return JsonValue.Array(items);
            }

            while (true)
            {
                SkipWhitespace();
                var item = ReadValue(depth);
                if (item == null)
                {
                    return null;
                }

                items.Add(item);
                SkipWhitespace();
                var c = Peek();
                if (c == ',')
                {
                    _at++;
                    continue;
                }

                if (c == ']')
                {
                    _at++;
                    return JsonValue.Array(items);
                }

                return Fail(JsonParseError.ExpectedCommaOrEnd);
            }
        }

        private string? ReadString()
        {
            _at++; // opening quote
            var start = _at;
            StringBuilder? builder = null;

            while (true)
            {
                if (_at >= _text.Length)
                {
                    Fail(JsonParseError.UnterminatedString);
                    return null;
                }

                var c = _text[_at];
                if (c == '"')
                {
                    if (builder == null)
                    {
                        var plain = _text.Substring(start, _at - start);
                        _at++;
                        return plain;
                    }

                    builder.Append(_text, start, _at - start);
                    _at++;
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    builder ??= new StringBuilder();
                    builder.Append(_text, start, _at - start);
                    _at++;
                    if (!TryReadEscape(out var escaped))
                    {
                        return null;
                    }

                    builder.Append(escaped);
                    start = _at;
                    continue;
                }

                if (c < 0x20)
                {
                    Fail(JsonParseError.UnescapedControlCharacter);
                    return null;
                }

                _at++;
            }
        }

        private bool TryReadEscape(out char value)
        {
            value = '\0';
            if (_at >= _text.Length)
            {
                Fail(JsonParseError.UnexpectedEndOfInput);
                return false;
            }

            switch (_text[_at])
            {
                case '"':
                    value = '"';
                    break;
                case '\\':
                    value = '\\';
                    break;
                case '/':
                    value = '/';
                    break;
                case 'b':
                    value = '\b';
                    break;
                case 'f':
                    value = '\f';
                    break;
                case 'n':
                    value = '\n';
                    break;
                case 'r':
                    value = '\r';
                    break;
                case 't':
                    value = '\t';
                    break;
                case 'u':
                    _at++;
                    return TryReadUnicodeEscape(out value);
                default:
                    // The offending character is wire data, so it names its offset
                    // and nothing more.
                    Fail(JsonParseError.UnknownEscape);
                    return false;
            }

            _at++;
            return true;
        }

        private bool TryReadUnicodeEscape(out char value)
        {
            value = '\0';
            if (_at + 4 > _text.Length)
            {
                FailAt(JsonParseError.TruncatedUnicodeEscape, _text.Length);
                return false;
            }

            var code = 0;
            for (var i = 0; i < 4; i++)
            {
                var c = _text[_at + i];
                int digit;
                if (c >= '0' && c <= '9')
                {
                    digit = c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    digit = c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    digit = c - 'A' + 10;
                }
                else
                {
                    FailAt(JsonParseError.InvalidUnicodeEscape, _at + i);
                    return false;
                }

                code = (code << 4) | digit;
            }

            _at += 4;

            // A surrogate half is kept as-is, the way JSON.parse does: a pair is
            // reassembled naturally because both halves are appended in order, and an
            // unpaired half is the caller's data to keep. The writer is what makes an
            // unpaired half survive the trip back out.
            value = (char)code;
            return true;
        }

        private bool TryReadNumber(out double value)
        {
            value = 0d;
            var start = _at;
            if (Peek() == '-')
            {
                _at++;
            }

            if (_at >= _text.Length)
            {
                Fail(JsonParseError.UnexpectedEndOfInput);
                return false;
            }

            if (_text[_at] == '0')
            {
                _at++;
            }
            else if (_text[_at] >= '1' && _text[_at] <= '9')
            {
                while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9')
                {
                    _at++;
                }
            }
            else
            {
                Fail(JsonParseError.ExpectedValue);
                return false;
            }

            if (_at < _text.Length && _text[_at] == '.')
            {
                _at++;
                var digits = 0;
                while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9')
                {
                    _at++;
                    digits++;
                }

                if (digits == 0)
                {
                    Fail(JsonParseError.ExpectedFractionDigit);
                    return false;
                }
            }

            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                _at++;
                if (_at < _text.Length && (_text[_at] == '+' || _text[_at] == '-'))
                {
                    _at++;
                }

                var digits = 0;
                while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9')
                {
                    _at++;
                    digits++;
                }

                if (digits == 0)
                {
                    Fail(JsonParseError.ExpectedExponentDigit);
                    return false;
                }
            }

            var literal = _text.Substring(start, _at - start);
            if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                // Underflow to zero is fine and matches JSON.parse; overflow is not,
                // because Infinity has no JSON spelling to write back out.
                value = 0d;
                FailAt(JsonParseError.NumberOutOfRange, start);
                return false;
            }

            // .NET Core 3.0 made double.TryParse IEEE-754 compliant and kept the sign
            // of a negative zero; Unity's Mono predates that and hands back +0 for
            // "-0" and for anything that underflows from below. Restoring the sign
            // here is what makes the value tree the same shape on both runtimes —
            // JSON.parse("-0") is -0 too. The writer still spells it "0".
            if (value == 0d && _text[start] == '-')
            {
                value = -0d;
            }

            return true;
        }

        private bool Expect(string literal)
        {
            if (_at + literal.Length > _text.Length
                || string.CompareOrdinal(_text, _at, literal, 0, literal.Length) != 0)
            {
                Fail(JsonParseError.ExpectedLiteral);
                return false;
            }

            _at += literal.Length;
            return true;
        }

        private char Peek() => _at < _text.Length ? _text[_at] : '\0';

        private void SkipWhitespace()
        {
            while (_at < _text.Length)
            {
                var c = _text[_at];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    _at++;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
