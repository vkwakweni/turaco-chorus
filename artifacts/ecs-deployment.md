---
title: ECS Deployment (EC2 Launch Type)
last-updated: 2026-09-02
---

# ECS Deployment (EC2 Launch Type)

Design for Phase 4's compute deploy: how the containerised service actually runs in AWS, and how the AI provider API key reaches it.

## Scope and decision

- Launch type: **ECS on EC2**, not Fargate.
Fargate has no AWS free tier and bills per vCPU/memory-second continuously while the service runs.
EC2 does have a free tier — 750 hrs/month of `t3.micro` — but only for an account's first 12 months.
Confirmed via the account's welcome email: this account's free tier runs through **19 Jan 2027**.
- Single instance, no load balancer: a `t3.micro` Auto Scaling Group fixed at size 1, task placed on it via a capacity provider.
The task gets a public IP directly (no ALB) — cheapest option, at the cost of no HTTPS and no health-check-based restarts.
A stable **Elastic IP** and a **real subdomain** (below) close the "looks like a demo" gap without adding an ALB; HTTPS stays out of scope for this pass.
- Networking: the account's **default VPC**, looked up rather than created, so this stack never provisions a NAT gateway or any other new VPC spend.
The instance sits in a public subnet with a public IP, reaching Cognito/DynamoDB/the AI provider directly over the internet — no VPC endpoints needed at this scale.

## Two-stack split

`infra/lib/infra-stack.ts` (`TuracoChorusStack`) currently holds both DynamoDB tables (`TuracoChorusConsent`, `TuracoChorusAskAudit`), both still on `RemovalPolicy.DESTROY` — that flip to `RETAIN` is a known, not-yet-done Phase 5 item.
Adding compute resources into that same stack would mean a `cdk destroy` aimed at tearing down the EC2 side also deletes real consent/audit data already exercised against live infrastructure.

Compute lives in its own stack instead: **`TuracoChorusComputeStack`** (`infra/lib/compute-stack.ts`).
This keeps "tear down compute" and "tear down data" permanently independent, not just for this one deploy.

## Compute stack contents

