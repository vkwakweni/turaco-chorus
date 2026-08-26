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
- Real `UserPoolId`/`Region`/`AppClientId` values: set via user secrets, not recorded here.

## `ILogDataSource` (DynamoDB)

Read-only access to the upstream application's own table — owned and evolved by that application, not by Turaco Chorus.

- Config shape matches the worked example in `dynamodb-adapter.md`'s per-installer configuration section (PK/SK, entry/lookup sort-key prefixes, one `Lookup`-sourced dimension for category, one `DirectAttribute`-sourced dimension for date).
- Real `TableName`/region: set via user secrets, not recorded here.

**Known gap:** `dynamodb-adapter.md` specifies this table name should reach config via an SSM Parameter Store export from the upstream application's own CDK stack (cross-stack, no hardcoding/duplication) — that export doesn't exist yet on the upstream side. Until it does, the real table name is supplied manually via user secrets as a stand-in. A development note still needs to be left in the upstream application's own repo to add the export (see "Still open" below — not yet done).

## `IConsentStore` / `IAuditLogger` (DynamoDB, owned by Turaco Chorus)

Two tables (`TuracoChorusConsent`, `TuracoChorusAskAudit`) owned outright by Turaco Chorus's own CDK stack (`infra/lib/infra-stack.ts`) — not yet added as constructs, not yet deployed.

- Config keys: `TableName` each (see `DynamoDbConsentStoreOptions`/`DynamoDbAskAuditLoggerOptions`).
- Real table names: set via user secrets once the stack is deployed.

## `IInsightEngine` (Claude / Gemini)

Config-switchable via an `InsightProvider` key (`Claude` | `Gemini`) — both adapters stay wired in; only the selected one needs a real API key.

- Gemini is the one being set up first (ongoing free tier, no Claude API spend required to run this locally).
- Real API key: set via user secrets once created in Google AI Studio.

## Still open

- [ ] Populate user secrets with the real Cognito/DynamoDB identifiers above
- [ ] Add the SSM table-name export to the upstream application's own CDK stack (tracked as a dev note in that repo)
- [ ] Add `TuracoChorusConsent`/`TuracoChorusAskAudit` constructs to `infra/lib/infra-stack.ts` and deploy
- [ ] Create a Gemini API key and add it to user secrets
- [ ] Do a mock end-to-end setup against a fictitious upstream schema (e.g. the README's "Acme Habit Tracker" example, not the real upstream this deployment points at) — proves the config system is genuinely installer-agnostic rather than only working because it happens to match the one real deployment it's been tested against so far
