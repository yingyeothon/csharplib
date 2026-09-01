# Yingyeothon.Codec

A dependency-free JSON value tree, parser and writer, plus the codec abstraction the
other packages serialize through. It exists because the gateway's wire format needs
two things no reflection-based serializer gives you on IL2CPP: a value model that
tells "the field is absent" apart from "the field is null", and a passthrough that
leaves a game's own payload exactly as it arrived.

Every frame and payload on the gateway API is a `JsonValue`, so this package is on the path whatever you build: [the guide](../../docs/README.md).

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

## Limits and strictness

Everything here is a decision with a test pinning it, not an accident.

| | |
| --- | --- |
| Input length | `Json.MaxLength` (1 MiB of characters) for `Parse`/`TryParse`. A document that is legitimately larger — a downloaded map asset — uses `ParseBig(text, maxLength)`, up to `Json.MaxBigLength` (64 MiB). Checked before any scanning. |
| Nesting | `Json.MaxDepth` (64) containers, enforced by the parser **and** the writer. A value nested deeper cannot be read back, so writing it would put an unreadable frame on the wire, and recursing over a hand-built tree would overflow the main thread's stack. |
| Numbers | `double` only. Integers past 2^53 lose precision (`9007199254740993` reads back as `...92`); a field needing more range must arrive as a string. Overflow (`1e400`) is refused; underflow (`1e-400`) is `0`, as in `JSON.parse`. |
| Refused | Trailing commas, comments, unquoted or single-quoted keys, `NaN`/`Infinity`, leading zeros, a leading `+`, a byte-order mark, and any whitespace other than space, tab, CR and LF. |
| Duplicate keys | The last value wins, in the first key's position — the same rule `JSON.parse` applies. |
| Output | Compact, invariant-culture, and pinned by test: `1E2` → `100`, `-0` → `0`, `1e-320` → `1E-320`, `1e21` → `1E+21`. Valid JSON, but not byte-identical to `JSON.stringify`. |
| Unpaired surrogates | Re-escaped as `\udXXX` on the way out, so the string survives the UTF-8 encode a WebSocket text frame performs. |

## Why a parse failure is a code

`TryParse` reports a `JsonParseFailure`: a `JsonParseError` and a character offset,
and nothing copied out of the document. Two reasons, both operational.

The gateway socket parses every inbound frame, so a peer sending garbage must not be
able to charge the client an exception per frame — the parser core never throws, and
`Json.Parse` is a thin wrapper for callers that want one.

And a refusal is reported through `ProtocolError` into whatever log writer the
consumer installed. A message quoting the offending escape or number literal is a
frame body in a log, which `rules/security.md` forbids: it is whatever the peer just
sent, and that may be a payload or a credential echo.

```csharp
if (!Json.TryParse(frame, out var value, out var failure))
{
    logger.Warn("frame is not JSON: " + failure);   // "UnknownEscape at 17"
}
```

## Absent is not null

`JsonValue` is a reference type on purpose. A `JsonValue?` holding C# `null` means
the field was **not on the wire**; `JsonValue.Null` means it was there and held JSON
`null`. The gateway marshals with Go's `omitempty`, so those are different frames,
and folding them together is how a client ends up sending `"dir": null` for a
position that has no facing.

`JsonObjectBuilder.Set(key, null)` therefore omits the key, and `SetNull(key)` is the
separate, deliberate way to write a JSON null.

## Public API

- `JsonValue` — an immutable JSON value. `Kind`, `IsNull`, the `Of` / `Array` /
  `ArrayOf` / `Object` factories, the `As*` readers, and the field accessors
  `TryGetMember`, `GetMemberOrNull`, `GetString`, `GetNumber`, `GetBool`,
  `GetArrayOrEmpty`. Structural equality ignoring member order; `ToString()` is
  compact JSON.
- `JsonKind` — `Null`, `Bool`, `Number`, `String`, `Array`, `Object`.
- `Json` — `Parse`, `ParseBig`, `TryParse` (with or without a failure),
  `TryParseBig`, `Stringify`, `Object()`, `Array`, `ArrayOfStrings`, and the limits
  `MaxLength`, `MaxBigLength`, `MaxDepth`.
- `JsonObjectBuilder` — `Set` (a null argument omits the field), `SetNull`, `Build`.
  `Build` snapshots; it does not reset the builder.
- `JsonWriter.Write`, `ICodec<TWire>`, `JsonCodec.Instance` / `Encode` / `Decode`.
- `JsonParseError`, `JsonParseFailure`, `JsonParseException`, `JsonKindException`,
  `JsonNumberException`.

## Differences from `@yingyeothon/codec`

- `Codec<B>.encode<T>` / `decode<T>` mapped arbitrary values through
  `JSON.stringify`. That needs reflection in C#, which IL2CPP's managed stripper
  breaks silently at runtime, so the contract is narrowed to `JsonValue` and every
  frame type parses itself by hand.
- `jsonCodec.encode(undefined)` returned the literal string `"undefined"`, which is
  not valid JSON and which its own `decode` refused. C# has no `undefined`, so
  `JsonCodec.Encode(null)` throws `ArgumentNullException` instead.
- Nesting and input length are bounded so a hostile frame cannot overflow the stack
  of a game's main thread or make it allocate without limit. `JSON.parse` has no such
  bound; a browser tab dying is not the same event as a game client dying.
- `JSON.stringify` is well-formed since ES2019 and re-escapes an unpaired surrogate;
  this writer does the same, because a raw unpaired half does not survive the UTF-8
  encode a WebSocket text frame performs and the peer would receive U+FFFD.
- `JSON.parse` throws a `SyntaxError` whose message quotes the input. This parser
  reports a code and an offset instead, and never quotes anything: the refusal ends
  up in a consumer's log, and the input is a frame body.
- Numbers are always read and written with `CultureInfo.InvariantCulture`. A
  comma-decimal locale would otherwise put `1,5` on the wire, and the gateway drops
  that whole frame as `bad_message` without telling the client.

## Samples

One importable sample ships with this package: _Package Manager → the package →
Samples → Import_.
