# Matchmaking

match-client supplies live matched results (including ordered Members and callback
Result), plus deferred submit/status/cancel/accept/decline. The raw JSON retains
future fields. Wire clients apply no polling policy; the game kit reads match.mode
from kit-config and applies foreground polling and retries.

Deferred polling defaults to four seconds and never runs faster than one second.
Push notifications wake a status read; their data is not authoritative state.
Cooldown honors retryAfter, confirming waits 1.5 seconds, transient 429/503 backs
off up to thirty seconds. A proposal refusal reads status instead of blindly retrying.

Live callback-less matching requires an already-connected lobby. The member with
lowest userId creates the room; only matched users' invitations are accepted for
thirty seconds. Callback channels use an explicit MatchCallback option and return
the callback result without room formation.
