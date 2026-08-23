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
├── AppClientId         — validated against the audience-equivalent claim (see TokenType)
├── TokenType            — "IdToken" | "AccessToken"; see below
└── UserIdClaim          — which JWT claim becomes `userId` (defaults to "sub")
```

## Token type and audience validation

Cognito issues two different token types per login, and which one an installer's frontend forwards as the Bearer token changes how validation works:

- **ID tokens** — identity-assertion tokens; carry an `aud` claim equal to the app client id, plus profile claims (e.g. `email`) depending on requested scopes.
- **Access tokens** — authorization-scoped tokens; carry `client_id` instead of `aud`, and no profile claims.

`TokenType` determines two things:

- Which claim `AppClientId` is validated against — `aud` for `IdToken`, `client_id` for `AccessToken`.
- The adapter also validates the token's own `token_use` claim ("id" or "access") matches the configured `TokenType` — this rejects a token of the wrong type outright, rather than letting a mismatched token silently pass or fail the audience check for an unrelated reason.

`UserIdClaim` interacts with `TokenType`: `sub` (the default) is present on both token types, but profile claims like `email` are only present on ID tokens — configuring `UserIdClaim` to a profile claim implicitly requires `TokenType: IdToken`. This isn't cross-validated at startup; see Known limitations.

## Known limitations

- Setting `UserIdClaim` to a non-`sub` value against an `AccessToken`-configured deployment fails at request time (the claim is simply absent) rather than being caught at configuration/startup time — no cross-field validation between `TokenType` and `UserIdClaim` exists yet.

## Logger's World configuration (this deployment)

```
UserPoolId: <Logger's World's Cognito user pool id — construct id LoggersWorldUserPoolV2>
Region: <same region as the Turaco Chorus deployment>
AppClientId: <Logger's World's Cognito app client id — construct id LoggersWorldUserPoolClientV2>
TokenType: IdToken
UserIdClaim: "sub"
```
