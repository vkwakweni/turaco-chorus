---
title: Technology Glossary
last-updated: 2026-08-11
---

# Tech Stack

Split in two, to keep what's core clearly separate from what's an adapter — same distinction as `domain-interfaces-and-objects.md`. The core platform is generic: it hosts the domain/orchestration logic and every adapter, but has no dependency on any of the concrete technologies below it. The adapters table is one row per port; which concrete technology satisfies a port is a swappable implementation detail, not something the core depends on.

## Core platform

| Layer | Technology | Notes |
|---|---|---|
| Service runtime | .NET 8 Web API | Hosts the core domain/orchestration logic and every adapter; standalone repo, no shared code with whatever service it reads from |
| Secrets | AWS Secrets Manager | Holds provider credentials (e.g. the Claude API key) outside source control and environment files; injected into the ECS task at runtime |
| Containerization | Docker | Packages the .NET service for deployment |
| Compute | AWS ECS Fargate | Runs the containerized service |
| Infrastructure as Code | AWS CDK (TypeScript) | Own stack: ECS service, IAM role; audit/consent storage added once Phase 3 picks their adapters |
| CI/CD | GitHub Actions | Build → test → Docker build → push → deploy |
| Source control | GitHub | Own repo, own pipeline, own deploy cadence |

## Adapters

One row per port defined in `domain-interfaces-and-objects.md`. `IConsentStore` and `IAuditLogger` have no chosen technology yet — that decision is deferred to Phase 3 (see `roadmap.md`).

| Port | Adapter | Technology | Notes |
|---|---|---|---|
| `IIdentityVerifier` | `CognitoIdentityVerifier` | Amazon Cognito (JWT) | Verifies the caller's credential against Cognito's JWKS endpoint; derives `userId` from the token's `sub` claim |
| `ILogDataSource` | `DynamoDbLogDataSource` | AWS SDK for .NET (DynamoDB) + AWS IAM (least-privilege role) + SSM Parameter Store | Read-only access to the upstream service's DynamoDB table; table name/ARN is published via that service's own CDK stack and consumed here — never hardcoded or duplicated |
| `IInsightEngine` | *(unnamed — Claude adapter)* | Anthropic Claude API (Messages API) | Called twice per `/ask` request — range extraction, then answering — both calls carrying a fixed, adapter-supplied system prompt |
| `IConsentStore` | *(TBD)* | *(TBD)* | Storage decision deferred to Phase 3 |
| `IAuditLogger` | *(TBD)* | *(TBD)* | Storage decision deferred to Phase 3 |

## Tech glossary

* **Amazon Cognito**: Identity provider whose JWTs authenticate every request. `CognitoIdentityVerifier` verifies the token's signature against Cognito's JWKS endpoint and derives `userId` from its `sub` claim — see `domain-interfaces-and-objects.md`'s `IIdentityVerifier`.
* **Anthropic Claude API**: LLM API used to answer natural-language questions about a user's log data. Called twice per `/ask` request — once to resolve the question's date range, once to produce the answer — both calls carrying a fixed, adapter-supplied system prompt. Only aggregated stats (counts, categories, date ranges) are ever sent to it — never raw log entry text — per the Ethics-by-Design requirements.
* **AWS ECS Fargate**: Serverless container hosting — runs the Docker image without managing servers. Used here instead of Lambda since the .NET service is a long-running Web API, not a single-invocation function.
* **AWS IAM (least-privilege role)**: Scoped role granting this service read-only access to the upstream service's DynamoDB table and nothing else — the enforced service boundary between the two repos.
* **AWS SDK for .NET**: Official AWS client library for .NET; used by `DynamoDbLogDataSource` (the `ILogDataSource` adapter) to read — never write — from the upstream service's DynamoDB table.
* **AWS Secrets Manager**: Stores provider credentials outside source control and environment files; injected into the ECS task at runtime.
* **AWS SSM Parameter Store**: Holds the DynamoDB table name/ARN exported from the upstream service's CDK stack, so this service can look it up without hardcoding or duplicating infra state.
* **Docker**: Containerises the .NET service for consistent build/deploy across CI and ECS.
* **GitHub Actions**: CI/CD runner — lints, tests, builds the Docker image, pushes it, and triggers the ECS deploy.
* **.NET 8 Web API**: The service itself — exposes `/stats`, `/ask`, and `/consent` endpoints.