- VPC: `ec2.Vpc.fromLookup(..., { isDefault: true })`.
- `ecs.Cluster` over that VPC.
- One-instance Auto Scaling Group via `cluster.addCapacity()`: `t3.micro`, `EcsOptimizedImage.amazonLinux2()`, public subnet, `associatePublicIpAddress: true`, min/max/desired all `1`. `addCapacity()` creates the ASG and registers it with the cluster (as a managed capacity provider) in one call — no need to wire an `AsgCapacityProvider` by hand.
- `Ec2TaskDefinition` pulling the existing `turaco-chorus` ECR repository (from `github-oidc-stack.ts`) by tag `latest`.
- Container port mapping: host `80` → container `8080` (the .NET 8 container image's default HTTP port). Host `80` rather than `8080` so the real URL has no port number in it — `http://turaco.literaturelounge.org`, not `:8080` appended.
- Security group: inbound `80` from the deployer's own IP only (temporary, while identity verification is fake — see "Per-port fake/real split" below); egress open (default).
- Task role: least-privilege on the three DynamoDB tables the service actually uses — `dynamodb:Query` only on the log data table(s) (read-only, matching `dynamodb-adapter.md`'s documented IAM policy exactly), full read/write on consent and audit (owned by `TuracoChorusStack`). Where each table name comes from is covered next.

## Per-port fake/real split

This deployment is deliberately **not** wired to any specific upstream application. `AdapterRegistration.cs` previously only supported all-fake (`UseFakeAdapters`, local dev) or all-real; it now also supports `UseFakeIdentityVerifier`/`UseFakeLogDataSource` independently — this deploy sets both `true`, so:

- `IIdentityVerifier` and `ILogDataSource` run as their in-memory fakes — no real Cognito pool, no real upstream DynamoDB table, anywhere in this deployment.
- `IConsentStore`, `IAuditLogger`, `IInsightEngine` stay real — Turaco Chorus's own DynamoDB tables and a real Gemini/Claude call. This is what Phase 4's two checklist items actually needed to prove: the container runs on ECS/EC2, and the Secrets-Manager-injected API key genuinely gets used.
- `Cognito:*`/`DynamoDb:LogData:*` are excluded from the container's environment entirely (not merely unused) while these flags are `true` — see `toContainerEnvironment`'s `excludePrefixes` — and the log-data `dynamodb:Query` IAM grant is skipped outright, so this deployment holds no IAM permission on any real upstream table either.

**Making the fake identity verifier actually usable**: `FakeIdentityVerifier` starts with an empty credential registry, and the only thing that ever seeds it (`DevSeedData.cs`) is wrapped in `#if DEBUG` — stripped out of the `dotnet publish -c Release` build this Dockerfile produces. So without more, the deployed container would reject every request, including the deployer's own. `PartialFakeSeedData.cs` (new, not DEBUG-gated) registers exactly one test credential/user pair at startup, and seeds `FakeLogDataSource` with a plausible stats fixture for that user — the minimum needed to actually exercise `/stats`/`/ask`/`/consent` against this deployment.

**The credential itself**: a dedicated Secrets Manager secret (`FakeAuthTestCredential`) with a CDK-generated random 32-character value — not the `"dev-token"` literal already sitting in git/session history, which would otherwise be a public, guessable bearer token. Retrieve it after deploy:
```
aws secretsmanager get-secret-value --secret-id <FakeAuthTestCredentialSecretArnOutput> --query SecretString --output text --region af-south-1
```
Use it as `Authorization: Bearer <value>`; the test user id is the fixed constant `demo-user`.

**Network-level backstop**: since a fake identity verifier is a known single credential rather than real per-user verification, the security group's inbound rule is temporarily restricted to the deployer's own IP (`TEMPORARY_ALLOWED_INGRESS_CIDR`) instead of `0.0.0.0/0` — belt-and-suspenders on top of the credential itself being unguessable. Widen back to `ec2.Peer.anyIpv4()` once `USE_FAKE_IDENTITY_VERIFIER` flips to `false` alongside a real Cognito pool.

**Reversing this later**: flipping `USE_FAKE_IDENTITY_VERIFIER`/`USE_FAKE_LOG_DATA_SOURCE` to `false` (once there's a real upstream worth wiring in, Logger's World or otherwise) automatically restores the excluded Cognito/LogData config and the IAM grant — nothing else in the stack needs to change.

## Installer config (Cognito, upstream table shape)

The values below are only read into the container while `USE_FAKE_IDENTITY_VERIFIER`/`USE_FAKE_LOG_DATA_SOURCE` are `false` (see above) — right now, neither is, so this section describes the mechanism for whenever a real upstream is deliberately wired in later, not this deployment's current state.

The same "no real identifiers in committed code" rule `environment-setup.md` applies to local user secrets applies equally to `infra/lib/compute-stack.ts` — a committed file — so none of the real Cognito/DynamoDB-log-data values can be hardcoded into it either.

- `infra/config/task-environment.example.json`: committed, fictitious ("Acme Habit Tracker") template — same values as `README.md`'s worked example, just reshaped into flat `Section:Key` JSON.
- `infra/config/task-environment.local.json`: gitignored, holds the real values. Keys are written exactly like `dotnet user-secrets` keys (`"Cognito:UserPoolId"`, `"DynamoDb:LogData:Dimensions:0:Name"`, etc.) — copy straight out of `dotnet user-secrets list` output, reshaped into JSON.
- `compute-stack.ts` reads this file at synth time, throws a clear error naming the missing file if it isn't there (mirrors the app's own `ConfigReading.RequireString` fail-fast convention), and converts each `:` to `__` when building the container's environment map — the exact separator ASP.NET Core's environment-variable config provider expects.
- **Not** in this file: `DynamoDb:Consent:TableName` and `DynamoDb:Audit:TableName` come directly from the `TuracoChorusStack` table objects passed into the compute stack's props (a real CDK cross-stack reference — safe here since, unlike the upstream log-data table, Turaco Chorus owns these tables itself). Nor the AI provider API key — that's Secrets Manager, set out-of-band, never in this file.

## Elastic IP and reassociation

An auto-assigned public IP changes on every instance replacement (patching, ASG recovery, a manual restart). An Elastic IP fixes this: same underlying per-hour public-IPv4 charge as an auto-assigned one, so making it static costs nothing extra — but a new instance doesn't know about it automatically, since the ASG only manages the instance, not the address.

- `ec2.CfnEIP`, allocated once, independent of the ASG's instance lifecycle — it outlives any single instance.
- Reassociation on boot, not a separate Lambda/lifecycle hook: the instance's user data calls the AWS CLI (`aws ec2 associate-address --instance-id <self, via instance metadata> --allocation-id <eip-alloc-id> --region af-south-1`) using its own instance role.
Simpler than a lifecycle-hook Lambda for a single, fixed-size-1 ASG — every replacement instance just re-attaches the same address to itself on startup.
- Instance role: `ec2:AssociateAddress` scoped to that one EIP's allocation ID (plus a necessary `instance/*` wildcard, since the replacement instance's ID isn't known ahead of time); `ec2:DescribeAddresses` stays `*` since that action has no resource-level scoping in IAM at all.

## DNS delegation: `turaco.literaturelounge.org`

`literaturelounge.org` is registered and DNS-hosted at Squarespace, not Route 53. Rather than migrating the whole domain, only the `turaco` subdomain gets delegated — every other record Squarespace already serves for `literaturelounge.org` (main site, email, anything else) stays exactly where it is, untouched.

- `route53.PublicHostedZone` for `turaco.literaturelounge.org`, created in the compute stack.
- One A record inside it: `turaco.literaturelounge.org` → the Elastic IP above.
- The hosted zone's four assigned nameservers are exposed via `CfnOutput` (`route53.PublicHostedZone` only gets its `ns-*`/`awsdns-*` values at deploy time, not before).
- **One manual, one-time step outside CDK**: after the first deploy, take those four nameserver values and add them at Squarespace as a custom **NS** record — host `turaco`, one row per nameserver. That's the delegation; everything after it (the A record, IP changes) is managed entirely from the Route 53 side, no further Squarespace changes needed.
- DNS propagation for a fresh NS delegation can take anywhere from minutes to ~24-48 hours depending on caching along the path — expected, not a sign anything's broken.

## Secrets Manager

Closes out Phase 4's second checklist item alongside the deploy step, since the task definition is where both land together.

- One `secretsmanager.Secret` per deployment, holding the selected AI provider's API key (Gemini for now, per `environment-setup.md`).
- Granted read access to the task's execution role automatically (CDK grants this when a secret is passed via the container's `secrets` map) — arrives in the container as `Gemini__ApiKey` (or `Claude__ApiKey`), the environment-variable form of the `Gemini:ApiKey`/`Claude:ApiKey` config key the app already reads locally through user secrets.
- No real key value ever committed: set once, out-of-band, via `aws secretsmanager put-secret-value` (or the console) after the stack deploys the empty secret.

