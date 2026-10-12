# Yingyeothon.GameKit

Purpose-shaped session, content, conditional save, host room, deterministic turns, board, mail, friends, matchmaking and push modules.

## Install

Not in a release yet: the release that ships this package adds its install URL here.

Dependencies: `com.yingyeothon.platform-client`, `com.yingyeothon.match-client`, `com.yingyeothon.push-client`, `com.yingyeothon.gamebase-client`, `com.yingyeothon.auth-client`, `com.yingyeothon.kvstore-client`, `com.yingyeothon.codec`, `com.yingyeothon.logger`. Add dependencies before this package on the same release tag.

## Usage

```csharp
var kit = GameKit.Create(new GameKitOptions { Config = kitConfig });
var adopted = kit.Session.FromTokenAsync(channelJwt);
// Poll each frame while awaiting adopted, then use kit.Save / Room / Turns.
```

## Poll and lifetime

Call `Poll()` from one serialized owner, usually Unity Update. I/O answers are queued;
parsing, state changes and task settlement run on the pump. Never block on a pending
task. Continue pumping after disposal until outstanding tasks have settled.
Use the KV `UnityWebRequestTransport.Instance` for HTTP on WebGL and inject a
WebGL socket factory for live sockets. No engine or Firebase dependency enters the core.

## Public API

- `GameKit`.
- `GameKitOptions`.
- `IGameKit`.
- `IKitBoard`.
- `IKitContent`.
- `IKitFriends`.
- `IKitLoginAdapter`.
- `IKitMail`.
- `IKitMatchmaking`.
- `IKitPush`.
- `IKitPushTopics`.
- `IKitQueueStorage`.
- `IKitRoom`.
- `IKitSave`.
- `IKitSession`.
- `IKitTokenStorage`.
- `IKitTurnGame`.
- `IKitTurns`.
- `KitException`.
- `KitJson`.
- `KitMailSendOptions`.
- `KitRoomEvent`.
- `KitSaveOptions`.
- `KitTurnsOptions`.

## Differences from the TypeScript model

The service contract is normative. JSON values are immutable `JsonValue` trees,
not reflection-based serialization. Stateful implementations are hidden behind
interfaces and factories. Async results require `Poll`, preserving Unity thread
and transition ordering.

See [the integration guide](../../docs/game-kit.md) for module behavior.

## Unity runner and Firebase sample

The Game Kit Quickstart sample provides `GameKitRunner.Bind` for Update/pause.
On destruction it transfers the kit to a persistent shutdown runner until
`ShutdownAsync` settles, then destroys that temporary object. Firebase Messaging is an opt-in
sample with a separate asmdef. Import the Firebase Unity SDK, complete its dependency
checks, then enable `YYT_FIREBASE_MESSAGING`. Configure the sample as PushTopics
before creating the kit, and Attach after session adoption. Supply your own Android
Firebase configuration. No Firebase dependency enters Runtime.
