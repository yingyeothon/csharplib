using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Yingyeothon.Codec
{
    /// <summary>
    /// A strict, allocation-modest recursive-descent JSON parser.
    /// </summary>
    /// <remarks>
    /// Strict per RFC 8259: no trailing commas, no comments, no unquoted keys, no
    /// single quotes, no <c>NaN</c>/<c>Infinity</c>, no leading zeros or a leading
    /// <c>+</c>, and nothing but whitespace after the top-level value. Nesting is
    /// bounded so a hostile frame cannot overflow the stack of a game's main thread.
    /// </remarks>
    internal sealed class JsonParser
    {
        private const int MaxDepth = 64;

        private readonly string _text;
        private int _at;

        private JsonParser(string text)
        {
            _text = text;
        }

        internal static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var parser = new JsonParser(text);
            parser.SkipWhitespace();
            var value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (parser._at != text.Length)
            {
                throw parser.Fail("Unexpected trailing content");
            }

            return value;
        }

        private JsonValue ReadValue(int depth)
        {
            if (depth > MaxDepth)
            {
                throw Fail("Nesting is deeper than " + MaxDepth);
            }

            if (_at >= _text.Length)
            {
                throw Fail("Unexpected end of input");
            }

            var c = _text[_at];
            switch (c)
            {
                case '{':
                    return ReadObject(depth);
                case '[':
                    return ReadArray(depth);
                case '"':
                    return JsonValue.Of(ReadString());
                case 't':
                    Expect("true");
                    return JsonValue.Of(true);
                case 'f':
                    Expect("false");
                    return JsonValue.Of(false);
                case 'n':
                    Expect("null");
                    return JsonValue.Null;
                default:
                    return JsonValue.Of(ReadNumber());
            }
        }

        private JsonValue ReadObject(int depth)
        {
            _at++; // '{'
            var members = new List<KeyValuePair<string, JsonValue>>();
            SkipWhitespace();
            if (Peek() == '}')
            {
                _at++;
                return JsonValue.Object(members);
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw Fail("Expected a quoted object key");
                }

                var key = ReadString();
                SkipWhitespace();
                if (Peek() != ':')
                {
                    throw Fail("Expected ':' after an object key");
                }

                _at++;
                SkipWhitespace();
                members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue(depth + 1)));
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

                throw Fail("Expected ',' or '}' in an object");
            }
        }

        private JsonValue ReadArray(int depth)
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
                items.Add(ReadValue(depth + 1));
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

                throw Fail("Expected ',' or ']' in an array");
            }
        }

        private string ReadString()
        {
            _at++; // opening quote
            var start = _at;
            StringBuilder? builder = null;

            while (true)
            {
                if (_at >= _text.Length)
                {
                    throw Fail("Unterminated string");
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
                    builder.Append(ReadEscape());
                    start = _at;
                    continue;
                }

                if (c < 0x20)
                {
                    throw Fail("A control character must be escaped in a string");
                }

                _at++;
            }
        }

        private char ReadEscape()
        {
            if (_at >= _text.Length)
            {
                throw Fail("Unterminated escape sequence");
            }

            var c = _text[_at++];
            switch (c)
            {
                case '"':
                    return '"';
                case '\\':
                    return '\\';
                case '/':
                    return '/';
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'u':
                    return ReadUnicodeEscape();
                default:
                    throw Fail("Unknown escape sequence '\\" + c + "'");
            }
        }

        private char ReadUnicodeEscape()
        {
            if (_at + 4 > _text.Length)
            {
                throw Fail("Truncated \\u escape sequence");
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
                    throw Fail("Invalid hex digit in a \\u escape sequence");
                }

                code = (code << 4) | digit;
            }

            _at += 4;

            // A surrogate half is kept as-is, the way JSON.parse does: the pair is
            // reassembled naturally because both halves are appended in order.
            return (char)code;
        }

        private double ReadNumber()
        {
            var start = _at;
            if (Peek() == '-')
            {
                _at++;
            }

            if (_at >= _text.Length)
            {
                throw Fail("Unexpected end of input in a number");
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
                throw Fail("Expected a JSON value");
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
                    throw Fail("Expected a digit after the decimal point");
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
                    throw Fail("Expected a digit in the exponent");
                }
            }

            var literal = _text.Substring(start, _at - start);
            if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new JsonParseException("Number '" + literal + "' is out of range", start);
            }

            return value;
        }

        private void Expect(string literal)
        {
            if (_at + literal.Length > _text.Length
                || string.CompareOrdinal(_text, _at, literal, 0, literal.Length) != 0)
            {
                throw Fail("Expected '" + literal + "'");
            }

            _at += literal.Length;
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

        private JsonParseException Fail(string message) => new JsonParseException(message, _at);
    }
}
