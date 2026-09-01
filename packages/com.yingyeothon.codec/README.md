# Yingyeothon.Codec

A dependency-free JSON value tree, parser and writer, plus the codec abstraction the
other packages serialize through. It exists because the gateway's wire format needs
two things no reflection-based serializer gives you on IL2CPP: a value model that
tells "the field is absent" apart from "the field is null", and a passthrough that
leaves a game's own payload exactly as it arrived.

## Install

Unity Package Manager, _Add package from git URL_:

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.codec
```

## Usage

```csharp
using Yingyeothon.Codec;

var frame = Json.Object()
    .Set("type", "pos")
    .Set("zone", "town")
    .Set("x", 1.5)
    .Set("dir", (string?)null)   // absent, not null: Go omitempty is not a JSON null
    .Build();

string wire = Json.Stringify(frame);          // {"type":"pos","zone":"town","x":1.5}

if (Json.TryParse(wire, out JsonValue parsed))
{
    string? zone = parsed.GetString("zone");   // "town"
    double? x = parsed.GetNumber("x");         // 1.5
    bool hasDir = parsed.TryGetMember("dir", out _);  // false
}
```

## Public API

- `JsonValue` — an immutable JSON value. `Kind`, `IsNull`, the `Of` / `Array` /
  `ArrayOf` / `Object` factories, the `As*` readers, and the field accessors
  `TryGetMember`, `GetMemberOrNull`, `GetString`, `GetNumber`, `GetBool`,
  `GetArrayOrEmpty`. Structural equality; `ToString()` is compact JSON.
- `JsonKind` — `Null`, `Bool`, `Number`, `String`, `Array`, `Object`.
- `Json` — `Parse`, `TryParse`, `Stringify`, `Object()`, `Array`, `ArrayOfStrings`.
- `JsonObjectBuilder` — `Set` (a null argument omits the field), `SetNull`, `Build`.
- `JsonWriter.Write`, `ICodec<TWire>`, `JsonCodec.Instance` / `Encode` / `Decode`.
- `JsonParseException`, `JsonKindException`.

## Absent is not null

`JsonValue` is a reference type on purpose. A `JsonValue?` holding C# `null` means
the field was **not on the wire**; `JsonValue.Null` means it was there and held JSON
`null`. The gateway marshals with Go's `omitempty`, so those are different frames,
and folding them together is how a client ends up sending `"dir": null` for a
position that has no facing.

`JsonObjectBuilder.Set(key, null)` therefore omits the key, and `SetNull(key)` is the
separate, deliberate way to write a JSON null.

## Differences from `@yingyeothon/codec`

- `Codec<B>.encode<T>` / `decode<T>` mapped arbitrary values through
  `JSON.stringify`. That needs reflection in C#, which IL2CPP's managed stripper
  breaks silently at runtime, so the contract is narrowed to `JsonValue` and every
  frame type parses itself by hand.
- `jsonCodec.encode(undefined)` returned the literal string `"undefined"`, which is
  not valid JSON and which its own `decode` refused. C# has no `undefined`, so
  `JsonCodec.Encode(null)` throws `ArgumentNullException` instead.
- Nesting is bounded (64 levels) so a hostile frame cannot overflow the stack of a
  game's main thread.
- Numbers are always read and written with `CultureInfo.InvariantCulture`. A
  comma-decimal locale would otherwise put `1,5` on the wire, and the gateway drops
  that whole frame as `bad_message` without telling the client.
