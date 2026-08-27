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
          CognitoIdentityVerifier  DynamoDb  DynamoDbLogDataSource  Claude/Gemini adapter
          (Amazon Cognito JWT)     ConsentStore (AWS SDK, read-only) (config-switchable,
                                                                       2 calls/request)
                                                                            │
                                                                   DynamoDbAskAuditLogger
```

All five ports have real adapters now (see "Configuration" below for how to wire them in) — the
diagram's adapter names are the concrete types, config-driven per installer rather than
per-application.

## Project layout

- [`src/TuracoChorus/`](src/TuracoChorus/) — the .NET 8 Web API: core domain logic, ports, and adapters
- [`infra/`](infra/) — AWS CDK (TypeScript): `TuracoChorusStack`, deploying the service to ECS Fargate
- [`artifacts/`](artifacts/) — design docs (see above)

## Local development setup

Running the service locally against the Phase 2 fakes requires two [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) values — a demo bearer token and the `userId` it resolves to — so `/stats` etc. can be exercised with `curl` without any real Cognito credential. These are never committed to source control; `dotnet user-secrets` stores them in a file outside the repo entirely. Run once, from `src/TuracoChorus/`:

```bash
dotnet user-secrets init
dotnet user-secrets set "DevSeedData:Token" "<any value>"
dotnet user-secrets set "DevSeedData:UserId" "<any value>"
```

This only takes effect in `Debug` builds (see `DevSeedData.cs`) — it's compiled out of `Release` builds entirely, so it can never reach a real deployment. As of the real-adapter wiring below, this fakes path also requires one more value — set `UseFakeAdapters` alongside the two above:

```bash
dotnet user-secrets set "UseFakeAdapters" "true"
```

Then:

```bash
curl -H "Authorization: Bearer <the token you set above>" http://localhost:5006/stats
```

## Configuration

Every installation-specific value:
* Your AWS account
* Your Cognito pool
* Your own upstream data table
* your AI provider key

is read from ASP.NET Core configuration (`IConfiguration`)

At startup, a small hand-written reader per adapter (`src/TuracoChorus/Configuration/`) reads
these keys directly and throws immediately, naming the exact missing or malformed key, if
anything required is absent. The app will not start silently misconfigured.

### Running without any real credentials

- `UseFakeAdapters` (bool, optional, default `false`): set `true` to run entirely against  in-memory fakes (no AWS account, Cognito pool, DynamoDB table, or AI key needed). Every key below is ignored in that mode. See "Local development setup" above.

### Host-level

| Key | Type | Required | Notes |
|---|---|---|---|
| `Aws:Region` | string | yes* | Region for the shared DynamoDB client, e.g. `us-east-1` |
| `InsightProvider` | `Claude` \| `Gemini` | yes* | Selects the registered `IInsightEngine`; only that provider's API key needs a real value |

\* unless `UseFakeAdapters` is `true`

### `IIdentityVerifier` — Amazon Cognito

| Key | Type | Required | Notes |
|---|---|---|---|
| `Cognito:UserPoolId` | string | yes | |
| `Cognito:Region` | string | yes | |
| `Cognito:AppClientId` | string | yes | |
| `Cognito:TokenType` | `IdToken` \| `AccessToken` | yes | Which token type your clients present |
| `Cognito:UserIdClaim` | string | no | default `sub` |

### `ILogDataSource` — DynamoDB (your own upstream table)

| Key | Type | Required | Notes |
|---|---|---|---|
| `DynamoDb:LogData:TableName` | string | yes | |
| `DynamoDb:LogData:PartitionKeyAttribute` | string | yes | e.g. `PK` |
| `DynamoDb:LogData:PartitionKeyValueTemplate` | string | yes | e.g. `USER#{sourceId}` |
| `DynamoDb:LogData:DateAttribute` | string | yes | Attribute used for `from`/`to` range filtering |
| `DynamoDb:LogData:SortKeyAttribute` | string | no | Only needed if entries share a partition with other item types |
| `DynamoDb:LogData:EntrySortKeyPrefix` | string | no | Isolates entry items from other item types in that partition |
| `DynamoDb:LogData:Dimensions` | array | no | See below — omit entirely for total-only stats |

