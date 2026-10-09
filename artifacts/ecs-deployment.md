---
title: ECS Deployment (EC2 Launch Type)
last-updated: 2026-10-09
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

## Stack split

Three stacks total:

1. **`TuracoChorusStack`** (`infra/lib/infra-stack.ts`) — the two DynamoDB tables Turaco Chorus owns itself (`TuracoChorusConsent`, `TuracoChorusAskAudit`).
    - Both on `RemovalPolicy.RETAIN` — a `cdk destroy` of this stack orphans the tables instead of deleting them.
2. **`TuracoChorusComputeStack`** (`infra/lib/compute-stack.ts`) — the EC2/ECS compute side.
    - Kept out of `TuracoChorusStack` from the start: a `cdk destroy` aimed at tearing down the EC2 side must never risk real consent/audit data already exercised against live infrastructure, `RemovalPolicy` aside. Keeps "tear down compute" and "tear down data" permanently independent, not just for this one deploy.
3. **`TuracoChorusGithubOidcStack`** (`infra/lib/github-oidc-stack.ts`) — a one-time CI/CD bootstrap.
    - GitHub Actions' OIDC trust, the `turaco-chorus` ECR repository, and the CI deploy role (see `ci.yml`). Orthogonal to the data/compute split above, not a third participant in it — deployed once, rarely touched again.

## Compute stack contents

