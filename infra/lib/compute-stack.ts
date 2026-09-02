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

const CONTAINER_PORT = 8080;
const HOST_PORT = 80;
const SUBDOMAIN = 'turaco.literaturelounge.org';
const ECR_REPOSITORY_NAME = 'turaco-chorus';

// Deliberately independent of whichever upstream application would otherwise supply real
// Cognito/DynamoDB values — see artifacts/ecs-deployment.md's "per-port fake/real split".
// Consent/Audit/Insight stay real either way; only these two ports run fake. Flip both to
// false (and widen TEMPORARY_ALLOWED_INGRESS_CIDR back to ec2.Peer.anyIpv4()) once a real
// upstream is deliberately wired in.
const USE_FAKE_IDENTITY_VERIFIER = true;
const USE_FAKE_LOG_DATA_SOURCE = true;
const FAKE_TEST_USER_ID = 'demo-user';

// Restricts inbound traffic to just this one IP while identity verification is fake, since a
// fake verifier's registry (see PartialFakeSeedData) accepts only one known test credential —
// not a real per-user Cognito check. Widen to ec2.Peer.anyIpv4() once USE_FAKE_IDENTITY_VERIFIER
// is false and real Cognito verification is in place.
const TEMPORARY_ALLOWED_INGRESS_CIDR = '197.184.70.18/32';

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

/** Every distinct table ARN this task needs read (`Query`-only) access to: the main
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

    const instanceSecurityGroup = new ec2.SecurityGroup(this, 'InstanceSecurityGroup', {
      vpc,
      description: 'Turaco Chorus EC2 instance - inbound app port only',
    });
    instanceSecurityGroup.addIngressRule(
      ec2.Peer.ipv4(TEMPORARY_ALLOWED_INGRESS_CIDR),
      ec2.Port.tcp(HOST_PORT),
      'Allow inbound app traffic from the deployer only, while identity verification is fake',
    );

    const autoScalingGroup = cluster.addCapacity('CapacityProvider', {
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.MICRO),
      machineImage: ecs.EcsOptimizedImage.amazonLinux2(),
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

    // Auto-generated, genuinely random — retrieve via `aws secretsmanager get-secret-value`
    // to use as the test Authorization bearer token. Only created while the identity verifier
    // is fake; PartialFakeSeedData registers this exact value against FAKE_TEST_USER_ID.
    const fakeTestCredentialSecret = USE_FAKE_IDENTITY_VERIFIER
      ? new secretsmanager.Secret(this, 'FakeAuthTestCredential', {
        description: 'Turaco Chorus fake-identity-verifier test credential (bearer token)',
        generateSecretString: { excludePunctuation: true, passwordLength: 32 },
      })
      : undefined;

    const excludedConfigPrefixes: string[] = [
      ...(USE_FAKE_IDENTITY_VERIFIER ? ['Cognito:'] : []),
      ...(USE_FAKE_LOG_DATA_SOURCE ? ['DynamoDb:LogData:'] : []),
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
        UseFakeIdentityVerifier: String(USE_FAKE_IDENTITY_VERIFIER),
        UseFakeLogDataSource: String(USE_FAKE_LOG_DATA_SOURCE),
        ...(USE_FAKE_IDENTITY_VERIFIER ? { FakeAuth__TestUserId: FAKE_TEST_USER_ID } : {}),
      },
      secrets: {
        [`${insightProvider}__ApiKey`]: ecs.Secret.fromSecretsManager(aiProviderSecret),
        ...(fakeTestCredentialSecret
          ? { FakeAuth__TestCredential: ecs.Secret.fromSecretsManager(fakeTestCredentialSecret) }
          : {}),
      },
    });

    container.addPortMappings({
      containerPort: CONTAINER_PORT,
      hostPort: HOST_PORT,
      protocol: ecs.Protocol.TCP,
    });

    props.consentTable.grantReadWriteData(taskDefinition.taskRole);
    props.auditTable.grantReadWriteData(taskDefinition.taskRole);

    if (!USE_FAKE_LOG_DATA_SOURCE) {
      // Least-privilege, read-only, Query-only — matches dynamodb-adapter.md's documented IAM
      // policy for ILogDataSource: no GetItem/Scan, so a bug that skips the key condition fails
      // outright rather than leaking other users' data. Skipped entirely while log data is
      // fake — this deployment has no business holding IAM permissions on a real upstream
      // table it will never query.
      taskDefinition.taskRole.addToPrincipalPolicy(new iam.PolicyStatement({
        actions: ['dynamodb:Query'],
        resources: logDataTableArns(taskEnvironmentConfig, this.region, this.account),
      }));
    }

    new ecs.Ec2Service(this, 'Service', {
      cluster,
      taskDefinition,
      desiredCount: 1,
    });

    const hostedZone = new route53.PublicHostedZone(this, 'SubdomainHostedZone', {
      zoneName: SUBDOMAIN,
    });

    new route53.ARecord(this, 'ServiceARecord', {
      zone: hostedZone,
      target: route53.RecordTarget.fromIpAddresses(eip.ref),
    });

    new cdk.CfnOutput(this, 'NameServersOutput', {
      description: `Add these as a custom NS record for host "turaco" at literaturelounge.org's DNS host (Squarespace) — one-time, first deploy only`,
      value: cdk.Fn.join(', ', hostedZone.hostedZoneNameServers!),
    });

    new cdk.CfnOutput(this, 'ServiceUrlOutput', {
      value: `http://${SUBDOMAIN}`,
    });

    new cdk.CfnOutput(this, 'ElasticIpOutput', {
      value: eip.ref,
    });

    if (fakeTestCredentialSecret) {
      new cdk.CfnOutput(this, 'FakeAuthTestCredentialSecretArnOutput', {
        description: `Retrieve via: aws secretsmanager get-secret-value --secret-id <this-arn> --query SecretString --output text --region ${this.region} — use as the Authorization: Bearer <value> header. Test user id is "${FAKE_TEST_USER_ID}".`,
        value: fakeTestCredentialSecret.secretArn,
      });
    }
  }
}
