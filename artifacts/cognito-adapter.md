---
title: Cognito Adapter Configuration
last-updated: 2026-08-18
---

# Cognito Adapter Configuration

Design for `CognitoIdentityVerifier`, the concrete adapter behind `IIdentityVerifier` (see `domain-interfaces-and-objects.md`).
Config-driven per installer, same pattern as `DynamoDbLogDataSource` (see `dynamodb-adapter.md`): one adapter, reusable across installers on Cognito, no installer-specific code.
Still coupled to Cognito as a technology — a different identity provider (arbitrary OIDC, API key) is future work, not this adapter's job.

## Scope

- Configuration is supplied at deploy time (static), one Turaco Chorus deployment per installer.
- The adapter only speaks Cognito's JWT/JWKS format — the pool it verifies against is configurable, the technology is not.
- The JWKS endpoint is always derived from `Region` + `UserPoolId` via Cognito's standard URL pattern, not separately configured.

## Configuration schema

```
CognitoIdentityVerifierOptions
├── UserPoolId       — e.g. "us-east-1_XXXXXXXXX"
├── Region            — e.g. "us-east-1"
├── AppClientId         — for audience validation
└── UserIdClaim          — which JWT claim becomes `userId` (defaults to "sub")
```

## Logger's World configuration (this deployment)

```
UserPoolId: <Logger's World's Cognito user pool id — construct id LoggersWorldUserPoolV2>
Region: <same region as the Turaco Chorus deployment>
AppClientId: <Logger's World's Cognito app client id — construct id LoggersWorldUserPoolClientV2>
UserIdClaim: "sub"
```
