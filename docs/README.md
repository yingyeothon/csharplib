# csharplib documentation

The seven packages in this repository are the client half of the yyt platform. This
folder is the guide; the package READMEs are the per-package reference, and the
`service` repository owns the wire protocol.

## Start here

1. **[Getting started](getting-started.md)** — an empty Unity project to a connected,
   moving client, in order, with nothing assumed.
2. **[Console and options](console-and-options.md)** — what the yyt console hands you
   and which option each value goes into. Every option, its default and its effect.
3. **[Authentication](authentication.md)** — how a game client gets the channel JWT
   that every socket needs.

## By what you are building

| You want to | Read |
| --- | --- |
| Show other players, chat, parties | [Lobby](lobby.md) |
| Run a dungeon against a game actor | [Dungeon (`q`)](dungeon.md) |
| Read announcements, save a player's record | [Key-value store](kvstore.md) |
| Handle drops, reconnects and shutdown | [Connection lifecycle](connection-lifecycle.md) |
| Understand a refusal or a close code | [Errors and close codes](errors.md) |
| Ship on Unity, IL2CPP or WebGL | [Unity](unity.md) |
| Work out why nothing is happening | [Troubleshooting](troubleshooting.md) |
| Read or build a JSON frame | [`Yingyeothon.Codec`](../packages/com.yingyeothon.codec/README.md) |
| Log without leaking a token or a payload | [`Yingyeothon.Logger`](../packages/com.yingyeothon.logger/README.md) |
| Read or write the key-value store by hand | [`Yingyeothon.KvStore`](../packages/com.yingyeothon.kvstore-client/README.md) |
| Sign a player in, or check a kept token | [`Yingyeothon.Auth`](../packages/com.yingyeothon.auth-client/README.md) |
| Load a database, music or a level pack from the CDN | [Asset bundles](assets.md) |
| Decouple your own game events | [`Yingyeothon.EventBroker`](../packages/com.yingyeothon.event-broker/README.md) |

`gamebase-client` (the realtime gateway), `kvstore-client` (the key-value store),
`auth-client` (the channel JWT both of them take) and `asset-client` (files on the CDN)
are the four that talk to the platform, and each stands on its own. `codec` is on all
four APIs — every frame, payload, stored value, raw config and manifest is a `JsonValue`
— so you will read it whatever you build; `logger` and `event-broker` are optional.

## Reference

Generated from the assemblies themselves and gated in CI, so it cannot drift from the
code. Every public type and member, with the same summary your IDE shows:

- [`Yingyeothon.Gamebase.Client`](api/Yingyeothon.Gamebase.Client.md)
- [`Yingyeothon.KvStore`](api/Yingyeothon.KvStore.md)
- [`Yingyeothon.Auth`](api/Yingyeothon.Auth.md)
- [`Yingyeothon.Assets`](api/Yingyeothon.Assets.md)
- [`Yingyeothon.Codec`](api/Yingyeothon.Codec.md)
- [`Yingyeothon.Logger`](api/Yingyeothon.Logger.md)
- [`Yingyeothon.EventBroker`](api/Yingyeothon.EventBroker.md)

`GamebaseRunner`, `UnityWebRequestTransport`, `AuthUnityWebRequestTransport`,
`AssetUnityWebRequestTransport` and `UnityDebugWriter` are the five public types absent
from it: they sit behind `#if UNITY_5_3_OR_NEWER`, so the `dotnet` build the reference is
generated from never sees them. [Unity](unity.md#polling) documents the first,
[Unity § Key-value store](unity.md#key-value-store) the second, the
[auth-client](../packages/com.yingyeothon.auth-client/README.md#threads-and-webgl) and
[asset-client](../packages/com.yingyeothon.asset-client/README.md#webgl) READMEs the
next two, and the [logger README](../packages/com.yingyeothon.logger/README.md) the last.

Each package's README also carries its own `## Public API` summary and the deliberate
differences from its `@yingyeothon/*` original — read those before "fixing" a
behaviour to match tslib, because the difference is often the fix:
[codec](../packages/com.yingyeothon.codec/README.md),
[logger](../packages/com.yingyeothon.logger/README.md),
[event-broker](../packages/com.yingyeothon.event-broker/README.md),
[gamebase-client](../packages/com.yingyeothon.gamebase-client/README.md),
[kvstore-client](../packages/com.yingyeothon.kvstore-client/README.md),
[auth-client](../packages/com.yingyeothon.auth-client/README.md) (whose model is
flutterlib's package; tslib has no client counterpart),
[asset-client](../packages/com.yingyeothon.asset-client/README.md).

## What lives in the `service` repository

This SDK follows the platform; it never defines it. These are the normative documents,
all public, in [`yingyeothon/service`](https://github.com/yingyeothon/service):

| Document | What it settles |
| --- | --- |
| `gateway/README.md` | **The wire spec.** Frame tables both directions, handshake refusals, close codes |
| `services/auth/README.md` | The sign-in endpoints and the token they issue |
| `docs/auth-game-contract.md` | The JWT's claims, lifetime and reuse rules |
| `cli/README.md` | The `yyt` CLI: provisioning channels and publishing map assets |
| `services/match/README.md` | The matchmaking socket, one source of a `gameId` |
| `services/state/README.md` | The doc store, and the _KV routes_ the key-value store client speaks |
| `docs/kvstore.md` | The key-value store: principals, scopes, encryption |
| `examples/sample-morpg/README.md` | A worked game, and the map asset format |

When this guide and the gateway README disagree, the gateway README is right — and
that is a bug here worth reporting.