Each entry in `Dimensions` is either a `Direct` (read a bucket value straight off an attribute) or
a `Lookup` (resolve an id to a display name via another item) dimension, picked by its `Type`:

```json
"DynamoDb": {
  "LogData": {
    "Dimensions": [
      { "Name": "day", "Type": "Direct", "AttributeName": "loggedAt" },
      {
        "Name": "category", "Type": "Lookup",
        "IdAttributeName": "habitTypeId",
        "LookupPartitionKeyValueTemplate": "USER#{sourceId}",
        "LookupSortKeyValueTemplate": "HABITTYPE#{habitTypeId}",
        "LookupNameAttribute": "displayName"
      }
    ]
  }
}
```

(This JSON is illustrative only — see the worked example below for how to actually set nested
values like this via `dotnet user-secrets`.) `LookupSource` also accepts an optional
`LookupTableName`, defaulting to `DynamoDb:LogData:TableName` when the lookup items are colocated
with entries in the same table.

### `IConsentStore` / `IAuditLogger` — DynamoDB (owned by this service)

| Key | Type | Required | Notes |
|---|---|---|---|
| `DynamoDb:Consent:TableName` | string | yes | Turaco Chorus owns and creates this table itself (see `artifacts/tech-stack.md`) |
| `DynamoDb:Audit:TableName` | string | yes | Same — append-only audit log |

### `IInsightEngine` — Claude or Gemini

| Key | Type | Required | Notes |
|---|---|---|---|
| `Claude:ApiKey` | string | only if `InsightProvider` is `Claude` | |
| `Claude:Model` | string | no | default `claude-haiku-4-5` |
| `Claude:BaseUrl` | string | no | default `https://api.anthropic.com` |
| `Gemini:ApiKey` | string | only if `InsightProvider` is `Gemini` | |
| `Gemini:Model` | string | no | default `gemini-3.6-flash` |
| `Gemini:BaseUrl` | string | no | default `https://generativelanguage.googleapis.com` |

### Worked example

A fictitious deployment against a made-up app ("Acme Habit Tracker") — replace every value with
your own:

```bash
dotnet user-secrets set "Aws:Region" "us-east-1"
dotnet user-secrets set "InsightProvider" "Gemini"

dotnet user-secrets set "Cognito:UserPoolId" "us-east-1_ExAmPle123"
dotnet user-secrets set "Cognito:Region" "us-east-1"
dotnet user-secrets set "Cognito:AppClientId" "1example23456789abcdefghij"
dotnet user-secrets set "Cognito:TokenType" "AccessToken"

dotnet user-secrets set "DynamoDb:LogData:TableName" "AcmeHabitEntries"
dotnet user-secrets set "DynamoDb:LogData:PartitionKeyAttribute" "PK"
dotnet user-secrets set "DynamoDb:LogData:PartitionKeyValueTemplate" "USER#{sourceId}"
dotnet user-secrets set "DynamoDb:LogData:SortKeyAttribute" "SK"
dotnet user-secrets set "DynamoDb:LogData:EntrySortKeyPrefix" "ENTRY#"
dotnet user-secrets set "DynamoDb:LogData:DateAttribute" "loggedAt"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:0:Name" "day"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:0:Type" "Direct"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:0:AttributeName" "loggedAt"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:Name" "category"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:Type" "Lookup"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:IdAttributeName" "habitTypeId"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:LookupPartitionKeyValueTemplate" "USER#{sourceId}"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:LookupSortKeyValueTemplate" "HABITTYPE#{habitTypeId}"
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:LookupNameAttribute" "displayName"

dotnet user-secrets set "DynamoDb:Consent:TableName" "AcmeConsent"
dotnet user-secrets set "DynamoDb:Audit:TableName" "AcmeAskAudit"

dotnet user-secrets set "Gemini:ApiKey" "<your-gemini-api-key>"
```

## Status

Phase 1 (requirements & design) and Phase 2 (core domain & application logic) complete. Phase 3 (adapters, integration & Ethics-by-Design enforcement) in progress — all five adapters are implemented and DI-wired behind config; adapter-level integration tests against real infrastructure are next — see `roadmap.md`.

