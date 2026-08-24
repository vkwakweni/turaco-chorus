---
title: Development Phases
last-updated: 2026-08-13
---

# Development Phases

## Project: Turaco Chorus

Turaco Chorus is a companion .NET 8 service that can be connected to upstream applications, letting users ask natural-language questions about their data — answered by a connected AI provider's API, grounded only in aggregated statistics, never raw entry text.

It exists as a live demonstration of Ethics by Design: consent, data minimisation, and audit logging aren't bolted on after the AI feature works, they're the requirements the AI feature is built to satisfy. The project also doubles as a real cross-service integration exercise — a separate repo, its own IAM boundary, its own CDK stack and CI/CD pipeline — reading an upstream application's data without ever touching its code, the way a real third-party integration would. The name and design deliberately avoid coupling to a specific application, so the same pattern (explainable, consent-gated, audit-logged AI over someone else's data) could be pointed at another data source later.

**Scope confirmed:** `/stats` + `/ask` + `/consent` is the final endpoint set for this pass. n8n integration and frontend integration stay deferred (see Later / Further Development below).

| Phase | Stage | Focus |
|---|---|---|
| 1 | Requirements & Design (incl. Ethics by Design) + Repo Scaffold | API contract, domain model, interaction flows, Ethics-by-Design requirements doc, repo scaffold |
| 2 | Core Domain & Application Logic | All five ports + orchestration logic for `/stats`, `/ask`, `/consent`; unit-tested against fakes |
| 3 | Adapters, Integration & Ethics-by-Design Enforcement | Concrete adapters for all five ports (Cognito, DynamoDB, Claude, consent storage, audit storage), wired in via DI, replacing the fakes |
| 4 | Containerization + CI/CD | Dockerfile, GitHub Actions pipeline, ECS Fargate deploy |
| 5 | Testing, polish, docs | End-to-end test, requirement-to-test traceability writeup |

**Sequencing logic:** the Ethics-by-Design requirements doc comes first (Phase 1) since it defines what the consent, audit, and data-minimization implementation on Phase 3 must satisfy — implementation without the requirement doc first would risk retrofitting these controls instead of designing for them. Core domain and orchestration logic (Phase 2) come before any concrete adapter (Phase 3), so that all business logic — across all three endpoints — is designed, wired, and unit-tested against fakes first, and only proven-correct logic gets connected to real infrastructure. This also means the two undecided adapters (`IConsentStore`, `IAuditLogger`) don't need a storage decision until Phase 3, once Phase 2's tests have made their real usage concrete.

## Phase 1 — Requirements & Design (incl. Ethics by Design) + Repo Scaffold

- [x] Define API contract: `/stats` (aggregates), `/ask` (NL query in, answer + data-provenance out), `/consent` (opt-in toggle per user) — see `api-contract.md`
- [x] Define domain model: OO interfaces decoupling Turaco Chorus's core logic from Logger's World/DynamoDB specifics (`IConsentStore`, `ILogDataSource`, `IInsightEngine`, `IAuditLogger`) — see `domain-interfaces-and-objects.md`
- [x] Define interaction flows: backend equivalent of wireframes, one call-sequence diagram per endpoint — see `interaction-flows.md`
- [x] Decide client authentication/authorization mechanism: `IIdentityVerifier` port (fifth domain interface), with a `CognitoIdentityVerifier` adapter as the first implementation, verifying the application's own Cognito JWT — see `domain-interfaces-and-objects.md`. Interface stays generic; a more portable (non-Cognito-specific) adapter is future work, not required this pass.
- [x] Write the Ethics-by-Design requirements doc: structured per the EbD-AI framework (Brey & Dainow, 2024), walking all six values (human agency, privacy/data governance, fairness, well-being, transparency, accountability/oversight) — see `ethics-by-design.md`
- [x] Scaffold new repo: `.NET 8 Web API` project (`src/TuracoChorus/`), `infra/` (CDK, stack renamed to `TuracoChorusStack`), `README.md` with architecture diagram

## Phase 2 — Core Domain & Application Logic

- [x] Finalize the five port interfaces (`IIdentityVerifier`, `IConsentStore`, `ILogDataSource`, `IInsightEngine`, `IAuditLogger`) as C# interfaces in the core/domain project, matching `domain-interfaces-and-objects.md`
- [x] Implement orchestration logic for all three endpoints per `interaction-flows.md`, depending only on port interfaces: `/stats` (auth → `GetStatsAsync`), `/ask` (auth → consent check → `ExtractRangeAsync` → `GetStatsAsync` → `AskAsync` → `RecordAuditEntryAsync`, including the 403+audit-denied branch), `/consent` (auth → `GetConsentAsync`/`SetConsentAsync`)
- [x] Write fake/in-memory test doubles for all five ports
- [x] Unit test each orchestration flow against the fakes: happy paths, the consent-denied path, the no-raw-text-to-`IInsightEngine` guarantee, the audit-entry-written-on-every-`/ask`-call guarantee
- [x] Wire orchestration + fakes behind DI so all three endpoints run end-to-end against fakes, no AWS/Claude credentials required yet

