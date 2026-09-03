---
title: Architecture & Requirement Traceability
last-updated: 2026-09-03
---

# Architecture & Requirement Traceability

This doc closes the loop Phase 1 opened: every requirement written down before implementation began, traced through to where it was designed, where it was implemented, and how it was verified. Nothing here is a new requirement — it's an index into work already documented elsewhere, organised so a reader can follow one row from intent to proof.

## System shape

Turaco Chorus uses the Ports and Adapters (Hexagonal) design pattern:

- Five ports owned by the core domain: `IIdentityVerifier`, `IConsentStore`, `ILogDataSource`, `IInsightEngine`, `IAuditLogger`
    - Each has one or more adapters translating to a real technology.

Three HTTP endpoints sit in front of that core, each following a fixed call sequence through those ports: `GET /stats`, `POST /ask`, `GET`/`PUT /consent`.

Full detail:
- `domain-interfaces-and-objects.md`: ports/objects
- `api-contract.md`: HTTP contract
- `interaction-flows.md`: call sequences
- `tech-stack.md` : concrete adapters
- `ecs-deployment.md`: how it actually runs

## Functional requirements

| Requirement | Auth / consent | Design | Implementation | Test / verification |
|---|---|---|---|---|
| `GET /stats` returns aggregated stats for the caller's own data | Auth only | `api-contract.md`, `interaction-flows.md` | `Program.cs`'s `/stats` route → `StatsOrchestrator` → `ILogDataSource` | Orchestration unit tests (fakes); `DynamoDbLogDataSource` unit tests; live: real `/stats` call against the deployed service, 2026-09-03 |
| `POST /ask` denial short-circuits before `ILogDataSource`/`IInsightEngine` are touched at all, including range extraction | Auth + consent | `api-contract.md`, `interaction-flows.md`, `ethics-by-design.md` §2 | `AskOrchestrator`'s consent check ordered before both `IInsightEngine` calls | Orchestration unit tests (consent-denied path); Phase 3's manual consent-denial verification against real adapters (`environment-setup.md`) |
| `POST /ask` calls `IInsightEngine` twice — range extraction, then answering — never sending raw entry text, only `AggregateStats` | Auth + consent | `domain-interfaces-and-objects.md`, `interaction-flows.md` | `ClaudeInsightEngine`/`GeminiInsightEngine`; `AggregateStats` has no field capable of holding raw text | Orchestration unit tests (no-raw-text-to-`IInsightEngine` guarantee); live: real Gemini call verified via Google AI Studio's own request log, 2026-09-03 |
| `POST /ask` writes an `AuditEntry` regardless of outcome (granted or denied) | Auth + consent | `ethics-by-design.md` §6, `interaction-flows.md` | `AskOrchestrator` calls `IAuditLogger.RecordAuditEntryAsync` on both branches | Orchestration unit tests (audit-on-every-call guarantee); Phase 3's manual denial-path verification; live: real `TuracoChorusAskAudit` item confirmed via direct `aws dynamodb query`, 2026-09-03 |
| `GET`/`PUT /consent` read/write the caller's consent state; revoking doesn't delete prior audit records | Auth only | `api-contract.md`, `interaction-flows.md`, `ethics-by-design.md` §2 | `ConsentOrchestrator` → `DynamoDbConsentStore`; audit table is append-only, no delete path exists anywhere in the code | Orchestration unit tests; `DynamoDbConsentStore` unit tests; live: real grant/read round-trip against the deployed service, 2026-09-03 |
| `userId` is always derived server-side from the verified credential, never accepted from the request body | Auth (all routes) | `api-contract.md` | Every route calls `BearerAuth.AuthenticateAsync` before touching any other port; no request DTO has a `userId` field | Reviewed directly against `openapi.yaml`'s schemas (no automated schema-conformance test exists yet — a gap, not a pass) |

## Ethics-by-Design requirements

One row per `ethics-by-design.md` value.

| Value | Design requirement | Implementation | Test / verification |
|---|---|---|---|
| 1. Human agency | `/ask` is informational only — no write path back into the user's data; system prompt instructs the AI to answer, not recommend | `ILogDataSource` is read-only by interface shape; system prompt hardcoded in the adapter, never caller-supplied | Structural (interface has no write method) — no dedicated automated test; prompt wording reviewed manually during Phase 3 |
| 2. Privacy and data governance | Only aggregates ever leave the service boundary; consent checked before every `/ask`, including range extraction; least-privilege read-only IAM on the upstream table | `AggregateStats` domain object; `AskOrchestrator`'s check ordering; `DynamoDbLogDataSource`'s `dynamodb:Query`-only IAM policy (`dynamodb-adapter.md`) | Orchestration unit tests (consent-denied short-circuit, no-raw-text guarantee); Phase 3's manual consent-denial verification; live: real audit item inspected directly, `aggregatedDataSent` contains only counts/buckets, no raw text, 2026-09-03 |
| 3. Fairness | No special-casing by user, account age, or data volume — same code path for everyone | Orchestration logic branches only on domain state (consent granted/denied), never on `userId` itself | Implicit in orchestration unit tests (same path exercised regardless of which fake user is passed) — no dedicated fairness test, since there's no model or ranking to audit |
| 4. Well-being | System prompt reports facts neutrally, doesn't editorialise about the user's own habits | System prompt wording, identical across `ClaudeInsightEngine`/`GeminiInsightEngine` | Manual review only, during Phase 3 adapter implementation — prompt wording isn't structurally testable |
| 5. Transparency | Every `/ask` answer carries `dataUsed`, not optional; `/ask` is explicitly and only an AI endpoint | `Answer.dataUsed` is a required field on the domain object, not nullable | Orchestration unit tests assert `dataUsed` is populated on every `AskAllowed` result; live: real response carried `dataUsed` correctly, 2026-09-03 |
| 6. Accountability and oversight | `IAuditLogger` called once per `/ask`, regardless of outcome; append-only; human-readable schema | `AskOrchestrator`'s unconditional audit call; `DynamoDbAskAuditLogger`; audit schema documented in `tech-stack.md` | Orchestration unit tests; Phase 3's manual denial-path verification (both granted and denied paths produce records); live: real item confirmed via direct query, 2026-09-03 |

## Known gaps

Traced honestly, not just favourably — three requirements above have no automated test, only manual/structural verification:

- **Request-schema conformance**, from the Functional requirements table's `userId` row: checked against `openapi.yaml` by eye, not by a running test.
- **No output-side check, human agency**, from the Ethics-by-Design table's value 1: the prompt instructs the AI not to recommend actions, but nothing verifies the AI's actual generated answers honour that — only the prompt's own wording was ever reviewed, not real model output across invocations. An LLM can deviate from its instructions; nothing here would catch it.
- **No output-side check, well-being**, from the Ethics-by-Design table's value 4: same shape of gap — the prompt asks for a neutral, non-judgemental tone, but no real answer has ever been checked against that beyond spot-checking during Phase 3's development.

Closing these would need a dedicated approach (a schema-validation test harness; some kind of prompt-output assertion) — noted as a candidate for `roadmap.md`'s "Later" section rather than solved here.
