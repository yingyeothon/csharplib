# Game kit

Create one kit from the console's kit-config JSON and inject transports, a monotonic
clock and host-owned adapters. Call Poll from Unity Update. The kit owns its socket
and HTTP pumps; do not pump them independently. Forward OnApplicationPause to
SetForeground(!paused). Continue pumping task completion after Dispose.

## Session and persistence

FromTokenAsync adopts claims without validating a signature; each service authenticates
requests. Platform Now uses a monotonic midpoint anchor from anonymous /time and
reports unavailable before its first synchronization. Token and durable offline queue
storage are opt-in. Never use the device wall clock to infer token expiry.

Save writes use known versions. Conflicts require an explicit merge policy; counters
and claims always need a live response. Durable queues are isolated by endpoint,
auth channel, collection and player. Sign-out retains queue records. Registered
save-name policies allow restored queue conflicts to merge; inline callbacks cannot
be serialized.

## Room and turns

Room events include authenticated sender identity, a random sender session and
sequence. The entire envelope is limited to 8192 UTF-8 bytes. Reserve kit. names
for the kit. Host snapshots are accepted only from the current host; larger games
send their own delta events. Host succession runs on Poll with the last same-room
snapshot. Reconnect requests host state and turn logs.

Turns use a supplied deterministic reducer and immutable JSON state. Every peer
uses identical initial state and player order. Hashes are SHA-256 of RFC 8785 JSON;
mismatch freezes the game. Resync selects one responder, preserves verified history,
validates the full log before replacing state and buffers live moves. Persisted
revision chunks are committed through a conditional manifest; partial revisions
are not restored. Elapsed player time is informational.

## Board, inbox and friends

Board submits then reads each period's own rank. Mail claim reads and conditionally
deletes, returning already_claimed to the loser. Polling emits each observed letter
once per session. Friends join social relations with gateway presence batches.
Matchmaking and Android push have dedicated [matchmaking](matchmaking.md) and
[push](push.md) guides. Wire-level routes are in [platform](platform.md).
