---
title: DynamoDB Adapter Configuration
last-updated: 2026-08-18
---

# DynamoDB Adapter Configuration

Design for `DynamoDbLogDataSource`, the concrete adapter behind `ILogDataSource` (see `domain-interfaces-and-objects.md`).

- Same pattern as `CognitoIdentityVerifier` (see `cognito-adapter.md`): built generic from the start via configuration, not hardcoded per installer.
- Here genericity extends further, to the DynamoDB item shape itself:
  - Any installer's table maps to `AggregateStats` purely through configuration.
  - No adapter code changes required per installer.

## Scope

- Configuration is supplied at deploy time (static), one Turaco Chorus deployment per installer — not a runtime multi-tenant registration.
- The installer's table must live in the same AWS account as the Turaco Chorus deployment; access is granted via a least-privilege IAM role scoped to the table ARN(s), not credentials passed in config.
- Targets DynamoDB's common single-table pattern: partition key (+ optional sort key), flat top-level attributes. Doesn't attempt to support arbitrary GSIs or multi-condition filters.
- Query efficiency is bounded: entries are fetched by partition key (+ optional sort-key prefix), then filtered by date range and bucketed by dimension in application code, not via a native DynamoDB range query. Acceptable at this project's scale.

## Configuration schema

- No dimension is built in — not even date-bucketing.
- `AggregateStats.dimensions` is entirely defined by the `Dimensions` list below.
  - An installer wanting date-bucketed output configures a dimension whose `Source` points at the same attribute as `DateAttribute` — that's just one configured dimension among any others, not a distinct mechanism.
- `DateAttribute` itself stays a separate, required, fixed-purpose field:
  - Used only for `from`/`to` query-range filtering — a port-level concern.
  - Structurally unrelated to which dimensions get reported back.

```
DynamoDbLogDataSourceOptions
├── TableName                      — the installer's DynamoDB table name
├── Region                         — AWS region
├── PartitionKeyAttribute          — e.g. "PK"
├── PartitionKeyValueTemplate      — e.g. "USER#{sourceId}"
├── SortKeyAttribute               — optional; only needed if entries share a partition with other item types
├── EntrySortKeyPrefix             — optional; isolates entry items from other item types in that partition
├── DateAttribute                  — e.g. "createdAt" (ISO-8601); used only for from/to range filtering
└── Dimensions                     — list of installer-defined output dimensions, may be empty (total-only stats):
      └── { Name, Source } where Source is one of:
            ├── DirectAttribute    { AttributeName }
            └── Lookup             { IdAttributeName, LookupTableName, LookupPartitionKeyValueTemplate, LookupSortKeyValueTemplate, LookupNameAttribute }
```

- `LookupTableName` is optional per dimension and defaults to `TableName` when omitted.
  - A dimension whose definition items are colocated with entries (as Logger's World's category definitions are) never sets it.
  - A dimension using a separate table for its definitions sets it explicitly, with no change to the adapter itself.

## Logger's World configuration (this deployment)

```
TableName: <physical name of the LoggersWorldTable construct — see roadmap.md/tech-stack.md for how this reaches config via SSM Parameter Store>
PartitionKeyAttribute: "PK"
PartitionKeyValueTemplate: "USER#{sourceId}"
SortKeyAttribute: "SK"
EntrySortKeyPrefix: "ENTRY#"
DateAttribute: "createdAt"
Dimensions:
  - Name: "category"
    Source: Lookup {
      IdAttributeName: "typeId"
      LookupPartitionKeyValueTemplate: "USER#{sourceId}"
      LookupSortKeyValueTemplate: "TYPE#{typeId}"
      LookupNameAttribute: "name"
    }
  - Name: "date"
    Source: DirectAttribute { AttributeName: "createdAt" }
```

## Read access: IAM policy and cross-repo table discovery

**IAM policy** — least-privilege, read-only, scoped to the specific table ARN(s) actually used by a given installer's config:

