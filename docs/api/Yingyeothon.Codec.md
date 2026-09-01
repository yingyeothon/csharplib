# Yingyeothon.Codec

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.codec/README.md`](../../packages/com.yingyeothon.codec/README.md).

## Contents

- [`ICodec<TWire>`](#interface-icodectwire)
- [`Json`](#static-class-json)
- [`JsonCodec`](#static-class-jsoncodec)
- [`JsonKind`](#enum-jsonkind)
- [`JsonKindException`](#class-jsonkindexception)
- [`JsonNumberException`](#class-jsonnumberexception)
- [`JsonObjectBuilder`](#class-jsonobjectbuilder)
- [`JsonParseError`](#enum-jsonparseerror)
- [`JsonParseException`](#class-jsonparseexception)
- [`JsonParseFailure`](#struct-jsonparsefailure)
- [`JsonValue`](#class-jsonvalue)
- [`JsonWriter`](#static-class-jsonwriter)

## interface ICodec<TWire>

| Member | Summary |
| --- | --- |
| `Decode(TWire) : JsonValue` |  |
| `Encode(JsonValue) : TWire` |  |

## static class Json

Entry points for reading and writing JSON.

| Member | Summary |
| --- | --- |
| `Array(JsonValue[]) : JsonValue` | Builds an array from a parameter list. |
| `ArrayOfStrings(IEnumerable<String>) : JsonValue` | Builds an array of strings. A null item is refused, as everywhere else. |
| `MaxBigLength : Int32` | The largest limit `ParseBig` will accept. |
| `MaxDepth : Int32` | How many nested arrays or objects a document may contain. |
| `MaxLength : Int32` | The longest document `Parse` and `TryParse` accept, in characters. |
| `Object() : JsonObjectBuilder` | Starts building an object whose absent fields are simply omitted. |
| `Parse(String) : JsonValue` | Parses JSON text, throwing `JsonParseException` on malformed input. |
| `ParseBig(String, Int32) : JsonValue` | Parses a document that is deliberately larger than a frame, up to `maxLength` characters. |
| `Stringify(JsonValue) : String` | Serializes a value as compact JSON. |
| `TryParse(String, JsonValue&) : Boolean` | Parses JSON text without throwing, for a caller that only wants to know whether it worked. |
| `TryParse(String, JsonValue&, JsonParseFailure&) : Boolean` | Parses JSON text without throwing, reporting why it was refused. |
| `TryParseBig(String, Int32, JsonValue&, JsonParseFailure&) : Boolean` | Non-throwing `ParseBig` . |

## static class JsonCodec

The JSON text codec.

| Member | Summary |
| --- | --- |
| `Decode(String) : JsonValue` | Parses JSON text, throwing `JsonParseException` on malformed input. |
| `Encode(JsonValue) : String` | Serializes a value as compact JSON. |
| `Instance : ICodec<String>` | The shared, stateless codec instance. |

## enum JsonKind

The six JSON value kinds.

- `Array` — A JSON array.
- `Bool` — `true` or `false` .
- `Null` — JSON `null` . Distinct from a field that was absent.
- `Number` — A JSON number, held as a `double` .
- `Object` — A JSON object.
- `String` — A JSON string.

## class JsonKindException

Thrown when a `JsonValue` is read as the wrong kind.

| Member | Summary |
| --- | --- |
| `Actual : JsonKind get` | The kind the value actually held. |
| `Expected : JsonKind get` | The kind the accessor required. |
| `ctor(JsonKind, JsonKind)` |  |

## class JsonNumberException

Thrown when a JSON number is the right kind but the wrong shape for the requested conversion — fractional, or outside the target's range.

| Member | Summary |
| --- | --- |
| `ctor(String)` | Creates an exception with a message that must not quote the value. |

## class JsonObjectBuilder

Builds a JSON object where a null argument means "leave the field off the wire", which is how Go's `omitempty` marshals an empty value. Writing `null` explicitly is a separate, deliberate call.

| Member | Summary |
| --- | --- |
| `Build() : JsonValue` | Produces the object. |
| `Set(String, JsonValue) : JsonObjectBuilder` | Adds a member. A null `value` omits the field entirely. |
| `Set(String, Nullable<Boolean>) : JsonObjectBuilder` | Adds a bool member. A null `value` omits the field. |
| `Set(String, Nullable<Double>) : JsonObjectBuilder` | Adds a number member. A null `value` omits the field. |
| `Set(String, String) : JsonObjectBuilder` | Adds a string member. A null `value` omits the field. |
| `SetNull(String) : JsonObjectBuilder` | Adds an explicit JSON `null` , which is not the same as omitting the field. |
| `ctor()` |  |

## enum JsonParseError

Why a document was refused. Part of the public contract: these names reach logs.

- `DepthExceeded` — More nested arrays or objects than `Json.MaxDepth` allows.
- `ExpectedColon` — An object key was not followed by `:` .
- `ExpectedCommaOrEnd` — An array or object element was not followed by `,` or its closing bracket.
- `ExpectedExponentDigit` — An exponent marker was not followed by a digit.
- `ExpectedFractionDigit` — A decimal point was not followed by a digit.
- `ExpectedKey` — An object member did not begin with a quoted key.
- `ExpectedLiteral` — `true` , `false` or `null` was started but not spelled out.
- `ExpectedValue` — A value was expected and the character there cannot begin one.
- `InputTooLong` — The text was longer than the limit the caller allowed.
- `InvalidUnicodeEscape` — A `\u` escape contained something that is not a hex digit.
- `None` — No failure.
- `NullInput` — The text was a null reference.
- `NumberOutOfRange` — The number does not fit in a `Double` .
- `TrailingContent` — Something other than whitespace followed the top-level value.
- `TruncatedUnicodeEscape` — The text ended inside a `\u` escape.
- `UnescapedControlCharacter` — A character below U+0020 appeared in a string without an escape.
- `UnexpectedEndOfInput` — The text ended in the middle of a value.
- `UnknownEscape` — A backslash was followed by something JSON does not escape.
- `UnterminatedString` — The text ended before a string's closing quote.

## class JsonParseException

Thrown when a string cannot be parsed as JSON.

| Member | Summary |
| --- | --- |
| `Error : JsonParseError get` | Why the document was refused. |
| `Failure : JsonParseFailure get` | The reason and offset, in the form the non-throwing path reports. |
| `Index : Int32 get` | The character offset the parser refused. |
| `ctor(JsonParseFailure)` | Creates an exception describing `failure` . |

## struct JsonParseFailure

A parse failure: a reason and the offset it was found at.

| Member | Summary |
| --- | --- |
| `Equals(JsonParseFailure) : Boolean` |  |
| `Equals(Object) : Boolean` |  |
| `Error : JsonParseError get` | Why the document was refused. |
| `GetHashCode() : Int32` |  |
| `Index : Int32 get` | The character offset the parser refused, or 0 when the input was rejected before any scanning (a null reference, or text over the length limit). |
| `IsFailure : Boolean get` | Whether this describes a failure at all. |
| `ToString() : String` | The reason and the offset, and nothing from the document itself. |
| `ctor(JsonParseError, Int32)` | Creates a failure. |

## class JsonValue

An immutable JSON value: null, bool, number, string, array or object.

| Member | Summary |
| --- | --- |
| `Array(IEnumerable<JsonValue>) : JsonValue` | Builds an array. The items are copied, so later edits do not leak in. |
| `ArrayOf(JsonValue[]) : JsonValue` | Builds an array from a parameter list. |
| `AsArray() : IReadOnlyList<JsonValue>` | The array, or throws `JsonKindException` . |
| `AsBool() : Boolean` | The boolean, or throws `JsonKindException` . |
| `AsInt32() : Int32` | Reads the number as an `Int32` , refusing a fractional or out-of-range value. |
| `AsNumber() : Double` | The number, or throws `JsonKindException` . |
| `AsObject() : IReadOnlyList<KeyValuePair<String, JsonValue>>` | The object's members in wire order. |
| `AsString() : String` | The string, or throws `JsonKindException` . |
| `Equals(JsonValue) : Boolean` |  |
| `Equals(Object) : Boolean` |  |
| `GetArrayOrEmpty(String) : IReadOnlyList<JsonValue>` | The member as an array, or an empty list when it is absent, null or not an array. |
| `GetBool(String) : Nullable<Boolean>` | The member as a bool, or null when it is absent or JSON null. |
| `GetHashCode() : Int32` |  |
| `GetMemberOrNull(String) : JsonValue` | The member, or a C# null when the field is absent. |
| `GetNumber(String) : Nullable<Double>` | The member as a number, or null when it is absent or JSON null. |
| `GetString(String) : String` | The member as a string, or null when it is absent or JSON null. |
| `IsNull : Boolean get` | Whether this is JSON `null` . A C# null reference means the field was absent instead. |
| `Kind : JsonKind get` | Which of the six JSON kinds this value is. |
| `Null : JsonValue` | The JSON `null` literal. Not the same as a C# null reference. |
| `Object(IEnumerable<KeyValuePair<String, JsonValue>>) : JsonValue` | Builds an object. Members keep the order given; a repeated key replaces the earlier value in place rather than appending a second entry. |
| `Of(Boolean) : JsonValue` | A boolean value. |
| `Of(Double) : JsonValue` | A number. Every number is a `double` ; past 2^53 integers lose precision. |
| `Of(Int32) : JsonValue` | A number. |
| `Of(String) : JsonValue` | A string value. |
| `ToString() : String` | Serializes this value as compact JSON. |
| `TryGetMember(String, JsonValue&) : Boolean` | Looks a member up. Returns false when this is not an object or the key is absent, which is how an `omitempty` field reads. |

## static class JsonWriter

Serializes a `JsonValue` to compact JSON text.

| Member | Summary |
| --- | --- |
| `Write(JsonValue) : String` | Writes `value` as compact JSON. |
