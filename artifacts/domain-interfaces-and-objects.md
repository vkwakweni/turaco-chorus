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

**Persistence ownership differs by port:**
- `ILogDataSource` reads data that belongs to whatever *upstream* application Turaco Chorus is connected to — the technology behind it isn't Turaco Chorus's choice, since it's dictated by that application's own existing storage.
- `IConsentStore` and `IAuditLogger`, by contrast, hold data that belongs to Turaco Chorus itself — consent decisions, its own audit trail — which no upstream application has any stake in.

## Interfaces

Each interface below follows the same structure: the question it answers, the interface itself, what it returns, and its adapter(s) — planned or already decided.

### `IIdentityVerifier` — the Identity side

**Question:** whose verified `userId` does this credential belong to, if it's valid at all? Called by the inbound HTTP layer before any other port, on every route (see `interaction-flows.md`'s Auth step).

```C#
interface IIdentityVerifier
    Task<string> VerifyIdentityAsync(string rawCredential)
```

**Returns:** the verified `userId` on success. Any failure (thrown exception, or however the adapter signals invalidity) is treated by the inbound layer as `401 Unauthorized`, before any other port is called.

**Adapters:** `CognitoIdentityVerifier` — verifies the caller's Cognito JWT against a configured Cognito user pool, returns a configured claim (default `sub`) as `userId`. Built generic at the pool level: pool id, region, app client id, and the userId claim are deploy-time configuration (see the Adapters table in `tech-stack.md`), so the same adapter works for any installer on Cognito without code changes. Deliberate near-term coupling stays at the technology level, not the pool level: the adapter still only speaks Cognito's JWT/JWKS format. A more portable adapter (arbitrary OIDC provider, or an API key) is future work — see `roadmap.md`.

### `IConsentStore` — the User side

**Question:** is this user allowed to have their data sent to the AI provider?

```C#
interface IConsentStore
    Task<ConsentRecord> GetConsentAsync(string userId)
    Task<ConsentRecord> SetConsentAsync(string userId, bool granted)
```

**Returns:** the caller's current or updated `ConsentRecord`. Knows nothing about what "user" means upstream — no assumption about which auth provider or account model is behind it; `userId` is an opaque string the caller supplies.

**Adapters:** `DynamoDbConsentStore` (planned) — own DynamoDB table (construct id `TuracoChorusConsent`), PK `userId` only, one row per user.

### `ILogDataSource` — the Data side

**Question:** what do this user's logs look like in aggregate, over a range?

```C#
interface ILogDataSource
    Task<AggregateStats> GetStatsAsync(string sourceId, DateOnly? from, DateOnly? to)
```

**Returns:** `AggregateStats` — never raw entry text, a constraint enforced by the return type itself (it has no field capable of holding it), not by a runtime check. `from`/`to` are nullable — a null bound means open-ended ("earliest available" / "latest"), resolved however the adapter sees fit — but whatever it resolves to, `AggregateStats.range` always comes back concrete, never null.

**Adapters:** `DynamoDbLogDataSource` — reads the upstream service's DynamoDB table, read-only, mapping its own item shapes into `AggregateStats`. Built generic, not hardcoded to one installer: the item-shape mapping (key structure, date attribute, dimension resolution) is supplied as configuration per deployment — see `dynamodb-adapter.md`. The `sourceId` parameter is named from the adapter's perspective, not the caller's: the caller supplies its own `userId`, and translating that into whatever id the data source actually keys on — if needed at all — is the adapter's job.

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

**Adapters:** two — `ClaudeInsightEngine` (Anthropic Claude API) and `GeminiInsightEngine` (Google Gemini API), genuinely interchangeable behind this one port, not a primary plus a stub. Both attach a fixed system prompt, supplied by the adapter itself, never the caller — keeping it adapter-internal rather than a parameter closes off a prompt-injection surface at exactly the boundary the Ethics-by-Design requirements protect. Both prompts are word-for-word identical between the two adapters. Final wording is settled in `ethics-by-design.md`, not here. Built config-driven per installer, same pattern as `CognitoIdentityVerifier`/`DynamoDbLogDataSource`.

### `IAuditLogger` — the accountability side

**Question:** what happened, for the record?

```C#
interface IAuditLogger
    Task RecordAuditEntryAsync(AuditEntry entry)
```

**Returns:** nothing — write-only. Called once per `/ask` request, regardless of outcome (including consent denials), per the Ethics-by-Design audit requirement.

**Adapters:** `DynamoDbAskAuditLogger` (planned) — own DynamoDB table (construct id `TuracoChorusAskAudit`), PK `userId`, SK `timestamp`, append-only.

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
└── dimensions: [{ name: string, buckets: [{ value: string, count: int }] }]

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

`AggregateStats.dimensions` has no built-in concepts — not even date-bucketing. Every dimension is defined entirely by the installer's `ILogDataSource` adapter configuration (see `dynamodb-adapter.md`). Dimension names should be unique within `dimensions`, and bucket values unique within a dimension's `buckets`; order in either list is adapter-determined, never contractual. An empty `dimensions` list is a valid response (total-only stats), not a special case.

**Known gap, deferred:** `ConsentRecord.GrantedAt` is currently only set when `granted` is `true` — so it's `null` both for "never made a consent decision" and for "explicitly revoked." Those are meaningfully different facts (accountability requires distinguishing them), currently indistinguishable. The intended fix — populate `GrantedAt` on every status change, not just on grant, so it reads as "date of the last decision" and `null` means only "never decided" — is noted in `roadmap.md`'s Later section rather than implemented now.
