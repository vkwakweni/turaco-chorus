---
title: Development Phases
last-updated: 2026-08-28
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
| 4 | Containerization + CI/CD | Dockerfile, GitHub Actions pipeline, ECS deploy (EC2 launch type) |
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
- [x] Design `DynamoDbLogDataSource`'s read access (deferred from Phase 1): IAM role with least-privilege read-only policy scoped to Logger's World's table — the CDK cross-stack export/SSM parameter for the table name originally designed here was superseded once the config system was reframed to be genuinely installer-agnostic: the table name is now a plain config value like every other adapter setting, no cross-stack dependency on the upstream's own CDK stack (see `dynamodb-adapter.md`'s "Table name configuration"); the SSM-export design is preserved below under Later / Further Development
- [x] Design `CognitoIdentityVerifier`'s configuration (deferred from Phase 1): which Cognito user pool it verifies against, and how the .NET service validates JWTs against Cognito's JWKS endpoint
- [x] Implement `CognitoIdentityVerifier` (`TuracoChorus.Adapters.Cognito`) per `cognito-adapter.md`'s design — JWKS fetch/cache, `TokenType`-driven audience/`token_use` validation; unit-tested against locally-signed JWTs, no real AWS calls
- [x] Implement `DynamoDbLogDataSource` (`TuracoChorus.Adapters.DynamoDb`) per `dynamodb-adapter.md`'s design — query scoping, colocated-lookup optimization, dimension resolution split into a pure `DynamoDbAggregateStatsBuilder`; unit-tested against hand-built items, no real table needed
- [x] Implement `ClaudeInsightEngine` for `IInsightEngine` (`TuracoChorus.Adapters.Claude`) — hand-rolled Messages API client (no official Anthropic .NET SDK exists), structured-JSON prompts, `stop_reason` completion checking; unit-tested against hand-built responses and a fake HTTP handler
- [x] Implement `GeminiInsightEngine` as a second, genuinely interchangeable `IInsightEngine` adapter (`TuracoChorus.Adapters.Gemini`) — not originally scoped; added so the service has a working AI provider without needing Claude API credits (Claude has no ongoing free tier, Gemini does). Same contract, word-for-word identical system prompts, native structured-output support; same test shape as Claude
- [x] Cross-adapter design review across all four: constructor shape, options-POCO conventions, error-handling consistency (introduced the `InsightResponseParseException`/`QuestionNotAnsweredException` split), and project structure — fixed `DynamoDbLogDataSourceOptions`'s null-default inconsistency and removed its unused `Region` field along the way
- [x] Implement the chosen `IConsentStore` and `IAuditLogger` adapters, same one-project-per-adapter structure
- [x] Swap DI registrations from fakes to real adapters
  - [x] Add project references from `TuracoChorus` to all six adapter projects (`TuracoChorus.Adapters.Cognito`, `.DynamoDb`, `.DynamoDb.Consent`, `.DynamoDb.Audit`, `.Claude`, `.Gemini` — both AI adapters, since `IInsightEngine` will be config-switchable, not hardcoded to one), plus the `AWSSDK.DynamoDBv2` package reference
  - [x] Add an `InsightProvider` config switch (`Claude` | `Gemini`) and per-adapter config sections in `appsettings.json`/user secrets: Cognito's user pool id/region/app client id/token type, the three DynamoDB table names (log data, consent, audit) plus the log data source's dimension config, and both providers' API keys (only the selected one needs a real value) — reframed mid-implementation into a genuinely installer-facing design: hand-written fail-fast config readers (`src/TuracoChorus/Configuration/`), zero real values or defaults committed anywhere, full schema documented in `README.md`'s new "Configuration" section, and an opt-in `UseFakeAdapters` toggle preserving the fakes path for no-credentials demo runs
  - [x] Register a single shared `IAmazonDynamoDB` client and swap all five port registrations in `Program.cs` from fakes to real adapters, resolving `IInsightEngine` from the `InsightProvider` switch — see `AdapterRegistration.cs`
  - [x] Re-run Phase 2's orchestration unit tests unchanged and confirm they still pass — proves the core logic didn't need to change now that real adapters are wired in (confirmed: `TuracoChorus.Core.Tests` passes unmodified)
- [x] Add adapter-level integration tests against real infrastructure (distinct from Phase 2's fakes-based orchestration unit tests). Pattern: each test lives alongside its adapter's unit tests in the same `*.Tests` project, tagged `[Trait("Category", "Integration")]`; the project's own `VSTestTestCaseFilter` (`Category!=Integration`) keeps a plain `dotnet test` green without credentials, and `dotnet test --filter "Category=Integration"` opts in explicitly. Real values (tokens, table names, keys) come from environment variables at test run time, never hardcoded/committed. **The test files themselves are gitignored for now** — written and (where noted) verified locally, not yet published in the tracked repo.
  - [x] `CognitoIdentityVerifier`: verify a real JWT issued by an actual Cognito user pool — verified; token minted out-of-band via the upstream app's own tooling (e.g. Logger's World's `get-test-token.js`), since this app client only supports SRP auth
  - [x] `DynamoDbLogDataSource`: read from a real table — verified (empty `Dimensions`, i.e. total-only stats — dimension-resolution logic is already covered by hand-built-item unit tests; this only proves the real AWS SDK round-trip)
  - [x] `DynamoDbConsentStore` and `DynamoDbAskAuditLogger`: real read/write against real tables — verified (consent set/get/revoke round-trip; audit write confirmed via a direct `GetItemAsync`, since `IAuditLogger` is write-only)
  - [x] `ClaudeInsightEngine`: one real Messages API call — written (mirrors the Gemini test exactly); not being tested right now — no Claude API key exists yet (only Gemini's was created), and getting one is deliberately deferred
  - [x] `GeminiInsightEngine`: one real `generateContent` call — verified, using the real key already in user secrets
- [x] Verify each Ethics-by-Design requirement holds end-to-end with real adapters: consent gating, no raw-text leakage, audit completeness — see `environment-setup.md`'s "Ethics-by-Design verification (consent-denial path)"
  - [x] Fold the "Two Adapters, One Port" artifact into a permanent `artifacts/` doc (e.g. `ai-provider-adapters.md`), matching `cognito-adapter.md`/`dynamodb-adapter.md`'s treatment — currently just a claude.ai Artifact, not yet a repo file

## Phase 4 — Containerization + CI/CD

- [x] Dockerfile for the .NET service
- [x] GitHub Actions pipeline: build → test → lint → Docker build → push to registry (ECR/GHCR)
- [x] Deploy step to ECS via the CDK stack from Phase 1 — switched from the originally-scoped Fargate to the **EC2 launch type**, since Fargate has no free tier and this account's EC2 free tier runs through 19 Jan 2027; introduces a new `TuracoChorusComputeStack`, kept separate from `TuracoChorusStack`'s DynamoDB tables so tearing down compute never risks table data; also adds a stable Elastic IP and a real subdomain (`turacochorus.literaturelounge.org`, delegated from Squarespace via NS records) so the deploy doesn't read as a bare-IP demo — see `ecs-deployment.md`. Deliberately **not** wired to any specific upstream application: `IIdentityVerifier`/`ILogDataSource` run fake (new per-port split in `AdapterRegistration.cs`), `IConsentStore`/`IAuditLogger`/`IInsightEngine` stay real — see `ecs-deployment.md`'s "Per-port fake/real split". Deployed and verified live (ECS service stable, single task, no restarts); the Squarespace NS delegation record has been added for host `turacochorus` — DNS propagation not yet confirmed
- [x] Wire up secrets (Gemini/Claude API key) via AWS Secrets Manager, injected into the ECS task — see `ecs-deployment.md`. Real Gemini key set and verified live via a forced ECS redeployment; that redeploy also surfaced and fixed a stuck-rollout bug in the service's deployment configuration (single instance + fixed host port can't run two tasks at once) — see `ecs-deployment.md`'s "Deployment configuration"

## Phase 5 — Testing, polish, docs

- [x] End-to-end test: seed sample data in Logger's World's table, hit `/ask` from a fresh deploy, verify response + audit log entry — re-scoped to stay independent of Logger's World; verified against the live deployment with the seeded `demo-user` (real Gemini answer, real audit write, no raw text)
- [x] Write up architecture doc referencing back to the Phase 1 requirements doc, showing requirement → design → implementation → test traceability — see `architecture.md`
- [x] Choose a logo — placeholder for now, `assets/logo.svg`, wired into `README.md`; may be revisited for a final design later
- [x] Switch `TuracoChorusConsent`/`TuracoChorusAskAudit`'s DynamoDB `RemovalPolicy` from `DESTROY` (dev-stage default, set while building/testing Phase 3) to `RETAIN` before an official/production deployment — deployed live, metadata-only change, no downtime
- [ ] README polish, CI badge
- [ ] Buffer for whatever slipped

## Later / Further Development

- Frontend integration: surface the NL query box inside Logger's World's UI, calling this service's `/ask` endpoint directly
- CDK cross-stack table-name discovery: instead of plainly configuring `DynamoDb:LogData:TableName` (current design, see `dynamodb-adapter.md`), the upstream stack could publish its table name via an SSM parameter, which Turaco Chorus's stack resolves as a CloudFormation dynamic reference and injects as an ECS environment variable. This creates a deploy-ordering dependency between the two stacks — worth it only when the same operator controls both, which a genuine third-party installer wouldn't.
- Replace the ASCII diagrams in `README.md` and `interaction-flows.md` with `.drawio` files, matching Logger's World's `architecture.drawio` convention — not required for the current design-doc pass
- ~~`ConsentRecord.GrantedAt` is `null` whenever `Granted` is `false`~~ — resolved while implementing `DynamoDbConsentStore`/`FakeConsentStore`: `GrantedAt` is now populated on every status change, granted or revoked, so it reads as "date of the last decision" and `null` means only "never decided"
- `TuracoChorusAskAudit`'s sort key is a millisecond-precision ISO-8601 timestamp, scoped per-user (partition key `userId`). Two `/ask` calls from the *same* user finishing in the same millisecond would collide and silently overwrite one audit entry — practically negligible given each request crosses two LLM round trips before reaching the audit write, but not mathematically impossible (e.g. a double-submit or client retry). A uniqueness suffix on the sort key would close this off completely if it's ever worth the added complexity