- VPC: `ec2.Vpc.fromLookup(..., { isDefault: true })`.
- `ecs.Cluster` over that VPC.
- One-instance Auto Scaling Group via `cluster.addCapacity()`: `t3.micro`, `EcsOptimizedImage.amazonLinux2()`, public subnet, `associatePublicIpAddress: true`, min/max/desired all `1`. `addCapacity()` creates the ASG and registers it with the cluster (as a managed capacity provider) in one call — no need to wire an `AsgCapacityProvider` by hand.
- `Ec2TaskDefinition` pulling the existing `turaco-chorus` ECR repository (from `github-oidc-stack.ts`) by tag `latest`.
- Container port mapping: host `80` → container `8080` (the .NET 8 container image's default HTTP port). Host `80` rather than `8080` so the real URL has no port number in it — `http://turacochorus.literaturelounge.org`, not `:8080` appended.
- Security group: inbound `80` from a small managed-prefix-list allow-list only (temporary, while identity verification is fake — see "IP allow-list" and "Per-port fake/real split" below); egress open (default).
- Task role: least-privilege on the three DynamoDB tables the service actually uses — `dynamodb:Query`/`dynamodb:GetItem` only on the log data table(s) (read-only, matching `dynamodb-adapter.md`'s documented IAM policy exactly), full read/write on consent and audit (owned by `TuracoChorusStack`). Where each table name comes from is covered next.

## Per-port fake/real split

This deployment is deliberately **not** wired to any specific upstream application. `AdapterRegistration.cs` previously only supported all-fake (`UseFakeAdapters`, local dev) or all-real; it now also supports `UseFakeIdentityVerifier`/`UseFakeLogDataSource` independently — this deploy sets both `true`, so:

- `IIdentityVerifier` and `ILogDataSource` run as their in-memory fakes — no real Cognito pool, no real upstream DynamoDB table, anywhere in this deployment.
- `IConsentStore`, `IAuditLogger`, `IInsightEngine` stay real — Turaco Chorus's own DynamoDB tables and a real Gemini/Claude call. This is what Phase 4's two checklist items actually needed to prove: the container runs on ECS/EC2, and the Secrets-Manager-injected API key genuinely gets used.
- `Cognito:*`/`DynamoDb:LogData:*` are excluded from the container's environment entirely (not merely unused) while these flags are `true` — see `toContainerEnvironment`'s `excludePrefixes` — and the log-data IAM grant is skipped outright, so this deployment holds no IAM permission on any real upstream table either.

**Making the fake identity verifier actually usable**: `FakeIdentityVerifier` starts with an empty credential registry, and the only thing that ever seeds it (`DevSeedData.cs`) is wrapped in `#if DEBUG` — stripped out of the `dotnet publish -c Release` build this Dockerfile produces. So without more, the deployed container would reject every request, including the deployer's own. `PartialFakeSeedData.cs` (new, not DEBUG-gated) registers exactly one test credential/user pair at startup, and seeds `FakeLogDataSource` with a plausible stats fixture for that user — the minimum needed to actually exercise `/stats`/`/ask`/`/consent` against this deployment.

**The credential itself**: a dedicated Secrets Manager secret (`FakeAuthTestCredential`) with a CDK-generated random 32-character value — not the `"dev-token"` literal already sitting in git/session history, which would otherwise be a public, guessable bearer token. Retrieve it after deploy:
```
aws secretsmanager get-secret-value --secret-id <FakeAuthTestCredentialSecretArnOutput> --query SecretString --output text --region af-south-1
```
Use it as `Authorization: Bearer <value>`; the test user id is the fixed constant `demo-user`.

**Network-level backstop**: since a fake identity verifier is a known single credential rather than real per-user verification, the security group's inbound rule is temporarily restricted to a small IP allow-list instead of `0.0.0.0/0` — belt-and-suspenders on top of the credential itself being unguessable. The allow-list is only dropped by opting in to a public deployment, which requires a real identity verifier (see "Deployment mode and public HTTPS").

**Reversing this later**: setting `useFakeIdentityVerifier`/`useFakeLogDataSource` to `false` in `config/deployment.local.json` (once there's a real upstream worth wiring in) automatically restores the excluded Cognito/LogData config and the IAM grant — nothing else in the stack needs to change. The file is optional and gitignored; without it both ports stay fake.

## IP allow-list (managed prefix list, referenced not managed)

A home IP isn't stable — it changed within the first day of this deployment (ISP reassignment). The security group's inbound rule points at an AWS **managed prefix list**, a named, independently-editable collection of CIDRs, rather than a literal CIDR on the rule itself.

The important design point: `compute-stack.ts` only ever **references** this prefix list by ID (`ec2.PrefixList.fromPrefixListId(...)`) — it never declares the list's `entries` as a CDK-managed property. If CDK owned the entries, any *unrelated* future `cdk deploy` would silently revert a quick CLI-made IP change back to whatever's in the template — the entire point of "update it without touching code" would only hold until the next deploy. Reading it by ID sidesteps that: CloudFormation never manages this resource's membership, so nothing here can drift or get reverted, ever.

- The prefix list is created **once**, directly via the AWS CLI, not by CDK:
  ```
  aws ec2 create-managed-prefix-list --region af-south-1 \
    --prefix-list-name "turaco-chorus-allowed-ingress" \
    --address-family IPv4 --max-entries 5 \
    --entries "Cidr=<ip>/32,Description=<label>"
  ```
- Its ID is real-account-specific, so — same rule as the Cognito/LogData values below — it isn't hardcoded in `compute-stack.ts`. It lives in `infra/config/allowed-ingress.local.json` (gitignored; `allowed-ingress.example.json` is the committed template), read at synth time via the same fail-fast-if-missing pattern as `task-environment.local.json`.
- **Updating the allowed IP** (the actual point of all this): no code, no CDK, no redeploy — ever:
  ```
  aws ec2 get-managed-prefix-list-entries --prefix-list-id <id> --region af-south-1
  aws ec2 modify-managed-prefix-list --region af-south-1 --prefix-list-id <id> \
    --current-version <version-from-above> \
    --add-entries Cidr=<new-ip>/32,Description=<label> \
    --remove-entries Cidr=<old-ip>/32
  ```
- `maxEntries: 5` leaves headroom for a second location's IP later without needing to recreate the list.

## Installer config (Cognito, upstream table shape)

The values below are only read into the container while `useFakeIdentityVerifier`/`useFakeLogDataSource` are `false` in `config/deployment.local.json` (see "Deployment mode and public HTTPS"). Without that file both are `true`, and this section describes the mechanism for whenever a real upstream is deliberately wired in, not the demo deployment's state.

The same "no real identifiers in committed code" rule `environment-setup.md` applies to local user secrets applies equally to `infra/lib/compute-stack.ts` — a committed file — so none of the real Cognito/DynamoDB-log-data values can be hardcoded into it either.

- `infra/config/task-environment.example.json`: committed, fictitious ("Acme Habit Tracker") template — same values as `README.md`'s worked example, just reshaped into flat `Section:Key` JSON.
- `infra/config/task-environment.local.json`: gitignored, holds the real values. Keys are written exactly like `dotnet user-secrets` keys (`"Cognito:UserPoolId"`, `"DynamoDb:LogData:Dimensions:0:Name"`, etc.) — copy straight out of `dotnet user-secrets list` output, reshaped into JSON.
- `compute-stack.ts` reads this file at synth time, throws a clear error naming the missing file if it isn't there (mirrors the app's own `ConfigReading.RequireString` fail-fast convention), and converts each `:` to `__` when building the container's environment map — the exact separator ASP.NET Core's environment-variable config provider expects.
- **Not** in this file: `DynamoDb:Consent:TableName` and `DynamoDb:Audit:TableName` come directly from the `TuracoChorusStack` table objects passed into the compute stack's props (a real CDK cross-stack reference — safe here since, unlike the upstream log-data table, Turaco Chorus owns these tables itself). Nor the AI provider API key — that's Secrets Manager, set out-of-band, never in this file.

## Deployment mode and public HTTPS

Which ports run fake, and whether the service is public, is chosen by an optional local file, `infra/config/deployment.local.json` (gitignored; `deployment.example.json` is the committed template). Missing file means the demo deployment described above, byte for byte: both ports fake, plain HTTP, inbound from the allow-list only.

| Key | Default | Effect when changed |
| --- | --- | --- |
| `useFakeIdentityVerifier` | `true` | `false` reads the `Cognito:*` values from `task-environment.local.json` into the container and drops the fake test credential secret |
| `useFakeLogDataSource` | `true` | `false` reads the `DynamoDb:LogData:*` values and grants the task role read-only `Query`/`GetItem` on those tables |
| `publicHttps` | `false` | `true` serves the app over HTTPS from the whole internet, through a proxy container (below) |

Unknown keys and non-boolean values are errors, so a typo cannot silently leave a port fake. `publicHttps: true` with `useFakeIdentityVerifier: true` is refused outright, because a fake verifier accepts one known credential rather than a per-user check. The logic is in `infra/lib/deployment-mode.ts` and is unit tested.

**Why Caddy on the instance, not an ALB or CloudFront.** The project's stack choices stay inside the free tier. An ALB is free only for the account's first 12 months (to 19 Jan 2027) and costs money after. CloudFront is effectively free, but its connection to the instance would be plain HTTP over the public internet, carrying the bearer tokens in cleartext, and fixing that needs a certificate on the instance anyway. Caddy on the instance is free and keeps TLS all the way to the instance, at the price of operating it ourselves.

**What `publicHttps` changes**

- A second container, `caddy`, joins the task. It runs `caddy reverse-proxy --from <subdomain> --to app:8080`, which obtains and renews a Let's Encrypt certificate for the subdomain, redirects HTTP to HTTPS and forwards to the app.
- The app container is no longer published on the host. Only the proxy is (ports 80 and 443), and it reaches the app by an ECS container link, so the app cannot be reached except through the proxy.
- The security group opens ports 80 and 443 to the internet. Port 80 is needed for Let's Encrypt's HTTP challenge and the redirect, which is why the allow-list cannot stay while the certificate is being issued.
- The proxy's `/data` (certificates and account key) is a host directory, `/var/lib/caddy-data`, so a task restart on the same instance keeps its certificate. A replaced instance starts empty and asks for a new certificate, which Let's Encrypt allows at this rate (its limits are on repeated certificates for the same name in a week).
- The ServiceUrl output becomes `https://`.

**Before the first public deploy**

1. `deployment.local.json` has `useFakeIdentityVerifier: false`, and `task-environment.local.json` has the real `Cognito:*` values, so the container verifies real tokens.
2. `task-environment.local.json` has `AllowedOrigins` set to the browser origin that will call the API (CORS).
3. Nothing else listens on ports 80 or 443 on the instance.

**To confirm on the first deploy** (not verified from documentation or a live run): that the proxy forwards the client address to the app in `X-Forwarded-For` (the rate limiter on `/ask` depends on it), and that the official `caddy` image keeps its data in `/data`.

## Elastic IP and reassociation

An auto-assigned public IP changes on every instance replacement (patching, ASG recovery, a manual restart). An Elastic IP fixes this: same underlying per-hour public-IPv4 charge as an auto-assigned one, so making it static costs nothing extra — but a new instance doesn't know about it automatically, since the ASG only manages the instance, not the address.

- `ec2.CfnEIP`, allocated once, independent of the ASG's instance lifecycle — it outlives any single instance.
- Reassociation on boot, not a separate Lambda/lifecycle hook: the instance's user data calls the AWS CLI (`aws ec2 associate-address --instance-id <self, via instance metadata> --allocation-id <eip-alloc-id> --region af-south-1`) using its own instance role.
Simpler than a lifecycle-hook Lambda for a single, fixed-size-1 ASG — every replacement instance just re-attaches the same address to itself on startup.
- Instance role: `ec2:AssociateAddress` scoped to that one EIP's allocation ID (plus a necessary `instance/*` wildcard, since the replacement instance's ID isn't known ahead of time); `ec2:DescribeAddresses` stays `*` since that action has no resource-level scoping in IAM at all.
- Machine image is `EcsOptimizedImage.amazonLinux2023()`, not `amazonLinux2()` — found live: AL2's ECS-optimized AMI doesn't ship the AWS CLI at all, so the `associate-address` call silently failed at boot (`aws: command not found`, buried in cloud-init's user-data output), the instance kept only its own auto-assigned public IP, and the Elastic IP — what DNS actually resolves to — was left permanently unassociated with any running instance. Silent and easy to miss: ECS/CloudFormation both reported the deploy as healthy throughout, since nothing about task placement or service stability depends on the EIP. AL2023 ships AWS CLI v2 pre-installed, closing the gap without adding an install step to user data.

## DNS delegation: `turacochorus.literaturelounge.org`

`literaturelounge.org` is registered and DNS-hosted at Squarespace, not Route 53. Rather than migrating the whole domain, only the `turaco` subdomain gets delegated — every other record Squarespace already serves for `literaturelounge.org` (main site, email, anything else) stays exactly where it is, untouched.

- `route53.PublicHostedZone` for `turacochorus.literaturelounge.org`, created in the compute stack.
- One A record inside it: `turacochorus.literaturelounge.org` → the Elastic IP above.
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

## CI-triggered deploy (planned)

Not implemented yet — tracked as a sub-item under Phase 5's buffer in `roadmap.md`. Written up here ahead of doing it so the path is settled before touching live IAM or the pipeline.

Right now, `ci.yml`'s `build-and-push` job (gated to `push` on `main` only) builds and pushes a new image to ECR on every merge, but nothing tells the running service to pick it up — that's still the manual `--force-new-deployment` step described above. `tech-stack.md` currently claims GitHub Actions "triggers the ECS deploy"; today that's false. This section is the path to make it true.

**What's already there to build on:**
- The `build-and-push` job's `if: github.event_name == 'push' && github.ref == 'refs/heads/main'` gate already is "after a merge to main" — no new trust boundary needed.
- The deploy role (`github-actions-turaco-chorus-deploy`, in `github-oidc-stack.ts`) already exists and is already OIDC-trusted only for `ref:refs/heads/main` — it just doesn't hold any ECS permissions yet, only ECR push.
- The live cluster and service have CDK-auto-generated names (`TuracoChorusComputeStack-ClusterEB0386A7-...` / `...-ServiceD69D759B-...`, from `aws ecs list-services`) since neither `clusterName` nor `serviceName` is set in `compute-stack.ts`. Giving them clean explicit names now would force CloudFormation to *replace* the live cluster and service — real downtime for a cosmetic win. The plan below references the existing generated ARN as-is, the same way `ci.yml` already hardcodes the deploy role's ARN and region.

**The four steps:**
1. `github-oidc-stack.ts` — add a policy statement to `deployRole`: `ecs:UpdateService` + `ecs:DescribeServices`, resource-scoped to that one service ARN only (never `*`, never the whole cluster). Requires one `cdk deploy` of `TuracoChorusGithubOidcStack` — IAM-only, no downtime by itself.
2. `compute-stack.ts` — add two `CfnOutput`s (cluster ARN, service name). Purely additive, no resource replacement, no downtime — so the next lookup doesn't need another `aws ecs list-services` dig.
3. `ci.yml` — add a `deploy` job after `build-and-push`, same `push`-to-`main` gate, reusing the same role: `aws ecs update-service --cluster <arn> --service <name> --force-new-deployment --region af-south-1`, then `aws ecs wait services-stable` so the job doesn't go green until the new task is actually healthy.
4. Once live, correct the record: `tech-stack.md`'s CI/CD line stays as-is (it becomes true instead of needing a fix), and this doc's "Deployment configuration" section above gets a note that the `--force-new-deployment` step is now automatic rather than manual.

**The trade-off to accept going in:** every merge to main will then automatically bounce the live container. Per "Deployment configuration" above (`minHealthyPercent: 0`), that's a genuine stop-then-start — a real, brief outage on every merge, not a rolling zero-downtime deploy. Worth deciding deliberately, not inheriting as a side effect of wiring the pipeline up.

## Overlapping CI runs

Two merges to `main` a few minutes apart start two runs of `ci.yml`, each of which builds and pushes the same `latest` image tag and then forces a new ECS deployment. Seen on 2026-10-09 with the deployment-mode and rate-limit PRs: the order happened to be safe, because the first run's image was already pushed before the second build began. Without a guard, a faster pair could finish the builds in the wrong order and leave `latest` on the older commit. The workflow therefore has a `concurrency` group keyed on the ref with `cancel-in-progress: false`: a run waits for the previous one on `main` to finish, and GitHub drops older queued runs in favour of the newest, which is fine because each run builds the tip of `main`.

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

- No HTTPS by default: unless `publicHttps` is set, the endpoint is plain HTTP on a real domain name. Fine for a portfolio demo, not for anything handling real user credentials beyond the JWT already required by `IIdentityVerifier`. HTTPS is opt-in through a Caddy proxy container (see "Deployment mode and public HTTPS"); an ALB with a free ACM certificate was weighed and not chosen, because it costs money once the 12-month free tier ends.
- Single instance: a crashed task restarts via ECS, but a crashed *instance* takes the ASG's normal replacement time, during which the service is fully down (no second instance to fail over to) — the Elastic IP re-associates to the replacement automatically, so the domain keeps working once it's back, just not during the gap.
- Free-tier dependency: `t3.micro` is only free through **19 Jan 2027** for this account. After that, cost is comparable to the smallest Fargate task, without Fargate's zero-management story — a future revisit, not an immediate concern.
- First-deploy DNS delegation is a manual step (adding the NS record at Squarespace) — every subsequent `cdk deploy` is fully automated, but that one step can't be scripted since it lives outside AWS.
