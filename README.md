# Turaco Chorus

Turaco Chorus is a companion .NET 8 service that lets users ask natural-language questions about their own log data — answered by the Anthropic Claude API, grounded only in aggregated stats, never raw entry text. It's a Ports-and-Adapters (Hexagonal Architecture) portfolio project, built as a live demonstration of Ethics by Design: consent, data minimisation, and audit logging aren't bolted on after the AI feature works — they're the requirements the feature is built to satisfy.

Named after the Knysna Turaco — a bird tied to South Africa's indigenous Southern Cape forests, used as an indicator species for their health. The name plays on that idea: this service reads someone else's data and reports back on what it finds, the way an indicator species reveals the condition of the forest around it — and "Chorus" for the collective, speaking-back quality of turning data into an answer.

Full design docs live under [`artifacts/`](artifacts/): [`domain-interfaces-and-objects.md`](artifacts/domain-interfaces-and-objects.md) (the five ports and domain objects), [`interaction-flows.md`](artifacts/interaction-flows.md) (call sequences per endpoint), [`api-contract.md`](artifacts/api-contract.md) (the HTTP contract), [`ethics-by-design.md`](artifacts/ethics-by-design.md) (the EbD-AI requirements), [`tech-stack.md`](artifacts/tech-stack.md) (technology choices, split core vs. adapters), and [`roadmap.md`](artifacts/roadmap.md) (the phased build plan).

## Architecture

The core depends only on interfaces it defines itself ("ports"); every concrete technology sits behind one of them as a swappable "adapter". No adapter is visible to another, and the core has no dependency on any of them.

```
                          ┌─────────────────────────────┐
                          │           Client            │
                          └───────────────┬─────────────┘
                                          │ HTTP (/stats, /ask, /consent)
                          ┌───────────────▼───────────────┐
                          │      Core domain logic        │
                          │ (orchestration, no tech deps) │
                          └───┬───────┬───────┬───────┬───┘
                    ┌─────────┘       │       │       └─────────┐
                    │                 │       │                 │
         IIdentityVerifier   IConsentStore  ILogDataSource  IInsightEngine
                    │                 │       │                 │      \
                    │                 │       │                 │       IAuditLogger
                    ▼                 ▼       ▼                 ▼           │
          CognitoIdentityVerifier  (adapter  DynamoDbLogDataSource   Claude adapter
          (Amazon Cognito JWT)      TBD —     (AWS SDK, read-only)   (Anthropic API,
                                    Phase 3)                          2 calls/request)
                                                                            │
                                                                    (adapter TBD — Phase 3)
```

`IConsentStore` and `IAuditLogger` have no chosen adapter yet — that decision is deliberately deferred to Phase 3, once the core logic has been built and unit-tested against fakes for all five ports (see `roadmap.md`'s sequencing logic).

## Project layout

- [`src/TuracoChorus/`](src/TuracoChorus/) — the .NET 8 Web API: core domain logic, ports, and adapters
- [`infra/`](infra/) — AWS CDK (TypeScript): `TuracoChorusStack`, deploying the service to ECS Fargate
- [`artifacts/`](artifacts/) — design docs (see above)

## Status

Phase 1 (requirements & design) complete. Phase 2 (core domain & application logic) not yet started — see `roadmap.md`.

