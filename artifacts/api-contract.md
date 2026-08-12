---
title: API Contract
last-updated: 2026-08-12
---

# API Contract

The wire-level (HTTP) contract lives in [`openapi.yaml`](openapi.yaml) — the source of truth, a pre-development design artifact written before any implementation exists. Code is written to conform to it, not the other way around.

This doc covers what OpenAPI can't express: how each route maps onto the domain ports defined in `domain-interfaces-and-objects.md`. See `interaction-flows.md` for the full call sequence behind each one.

**Authentication:** every route requires it — the Auth step ahead of the Controller in every `interaction-flows.md` flow, which calls `IIdentityVerifier.VerifyAsync` (see `domain-interfaces-and-objects.md`). `userId` is always derived server-side from the verified credential, never accepted from the request — so no schema in `openapi.yaml` includes a `userId` field, on either the request or response side.

**Consent scope decision:** `/stats` does not require consent (pure aggregation, no data leaves the service boundary); `/ask` requires consent, since that's the only endpoint where data is sent to the AI provider.

## GET /stats

Returns aggregated statistics about the authenticated caller's own data — counts, categories, and per-day totals over an optional date range. Routes directly to `ILogDataSource.GetStatsAsync`, called with the `userId` derived from the verified credential; `AggregateStats.sourceId` is dropped when serializing the response, not echoed back as `userId` either — the caller already knows its own identity from its credential, so the response carries only the data it asked for. Requires authentication, not consent.

## POST /ask

Answers a natural-language question about the authenticated caller's own data, using an AI provider grounded only in aggregated stats — never raw entry text. `IConsentStore` is checked first; on denial, returns 403 without calling `ILogDataSource` or `IInsightEngine` at all — including the range-extraction step (see `interaction-flows.md`). On success, `IInsightEngine` is called twice: first with just the question text, to resolve a `RequestedRange`; then, once `ILogDataSource` has returned `AggregateStats` for that range, a second time with only that `AggregateStats` to produce the answer. Writes an `AuditEntry` via `IAuditLogger` regardless of outcome. Requires authentication and explicit consent.

## GET /consent

Returns whether the authenticated caller has opted in to `/ask`, and when. Routes directly to `IConsentStore.GetAsync`, called with the `userId` derived from the verified credential. Requires authentication.

## PUT /consent

Grants or revokes the authenticated caller's consent to use `/ask`. Routes directly to `IConsentStore.SetAsync`, called with the `userId` derived from the verified credential. Requires authentication. Revoking consent takes effect immediately for future `/ask` calls; it does not delete prior audit records (see `ethics-by-design.md`).
