# Frontend integration read APIs

Additive endpoints for the approved Recompensas section, global personal tournament
requests and profile/team Elo. Existing ranking and tournament routes retain their
behavior. No economic mutation is added.

## Personal tournament enrollment inbox

`GET /api/users/me/tournament-enrollments?status=Pending&page=1&pageSize=20`
requires an active authenticated account. Identity comes only from JWT claims.
Omit status for all statuses; accepted values are Pending, Admitted, Cancelled,
case-insensitive with surrounding whitespace removed. Invalid status returns400.
Page starts at1, size1–100; offsets beyond Int32 return400. Missing/inactive
identity returns401. No userId parameter can select another account.

Response: `{page,pageSize,hasMore,items}`. Each item contains tournamentId, title,
gameName, tournamentStatus, startDate, closedAt, isFree, entryPoints, requestId,
teamId/teamName, request status and players `{userId,username,confirmed}`. Debit
IDs and profile ranks are omitted. Individual registrations are included too.
A teammate sees their shared request; other requests in the same tournament are
not returned. Pagination occurs after filtering the unwound enrollment in Mongo.
The named index `tournament_enrollment_inbox` covers actor and tournament order;
it is created lazily on an active user's first inbox query, before aggregation.
No user, request, rating or economic document is changed by reads.
Ordering is tournament creation descending, tournament ID descending, request ID
ascending. The inbox includes archived statuses when requested; it does not infer
expiry or automatically cancel/refund anything. Refetch after a mutation to see
committed status. Non-transactional inbox reads represent current committed state,
not a reservation of a tournament seat.

## Current Elo and exact position

- `GET /api/rankings/players/{entityId}?gameName=Valorant`
- `GET /api/rankings/teams/{entityId}?gameName=Valorant`

Public read-only competitive data. Valid ObjectId and canonical game required;
invalid game/ID returns400 using existing error format `{code,message}`. Game
normalization follows existing Elo rules. Response contains entityId, gameName,
kind, rating, position, matchesPlayed, matchesWon and matchesLost. No rating row
means rating/position are null and counts zero (including a missing source entity).
No synthetic1000 rating or global position is created from historical Stats.Points.

Position is one-based ordinal, with ties broken by normalized name and entity ID,
matching the per-game leaderboard sort. It is computed across the whole game,
including entities beyond the first leaderboard page. Rating and position use
one MongoDB snapshot transaction to avoid combining observations from different
concurrent settlements. Player and team collections remain separate. Reads do
not initialize ratings, gates or indexes. Replica-set requirements are unchanged.

## Checks

`TEST_MONGODB_URI` must point to a disposable local replica set. Run
`dotnet run --project tests/FrontendReadTests`. Initial test-first compile failed
because these additive APIs did not yet exist; no runtime RED is claimed. The
suite checks private filtering before pagination, claims, statuses, exact ranks
and ties, player/team/game separation, unrated results, errors and no writes.
