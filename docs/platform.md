# Platform routes

Use platform-client for anonymous platform UTC time and authenticated board/social
routes. Every task needs its client's Poll. Time returns epoch milliseconds;
leaderboard score submission returns stored periods, and ranks require MeAsync.
The social and leaderboard JSON fields follow the service wire contract. Presence
uses a separate client configured with the gateway HTTP base and batches at most
fifty users. It never includes the JWT in the query.

The HTTP seam reuses the KV transports. PlatformHttpClient refuses redirects,
sanitizes error codes and retains no raw body or transport exception in refusals.
