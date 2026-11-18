# Reward redemptions (stage 3)

The catalogue describes real products, real USD face values and a US region.
Every delivered code is fictitious. No provider is contacted and no store funds,
real card inventory, purchases or payments are involved. The platform never
claims a real prize was purchased. Point costs are the project's chosen economy,
not a currency quotation: 2000 reward points per USD of face value.

## HTTP API

- `GET /api/rewards/catalog`: public catalogue, `isSimulation: true`, products,
  region/currency/value/cost, compatibility, availability and evidence link.
- `POST /api/rewards/redemptions`: authenticated active user, JSON containing only
  `productId` and `idempotencyKey`; returns200 with persistent receipt.
- `GET /api/rewards/redemptions?page=1&pageSize=20`: authenticated active user's
  own receipts. Page size1–100; newest first, stable ID tie-break.

Example:

```json
{"productId":"steam-usd-10","idempotencyKey":"order-2026-0001"}
```

With22500 points, the first request debits20000 and leaves2500. A repeated request
with that key returns the same receipt and code. Reusing the same key for another
product returns409. Receipt replay works even when the product was retired or
its price changed. Another user may use the same key independently.

Keys are1–100 ASCII letters/digits or `_ . : -`; product IDs are1–80 lowercase
letters/digits/hyphens. Identity always comes from JWT claims. The client cannot
set price, points, product snapshot, delivered code or target user.

Errors:400 invalid format/pagination;401 absent/inactive identity;404 unknown
product;409 unavailable product, conflicting key, insufficient balance. A failed
receipt insertion rolls back the debit. Transaction/database infrastructure
failures remain server errors and never create a success receipt.

## Persistence and retries

`RewardRedemptions` stores immutable receipt contents: user, idempotency key,
product snapshot, charged points, original debit ID, `DEMO-<PLATFORM>-<GUID>` code,
UTC creation time, simulation flag and an explicit nonredeemable-code disclaimer.
There are no receipt update/delete endpoints.

The repository creates unique indexes on `(UserId, IdempotencyKey)` and `Code`,
plus an index for user/time/ID history. A deterministic user/key receipt ID avoids
ambiguous lookup; debit and insertion join the existing `WalletRepository`
transaction/session. Concurrent attempts serialize through wallet updates and
MongoDB retries the transaction. Success replay is read before current catalogue
lookup, so later retirement cannot cause a second charge or a different code.
There is no HTTP points grant or account top-up route.

## Verified initial catalogue

Evidence checked2026-11-18. Sources are platform documentation and direct retailer
product listings (seller statements about the specific product), rather than
marketplace listings from unknown resellers. Availability here means enabled for
this simulation; it makes no statement about live retailer stock.

| Product ID | Product / face value | Region | Cost points |
|---|---|---|---:|
| steam-usd-10 | Steam Wallet USD10 | US / USD wallet |20000|
| steam-usd-20 | Steam Wallet USD20 | US / USD wallet |40000|
| epic-usd-15 | Fortnite Gift Card, Epic account balance USD15 | US |30000|
| ea-usd-25 | EA Wallet gift card USD25 | US |50000|
| riot-rp-usd-10 | League of Legends RP card USD10 face value | US purchase, NA server, PC |20000|
| riot-vp-usd-10 | VALORANT Points card USD10 face value | US purchase, NA server, PC |20000|
| playstation-usd-10 | PlayStation Store USD10 | US account |20000|
| nintendo-usd-10 | Nintendo eShop USD10 | US account |20000|
| xbox-usd-10 | Xbox Digital Gift Card USD10 | US Microsoft account |20000|

Riot cards are distinct products for RP and Valorant Points. Their denomination
is a real monetary face value; this catalogue does not invent or promise a fixed
number of RP/VP units. Steam/Epic/EA/console products represent platform balances.

- Steam: [USD10 Wallet card listing](https://www.gamestop.com/products/valve-steam-wallet-card-10/11109954.html),
  [USD20 Wallet card listing](https://www.bestbuy.com/product/valve-steam-wallet-20-gift-card/J33V32XTKG),
  [official Wallet information](https://help.steampowered.com/en/faqs/view/78E3-7431-1E88-AD59).
  Wallet-code currency restrictions apply. The separately offered
  [Steam digital gift](https://store.steampowered.com/digitalgiftcards/selectgiftcard?l=english)
  is delivered directly to a friend, so its denominations/delivery are not used
  as evidence that an actual USD20 digital gift or genuine code was purchased.
- Epic: [official Fortnite Gift Cards announcement](https://www.fortnite.com/news/fortnite-gift-cards-a-new-way-to-buy-v-bucks-games-and-more?lang=en-US)
  verifies USD15 and US availability; [account-balance FAQ](https://legal.epicgames.com/epicgames/account-balance-faq?lang=en-US)
  explains use through Epic's payment system. This is the new account-balance
  gift card, distinct from legacy V-Bucks cards.
- EA: [official EA Wallet/Card terms](https://www.ea.com/legal/ea-card-terms-and-conditions)
  and [Best Buy USD25 card listing](https://www.bestbuy.com/product/electronic-arts-ea-play-25-digital/J33V3286RR/sku/6454208).
  The listing's EA Play label describes a stored-value card usable for PC games,
  DLC and subscription, not a fixed-duration membership; it requires US residence
  and an EA Wallet. No EA in-game currency quantity is promised.
- Riot: [official prepaid-card rules](https://support.riotgames.com/en-us/riot/events/prepaid-gift-cards)
  verify USD10 LoL cards and NA restrictions; [Target VALORANT USD10 listing](https://www.target.com/p/-/A-93306184)
  corroborates the separate VALORANT denomination. Cross-game redemption is not
  promised; no numerical RP/VP conversion is stored.
- PlayStation: [official Store gift-card page](https://www.playstation.com/en-us/playstation-gift-cards/),
  [Best Buy USD10 listing](https://www.bestbuy.com/product/sony-10-playstation-store-gift-card-digital/6414390).
  This is for digital Store purchases, not physical hardware.
- Nintendo: [official eShop-card page](https://www.nintendo.com/us/retail-offers/nintendo-eshop-cards/),
  [Target USD10 listing and US-account terms](https://www.target.com/p/-/A-93562336).
- Xbox: [official Microsoft USD10 product](https://www.microsoft.com/en-us/p/xbox-gift-card-digital-code/cfq7ttc0k63h/002s).

The catalogue is server configuration (`RewardCatalog`); adding denominations or
regions requires new evidence and a code change. No external availability polling
or provider integration is introduced.

## Verification

The initial executable test failed at runtime against an empty catalogue stub
because the verified Steam USD10 product was absent. Implementation followed.
`tests/RewardTests` exercises the public service/controller against an isolated
local MongoDB replica-set database, including exact22500→2500 acceptance,
concurrent replay/payload conflicts/spending, receipt insertion rollback,
retirement/snapshot preservation, account status, claims, history isolation,
pagination, unique indexes and ledger reconciliation. The test drops only its
own random database in `finally`. Existing wallet/team/tournament suites cover
regressions. No real database or account credentials are used.

Receipt creation time is normalized to UTC BSON millisecond precision before
returning the first response. Full serialized receipt equality (including the
timestamp) is pinned for sequential and concurrent retries with a deterministic
submillisecond clock. Independent regression probes also assert rejected payloads
preserve persisted state hashes and that redemption competes safely with existing
wallet debit flows.
