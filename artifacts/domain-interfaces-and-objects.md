---
title: Domain Interfaces and Objects
last-updated: 2026-08-13
---

# Domain Interfaces and Objects

Turaco Chorus's core logic depends on five kinds of interfaces, namely: the identity-side, user-side, data-side, ai-side, and accountability-side. This means that the core logic for `/stats`, `/ask`, and `/consent` will be encapsulated, with adapter objects for working with different kinds of technologies.

This is an instance of **Ports and Adapters** (Hexagonal Architecture): the core logic has zero dependencies on the outside world — databases, APIs, frameworks. Everything external talks to the core only through interfaces the core itself defines.

- A **port** is an interface *owned by the core domain*, describing what it needs, in the core's own vocabulary.
    - For example, `ILogDataSource` is a port: it says "give me `AggregateStats` for a `sourceId` and date range," with no mention of DynamoDB, tables, or the external application.
- An **adapter** is a class that implements a port by translating to/from a specific external system.
    - For example, `DynamoDbLogDataSource : ILogDataSource` is an adapter: it knows about DynamoDB's `LogType`/`LogEntry` item shapes and translates them into the generic `AggregateStats` the port promises.

By the adapter depending on the port, it means that many technologies can interface with the core implementations, without touching the business logic.


## Interfaces

Each interface below follows the same structure: the question it answers, the interface itself, what it returns, and its adapter(s) — planned or already decided.

### `IIdentityVerifier` — the Identity side

**Question:** whose verified `userId` does this credential belong to, if it's valid at all? Called by the inbound HTTP layer before any other port, on every route (see `interaction-flows.md`'s Auth step).

```C#
interface IIdentityVerifier
    Task<string> VerifyIdentityAsync(string rawCredential)
```

**Returns:** the verified `userId` on success. Any failure (thrown exception, or however the adapter signals invalidity) is treated by the inbound layer as `401 Unauthorized`, before any other port is called.

**Adapters:** `CognitoIdentityVerifier` (planned) — verifies the caller's Cognito JWT against the application's own Cognito user pool, returns the token's `sub` claim as `userId`. Deliberate near-term coupling: the interface stays generic (a raw credential string in, a verified `userId` out), but this first adapter is written specifically for the application's own Cognito pool. A more portable adapter (arbitrary OIDC provider, or an API key) is future work — see `roadmap.md`.

### `IConsentStore` — the User side

**Question:** is this user allowed to have their data sent to the AI provider?

```C#
interface IConsentStore
    Task<ConsentRecord> GetConsentAsync(string userId)
    Task<ConsentRecord> SetConsentAsync(string userId, bool granted)
```

**Returns:** the caller's current or updated `ConsentRecord`. Knows nothing about what "user" means upstream — no assumption about which auth provider or account model is behind it; `userId` is an opaque string the caller supplies.

**Adapters:** not yet decided — deferred to Phase 3 (see `roadmap.md`).

### `ILogDataSource` — the Data side

**Question:** what do this user's logs look like in aggregate, over a range?

```C#
interface ILogDataSource
    Task<AggregateStats> GetStatsAsync(string sourceId, DateOnly? from, DateOnly? to)
```

**Returns:** `AggregateStats` — never raw entry text, a constraint enforced by the return type itself (it has no field capable of holding it), not by a runtime check. `from`/`to` are nullable — a null bound means open-ended ("earliest available" / "latest"), resolved however the adapter sees fit — but whatever it resolves to, `AggregateStats.range` always comes back concrete, never null.

**Adapters:** `DynamoDbLogDataSource` (planned) — reads the upstream service's DynamoDB table, read-only, mapping its own item shapes into `AggregateStats`. The `sourceId` parameter is named from the adapter's perspective, not the caller's: the caller supplies its own `userId`, and translating that into whatever id the data source actually keys on — if needed at all — is the adapter's job.

### `IInsightEngine` — the AI side

**Questions:**
- Given a natural-language question, what date range does it need data for?
- Given that data and the question, what's the explainable answer — one that cites which data produced it?

```C#
interface IInsightEngine
    Task<RequestedRange> ExtractRangeAsync(string question)
    Task<Answer> AskAsync(AggregateStats stats, string question)
```

**Returns:** `ExtractRangeAsync` returns a `RequestedRange` resolved from the question text alone — it never sees `AggregateStats`. `AskAsync` returns the final `Answer`, built only from the `AggregateStats` it's given, so it's structurally incapable of leaking anything beyond what's already been aggregated.

**Adapters:** a Claude adapter (planned, unnamed) — wraps the Anthropic Claude API. Both methods attach a fixed system prompt, supplied by the adapter itself, never the caller — keeping it adapter-internal rather than a parameter closes off a prompt-injection surface at exactly the boundary the Ethics-by-Design requirements protect. Final wording is settled in `ethics-by-design.md`, not here.

### `IAuditLogger` — the accountability side

**Question:** what happened, for the record?

```C#
interface IAuditLogger
    Task RecordAuditEntryAsync(AuditEntry entry)
```

**Returns:** nothing — write-only. Called once per `/ask` request, regardless of outcome (including consent denials), per the Ethics-by-Design audit requirement.

**Adapters:** not yet decided — deferred to Phase 3 (see `roadmap.md`).

## Domain objects

Plain data, no behavior — the shapes that flow between interfaces.

```
RequestedRange
├── from: date | null
└── to: date | null

DateRange
├── from: date
└── to: date

AggregateStats
├── sourceId: string
├── range: DateRange
├── totalEntries: int
├── categories: [{ name: string, count: int }]
└── entriesByDate: [{ date: date, count: int }]

ConsentRecord
├── userId: string
├── granted: bool
└── grantedAt: date | null

Answer
├── text: string
└── dataUsed: { statsQueried: string[], range: DateRange }

AuditEntry
├── userId: string
├── queryText: string
├── aggregatedDataSent: AggregateStats | null
├── consentGranted: bool
└── timestamp: datetime
```

`AggregateStats.sourceId` is deliberately named `sourceId`, not `userId` — it identifies whatever the data source's own concept of "owner" is, which the `ILogDataSource` adapter resolves from the `userId` `/stats` and `/ask` receive. The domain doesn't assume those two ids are always the same value; resolving one from the other, if needed at all, is the adapter's job.

**Known gap, deferred:** `ConsentRecord.GrantedAt` is currently only set when `granted` is `true` — so it's `null` both for "never made a consent decision" and for "explicitly revoked." Those are meaningfully different facts (accountability requires distinguishing them), currently indistinguishable. The intended fix — populate `GrantedAt` on every status change, not just on grant, so it reads as "date of the last decision" and `null` means only "never decided" — is noted in `roadmap.md`'s Later section rather than implemented now.
