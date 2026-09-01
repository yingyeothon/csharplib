# Troubleshooting

Symptom first, because that is what you have.

## Nothing happens at all

`await ConnectAsync()` never completes, no event ever fires, no log line appears.

**You are not polling.** Everything happens inside `Poll()`.

```csharp
void Update() { lobby?.Poll(); }
```

Check that `Update()` actually runs — the `MonoBehaviour` is enabled, its
`GameObject` is active, the scene did not unload it — and that `Poll()` is called
**before** any pause or `timeScale` guard. `GamebaseRunner.CreatePersistent()` avoids
the whole class of mistake.

## It connects, then immediately stops

`Stopped` fires after about five quick attempts, with no useful reason. The handshake
was refused, and a refusal is an HTTP status the client never sees. Diagnose by
elimination, in order of likelihood:

1. **The token is expired or wrong.** Check it directly:
   `GET https://auth.yyt.life/c/{authChannelId}/verify` with
   `Authorization: Bearer <jwt>`. A `401` here is your answer. Tokens last
   `tokenTtlSec` — 24 hours by default — and there is **no refresh**; sign in again.
2. **The channel expired.** Channels live 7 days. `yyt channels list` shows the status;
   `yyt channels extend <channel>` adds seven more. An expired channel answers `410`.
3. **The channel id is wrong, or of the wrong kind.** An `auth_…`, `topic_…` or
   `match_…` id where a gateway channel belongs answers `404`. A `q` id on a lobby
   client resolves as a dungeon channel with no game id and answers `403`, which looks
   exactly like being refused from a run.
4. **(Dungeon)** the `gameId` is unknown, or **your `sub` is not in its start event**.
   Both answer `403`, deliberately, so game ids cannot be probed. A run that already
   aborted is gone: allocate a new `gameId`.
5. **The URL carries its own query.** Pass the origin, `wss://gw.yyt.life`, not the
   console's full `wsUrl` — though the SDK replaces a duplicate `channel` rather than
   appending one, so this is rarely the cause.

Raise the logger to `Debug` and read the close codes; they are in [Errors](errors.md).

## Positions never appear for other players

- **You never sent `Pos`.** A player has no zone until their first `Pos`; `hello.Zone`
  is a default, not a placement. Nobody sees you and you get no snapshot.
- **Your `dir` is a number, or your locale writes `1,5`.** Both make the gateway refuse
  the whole frame as `bad_message`. Subscribe to `Refused` and you will see it; without
  that handler the position simply never lands. Only a frame you built by hand with
  `Send` can do either — see [Lobby](lobby.md#positions-and-zones).
- **You are in a different zone than you think.** `enter`, `leave` and `pos` for any
  zone but the one the last snapshot named are ignored.
- **`move_too_far`.** A jump larger than the channel's `maxMoveDelta` (4 by default) is
  refused. A real teleport should be a zone change, or the channel's limit should rise.

## `Say` or `Party` throws instead of sending

`InvalidOperationException` beginning `capability_off:` means the channel disables that
command or that chat scope. Check what the channel actually enabled:

```csharp
lobby.Capabilities?.Party;                        // null = unrestricted, false = off
lobby.Capabilities?.AllowsScope(SayScope.User);
```

Fix it in the console (`--cap-say user`, `--cap-party`), not in the client.

If `lobby.Capabilities` is null altogether, `hello` has not arrived yet — await
`ConnectAsync` first. A null *field* would mean unrestricted, but the console fills every
field in, so in practice you are looking at `false`.

## The session dies after a while of chatting

Close code `4003`: fifty refused messages on one socket. One oversized message is one
refusal and nothing more, so this is a stream of them, not a single mistake.

The usual cause is chat length; validate before calling `Say` —
[Errors](errors.md#what-the-sdk-does-not-check-for-you).

## Party fields are null or missing

They are not. The gateway marshals `leaderId`, `invited` and `max` with Go's
`omitempty`, so they are absent on the wire when empty, and this SDK fills them in as
`""`, an empty list and `0` before the frame reaches you. `Roster.Invited.Count` needs
no guard.

`Roster` itself is null before the first roster frame arrives, and `PartyId` is null
when the player is in no party.

## Connecting throws on WebGL

`WebSocketTransport.Default` throws on WebGL on purpose: `ClientWebSocket` is not
supported there and there is no thread for a receive loop. Supply your own
`WebSocketFactory` and `HttpFetcher` through the client options — see
[Unity](unity.md) and the `WebGL Transport` sample.

## `MapAsync` throws

- `InvalidOperationException` — called before `hello`. Await `ConnectAsync()` first.
- `MapFetchException` — the URL answered a non-2xx status, which the exception carries.
  A `403`/`404` usually means the channel points at an asset version that was deleted;
  re-point it with `yyt channels update <lobby> --map-url …`.
- The channel has no map at all: `hello.MapUrl` is empty.

Remember that a `MapAsync` continuation may resume on **any** thread. Marshal back
before touching the client or a `Transform`.

## The dungeon ended and I do not know why

`Finished` (close `1000`) means the game dropped you after ending normally: show the
result. `Aborted` (close `4001`) means the actor stopped consuming its queue: say so,
return to the lobby, and **allocate a new `gameId`** — the old run's queue is deleted
and retrying with it will be refused.

## Still stuck

Turn the logger on and read what the SDK decided. In Unity that means routing it to the
editor console — `ConsoleLogger` writes to `System.Console`, which the editor does not
show:

```csharp
Logger = FilteredLogger.Create(new FilteredLoggerOptions
{
    Severity = LogSeverity.Debug,
    Writer = LogWriters.FromAction((s, m, c) => UnityEngine.Debug.Log(LogWriters.Format(s, m, c))),
}),
```

It logs ids, close codes, attempt counts and lengths — never a token, a payload or a
close reason's text.

**Take Unity out of the picture.** The packages are plain `netstandard2.0`, so a
throwaway `dotnet` console app that references the built assemblies, calls
`ConnectAsync` and loops on `Poll()` separates "my token is wrong" from "my Unity setup
is wrong" in about a minute. That is also how this SDK is verified against a live
gateway before a release.

If a symptom is not here, the gateway's own README in the
[`service`](https://github.com/yingyeothon/service) repository is the normative spec.
