---
title: Environment Setup Log
last-updated: 2026-08-26
---

# Environment Setup Log

Running record of *what was set up and where it lives* for Phase 3's "swap DI registrations from fakes to real adapters" work — not a design doc (see `cognito-adapter.md`/`dynamodb-adapter.md`/`tech-stack.md` for the design), and not a values doc either.

**No real identifiers live in this file, or anywhere else committed to this repo.** Every value tied to *which* upstream application this deployment happens to be connected to — user pool id, app client id, AWS region, table names — is set only via .NET user secrets (local, uncommitted). This isn't just about secrecy: Turaco Chorus's design goal is to stay generic across installers (see `roadmap.md`), so nothing in the committed codebase or docs should name, or embed identifiers belonging to, any specific upstream application. `appsettings.json` carries no adapter-related keys at all, not even placeholder shape — the full schema lives in `README.md`'s "Configuration" section instead.

## `IIdentityVerifier` (Cognito)

Points at the upstream application's own Cognito user pool — Turaco Chorus verifies the same JWTs that application already issues, so no new Cognito resources were created for this deployment.

- Config keys used (see `CognitoIdentityVerifierOptions`): `UserPoolId`, `Region`, `AppClientId`, `TokenType`, `UserIdClaim`.
- `TokenType` is set to `AccessToken` for this deployment — the upstream application's own auth middleware verifies its access token (not id token) and reads `userId` from its `sub` claim, so Turaco Chorus is configured to verify the same token type its clients already hold.
- Real `UserPoolId`/`Region`/`AppClientId` values: set via user secrets — **done**.

## `ILogDataSource` (DynamoDB)

Read-only access to the upstream application's own table — owned and evolved by that application, not by Turaco Chorus.

- Config shape matches the worked example in `dynamodb-adapter.md`'s per-installer configuration section (PK/SK, entry/lookup sort-key prefixes, one `Lookup`-sourced dimension for category, one `DirectAttribute`-sourced dimension for date).
- Real `TableName` and the rest of the shape: set via user secrets — **done**. Supplied directly, per `dynamodb-adapter.md`'s "Table name configuration" — no SSM export or cross-stack dependency on the upstream's own CDK stack (that mechanism is deferred; see `roadmap.md`'s "Later / Further Development").

## `IConsentStore` / `IAuditLogger` (DynamoDB, owned by Turaco Chorus)

Two tables (`TuracoChorusConsent`, `TuracoChorusAskAudit`) owned outright by Turaco Chorus's own CDK stack (`infra/lib/infra-stack.ts`) — **deployed**. `RemovalPolicy.DESTROY` for now (dev-stage); switching to `RETAIN` before an official deployment is tracked in `roadmap.md`'s Phase 5.

- Config keys: `TableName` each (see `DynamoDbConsentStoreOptions`/`DynamoDbAskAuditLoggerOptions`).
- Real table names (the CDK-generated physical names): set via user secrets — **done**.

## `IInsightEngine` (Claude / Gemini)

Config-switchable via an `InsightProvider` key (`Claude` | `Gemini`) — both adapters stay wired in; only the selected one needs a real API key.

- Gemini is the one being set up first (ongoing free tier, no Claude API spend required to run this locally). `InsightProvider` is set to `Gemini` — **done**.
- Real API key: created in Google AI Studio, set via user secrets — **done**.

## End-to-end verification

With every value above set, the app boots cleanly against real adapters (`ASPNETCORE_ENVIRONMENT=Development dotnet run`) and every endpoint was manually exercised successfully against real infrastructure: `GET /stats` (real Cognito verification + real `DynamoDbLogDataSource` query), `GET`/`PUT /consent` (real read/write round-trip against `DynamoDbConsentStore`), and `POST /ask` (two real Gemini calls — range extraction, then answering — plus a real `DynamoDbAskAuditLogger` write). The written audit item confirmed the Ethics-by-Design guarantee holds for real: only aggregated stats reached the AI, never raw entry text.

One bug surfaced and fixed along the way: `GeminiInsightEngineOptions`'s default `Model` (`gemini-2.5-flash`) had been deprecated by Google for new API keys — updated to `gemini-3.6-flash` (the model Google's own error message recommends), with the README and reader test updated to match.

This was a one-off manual verification, not the automated integration test suite — that's still `roadmap.md`'s next item ("Add adapter-level integration tests against real infrastructure").

## Still open

- [ ] Do a mock end-to-end setup against a fictitious upstream schema (e.g. the README's "Acme Habit Tracker" example, not the real upstream this deployment points at) — proves the config system is genuinely installer-agnostic rather than only working because it happens to match the one real deployment it's been tested against so far
