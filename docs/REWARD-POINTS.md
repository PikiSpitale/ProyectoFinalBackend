# Reward points: stage 1

Wallets and point history are implemented independently of User. New accounts
created through IUserRepository.CreateAsync (local registration and Google) are
inserted with a wallet and a 1,000-point welcome entry in one Mongo transaction.
A failed transaction leaves none of these effects; retry after rollback is safe.
Login and external linking do not provision another bonus. Duplicate-account
errors retain the existing Mongo duplicate-key behavior.

Historical accounts without a wallet read zero and empty history, without writes
or automatic welcome credit. A later authorized domain credit may create a
zero-based wallet. Historical bonus policy remains undecided.

## HTTP contract

Both endpoints require authentication, an active account and identity from JWT.
There are no HTTP endpoints to credit, debit or set balances.

- GET /api/users/me/points -> { "balance": 1000 }
- GET /api/users/me/points/history?page=1&pageSize=20 ->
  { "page": 1, "pageSize": 20, "items": [...] }
- History entries contain id, userId, amount, reason, reference, reversesId,
  createdAt. Amounts are signed integer points, never currency or Elo.
- Page starts at1, pageSize1..100; invalid/overflow pagination returns400.
- Missing identity or inactive account returns401. Wallet errors contain code
  and message. No userId query parameter can select another wallet.
- History sorts createdAt descending, then id descending. Offset pagination
  reflects the current history; new concurrent entries can shift later pages.

## Internal transaction contract

WalletRepository.TransactionAsync supplies WalletTransaction and its Session.
Use the same session for all domain effects; never nest transactions or run
parallel operations within a session. Initialize domain indexes before starting.
CreditAsync/DebitAsync require positive amounts, reason, reference and an
operation key (maximum500characters). Keys are scoped by canonical user ID.
Same key/payload replays the entry; changed payload returns409.
RefundAsync derives beneficiary/amount from the original negative entry and
uses a deterministic identity, so changing a caller key cannot refund twice.
Insufficient funds or Int64 overflow returns409; rejected effects roll back.
Future callers must validate their domain permissions and target account before
using these internal operations; no client controls amounts or reason strings.

Indexes: wallet _id=user ObjectId, ledger _id=user+operation key, history index
user/createdAt/id. Transactions require a replica set; no standalone fallback.
The welcome amount is server-defined in ProvisionAsync, not client-controlled.

## Checks

Canonical executable: dotnet run --project tests/WalletTests from backend root.
TEST_MONGODB_URI must point to a disposable local replica set; the runner uses
and removes its own random database and never reads application secrets.
49 checks cover rollback, provisioning recovery, races, insufficient funds,
retries, refunds, historical accounts, pagination, claims, local signup/login,
Google callback/linking through a simulated external identity, and ledger
reconciliation. Real Google authentication requires separate browser
credentials and was not contacted during these tests.

Stage2 tournament points, stage3 redemptions and stage4 Elo are not implemented.

## Reward redemption integration

Stage3 uses the existing transaction and internal debit service. See [redemption API and verified catalogue](REWARD-REDEMPTIONS.md). No HTTP points-grant route is added.
