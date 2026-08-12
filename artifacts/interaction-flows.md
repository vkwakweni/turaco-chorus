---
title: Interaction Flows
last-updated: 2026-08-12
---

# Interaction Flows

These are the call sequences through the interfaces defined in `domain-interfaces-and-objects.md`, one per endpoint. They exist so implementation has a fixed sequence to build against, not just a request/response shape.

Every flow below starts with an **Auth** step: the inbound HTTP layer calls `IIdentityVerifier.VerifyIdentityAsync(rawCredential)` (see `domain-interfaces-and-objects.md`) before the Controller does anything else. The first adapter behind that port verifies the application's own Cognito JWT; a failed verification short-circuits with `401 Unauthorized` before any other port is called, in every flow below.

## GET /stats

Returns aggregated statistics about the caller's own data for the requested range, straight from `ILogDataSource.GetStatsAsync` — no consent check, since this is pure aggregation and nothing leaves the service boundary to any AI provider.

```
  Client                  Auth                    Controller              ILogDataSource          Adapter
  │ GET /stats?from&to (+ credential)             │                       │                       │
  ├───────────────────────>                       │                       │                       │
  <── 401 [if invalid] ───┤                       │                       │                       │
  │                       ├───────────────────────>                       │                       │
  │                       │                       ├── GetStatsAsync() ────>                       │
  │                       │                       │                       ├─── query database ────>
  │                       │                       │                       <───────────────────────┤
  │                       │                       <─── AggregateStats ────┤                       │
  <─────────────────── 200 OK ────────────────────┤                       │                       │
```

## POST /ask

Consent gate happens before any `IInsightEngine` call — a denial short-circuits with 403 and never touches the AI provider, including the range-extraction step below. `IInsightEngine` is called twice: once to resolve the question's date range, once to produce the final answer once `AggregateStats` for that range is in hand. Both calls carry a fixed system prompt supplied by the adapter — see `domain-interfaces-and-objects.md`.

```
  Client                  Auth                    Controller              IConsentStore           IInsightEngine          ILogDataSource          IAuditLogger
  │ POST /ask {question} (+ credential)           │                       │                       │                       │                       │
  ├───────────────────────>                       │                       │                       │                       │                       │
  │                         VerifyIdentityAsync() │                       │                       │                       │                       │
  <── 401 [if invalid] ───┤                       │                       │                       │                       │                       │
  │                       ├───────────────────────>                       │                       │                       │                       │
  │                       │                       ├── GetConsentAsync() ──>                       │                       │                       │
  │                       │                       <────── granted? ───────┤                       │                       │                       │
  │                       │                         [if not granted]      │                       │                       │                       │
  │                       │                       ├───────────────────────────── RecordAuditEntryAsync() [denied] ────────────────────────────────>
  <─────────────────────────────────────────────────────────────── 403 Forbidden ─────────────────────────────────────────────────────────────────┤
  │                       │                         [if granted]          │                       │                       │                       │
  │                       │                       ├──────────── ExtractRangeAsync() ──────────────>                       │                       │
  │                       │                       <─────────────── RequestedRange ────────────────┤                       │                       │
  │                       │                       ├────────────────────────── GetStatsAsync() ────────────────────────────>                       │
  │                       │                       <─────────────────────────── AggregateStats ────────────────────────────┤                       │
  │                       │                       ├───────────────── AskAsync() ──────────────────>                       │                       │
  │                       │                       <─────────────────── Answer ────────────────────┤                       │                       │
  │                       │                       ├──────────────────────────────── RecordAuditEntryAsync() ─────────────────────────────────────>
  <─────────────────── 200 OK ────────────────────┤                       │                       │                       │                       │
```

## GET /consent, PUT /consent

Returns or updates the caller's consent state via `IConsentStore` — `GetConsentAsync` to read it, `SetConsentAsync` to grant or revoke it. Revoking consent (`granted: false`) doesn't delete any prior `AuditEntry` records: audit history is append-only and survives revocation, per the Ethics-by-Design retention requirement (to be finalized in the EbD doc).

```
  Client                  Auth                    Controller              IConsentStore
  │ GET/PUT /consent [,granted] (+ credential)    │                       │
  ├───────────────────────>                       │                       │
  │                         VerifyIdentityAsync() │                       │
  <── 401 [if invalid] ───┤                       │                       │
  │                       ├───────────────────────>                       │
  │                       │                       ├─ Get/SetConsentAsync() ─>
  │                       │                       <─── ConsentRecord ─────┤
  <─────────────────── 200 OK ────────────────────┤                       │
```
