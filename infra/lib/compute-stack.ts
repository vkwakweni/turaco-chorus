import * as fs from 'fs';
import * as path from 'path';
import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as ecs from 'aws-cdk-lib/aws-ecs';
import * as ecr from 'aws-cdk-lib/aws-ecr';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as secretsmanager from 'aws-cdk-lib/aws-secretsmanager';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';
import { readDeploymentMode } from './deployment-mode';

const CONTAINER_PORT = 8080;
const HOST_PORT = 80;
const SUBDOMAIN = 'turacochorus.literaturelounge.org';
const ECR_REPOSITORY_NAME = 'turaco-chorus';

const HTTPS_HOST_PORT = 443;
const PROXY_IMAGE = 'caddy:2';
const PROXY_DATA_HOST_PATH = '/var/lib/caddy-data';
// Alias the proxy container reaches the app container by, over an ECS container link.
const APP_LINK_ALIAS = 'app';

// Which ports run fake, and whether the service is public over HTTPS, come from the optional
// local config/deployment.local.json (see deployment-mode.ts). Without that file the deployment
// stays the demo: both ports fake, plain HTTP, inbound limited to the IP allow-list. Consent/Audit/
// Insight stay real either way; see artifacts/ecs-deployment.md's "per-port fake/real split".
const FAKE_TEST_USER_ID = 'demo-user';

/**
 * Restricts inbound traffic to a small allow-list while identity verification is fake, since a
 * fake verifier's registry (see PartialFakeSeedData) accepts only one known test credential —
 * not a real per-user Cognito check.
 *
 * The prefix list itself is created *once*, outside CDK — see ecs-deployment.md's "IP allow-list"
 * — and only ever referenced here by ID via `fromPrefixListId` (a pure lookup, no `entries` prop).
 * If CDK declared the entries, any unrelated future `cdk deploy` would silently revert a quick
 * `aws ec2 modify-managed-prefix-list` update back to whatever's in this file — the whole point
 * of the prefix list (update the IP without touching code or redeploying) would only hold until
 * the next deploy. Reading it by ID avoids that entirely: CloudFormation never manages this
 * resource's membership, so nothing here can ever drift or get reverted.
 *
 * The ID itself is real-account-specific, so it comes from the same local, gitignored config
 * mechanism as the Cognito/LogData values (see "Installer config" below) — never hardcoded here.
 */
function readAllowedIngressPrefixListId(): string {
  const configPath = path.join(__dirname, '..', 'config', 'allowed-ingress.local.json');

  if (!fs.existsSync(configPath)) {
    throw new Error(
      `Missing ${configPath}. Create it (see config/allowed-ingress.example.json) with the ` +
      'id of a managed prefix list created via `aws ec2 create-managed-prefix-list` ' +
      '(see artifacts/ecs-deployment.md) before deploying TuracoChorusComputeStack.',
    );
  }

  const { prefixListId } = JSON.parse(fs.readFileSync(configPath, 'utf-8'));
  return prefixListId;
}

export interface TuracoChorusComputeStackProps extends cdk.StackProps {
  readonly consentTable: dynamodb.Table;
  readonly auditTable: dynamodb.Table;
}

/**
 * Reads the real, installer-specific config values (Cognito, upstream DynamoDB table shape,
 * region, AI provider selection) from a local, gitignored file — never hardcoded into this
 * committed source, same "no real identifiers in the repo" rule the .NET app itself follows
 * (see environment-setup.md). Fails fast with a clear message, mirroring the app's own
 * ConfigReading.RequireString convention, rather than deploying a silently misconfigured task.
 */
function readTaskEnvironmentConfig(): Record<string, string> {
  const configPath = path.join(__dirname, '..', 'config', 'task-environment.local.json');

  if (!fs.existsSync(configPath)) {
    throw new Error(
      `Missing ${configPath}. Copy config/task-environment.example.json to ` +
      'task-environment.local.json and fill in real values (see artifacts/ecs-deployment.md) ' +
      'before deploying TuracoChorusComputeStack.',
    );
  }

  return JSON.parse(fs.readFileSync(configPath, 'utf-8'));
}

/** Converts flat `Section:Key` config into container env vars, dropping any key under one of
 * `excludePrefixes` — used to keep upstream-specific (Cognito/LogData) values out of the
 * container entirely while their ports run fake, rather than merely unused. */