```
Effect: Allow
Action: dynamodb:Query
Resource: arn:aws:dynamodb:<region>:<account>:table/<physical table name>
```

- Only `dynamodb:Query` is granted:
  - Not `GetItem` — unnecessary once a whole-partition `Query` already returns everything needed (see below).
  - Not `Scan` — would allow reading every partition in the table, not just the one the adapter has a key for.
    - Unnecessary since `sourceId` always determines the partition key upfront.
    - Excluding it means a bug that skips the key condition fails outright rather than leaking other users' data.
  - No write actions — `ILogDataSource` never writes.
- If a future installer configures a separate `LookupTableName` for one or more dimensions, the policy grants `Query` on that table's ARN too — scoped only to what that installer's config actually references, not a blanket grant.

**Single-query optimization for colocated lookups**

- Applies to each `Lookup`-sourced dimension whose `LookupTableName` equals `TableName` and whose lookup partition key matches the entry partition key template.
  - True for Logger's World's `category` dimension: both `TYPE#` and `ENTRY#` items share `PK = USER#<ownerId>`.
- One `Query` with no sort-key condition returns entries and that dimension's lookup items together in a single read — resolving the N+1 risk noted below without caching.
- The adapter splits the batch:
  - `EntrySortKeyPrefix` for entry items.
  - Per colocated `Lookup`-sourced dimension, a prefix derived from that dimension's `LookupSortKeyValueTemplate` (everything before its first `{`, e.g. `"TYPE#"` from `"TYPE#{typeId}"`) for its lookup items.
- This derivation needs to be an explicit adapter rule, not an assumption:
  - A non-colocated dimension's lookup items won't appear in the same query result at all.
  - The adapter falls back to per-id fetches for that dimension.

**Cross-repo table discovery (SSM Parameter Store)**

- Turaco Chorus's CDK stack doesn't know Logger's World's physical table name at authoring time.
  - Only the CDK construct id `LoggersWorldTable` is known; the physical name is CloudFormation-generated.
- The bridge:
  1. Logger's World's stack (a change to that repo) publishes the table's physical name to a fixed SSM path, e.g. `/loggers-world/table-name`, via an `ssm.StringParameter` construct.
  2. Turaco Chorus's stack reads it with `ssm.StringParameter.valueForStringParameter(...)` — a CloudFormation dynamic reference, resolved fresh on every Turaco Chorus deploy, not a literal value baked in at `cdk synth` time.
  3. That resolved name is used twice:
     - To build the IAM policy's `Resource` ARN above.
     - To set a plain environment variable on the ECS task definition (e.g. `TURACO_LOGDATA_TABLE_NAME`) — mirroring how Logger's World's own stack already passes `TABLE_NAME` to its Lambda (`infra-stack.ts:38`), just crossing a stack boundary via SSM instead of an in-process CDK reference.
  4. At runtime, the .NET service only reads that environment variable — no SSM calls from the running service itself; DynamoDB access is authenticated via the ECS task role, picked up automatically by the AWS SDK.
- This creates a real **deploy-ordering dependency**: Logger's World must deploy (with its SSM parameter already written) before Turaco Chorus deploys or redeploys, since Turaco Chorus's stack reads that parameter at its own deploy time.
  - Not a code dependency, but a deployment-sequencing one between two otherwise-independent repos.

## Known limitations

- Dimension-lookup N+1 risk:
  - Resolved for colocated lookups (see the single-query optimization above).
  - Still open for a non-colocated dimension (separate `LookupTableName` or a differing partition key template) — needs caching or a batched fetch (e.g. `BatchGetItem`) before it's production-shaped. Deferred to implementation.
- Query is bounded to partition key + optional sort-key prefix; large per-user histories are filtered and bucketed in application code, not natively by DynamoDB. Acceptable at this project's scale.
- Config schema targets the common single-table pattern; installers needing GSIs or more complex filtering aren't supported without extending this schema later.
