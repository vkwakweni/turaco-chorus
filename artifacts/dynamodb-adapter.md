---
title: DynamoDB Adapter Configuration
last-updated: 2026-08-26
---

# DynamoDB Adapter Configuration

Design for `DynamoDbLogDataSource`, the concrete adapter behind `ILogDataSource` (see `domain-interfaces-and-objects.md`).

Same pattern as `CognitoIdentityVerifier` (see `cognito-adapter.md`): generic from the start, config-driven per installer, no adapter code changes needed to point at a new table. Genericity extends further here, to the DynamoDB item shape itself — any installer's table maps to `AggregateStats` purely through configuration.

## Scope

- One Turaco Chorus deployment per installer, configured at deploy time — not runtime multi-tenancy.
- The installer's table lives in the same AWS account as the Turaco Chorus deployment; access is via a least-privilege IAM role scoped to the table ARN(s), not credentials in config.
- Targets DynamoDB's common single-table pattern (partition key + optional sort key, flat top-level attributes) — no support for arbitrary GSIs or multi-condition filters.
- Query efficiency is bounded: entries are fetched by partition key (+ optional sort-key prefix), then filtered/bucketed in application code, not via a native range query. Acceptable at this project's scale.

## Configuration schema

```
DynamoDbLogDataSourceOptions
├── TableName                      — the installer's DynamoDB table name (see "Table name configuration" below)
├── PartitionKeyAttribute          — e.g. "PK"
├── PartitionKeyValueTemplate      — e.g. "USER#{sourceId}"
├── DateAttribute                  — e.g. "createdAt" (ISO-8601); used only for from/to range filtering
├── Dimensions                     — list of installer-defined output dimensions, may be empty (total-only stats):
│     └── { Name, Source } where Source is one of:
│           ├── DirectAttribute    { AttributeName }
│           └── Lookup             { IdAttributeName, LookupPartitionKeyValueTemplate, LookupSortKeyValueTemplate, LookupNameAttribute, LookupTableName? }
├── SortKeyAttribute?              — optional; only needed if entries share a partition with other item types
└── EntrySortKeyPrefix?            — optional; isolates entry items from other item types in that partition
```

- No dimension is built in, not even date-bucketing — `AggregateStats.dimensions` is entirely defined by `Dimensions`.
  - An installer wanting date-bucketed output just configures a dimension pointing at the same attribute as `DateAttribute`.
- `DateAttribute` is separate and required regardless: it's a port-level concern (`from`/`to` range filtering), structurally unrelated to which dimensions get reported.
- `LookupTableName` defaults to `TableName` when omitted — set it only when a dimension's definition items live in a separate table from entries.
  - E.g. an installer whose `category` names come from a shared, global `ProductCatalog` table (one row per category, reused across every user) rather than a per-user item colocated with entries in the main table.
- No `Region` field: the adapter is constructed with an already-configured `IAmazonDynamoDB` client, region-bound wherever that client is set up (see `README.md`'s "Configuration" section) — same "inject shared infrastructure, don't duplicate its configuration" rule the Cognito/Claude/Gemini adapters follow for their own clients.

## Table name configuration

`TableName` is a plain configuration value (`DynamoDb:LogData:TableName`), read directly from `IConfiguration` by `DynamoDbLogDataSourceOptionsReader` (`src/TuracoChorus/Configuration/`) — supplied via user secrets locally, or the deployment's own environment variables/secrets manager elsewhere, same as every other adapter setting.

No SSM Parameter Store, no CDK cross-stack reference: a genuine third-party installer has no coordinated access to the upstream application's own CDK stack, so Turaco Chorus never tries to look the table name up itself — the installer just supplies it, the same way they supply the IAM role's ARN. A tighter, SSM-based cross-stack version of this — worth it only when the same operator controls both stacks — is deferred; see `roadmap.md`'s "Later / Further Development".

## Read access: IAM policy

Every request issues one `Query` against one partition (the caller's `sourceId`, via `PartitionKeyValueTemplate`). That query returns entry items, plus lookup items for any colocated dimension (see "Single-query optimization" below). The IAM policy grants exactly that: least-privilege, read-only, scoped to the table ARN(s) a given installer's config actually references:

```
Effect: Allow
Action: dynamodb:Query
Resource: arn:aws:dynamodb:<region>:<account>:table/<physical table name>
```

- `dynamodb:Query` only
  - not `GetItem` (a whole-partition `Query` already returns everything needed, see below)
  - not `Scan` (would read every partition, not just the one the adapter has a key for; excluding it means a bug that skips the key condition fails outright rather than leaking other users' data)
  - no write actions (`ILogDataSource` never writes)
- If an installer configures a separate `LookupTableName` for one or more dimensions, the policy grants `Query` on that table's ARN too — scoped only to what's actually referenced.

### Single-query optimization for colocated lookups

Applies when a `Lookup`-sourced dimension's `LookupTableName` equals `TableName` and its lookup partition key matches the entry partition key template — e.g. a `category` dimension whose `TYPE#`/`ENTRY#` items share a partition.

- One `Query` with no sort-key condition returns entries and that dimension's lookup items together, resolving the N+1 risk below without caching.
- The adapter splits the result by prefix: `EntrySortKeyPrefix` for entries, and — per colocated dimension — everything before the first `{` in its `LookupSortKeyValueTemplate` (e.g. `"TYPE#"` from `"TYPE#{typeId}"`) for its lookup items.
  - This has to be an explicit adapter rule: a non-colocated dimension's lookup items won't appear in the same query result, and the adapter falls back to per-id fetches for those.

## Known limitations

- Dimension-lookup N+1: resolved for colocated lookups (above); still open for a non-colocated dimension (separate `LookupTableName` or a differing partition key) — needs caching or `BatchGetItem` before it's production-shaped. Deferred to implementation.
- Query is bounded to partition key + optional sort-key prefix; large per-user histories are filtered/bucketed in application code, not natively by DynamoDB. Acceptable at this project's scale.
- Config schema targets the common single-table pattern; installers needing GSIs or more complex filtering aren't supported without extending it.
