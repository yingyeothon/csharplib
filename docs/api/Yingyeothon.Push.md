# Yingyeothon.Push

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.push-client/README.md`](../../packages/com.yingyeothon.push-client/README.md).

## Contents

- [`IPushClient`](#interface-ipushclient)
- [`PushClient`](#static-class-pushclient)
- [`PushClientOptions`](#class-pushclientoptions)
- [`PushMessageKind`](#enum-pushmessagekind)
- [`PushRegisterOptions`](#class-pushregisteroptions)

## interface IPushClient

Push registration core, pumped by its owner.

| Member | Summary |
| --- | --- |
| `RegisterAsync(PushRegisterOptions, CancellationToken?) : Task` | Registers on every app start and FCM token refresh. |
| `UnregisterAsync(String, CancellationToken?) : Task` | Removes the named device token on sign-out. |

## static class PushClient

Core factory and pure message classifier.

| Member | Summary |
| --- | --- |
| `Classify(IReadOnlyDictionary<String, String>) : PushMessageKind` | Recognizes match wakeups; the caller must still fetch authoritative status. |
| `Create(PushClientOptions) : IPushClient` | Creates a core without a Firebase SDK dependency. |

## class PushClientOptions

Push wire configuration; Firebase remains owned by the game.

| Member | Summary |
| --- | --- |
| `ChannelId : String get set` | Push channel id from kit config. |
| `Http : PlatformClientOptions get set` | HTTP options carrying the player's JWT, never a server key. |
| `ctor()` |  |

## enum PushMessageKind

Classification of an incoming message, never trusted as a ticket answer.

- `Match`
- `Message`

## class PushRegisterOptions

FCM registration supplied by the app.

| Member | Summary |
| --- | --- |
| `Project : String get set` | Firebase project id from the game's embedded Firebase configuration. |
| `Token : String get set` | Device FCM registration token. Never log or persist it outside device storage. |
| `ctor()` |  |
