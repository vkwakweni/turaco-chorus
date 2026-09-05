<img src="assets/logo.svg" alt="Turaco Chorus logo" width="96" height="96">

# Turaco Chorus

Turaco Chorus is a .NET 8 microservice that lets users ask natural-language questions about their aggregated data, answered by either the Anthropic Claude API or Google's Gemini API, grounded only in aggregated statistics, never raw entry text. This was built as a live demonstration of the Ethics by Design framework: consent, data minimisation, and audit logging aren't bolted on after the AI feature works, but instead they're the requirements the feature is built to satisfy

Named after the colourful Knysna Turaco, a social bird with a piercing alarm call for warning other animals of danger, Turaco Chorus plays on the idea that this service can report back on the state of aggregated data. Turacos can also come in many colours, and similarly Turaco Chorus uses a ports-and-adapters architecture, so that upstream applications may select the adapter that works best for them, with the core functionality remaining the same.

Full design docs live under:
- [`artifacts/`](artifacts/): [`domain-interfaces-and-objects.md`](artifacts/domain-interfaces-and-objects.md) (the five ports and domain objects)
- [`interaction-flows.md`](artifacts/interaction-flows.md) (call sequences per endpoint)
- [`api-contract.md`](artifacts/api-contract.md) (the HTTP contract)
- [`ethics-by-design.md`](artifacts/ethics-by-design.md) (the EbD-AI requirements)
- [`tech-stack.md`](artifacts/tech-stack.md) (technology choices, split core vs. adapters)
- [`ecs-deployment.md`](artifacts/ecs-deployment.md) (how it's actually deployed)
- and [`roadmap.md`](artifacts/roadmap.md) (the phased build plan).

## Installation

Turaco Chorus is installed by whoever runs the upstream application it reads from: you point it at your own identity provider, your own data source, and an AI provider key, and it answers questions grounded in your own data. There are five stages below, roughly in the order you'd move through them:

1. **Running the container**: the actual install-and-run story, `docker build`/`docker run`, passing your configured values as environment variables, on any host.
2. **Wiring it into your app**: what your own app actually needs to do to use it once it's running — spoiler: no code changes, just an HTTP call using a token you already have.
3. **Configuration**: look here when you're ready to replace the example `docker run` values from "Running the container" with your own — the full reference for every value the service accepts (identity provider, your data source, the AI provider key), what's required, and what each one means.
4. **Local development**: run the service against in-memory fakes instead, no AWS account or credentials of any kind needed.
    - Use this to explore the API or make code changes without touching real infrastructure.
5. **Deploying to AWS**: how this project's own instance happens to be hosted
    - One option among many, not a requirement of the image itself.

Each stage is independent; skip ahead if you already know which one you need.

### Running the container

However you host it, the service is a plain Docker image built from the repo's `Dockerfile`:

1. Build the image:

   ```bash
   docker build -t turaco-chorus .
   ```

2. Run it. The full command needs two kinds of value together — build both up before running anything:
    - **Application config**: one `-e` flag per key documented in ["Configuration"](#configuration-wiring-in-real-adapters) below. Configuration lists each key with a `:` separator (e.g. `Cognito:UserPoolId`); as an environment variable, write it with `__` (double underscore) instead (`Cognito__UserPoolId`), since that's the separator ASP.NET Core's environment-variable provider expects.
    - **AWS credentials** for the DynamoDB/Cognito calls: infrastructure identity, not application config, so it's not in the Configuration reference at all. Either your keys, or your local credentials file mounted in:
      ```bash
      -e AWS_ACCESS_KEY_ID="<your-access-key-id>" \
      -e AWS_SECRET_ACCESS_KEY="<your-secret-access-key>" \
      -e AWS_REGION="us-east-1" \
      ```
      ```bash
      -v ~/.aws:/root/.aws:ro \
      ```
      Skip this part entirely if the container will run on AWS compute with an IAM role already attached (an EC2 instance profile, or an ECS task role) — the AWS SDK inside picks that up automatically. This project's own deployment does exactly that; see `taskDefinition.taskRole` in `infra/lib/compute-stack.ts` for a real, working example.

   Both kinds of value go into the same command:

   ```bash
   docker run -p 8080:8080 \
     -e Aws__Region="us-east-1" \
     -e InsightProvider="Gemini" \
     -e Cognito__UserPoolId="us-east-1_ExAmPle123" \
     -e Cognito__Region="us-east-1" \
     -e Cognito__AppClientId="1example23456789abcdefghij" \
     -e Cognito__TokenType="AccessToken" \
     -e DynamoDb__LogData__TableName="ClarrikerHabitEntries" \
     -e DynamoDb__LogData__PartitionKeyAttribute="PK" \
     -e DynamoDb__LogData__PartitionKeyValueTemplate="USER#{sourceId}" \
     -e DynamoDb__LogData__DateAttribute="loggedAt" \
     -e DynamoDb__Consent__TableName="ClarrikerConsent" \
     -e DynamoDb__Audit__TableName="ClarrikerAskAudit" \
     -e Gemini__ApiKey="<your-gemini-api-key>" \
     -e AWS_ACCESS_KEY_ID="<your-access-key-id>" \
     -e AWS_SECRET_ACCESS_KEY="<your-secret-access-key>" \
     -e AWS_REGION="us-east-1" \
     turaco-chorus
   ```

   Skip this step entirely if the container runs on AWS compute with an IAM role already attached (an EC2 instance profile, or an ECS task role) — the AWS SDK inside picks that up automatically, with nothing to pass in. This project's own deployment does exactly that; see `taskDefinition.taskRole` in `infra/lib/compute-stack.ts` for a real, working example.

### Wiring it into your app

Once it's running against your own Cognito pool, there's nothing to install on the upstream side. Turaco Chorus verifies the exact same JWT your app already issues to its users — it doesn't have its own login, its own users, or its own token format — so any client already holding one of your users' tokens can call it directly:

```bash
curl -H "Authorization: Bearer <the same token your app already gave this user>" \
  http://<wherever-you-run-it>/stats
```

In practice this means calling `/stats`, `/ask`, and `/consent` straight from wherever you want the feature to surface — your own frontend is the natural place (a settings toggle for `/consent`, a chat-style panel for `/ask`), but there's no requirement to go through it; a `curl` from anywhere holding a valid token works exactly the same. Nothing stops you from proxying it through your own backend instead, if you'd rather your client only ever talk to one origin.

For example, a React frontend that already has an `AuthContext` handing out its own Cognito access token — the same one it uses to call its own backend — needs nothing more than this to call each route. A shared internal helper attaches the token; one small exported function per route wraps it:

```tsx
const BASE_URL = "https://turacochorus.your-domain.com";

async function callTuracoChorus(path: string, getAccessToken: () => Promise<string>, init: RequestInit = {}) {
  const token = await getAccessToken(); // however your app already does this
  const response = await fetch(`${BASE_URL}${path}`, {
    ...init,
    headers: { ...init.headers, Authorization: `Bearer ${token}` },
  });

  if (!response.ok) {
    throw new Error(`Turaco Chorus returned ${response.status}`);
  }

  return response.json();
}

// GET /stats — aggregated totals for the current user, no consent required.
export function getStats(getAccessToken: () => Promise<string>) {
  return callTuracoChorus("/stats", getAccessToken);
  // { range: { from, to }, totalEntries, dimensions: [{ name, buckets: [{ value, count }] }] }
}

// POST /ask — a natural-language question, answered from those same aggregates; requires consent, or a 403.
export function askQuestion(getAccessToken: () => Promise<string>, question: string) {
  return callTuracoChorus("/ask", getAccessToken, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question }),
  });
  // { answer, dataUsed: { statsQueried, range } }
}

// GET /consent — whether the current user has opted in, and when.
export function getConsent(getAccessToken: () => Promise<string>) {
  return callTuracoChorus("/consent", getAccessToken);
  // { granted, grantedAt }
}

// PUT /consent — grant or revoke; takes effect immediately for the next /ask call.
export function setConsent(getAccessToken: () => Promise<string>, granted: boolean) {
  return callTuracoChorus("/consent", getAccessToken, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ granted }),
  });
  // { granted, grantedAt }
}
```

No new auth flow, no new token, no shared secret between the two apps — `getAccessToken` is whatever your app already calls before hitting its own API.

One thing to get right: `Cognito:TokenType` (see Configuration below) has to match whichever token type your existing clients already hold — `AccessToken` if they call your own backend with an access token, `IdToken` if they use the id token instead. Get this wrong and every call fails with `401`, since Turaco Chorus is validating the wrong claim set entirely.

Another: calling it directly from browser JS, like the example above does, needs `AllowedOrigins` (see Configuration below) set to your frontend's own origin. Without it, the request fails in the browser as a plain "failed to fetch" — no CORS policy is added by default, so nothing but a same-origin call (or a server-to-server call, e.g. proxying through your own backend) works out of the box.

### Configuration (wiring in real adapters)

Every installation-specific value — your AWS account, your Cognito pool, your own upstream data table, your AI provider key — is read from ASP.NET Core configuration (`IConfiguration`).

At startup, a small hand-written reader per adapter (`src/TuracoChorus/Configuration/`) reads these keys directly and throws immediately, naming the exact missing or malformed key, if anything required is absent. The app will not start silently misconfigured.

- `UseFakeAdapters` (bool, optional, default `false`): set `true` to run entirely against in-memory fakes (no AWS account, Cognito pool, DynamoDB table, or AI key needed). Every key below is ignored in that mode. See "Local development" below.

#### Host-level

| Key | Type | Required | Notes |
|---|---|---|---|
| `Aws:Region` | string | yes* | Region for the shared DynamoDB client, e.g. `us-east-1` |
| `InsightProvider` | `Claude` \| `Gemini` | yes* | Selects the registered `IInsightEngine`; only that provider's API key needs a real value |
| `AllowedOrigins` | comma-separated string | no | Origins allowed to call this API directly from browser JS, e.g. `https://app.example.com,http://localhost:5173`. Only needed if a frontend calls it directly rather than through your own backend — see "Wiring it into your app" above. Unset means no CORS policy at all, not "allow everything" |

\* unless `UseFakeAdapters` is `true`

#### `IIdentityVerifier` — Amazon Cognito

| Key | Type | Required | Notes |
|---|---|---|---|
| `Cognito:UserPoolId` | string | yes | |
| `Cognito:Region` | string | yes | |
| `Cognito:AppClientId` | string | yes | |
| `Cognito:TokenType` | `IdToken` \| `AccessToken` | yes | Which token type your clients present |
| `Cognito:UserIdClaim` | string | no | default `sub` |

#### `ILogDataSource` — DynamoDB (your own upstream table)

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

#### `IConsentStore` / `IAuditLogger` — DynamoDB (owned by this service)

| Key | Type | Required | Notes |
|---|---|---|---|
| `DynamoDb:Consent:TableName` | string | yes | Turaco Chorus owns and creates this table itself (see `artifacts/tech-stack.md`) |
| `DynamoDb:Audit:TableName` | string | yes | Same — append-only audit log |

#### `IInsightEngine` — Claude or Gemini

| Key | Type | Required | Notes |
|---|---|---|---|
| `Claude:ApiKey` | string | only if `InsightProvider` is `Claude` | |
| `Claude:Model` | string | no | default `claude-haiku-4-5` |
| `Claude:BaseUrl` | string | no | default `https://api.anthropic.com` |
| `Gemini:ApiKey` | string | only if `InsightProvider` is `Gemini` | |
| `Gemini:Model` | string | no | default `gemini-3.6-flash` |
| `Gemini:BaseUrl` | string | no | default `https://generativelanguage.googleapis.com` |

#### Worked example

A fictitious deployment against a made-up app ("Clarriker Habit Tracker") — replace every value with your own:

```bash
dotnet user-secrets set "Aws:Region" "us-east-1"
dotnet user-secrets set "InsightProvider" "Gemini"

dotnet user-secrets set "Cognito:UserPoolId" "us-east-1_ExAmPle123"
dotnet user-secrets set "Cognito:Region" "us-east-1"
dotnet user-secrets set "Cognito:AppClientId" "1example23456789abcdefghij"
dotnet user-secrets set "Cognito:TokenType" "AccessToken"

dotnet user-secrets set "DynamoDb:LogData:TableName" "ClarrikerHabitEntries"
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
dotnet user-secrets set "DynamoDb:LogData:Dimensions:1:LookupNameAttribute" "displayName"

dotnet user-secrets set "DynamoDb:Consent:TableName" "ClarrikerConsent"
dotnet user-secrets set "DynamoDb:Audit:TableName" "ClarrikerAskAudit"

dotnet user-secrets set "Gemini:ApiKey" "<your-gemini-api-key>"
```

### Local development (fakes only, no real credentials)

Running the service locally against the Phase 2 fakes requires two [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) values: a demo bearer token and the `userId` it resolves to. With those set, every endpoint can be exercised with `curl` without a real Cognito credential needed. These are never committed to source control; `dotnet user-secrets` stores them in a file outside the repo entirely.

Run once, from `src/TuracoChorus/`:

```bash
dotnet user-secrets init
dotnet user-secrets set "DevSeedData:Token" "<any value>"
dotnet user-secrets set "DevSeedData:UserId" "<any value>"
dotnet user-secrets set "UseFakeAdapters" "true"
```

`DevSeedData` only takes effect in `Debug` builds (see `DevSeedData.cs`) — it's compiled out of `Release` builds entirely, so it can never reach a real deployment. Then:

```bash
curl -H "Authorization: Bearer <the token you set above>" http://localhost:5006/stats
```

### Deploying to AWS

The `docker run` command from "Running the container" is enough to run the container anywhere. This project's own instance happens to be hosted on AWS, with the infrastructure as CDK (TypeScript) in `infra/`: two stacks, `TuracoChorusStack` (the `IConsentStore`/`IAuditLogger` DynamoDB tables Turaco Chorus owns itself) and `TuracoChorusComputeStack` (ECS on a single EC2 instance, Elastic IP, Secrets Manager, DNS). That's this deployment's own choice, not a requirement of the image — full walkthrough, including the free-tier rationale, the two-stack split, and how to safely pause or tear down compute without touching the tables, is in [`artifacts/ecs-deployment.md`](artifacts/ecs-deployment.md).

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

All five ports have real adapters now (see "Configuration" above for how to wire them in) — the
diagram's adapter names are the concrete types, config-driven per installer rather than
per-application.

### Project layout

- [`src/TuracoChorus/`](src/TuracoChorus/) — the .NET 8 Web API: core domain logic, ports, and adapters
- [`infra/`](infra/) — AWS CDK (TypeScript): `TuracoChorusStack` (data) and `TuracoChorusComputeStack` (ECS/EC2 compute) — see "Deploying to AWS" above
- [`artifacts/`](artifacts/) — design docs (see above)

## Status

Phases 1 through 4 (requirements & design, core domain logic, real adapters, containerisation & CI/CD) are complete, deployed, and verified live. Phase 5 (testing, polish, docs) is in progress — see `roadmap.md` for the full checklist.
