# Tournament points

Stage 2 implements the approved eight-slot economy: 250 points per player,
1000/600/200 prizes per eligible player; free events award 50 participation and
100 for winning, capped at 200 per Córdoba day. Five fixed starters confirm
individually, with no seat reservation. Filling the event or closing registration
refunds incomplete requests; incomplete events cancel at start. Staff definitive
closure pays once and freezes results. Every mutation shares a Mongo transaction
and tournament document write fence with wallet effects.

Existing test data cleanup was confirmed by the user. No remote migration runs
at startup. Empty legacy events are adapted on their first domain mutation.

## API

Existing `POST /api/Tournaments/{id}/register` debits/adopts an individual atomically;
user identity comes from JWT and player details come from the stored profile.
`POST /api/Tournaments/{id}/register-team` now creates an enrollment and confirms
only the captain. The successful response does not mean the team has a seat.

Authenticated members use:

- `GET /api/tournaments/{id}/enrollments` for their own requests, roster and status.
- `POST /api/tournaments/{id}/enrollments/{requestId}/confirm` for their own payment.
- `POST /api/tournaments/{id}/enrollments/{requestId}/cancel` for pending withdrawal
  and refund to every player who already confirmed.

Staff (`Moderator` or `Admin`) use:

- `POST /api/tournaments/{id}/cancel`: idempotent cancellation and refunds.
- `POST /api/tournaments/{id}/close`: idempotent definitive settlement after all
  seven valid bracket results. Ordinary editing, cancellation and deletion freeze.
- `POST /api/tournaments/{id}/adapt`: explicit adaptation of empty legacy events;
  populated legacy documents are rejected rather than billed retrospectively.

`DELETE` refuses events with enrollments; use cancellation to preserve history.
Requests cancelled after capacity runs out report `Cancelled` on later confirm
retries. Requests never reserve capacity. Every successful final admission closes
other pending requests and refunds their prior debits within that same transaction.

Creation requires `maxParticipants: 8` and accepts `isFree` (default false).
The deprecated client `prizePool` value is ignored. The response exposes `isFree`,
`entryPoints`, `championPoints`, `runnerUpPoints`, `semifinalPoints`, `closedAt`.
`prizePool` is a point total (2000 individual /10000 team; zero for free events),
not money. Frontend currency rendering must be updated in its later integration.
The per-event fields and economy version are snapshots; there is no edit route
that changes published entry/prize amounts after payments.

## Eligibility and persistence

Registration opens30 minutes before start and closes at start. An active account
must have the tournament game with nonempty in-game name and rank. Team snapshots
contain exactly five distinct starters including captain; substitutes never pay
or receive these prizes. Membership edits do not change snapshots. A player
cannot appear in multiple active enrollments within the same event.

Rank conditions accept empty/`Sin restricción`, or `Mínimo: <rango>`. Minimum-rank
comparison supports the Spanish tiers of Valorant, League of Legends and Rocket
League, numeric divisions and Roman numerals for League. Other formats or a
minimum in an unsupported game are rejected before charging, not silently treated
as eligible. Freeform conditions from older fixtures must be replaced before use.

`TournamentFreeDaily` uses a unique `_id` combining user and Córdoba calendar day;
concurrent closes serialize on that counter. No bonus for disqualification or
participation consisting only of byes/walkovers. Paid prizes go to eligible fixed
rosters. Settlement stores closed match snapshots, moderator and timestamp, ready
for later Elo processing without reopening prizes. No Elo is implemented here.

All admission, bracket, results, stats and settlement writes run in the wallet
transaction and write a tournament revision fence first. MongoDB transaction
conflicts retry the complete action. The ambient session exists only inside that
callback and is restored in `finally`; calls outside it retain normal read access.
No in-process lock decides capacity or wallet safety.

## Verification

`TEST_MONGODB_URI` must point at a disposable local replica set. The executable
`tests/TournamentTests` creates and drops its own randomly named database and
covers payment retries/insufficient funds, profile trust, ranks, overlapping
rosters, unauthorized confirmation, last-seat races, pending refunds, cancellation,
fixed-roster prizes, concurrent closure, frozen writes, free daily limits under
concurrent closes, legacy adaptation, route permissions and session cleanup.

No initial preimplementation RED was observed for the new T2 workflow. The first
integration run exposed scheduler clock inconsistency and a cancelled-request
retry error; both failures were corrected and the suite passed. Existing wallet
and team suites remain regression checks. Web SDK content exclusion for `tests/**`
prevents recursive output copies that otherwise caused MSB3030 during TeamTests.