function toContainerEnvironment(flatConfig: Record<string, string>, excludePrefixes: string[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(flatConfig)) {
    if (excludePrefixes.some((prefix) => key.startsWith(prefix))) {
      continue;
    }
    result[key.replace(/:/g, '__')] = value;
  }
  return result;
}

/** Every distinct table ARN this task needs read access to: the main
 * `DynamoDb:LogData:TableName`, plus any dimension's separately-configured `LookupTableName`. */
function logDataTableArns(flatConfig: Record<string, string>, region: string, account: string): string[] {
  const tableNames = new Set<string>();
  const mainTable = flatConfig['DynamoDb:LogData:TableName'];
  if (!mainTable) {
    throw new Error('task-environment.local.json is missing required key "DynamoDb:LogData:TableName".');
  }
  tableNames.add(mainTable);

  for (const [key, value] of Object.entries(flatConfig)) {
    if (/^DynamoDb:LogData:Dimensions:\d+:LookupTableName$/.test(key)) {
      tableNames.add(value);
    }
  }

  return Array.from(tableNames).map((name) => `arn:aws:dynamodb:${region}:${account}:table/${name}`);
}

export class TuracoChorusComputeStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: TuracoChorusComputeStackProps) {
    super(scope, id, props);

    const { useFakeIdentityVerifier, useFakeLogDataSource, publicHttps } = readDeploymentMode();

    const taskEnvironmentConfig = readTaskEnvironmentConfig();
    const insightProvider = taskEnvironmentConfig['InsightProvider'];
    if (insightProvider !== 'Claude' && insightProvider !== 'Gemini') {
      throw new Error(
        `task-environment.local.json's "InsightProvider" must be "Claude" or "Gemini", got ${JSON.stringify(insightProvider)}.`,
      );
    }

    const vpc = ec2.Vpc.fromLookup(this, 'DefaultVpc', { isDefault: true });
    const cluster = new ecs.Cluster(this, 'Cluster', { vpc });

    // Fixed address, independent of the ASG's instance lifecycle — see "Elastic IP and
    // reassociation" in ecs-deployment.md for why a replacement instance re-attaches it itself
    // via user data rather than through a separate Lambda/lifecycle hook.
    const eip = new ec2.CfnEIP(this, 'ServiceEip');

    // Only the demo deployment is limited to the allow-list; a public deployment has no use for it.
    const allowedIngressPrefixList = publicHttps
      ? undefined
      : ec2.PrefixList.fromPrefixListId(this, 'AllowedIngressPrefixList', readAllowedIngressPrefixListId());

