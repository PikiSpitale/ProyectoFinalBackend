# Elo and per-game rankings (stage 4)

Ratings start at1000, use K32, and round the final rating to the nearest integer
with midpoint rounding away from zero. Two1000 rivals become1016/984 after a
played win/loss. There are no Elo draws in the winner-required knockout bracket.
A win against a stronger opponent gives more Elo than the same win against a
weaker opponent. Reward wallets, declared ranks and legacy `Stats.Points` remain
independent; none supplies the initial Elo rating.

## Definitive closure

The existing staff-only `POST /api/tournaments/{id}/close` now processes Elo in
the same wallet transaction as reward settlement. Ordinary final-result entry
still allows corrections and does not process Elo. Definitive closure stores
`EloRuleVersion: 1` alongside the closed result snapshot. Retrying closure returns
success without another reward, history entry or rating update.

Only tournaments definitively closed after this feature is enabled are processed.
Already-closed tournaments (`ClosedAt != null`) are excluded even if they lack
an Elo marker. No historical backfill or reinterpretation of `Stats.Points` runs.

Matches process in `Round`, then `Position` order, including free tournaments.
Byes and walkovers (including abandonment wins) have no Elo adjustment. A later
played round sees ratings from earlier played rounds. All updates within a match
use the ratings captured before that match's first update.

For individual matches each player's rival strength is the other player's
rating for the same game. Team matches independently update:

- Each team's own rating versus the other team's own rating.
- Each of the five fixed starters versus the mean of the five opposing starters'
  personal ratings, captured before any of the ten players is changed.

Team Elo is not a roster average. It remains attached to team ID and game even
when a later tournament has a different roster. Historical player/team IDs and
names come from the tournament enrollment snapshot, so editing or deleting live
source entities cannot redirect adjustments to substitutes or new members.

## Storage and concurrent closes

- `PlayerEloRatings` and `TeamEloRatings`: separate entity/game ratings with match,
  win and loss counts. Unique `(EntityId, GameName)` and leaderboard sort indexes.
- `EloAdjustments`: immutable per-match/entity history with before/after, expected
  score, actual result, opponent strength and IDs, tournament/match/round/position,
  rule version and a UTC millisecond timestamp. `_id` is deterministic per event.
- `EloSettlements`: unique tournament processing marker stored in the transaction.
- `EloGames`: canonical game `_id` and durable revision fence.

Indexes and initial game-gate creation happen outside the closure transaction.
Concurrent first initialization handles duplicate-key election of the same gate.
Inside the existing transaction, every new closure writes the game revision
before loading ratings. This serializes closes for that game across API instances;
MongoDB retries conflicting transactions, so no in-process lock decides ordering.
The tournament fence also protects results/statistics/cancellation against closure.
All rating, history, marker, closed status and prize changes commit or roll back
together. A failure while saving Elo does not leave paid prizes or partial ratings.

## Public read API

Canonical game names: `League of Legends`, `Valorant`, `CS:GO`, `Rocket League`.
Query normalization trims surrounding whitespace and ignores case. Unknown names
return400; no arbitrary game string creates a mixed ranking. No write-ratings API
exists and clients cannot submit a rating or Elo adjustment.

- `GET /api/rankings/players?gameName=Valorant&page=1&pageSize=20`
- `GET /api/rankings/teams?gameName=Valorant&page=1&pageSize=20`
- `GET /api/rankings/players/{entityId}/history?gameName=Valorant&page=1&pageSize=20`
- `GET /api/rankings/teams/{entityId}/history?gameName=Valorant&page=1&pageSize=20`

These are public competitive-rating data, not wallet/account histories. Game is
required on these routes. Responses include page/pageSize/items. Entity IDs must
be valid ObjectIds; invalid game, entity ID and pagination return400. Page size
is1–100 and excessive offsets are rejected. Missing public entities or ratings
return empty results; they are not provisioned by a read request.

Rank entries expose entity ID, snapshot name, canonical game, kind (`player` or
`team`), explicit `rating` and played/won/lost counts. Ordering is rating descending,
normalized name ascending, then immutable entity ID; game is an additional stable
tie-break when legacy queries omit the game. History sorts newest timestamp then
ID; `Round`/`Position` explicitly record processing order within each tournament.

## Legacy endpoint compatibility

`GET /api/rankings?gameName=Valorant` retains its array and old player field names,
but now actually filters the requested game. `points` is deprecated as a display
alias of explicit `rating`; it is never wallet balance or legacy statistics.
Played/won/lost and computed win rate are Elo-eligible match counts. Unavailable
legacy avatar is null and legacy tournamentsWon remains0 rather than mixing an
unrelated statistic into the rating view.

When gameName is omitted, the endpoint returns up to50 player/game rows across
games. A player may therefore appear more than once with explicit game names.
This is a compatibility listing, not an aggregate cross-game rating. The new
per-game routes should be used for new clients. Accounts with only historical
Stats.Points do not appear until they play an eligible newly closed tournament.

## Verification

The initial pure-calculator executable failed at runtime against a stub that
left ratings unchanged, expecting1016/984. Integration tests use a disposable
local MongoDB replica-set database and public service/controller paths. Coverage
includes equal/opponent-sensitive results, rounding, prematch team averages,
team-owned ratings, stable snapshot roster and later team continuity, game
isolation, explicit tie ordering/pagination, free tournaments, bye/walkover skip,
old closed exclusion, concurrent first-game gates across separate service scopes,
shared-rating serialization, idempotent closure, forced Elo-history write failure
and whole-closure rollback/retry, and query errors. Existing wallet/team/reward/
tournament suites remain regression checks. No remote databases are accessed.
