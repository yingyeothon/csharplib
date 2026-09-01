using System;
using System.Collections.Generic;

namespace Yingyeothon.Codec
{
    /// <summary>
    /// An immutable JSON value: null, bool, number, string, array or object.
    /// </summary>
    /// <remarks>
    /// This is a reference type on purpose. A <c>JsonValue?</c> that is C# <c>null</c>
    /// means "the field is not on the wire at all" (Go's <c>omitempty</c>), while
    /// <see cref="Null"/> means "the field is present and holds JSON <c>null</c>".
    /// The gateway distinguishes the two, so the SDK must as well.
    ///
    /// Object members keep their insertion order, which makes serialization
    /// deterministic and therefore assertable in tests.
    /// </remarks>
    public sealed class JsonValue : IEquatable<JsonValue>
    {
        private static readonly JsonValue TrueValue = new JsonValue(true);
        private static readonly JsonValue FalseValue = new JsonValue(false);
        private static readonly IReadOnlyList<JsonValue> EmptyItems = new JsonValue[0];
        private static readonly IReadOnlyList<KeyValuePair<string, JsonValue>> EmptyMembers =
            new KeyValuePair<string, JsonValue>[0];

        private readonly bool _bool;
        private readonly double _number;
        private readonly string? _string;
        private readonly IReadOnlyList<JsonValue>? _items;
        private readonly IReadOnlyList<KeyValuePair<string, JsonValue>>? _members;
        private readonly Dictionary<string, int>? _index;

        private JsonValue()
        {
            Kind = JsonKind.Null;
        }

        private JsonValue(bool value)
        {
            Kind = JsonKind.Bool;
            _bool = value;
        }

        private JsonValue(double value)
        {
            Kind = JsonKind.Number;
            _number = value;
        }

        private JsonValue(string value)
        {
            Kind = JsonKind.String;
            _string = value;
        }

        private JsonValue(IReadOnlyList<JsonValue> items)
        {
            Kind = JsonKind.Array;
            _items = items;
        }

        private JsonValue(IReadOnlyList<KeyValuePair<string, JsonValue>> members, Dictionary<string, int> index)
        {
            Kind = JsonKind.Object;
            _members = members;
            _index = index;
        }

        /// <summary>The JSON <c>null</c> literal. Not the same as a C# null reference.</summary>
        public static readonly JsonValue Null = new JsonValue();

        /// <summary>Which of the six JSON kinds this value is.</summary>
        public JsonKind Kind { get; }

        public bool IsNull => Kind == JsonKind.Null;

        public static JsonValue Of(bool value) => value ? TrueValue : FalseValue;

        public static JsonValue Of(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "JSON has no NaN or Infinity.");
            }