## Deployment configuration (single instance, fixed port)

Found while rotating the real API key into the already-deployed secret (which requires a `--force-new-deployment` to actually reach the running container): the CDK/ECS defaults for a service's rolling deployment assume there's room to run the new and old task briefly side by side (`maxHealthyPercent: 200`, `minHealthyPercent: 50`). With one instance and a fixed host port (`80`), there's nowhere for a second task to go — the deployment got stuck indefinitely (two `deployments` entries, the new one permanently `Pending: 0, Running: 0`), and only resolved once the old task was manually stopped to free the port.

Fixed by setting the `Ec2Service` to stop-then-start instead: `minHealthyPercent: 0`, `maxHealthyPercent: 100`. This means a brief window of real downtime on every deploy (task restarts, service secrets change, etc.) rather than a stuck rollout — an acceptable trade for a single-instance deployment. `AvailabilityZoneRebalancing.ENABLED` (the CDK default) also has to be explicitly set to `DISABLED`, since AWS rejects `maxHealthyPercent <= 100` otherwise — moot anyway for a single-AZ, single-instance service with nothing to rebalance.

## Setup

```
cdk deploy --no-validation TuracoChorusComputeStack
```

The `--no-validation` flag is required, not optional: CDK's built-in template validator flags the Route 53 A record's `ResourceRecords` value (a `Ref` to the Elastic IP, resolved only at deploy time) against the literal-IPv4 pattern, and fails on the unresolved placeholder. Confirmed as a validator false positive, not a real problem — the synthesized template correctly contains `{"Ref": "ServiceEip"}`, valid CloudFormation that resolves to the real address at deploy time.

One command — cluster, ASG, capacity provider, task definition, service, security group, Elastic IP, hosted zone/A record, and the (empty) secret all come up together.
Two things still need doing once, out-of-band, after that:
1. Set the real API key (see Secrets Manager above).
2. Add the four NS records the stack outputs at Squarespace (see DNS delegation above) — only needed on the very first deploy; later deploys don't touch the hosted zone's nameservers.

## Cancelling it

In order of how reversible/cheap each option is:

1. **Pause** (cheapest, fully reversible, not a CDK operation):
   ```
   aws autoscaling set-desired-capacity --auto-scaling-group-name <name> --desired-capacity 0
   ```
   Terminates the one EC2 instance. Every CDK-defined resource stays in place. $0 while paused; set back to 1 to resume.
2. **Full teardown**:
   ```
   cdk destroy --no-validation TuracoChorusComputeStack
   ```
   Removes the ASG/instance, cluster, capacity provider, security group, task definition, secret, Elastic IP, and the Route 53 hosted zone/A record.
Because compute is its own stack, this never touches `TuracoChorusStack`'s tables.
The NS delegation record left at Squarespace becomes inert (nothing left to resolve it to) but isn't removed automatically — worth deleting there too if this is ever a permanent teardown, not just a pause.

## Known limitations

- No HTTPS: the endpoint is plain HTTP on a real domain name. Fine for a portfolio demo, not for anything handling real user credentials beyond the JWT already required by `IIdentityVerifier`. Adding HTTPS later means introducing an ALB + free ACM certificate — the tier that was explicitly not chosen this round.
- Single instance: a crashed task restarts via ECS, but a crashed *instance* takes the ASG's normal replacement time, during which the service is fully down (no second instance to fail over to) — the Elastic IP re-associates to the replacement automatically, so the domain keeps working once it's back, just not during the gap.
- Free-tier dependency: `t3.micro` is only free through **19 Jan 2027** for this account. After that, cost is comparable to the smallest Fargate task, without Fargate's zero-management story — a future revisit, not an immediate concern.
- First-deploy DNS delegation is a manual step (adding the NS record at Squarespace) — every subsequent `cdk deploy` is fully automated, but that one step can't be scripted since it lives outside AWS.