## Phase 3 — Adapters, Integration & Ethics-by-Design Enforcement

- [x] Decide storage/adapter approach for `IConsentStore` and `IAuditLogger` (deferred from Phase 1); design the audit-log schema now that storage is chosen
- [x] Design `DynamoDbLogDataSource`'s read access (deferred from Phase 1): IAM role with least-privilege read-only policy scoped to Logger's World's table, plus the CDK cross-stack export/SSM parameter for the table name
- [x] Design `CognitoIdentityVerifier`'s configuration (deferred from Phase 1): which Cognito user pool it verifies against, and how the .NET service validates JWTs against Cognito's JWKS endpoint
- [x] Implement `CognitoIdentityVerifier` (`TuracoChorus.Adapters.Cognito`) per `cognito-adapter.md`'s design — JWKS fetch/cache, `TokenType`-driven audience/`token_use` validation; unit-tested against locally-signed JWTs, no real AWS calls
- [x] Implement `DynamoDbLogDataSource` (`TuracoChorus.Adapters.DynamoDb`) per `dynamodb-adapter.md`'s design — query scoping, colocated-lookup optimization, dimension resolution split into a pure `DynamoDbAggregateStatsBuilder`; unit-tested against hand-built items, no real table needed
- [x] Implement `ClaudeInsightEngine` for `IInsightEngine` (`TuracoChorus.Adapters.Claude`) — hand-rolled Messages API client (no official Anthropic .NET SDK exists), structured-JSON prompts, `stop_reason` completion checking; unit-tested against hand-built responses and a fake HTTP handler
- [x] Implement `GeminiInsightEngine` as a second, genuinely interchangeable `IInsightEngine` adapter (`TuracoChorus.Adapters.Gemini`) — not originally scoped; added so the service has a working AI provider without needing Claude API credits (Claude has no ongoing free tier, Gemini does). Same contract, word-for-word identical system prompts, native structured-output support; same test shape as Claude
- [x] Cross-adapter design review across all four: constructor shape, options-POCO conventions, error-handling consistency (introduced the `InsightResponseParseException`/`QuestionNotAnsweredException` split), and project structure — fixed `DynamoDbLogDataSourceOptions`'s null-default inconsistency and removed its unused `Region` field along the way
- [x] Implement the chosen `IConsentStore` and `IAuditLogger` adapters, same one-project-per-adapter structure
- [ ] Swap DI registrations from fakes to real adapters — Phase 2's orchestration unit tests must pass unchanged, proving the core logic didn't need to change; add adapter-level integration tests (real Cognito verification, real DynamoDB read, real Claude call)
- [ ] Verify each Ethics-by-Design requirement holds end-to-end with real adapters: consent gating, no raw-text leakage, audit completeness
  - [ ] Fold the "Two Adapters, One Port" artifact into a permanent `artifacts/` doc (e.g. `ai-provider-adapters.md`), matching `cognito-adapter.md`/`dynamodb-adapter.md`'s treatment — currently just a claude.ai Artifact, not yet a repo file

## Phase 4 — Containerization + CI/CD

- [ ] Dockerfile for the .NET service
- [ ] GitHub Actions pipeline: build → test → lint → Docker build → push to registry (ECR/GHCR)
- [ ] Deploy step to ECS Fargate via the CDK stack from Phase 1
- [ ] Wire up secrets (Claude API key) via AWS Secrets Manager, injected into the ECS task

## Phase 5 — Testing, polish, docs

- [ ] End-to-end test: seed sample data in Logger's World's table, hit `/ask` from a fresh deploy, verify response + audit log entry
- [ ] Write up architecture doc referencing back to the Phase 1 requirements doc, showing requirement → design → implementation → test traceability
- [ ] Choose a logo
- [ ] README polish, CI badge
- [ ] Buffer for whatever slipped

## Later / Further Development

- Frontend integration: surface the NL query box inside Logger's World's UI, calling this service's `/ask` endpoint directly
- Replace the ASCII diagrams in `README.md` and `interaction-flows.md` with `.drawio` files, matching Logger's World's `architecture.drawio` convention — not required for the current design-doc pass
- ~~`ConsentRecord.GrantedAt` is `null` whenever `Granted` is `false`~~ — resolved while implementing `DynamoDbConsentStore`/`FakeConsentStore`: `GrantedAt` is now populated on every status change, granted or revoked, so it reads as "date of the last decision" and `null` means only "never decided"
- `TuracoChorusAskAudit`'s sort key is a millisecond-precision ISO-8601 timestamp, scoped per-user (partition key `userId`). Two `/ask` calls from the *same* user finishing in the same millisecond would collide and silently overwrite one audit entry — practically negligible given each request crosses two LLM round trips before reaching the audit write, but not mathematically impossible (e.g. a double-submit or client retry). A uniqueness suffix on the sort key would close this off completely if it's ever worth the added complexity