            return new JsonValue(value);
        }

        public static JsonValue Of(int value) => new JsonValue(value);

        public static JsonValue Of(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value), "Use JsonValue.Null for a JSON null.");
            }

            return new JsonValue(value);
        }

        /// <summary>Builds an array. The items are copied, so later edits do not leak in.</summary>
        public static JsonValue Array(IEnumerable<JsonValue> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            var copy = new List<JsonValue>();
            foreach (var item in items)
            {
                copy.Add(item ?? throw new ArgumentException("An array item cannot be a null reference; use JsonValue.Null.", nameof(items)));
            }

            return copy.Count == 0 ? new JsonValue(EmptyItems) : new JsonValue(copy);
        }

        /// <summary>Builds an array from a parameter list.</summary>
        public static JsonValue ArrayOf(params JsonValue[] items) => Array(items);

        /// <summary>
        /// Builds an object. Members keep the order given; a repeated key replaces
        /// the earlier value in place rather than appending a second entry.
        /// </summary>
        public static JsonValue Object(IEnumerable<KeyValuePair<string, JsonValue>> members)
        {
            if (members == null)
            {
                throw new ArgumentNullException(nameof(members));
            }

            var ordered = new List<KeyValuePair<string, JsonValue>>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (member.Key == null)
                {
                    throw new ArgumentException("An object key cannot be null.", nameof(members));
                }

                var value = member.Value ?? throw new ArgumentException(
                    "An object value cannot be a null reference; use JsonValue.Null.", nameof(members));

                if (index.TryGetValue(member.Key, out var at))
                {
                    ordered[at] = new KeyValuePair<string, JsonValue>(member.Key, value);
                }
                else
                {
                    index[member.Key] = ordered.Count;
                    ordered.Add(new KeyValuePair<string, JsonValue>(member.Key, value));
                }
            }

            return ordered.Count == 0
                ? new JsonValue(EmptyMembers, index)
                : new JsonValue(ordered, index);
        }

        // ---- readers -------------------------------------------------------

        public bool AsBool()
        {
            Require(JsonKind.Bool);
            return _bool;
        }

        public double AsNumber()
        {
            Require(JsonKind.Number);
            return _number;
        }

        /// <summary>Reads the number as an <see cref="int"/>, refusing a fractional or out-of-range value.</summary>
        /// <exception cref="JsonKindException">The value is not a number at all.</exception>
        /// <exception cref="JsonNumberException">
        /// The value is a number but is fractional or outside the <see cref="int"/> range.
        /// </exception>
        /// <remarks>
        /// There is no <c>AsInt64</c>. The value is stored as a <see cref="double"/>,
        /// so an integer past 2^53 has already lost precision by the time it gets
        /// here and no reader could give it back. A wire field that needs more range
        /// than that has to arrive as a string.
        /// </remarks>
        public int AsInt32()
        {
            var number = AsNumber();
            if (number < int.MinValue || number > int.MaxValue || Math.Floor(number) != number)
            {
                // Not a kind error: the kind is right and the range is not, and
                // "expected a Number but the value is a Number" told the reader
                // nothing. The value itself stays out of the message; it is wire data.
                throw new JsonNumberException("The JSON number is not an integer within the Int32 range.");
            }

            return (int)number;
        }

        public string AsString()
        {
            Require(JsonKind.String);
            return _string!;
        }

        public IReadOnlyList<JsonValue> AsArray()
        {
            Require(JsonKind.Array);
            return _items!;
        }

        /// <summary>The object's members in wire order.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> AsObject()
        {
            Require(JsonKind.Object);
            return _members!;
        }

        // ---- object field access ------------------------------------------

        /// <summary>
        /// Looks a member up. Returns false when this is not an object or the key is
        /// absent, which is how an <c>omitempty</c> field reads.
        /// </summary>
        public bool TryGetMember(string key, out JsonValue value)
        {
            if (Kind != JsonKind.Object || key == null || !_index!.TryGetValue(key, out var at))
            {
                value = Null;
                return false;
            }

            value = _members![at].Value;
            return true;
        }

        /// <summary>The member, or a C# null when the field is absent.</summary>
        public JsonValue? GetMemberOrNull(string key) => TryGetMember(key, out var value) ? value : null;

        /// <summary>The member as a string, or null when it is absent or JSON null.</summary>
        public string? GetString(string key)
            => TryGetMember(key, out var value) && value.Kind == JsonKind.String ? value.AsString() : null;

        /// <summary>The member as a number, or null when it is absent or JSON null.</summary>
        public double? GetNumber(string key)
            => TryGetMember(key, out var value) && value.Kind == JsonKind.Number ? value.AsNumber() : (double?)null;

        /// <summary>The member as a bool, or null when it is absent or JSON null.</summary>
        public bool? GetBool(string key)
            => TryGetMember(key, out var value) && value.Kind == JsonKind.Bool ? value.AsBool() : (bool?)null;

        /// <summary>The member as an array, or an empty list when it is absent, null or not an array.</summary>
        public IReadOnlyList<JsonValue> GetArrayOrEmpty(string key)
            => TryGetMember(key, out var value) && value.Kind == JsonKind.Array ? value.AsArray() : EmptyItems;

        // ---- equality ------------------------------------------------------

        public bool Equals(JsonValue? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other is null || other.Kind != Kind)
            {
                return false;
            }

            switch (Kind)
            {
                case JsonKind.Null:
                    return true;
                case JsonKind.Bool:
                    return _bool == other._bool;
                case JsonKind.Number:
                    return _number.Equals(other._number);
                case JsonKind.String:
                    return string.Equals(_string, other._string, StringComparison.Ordinal);
                case JsonKind.Array:
                    return ItemsEqual(_items!, other._items!);
                default:
                    return MembersEqual(other);
            }
        }

        private static bool ItemsEqual(IReadOnlyList<JsonValue> left, IReadOnlyList<JsonValue> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (var i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool MembersEqual(JsonValue other)
        {
            // Member order is not part of a JSON object's identity, so compare as a
            // set — through the index the other object already carries, because
            // building a lookup here would allocate a dictionary on every comparison,
            // and frames are compared per tick.
            var left = _members!;
            var right = other._members!;
            if (left.Count != right.Count)
            {
                return false;
            }

            // Keys are unique by construction, so equal counts plus "every key on the
            // left is on the right with an equal value" is set equality.
            var index = other._index!;
            for (var i = 0; i < left.Count; i++)
            {
                var member = left[i];
                if (!index.TryGetValue(member.Key, out var at) || !member.Value.Equals(right[at].Value))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as JsonValue);

        public override int GetHashCode()
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    return 0;
                case JsonKind.Bool:
                    return _bool ? 1 : 2;
                case JsonKind.Number:
                    return _number.GetHashCode();
                case JsonKind.String:
                    return StringComparer.Ordinal.GetHashCode(_string!);
                case JsonKind.Array:
                    return unchecked(3 + _items!.Count * 31);
                default:
                    // Order-independent, matching Equals.
                    var hash = 5;
                    foreach (var member in _members!)
                    {
                        hash ^= StringComparer.Ordinal.GetHashCode(member.Key);
                    }

                    return hash;
            }
        }

        /// <summary>Serializes this value as compact JSON.</summary>
        public override string ToString() => JsonWriter.Write(this);

        private void Require(JsonKind kind)
        {
            if (Kind != kind)
            {
                throw new JsonKindException(kind, Kind);
            }
        }
    }
}
