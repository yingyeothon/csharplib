# Getting started

From an empty Unity project to a connected, moving client. It takes one `Poll()` per
frame and a handful of strings, every one of which comes from the yyt console.

## 1. Install the packages

Add three git URLs in _Package Manager → + → Add package from git URL_:

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.codec
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.logger
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.gamebase-client
```

A git-URL package cannot resolve its own dependencies, so all three must be present;
adding them in this order avoids Package Manager reporting a missing one in between. The
minimum editor is **Unity 2021.3**.

**No release has been tagged yet**, so a git URL without a fragment tracks `main`. Once
a tag exists, append `#<tag>` to each URL so your team does not silently move. [Unity § Installing](unity.md#installing) has the rest:
asmdefs, `.meta` files, and vendoring into `Packages/`.

## 2. Collect the ids from the console

Someone on your team provisions the channels once, in the
[console](https://console.yyt.life/ui/) or with the `yyt` CLI — `cli/README.md` in the
[`service`](https://github.com/yingyeothon/service) repository is that recipe, and
[Console and options](console-and-options.md) maps each channel setting to the field it
becomes.

What you copy out of it:

| What to copy | Where the console shows it | Looks like |
| --- | --- | --- |
| Gateway origin | the lobby channel's `wsUrl`, **without the query string** | `wss://gw.yyt.life` |
| Lobby channel id | that channel's `id` | `lobby_0123456789abcdef` |
| Auth base URL | the auth channel's `startUrl`, origin only | `https://auth.yyt.life` |
| Auth channel id | the auth channel's `id` | `auth_0123456789abcdef` |
| Dungeon channel id | the `q` channel's `id`, only if you run dungeons | `q_0123456789abcdef` |
| Match channel id | the `match` channel's `id`, only if you use matchmaking | `match_0123456789abcdef` |

The console prints `wsUrl` with a query string already on it. **Pass only the
origin** — [Console and options](console-and-options.md#the-four-values) says why.

Everything else the lobby channel is configured with — chat scopes, party limit,
starting zone, map URL, position flush interval — reaches your client in the `hello`
frame rather than in your build.

## 3. Sign in and get a token

Every socket carries a **channel JWT** issued by your auth channel. It identifies a
player, so it is not something you can hard-code.

If your game already signs players in through a launcher or a provider SDK, one request
converts what you hold. The `SignIn` sample is that request
(_Package Manager → Yingyeothon Gamebase Client → Samples → Import_):

```csharp
using Yingyeothon.Gamebase.Client.Samples;   // the sample's own namespace

ChannelToken token = await ChannelSignIn.ExchangeAsync(
    authBaseUrl:   "https://auth.yyt.life",
    authChannelId: "auth_0123456789abcdef",
    provider:      "github",
    credential:    providerAccessToken);

string channelJwt = token.Jwt;
```

**Google is the other argument.** GitHub sends the provider's *access* token, Google its
*id* token, and the auth service refuses the wrong one with a `400` whose message names
which it wanted. For Google, pass the id token and say so:
`credentialIsIdToken: true`.

The token is good for the channel's `tokenTtlSec` (24 hours by default), it works for
the lobby socket and the dungeon socket alike, and **there is no refresh endpoint** —
when it expires the player signs in again. If you have no provider token yet, the
browser redirect flow is in [Authentication](authentication.md).

## 4. Create the client and poll it

Nothing this SDK receives is observed until `Poll()` runs. **A client that is never
polled never connects and never reconnects** —
[Connection lifecycle](connection-lifecycle.md) is the whole contract.

```csharp
using System.Collections.Generic;
using UnityEngine;
using Yingyeothon.Gamebase.Client;

public sealed class LobbyQuickstart : MonoBehaviour
{
    [SerializeField] private string url = "wss://gw.yyt.life";
    [SerializeField] private string channelId = "lobby_0123456789abcdef";

    private IGatewayLobbyClient _lobby;
    private string _zone;

    public async void Begin(string channelJwt)
    {
        _lobby = GatewayLobbyClient.Create(new GatewayLobbyClientOptions
        {
            Url = url,
            ChannelId = channelId,
            Token = channelJwt,      // rides in the subprotocol list, and is never logged
        });

        _lobby.PeerEnter += peer => Debug.Log($"enter {peer.UserId}");
        _lobby.PeerLeave += userId => Debug.Log($"leave {userId}");
        _lobby.PeerMove += Move;
        _lobby.Said += frame => Debug.Log($"{frame.From}: {frame.Text}");
        _lobby.Disconnected += e => Debug.Log($"dropped {e.Code}, reconnecting={e.WillReconnect}");
        _lobby.Stopped += e => Debug.LogWarning($"stopped: {e.Kind} ({e.Code})");
        _lobby.Refused += e => Debug.LogWarning($"refused: {e.Code}");

        // Fires on the first hello AND after every reconnect. On a reconnect the
        // gateway may already have restored the retained position and sent a
        // snapshot; announcing a position further than the channel's maxMoveDelta
        // from that one is refused as move_too_far, which arrives on Refused and
        // nowhere else. Send where the player actually is, and watch that handler.
        // Lobby § The peer map has the two cases.
        _lobby.Connected += hello =>
        {
            _zone = hello.Zone;
            _lobby.Pos(_zone, transform.position.x, transform.position.z, "n");
        };

        Hello hello = await _lobby.ConnectAsync();   // completes when `hello` arrives
        Debug.Log($"connected as {hello.UserId} in {hello.Zone}, tick {hello.Tick} ms");
    }

    private void Update()
    {
        // Unconditionally, and before any pause or timeScale check.
        _lobby?.Poll();
    }

    private void OnDestroy()
    {
        _lobby?.Dispose();
    }

    private static void Move(IReadOnlyList<Peer> peers)
    {
        foreach (var peer in peers)
        {
            // peer.X, peer.Y, peer.Dir — your own entry is already filtered out.
        }
    }
}
```

That is a complete lobby client, and it ships as the `LobbyQuickstart` sample.

## 5. Send your position

`Pos` is how a player exists in a zone at all. **Until the first `Pos` the player has no
zone** — `hello.Zone` is the channel's default, not a placement, so a client that never
announces is invisible and sees nothing.

```csharp
_lobby.Pos(_zone, x, y, "n");
```

Call it when the player moves, not every frame: the gateway coalesces positions and
broadcasts one batch per `hello.Tick` milliseconds. `dir` is your game's own facing
token, a string and never a number — [Lobby](lobby.md#positions-and-zones) says why the
distinction costs you every position frame.

A position that jumps further than the channel's `maxMoveDelta` is refused with
`move_too_far`, and that includes the first `Pos` after a reconnect if the gateway
restored a retained position somewhere else. [Lobby](lobby.md#the-peer-map) has what a
reconnect actually restores.

## 6. Keep going

[The index](README.md) routes by what you are building.
