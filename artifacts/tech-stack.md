---
title: Technology Glossary
last-updated: 2026-09-02
---

# Tech Stack

Split in two, to keep what's core clearly separate from what's an adapter — same distinction as `domain-interfaces-and-objects.md`. The core platform is generic: it hosts the domain/orchestration logic and every adapter, but has no dependency on any of the concrete technologies below it. The adapters table is one row per port; which concrete technology satisfies a port is a swappable implementation detail, not something the core depends on.

## Core platform

| Layer | Technology | Notes |
|---|---|---|
| Service runtime | .NET 8 Web API | Hosts the core domain/orchestration logic and every adapter; standalone repo, no shared code with whatever service it reads from |
| Secrets | AWS Secrets Manager | Holds provider credentials (e.g. the Gemini/Claude API key) outside source control and environment files; injected into the ECS task at runtime — see `ecs-deployment.md` |
| Containerization | Docker | Packages the .NET service for deployment |
| Compute | AWS ECS (EC2 launch type) | Runs the containerized service on a single free-tier-eligible `t3.micro`, not Fargate (no free tier) — see `ecs-deployment.md` |
| Networking/DNS | Elastic IP + Amazon Route 53 | Static public IP (re-associated to the instance on every ASG replacement) behind a real subdomain, `turacochorus.literaturelounge.org`, delegated from the domain's actual host (Squarespace) via NS records — see `ecs-deployment.md` |
| Infrastructure as Code | AWS CDK (TypeScript) | Two stacks: `TuracoChorusStack` (DynamoDB tables) and `TuracoChorusComputeStack` (ECS cluster/ASG/task/secret), kept separate so tearing down compute never risks the tables — see `ecs-deployment.md` |
| CI/CD | GitHub Actions | Build → test → Docker build → push → deploy |
| Source control | GitHub | Own repo, own pipeline, own deploy cadence |
| Testing framework | xUnit | Orchestration unit tests run against hand-written in-memory fakes per port, not a mocking library — see glossary |

## Adapters

One row per adapter, grouped by the port it implements; see port definitions in `domain-interfaces-and-objects.md`.

| Port | Adapter | Technology | Notes |
|---|---|---|---|
| `IIdentityVerifier` | `CognitoIdentityVerifier` | Amazon Cognito (JWT) | Verifies the caller's credential against Cognito's JWKS endpoint (derived from configured region + user pool id); derives `userId` from a configured claim (default `sub`). Config-driven per installer — pool id, region, app client id, userId claim — same adapter, no installer-specific code |
| `ILogDataSource` | `DynamoDbLogDataSource` | AWS SDK for .NET (DynamoDB) + AWS IAM (least-privilege role) | Read-only access to the upstream service's own DynamoDB table; table name/ARN supplied directly via config (user secrets/environment variables), same as every other adapter setting: no cross-stack dependency on the upstream's own CDK stack. Item-shape mapping is config-driven, not installer-specific adapter code — see `dynamodb-adapter.md` |
| `IInsightEngine` | `ClaudeInsightEngine` | Anthropic Claude API (Messages API) | Called twice per `/ask` request — range extraction, then answering — both calls carrying a fixed, adapter-supplied system prompt. Structured JSON output via prompt instruction, defensively parsed (strips a markdown fence if the model adds one anyway) |
| `IInsightEngine` | `GeminiInsightEngine` | Google Gemini API (`generateContent`) | Same contract as `ClaudeInsightEngine`, word-for-word identical system prompts — swappable behind the same port. Structured output enforced natively via `responseMimeType: "application/json"`. Google's free tier is ongoing (unlike Claude's one-time trial credit), so this is the adapter usable without any Claude API spend |
| `IConsentStore` | `DynamoDbConsentStore` | AWS SDK for .NET (DynamoDB) | Own table (construct id `TuracoChorusConsent`), PK `userId` only — one row per user, overwritten on every consent change |
| `IAuditLogger` | `DynamoDbAskAuditLogger` | AWS SDK for .NET (DynamoDB) | Own table (construct id `TuracoChorusAskAudit`), PK `userId` + SK `timestamp` (ISO-8601, millisecond precision) — append-only, one row per `/ask` call |

## Storage schemas

Item-level shape for the two tables Turaco Chorus owns outright (`ILogDataSource`'s table belongs to the upstream application, so it isn't Turaco Chorus's schema to define). DynamoDB items only enforce the key attributes (PK, SK); every other attribute is present-or-absent per item, not a fixed column set.

**`TuracoChorusConsent`** — one row per user, written only once a decision is made; no row means never decided. Overwritten in place on every subsequent change.

```
PK  userId           (S)
    granted          (BOOL)
    grantedAt        (S, ISO-8601 timestamp)
```

**`TuracoChorusAskAudit`** — append-only, one row per `/ask` call.

```
PK  userId           (S)
SK  timestamp          (S, ISO-8601, millisecond precision)
    queryText           (S)
    consentGranted       (BOOL)
    aggregatedDataSent    (M, nested `AggregateStats` — attribute omitted when null, i.e. on the consent-denied path)
```

See `roadmap.md`'s Later section for the known, low-probability same-millisecond collision limitation on `TuracoChorusAskAudit`'s sort key.

## Tech glossary

* **Amazon Cognito**: Identity provider whose JWTs authenticate every request. `CognitoIdentityVerifier` verifies the token's signature against Cognito's JWKS endpoint and derives `userId` from its `sub` claim — see `domain-interfaces-and-objects.md`'s `IIdentityVerifier`.
* **Anthropic Claude API**: LLM API used to answer natural-language questions about a user's log data. Called twice per `/ask` request — once to resolve the question's date range, once to produce the answer — both calls carrying a fixed, adapter-supplied system prompt. Only aggregated stats (counts, categories, date ranges) are ever sent to it — never raw log entry text — per the Ethics-by-Design requirements.
* **AWS ECS (EC2 launch type)**: Runs the Docker image on a single `t3.micro` EC2 instance (free-tier eligible for this account's first 12 months), managed by ECS via an Auto Scaling Group and capacity provider. Used instead of Lambda since the .NET service is a long-running Web API, not a single-invocation function; used instead of Fargate to avoid Fargate's per-second billing with no free tier — see `ecs-deployment.md`.
* **AWS IAM (least-privilege role)**: Scoped role granting this service read-only access to the upstream service's DynamoDB table and nothing else — the enforced service boundary between the two repos.
* **AWS SDK for .NET**: Official AWS client library for .NET; used by `DynamoDbLogDataSource` (the `ILogDataSource` adapter) to read — never write — from the upstream service's DynamoDB table.
* **AWS Secrets Manager**: Stores provider credentials outside source control and environment files; injected into the ECS task at runtime.
* **Docker**: Containerises the .NET service for consistent build/deploy across CI and ECS.
* **GitHub Actions**: CI/CD runner — lints, tests, builds the Docker image, pushes it, and triggers the ECS deploy.
* **Google Gemini API**: LLM API used to answer natural-language questions about a user's log data. Called twice per `/ask` request — once to resolve the question's date range, once to produce the answer — both calls carrying a fixed, adapter-supplied system prompt. Structured JSON output is enforced natively via `generationConfig.responseMimeType`. Only aggregated stats (counts, categories, date ranges) are ever sent to it — never raw log entry text — per the Ethics-by-Design requirements.
* **.NET 8 Web API**: The service itself — exposes `/stats`, `/ask`, and `/consent` endpoints.
* **xUnit**: .NET test framework used for orchestration unit tests, run against hand-written in-memory fakes per port rather than a mocking library — fakes hold real state, closer to how the ports actually behave.
