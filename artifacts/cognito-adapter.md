---
title: Cognito Adapter Configuration
last-updated: 2026-08-26
---

# Cognito Adapter Configuration

Design for `CognitoIdentityVerifier`, the concrete adapter behind `IIdentityVerifier` (see `domain-interfaces-and-objects.md`).

Same pattern as `DynamoDbLogDataSource` (see `dynamodb-adapter.md`): config-driven per installer, one adapter reusable across installers, no installer-specific code.

## Scope

- Configuration is supplied at deploy time (static), one Turaco Chorus deployment per installer.
- The adapter speaks Cognito's JWT/JWKS format; which pool it verifies against is config-driven (`Region` + `UserPoolId`, from which the JWKS endpoint is derived via Cognito's standard URL pattern — not separately configured).

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

Cognito issues two token types per login. `TokenType` tells the adapter which one an installer's frontend forwards as the Bearer token, and changes what gets checked:

| | ID token | Access token |
|---|---|---|
| **Purpose** | Identity assertion | Authorization scope |
| **`AppClientId` validated against** | `aud` claim | `client_id` claim |
| **Expected `token_use` claim** | `"id"` | `"access"` |
| **Profile claims** (e.g. `email`) | Present, if requested scopes include them | Absent |

The adapter checks two things against the configured `TokenType`: the audience-equivalent claim (against `AppClientId`) and the token's own `token_use` claim. A token of the wrong type is rejected outright — it can't silently pass, or fail the audience check for an unrelated reason.

`UserIdClaim` is constrained by this choice: `sub` (the default) is present on both token types, but a profile claim like `email` only exists on ID tokens — so setting `UserIdClaim` to a profile claim only works when `TokenType` is `IdToken`. This isn't cross-validated at startup; see Known limitations.

## Known limitations

- Only supports Amazon Cognito — an installer using a different identity provider (Auth0, Okta, a generic OIDC provider, API keys) needs a new `IIdentityVerifier` implementation; not built yet.
- Setting `UserIdClaim` to a non-`sub` value against an `AccessToken`-configured deployment fails at request time (the claim is simply absent) rather than being caught at configuration/startup time — no cross-field validation between `TokenType` and `UserIdClaim` exists yet.