    const instanceSecurityGroup = new ec2.SecurityGroup(this, 'InstanceSecurityGroup', {
      vpc,
      description: 'Turaco Chorus EC2 instance - inbound app port only',
    });
    if (allowedIngressPrefixList) {
      instanceSecurityGroup.addIngressRule(
        allowedIngressPrefixList,
        ec2.Port.tcp(HOST_PORT),
        'Allow inbound app traffic from the allow-list only, while identity verification is fake',
      );
    } else {
      // Port 80 is needed too: Let's Encrypt's HTTP challenge, and the redirect to HTTPS.
      instanceSecurityGroup.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(HOST_PORT), 'Public HTTP: certificate challenge and redirect to HTTPS');
      instanceSecurityGroup.addIngressRule(ec2.Peer.anyIpv4(), ec2.Port.tcp(HTTPS_HOST_PORT), 'Public HTTPS');
    }

    const autoScalingGroup = cluster.addCapacity('CapacityProvider', {
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
      machineImage: ecs.EcsOptimizedImage.amazonLinux2023(),
      minCapacity: 1,
      maxCapacity: 1,
      desiredCapacity: 1,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      associatePublicIpAddress: true,
    });
    autoScalingGroup.addSecurityGroup(instanceSecurityGroup);

    autoScalingGroup.addUserData(
      'TOKEN=$(curl -sX PUT "http://169.254.169.254/latest/api/token" -H "X-aws-ec2-metadata-token-ttl-seconds: 21600")',
      'INSTANCE_ID=$(curl -s -H "X-aws-ec2-metadata-token: $TOKEN" http://169.254.169.254/latest/meta-data/instance-id)',
      `aws ec2 associate-address --instance-id "$INSTANCE_ID" --allocation-id ${eip.attrAllocationId} --region ${this.region}`,
    );
    // AssociateAddress is scoped to this one EIP's allocation; DescribeAddresses has no
    // resource-level scoping in IAM at all, so that action alone stays "*".
    autoScalingGroup.role.addToPrincipalPolicy(new iam.PolicyStatement({
      actions: ['ec2:AssociateAddress'],
      resources: [
        `arn:aws:ec2:${this.region}:${this.account}:instance/*`,
        `arn:aws:ec2:${this.region}:${this.account}:elastic-ip/${eip.attrAllocationId}`,
      ],
    }));
    autoScalingGroup.role.addToPrincipalPolicy(new iam.PolicyStatement({
      actions: ['ec2:DescribeAddresses'],
      resources: ['*'],
    }));

    const repository = ecr.Repository.fromRepositoryName(this, 'Repository', ECR_REPOSITORY_NAME);

    const aiProviderSecret = new secretsmanager.Secret(this, 'AiProviderApiKey', {
      description: `Turaco Chorus ${insightProvider} API key — set the real value out-of-band after deploy`,
    });

    // Auto-generated, genuinely random: retrieve via `aws secretsmanager get-secret-value`
    // to use as the test Authorization bearer token. Only created while the identity verifier
    // is fake; PartialFakeSeedData registers this exact value against FAKE_TEST_USER_ID.
    const fakeTestCredentialSecret = useFakeIdentityVerifier
      ? new secretsmanager.Secret(this, 'FakeAuthTestCredential', {
        description: 'Turaco Chorus fake-identity-verifier test credential (bearer token)',
        generateSecretString: { excludePunctuation: true, passwordLength: 32 },
      })
      : undefined;

    const excludedConfigPrefixes: string[] = [
      ...(useFakeIdentityVerifier ? ['Cognito:'] : []),
      ...(useFakeLogDataSource ? ['DynamoDb:LogData:'] : []),
    ];

    const taskDefinition = new ecs.Ec2TaskDefinition(this, 'TaskDefinition');

    const container = taskDefinition.addContainer('TuracoChorusContainer', {
      image: ecs.ContainerImage.fromEcrRepository(repository, 'latest'),
      memoryReservationMiB: 400,
      cpu: 256,
      logging: ecs.LogDrivers.awsLogs({ streamPrefix: 'turaco-chorus' }),
      environment: {
        ...toContainerEnvironment(taskEnvironmentConfig, excludedConfigPrefixes),
        DynamoDb__Consent__TableName: props.consentTable.tableName,
        DynamoDb__Audit__TableName: props.auditTable.tableName,
        UseFakeIdentityVerifier: String(useFakeIdentityVerifier),
        UseFakeLogDataSource: String(useFakeLogDataSource),
        ...(useFakeIdentityVerifier ? { FakeAuth__TestUserId: FAKE_TEST_USER_ID } : {}),
      },
      secrets: {
        [`${insightProvider}__ApiKey`]: ecs.Secret.fromSecretsManager(aiProviderSecret),
        ...(fakeTestCredentialSecret
          ? { FakeAuth__TestCredential: ecs.Secret.fromSecretsManager(fakeTestCredentialSecret) }
          : {}),
      },
    });

    if (publicHttps) {
      // The app is not published on the host at all: only the proxy is, and it reaches the app by
      // container link. Caddy gets and renews the certificate for SUBDOMAIN itself, redirects HTTP to
      // HTTPS, and forwards to the app. The certificate store lives on a host directory so a task
      // restart on the same instance keeps it; a replaced instance asks for a new one.
      const proxyContainer = taskDefinition.addContainer('ProxyContainer', {
        image: ecs.ContainerImage.fromRegistry(PROXY_IMAGE),
        memoryReservationMiB: 64,
        command: ['caddy', 'reverse-proxy', '--from', SUBDOMAIN, '--to', `${APP_LINK_ALIAS}:${CONTAINER_PORT}`],
        logging: ecs.LogDrivers.awsLogs({ streamPrefix: 'proxy' }),
      });
      proxyContainer.addPortMappings(
        { containerPort: 80, hostPort: HOST_PORT, protocol: ecs.Protocol.TCP },
        { containerPort: 443, hostPort: HTTPS_HOST_PORT, protocol: ecs.Protocol.TCP },
      );
      proxyContainer.addLink(container, APP_LINK_ALIAS);
      taskDefinition.addVolume({ name: 'proxy-data', host: { sourcePath: PROXY_DATA_HOST_PATH } });
      proxyContainer.addMountPoints({ sourceVolume: 'proxy-data', containerPath: '/data', readOnly: false });
    } else {
      container.addPortMappings({
        containerPort: CONTAINER_PORT,
        hostPort: HOST_PORT,
        protocol: ecs.Protocol.TCP,
      });
    }

    props.consentTable.grantReadWriteData(taskDefinition.taskRole);
    props.auditTable.grantReadWriteData(taskDefinition.taskRole);

    if (!useFakeLogDataSource) {
      // Least-privilege, read-only, no Scan — matches dynamodb-adapter.md's IAM policy.
      // Skipped entirely while log data is fake, per useFakeLogDataSource above.
      taskDefinition.taskRole.addToPrincipalPolicy(new iam.PolicyStatement({
        actions: ['dynamodb:Query', 'dynamodb:GetItem'],
        resources: logDataTableArns(taskEnvironmentConfig, this.region, this.account),
      }));
    }

    const service = new ecs.Ec2Service(this, 'Service', {
      cluster,
      taskDefinition,
      desiredCount: 1,
      // Single instance, fixed host port: CDK's default rolling deploy tries to run two tasks
      // at once, which can't fit; got stuck once already. Stop-then-start avoids that.
      minHealthyPercent: 0,
      maxHealthyPercent: 100,
      // ENABLED (the CDK default) rejects maxHealthyPercent <= 100 outright; nothing to
      // rebalance across anyway with one instance.
      availabilityZoneRebalancing: ecs.AvailabilityZoneRebalancing.DISABLED,
    });

    const hostedZone = new route53.PublicHostedZone(this, 'SubdomainHostedZone', {
      zoneName: SUBDOMAIN,
    });

    new route53.ARecord(this, 'ServiceARecord', {
      zone: hostedZone,
      target: route53.RecordTarget.fromIpAddresses(eip.ref),
    });

    new cdk.CfnOutput(this, 'NameServersOutput', {
      description: `Add these as a custom NS record for host "${SUBDOMAIN.split('.')[0]}" at literaturelounge.org's DNS host (Squarespace) — one-time, first deploy only`,
      value: cdk.Fn.join(', ', hostedZone.hostedZoneNameServers!),
    });

    new cdk.CfnOutput(this, 'ServiceUrlOutput', {
      value: `${publicHttps ? 'https' : 'http'}://${SUBDOMAIN}`,
    });

    new cdk.CfnOutput(this, 'ElasticIpOutput', {
      value: eip.ref,
    });

    // For CI's `aws ecs update-service`/`wait services-stable` (see github-oidc-stack.ts and
    // ecs-deployment.md) — saves re-deriving these via `aws ecs list-services` by hand.
    new cdk.CfnOutput(this, 'ClusterArnOutput', {
      value: cluster.clusterArn,
    });

    new cdk.CfnOutput(this, 'ServiceNameOutput', {
      value: service.serviceName,
    });

    if (allowedIngressPrefixList) {
      new cdk.CfnOutput(this, 'AllowedIngressPrefixListIdOutput', {
        description: 'Update the IP allow-list without touching code or redeploying: aws ec2 get-managed-prefix-list-entries --prefix-list-id <this-id>, then modify-managed-prefix-list with --add-entries/--remove-entries',
        value: allowedIngressPrefixList.prefixListId,
      });
    }

    if (fakeTestCredentialSecret) {
      new cdk.CfnOutput(this, 'FakeAuthTestCredentialSecretArnOutput', {
        description: `Retrieve via: aws secretsmanager get-secret-value --secret-id <this-arn> --query SecretString --output text --region ${this.region} — use as the Authorization: Bearer <value> header. Test user id is "${FAKE_TEST_USER_ID}".`,
        value: fakeTestCredentialSecret.secretArn,
      });
    }
  }
}
