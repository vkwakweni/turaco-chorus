import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as ecr from 'aws-cdk-lib/aws-ecr';

const GITHUB_REPO = 'vkwakweni/turaco-chorus';

// CDK-auto-generated names — setting explicit ones would force CloudFormation to replace the
// live cluster/service. Re-derive via `aws ecs list-services` if they're ever recreated.
const ECS_CLUSTER_NAME = 'TuracoChorusComputeStack-ClusterEB0386A7-0vQtxtH3IPcE';
const ECS_SERVICE_NAME = 'TuracoChorusComputeStack-ServiceD69D759B-V0zNXNkBZUUY';

// Lets GitHub Actions assume an AWS role via OIDC instead of long-lived access-key secrets.
// Reuses the OIDC provider loggers-world's own stack already registered — IAM allows only
// one provider per issuer URL per account, so this stack must not declare a second one.
export class GithubOidcStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    const account = cdk.Stack.of(this).account;

    const provider = iam.OpenIdConnectProvider.fromOpenIdConnectProviderArn(
      this,
      'GithubOidcProvider',
      `arn:aws:iam::${account}:oidc-provider/token.actions.githubusercontent.com`,
    );

    const repository = new ecr.Repository(this, 'TuracoChorusRepository', {
      repositoryName: 'turaco-chorus',
    });

    const deployRole = new iam.Role(this, 'GithubActionsDeployRole', {
      roleName: 'github-actions-turaco-chorus-deploy',
      assumedBy: new iam.WebIdentityPrincipal(provider.openIdConnectProviderArn, {
        StringEquals: {
          'token.actions.githubusercontent.com:aud': 'sts.amazonaws.com',
        },
        StringLike: {
          // Restricts to pushes on main. The second pattern covers GitHub's occasional
          // owner/repo-ID suffix after a literal "@" — a real repo name can't contain
          // "@" itself, so a look-alike name can't match this.
          'token.actions.githubusercontent.com:sub': [
            `repo:${GITHUB_REPO}:ref:refs/heads/main`,
            `repo:${GITHUB_REPO.replace('/', '@*/')}@*:ref:refs/heads/main`,
          ],
        },
      }),
      maxSessionDuration: cdk.Duration.hours(1),
    });

    // ecr:GetAuthorizationToken has no resource-level permissions — it's always "*".
    deployRole.addToPolicy(new iam.PolicyStatement({
      actions: ['ecr:GetAuthorizationToken'],
      resources: ['*'],
    }));

    deployRole.addToPolicy(new iam.PolicyStatement({
      actions: [
        'ecr:BatchCheckLayerAvailability',
        'ecr:PutImage',
        'ecr:InitiateLayerUpload',
        'ecr:UploadLayerPart',
        'ecr:CompleteLayerUpload',
      ],
      resources: [repository.repositoryArn],
    }));

    // Lets CI actually trigger the deploy instead of that step staying manual — see
    // ecs-deployment.md. Scoped to this one service only; DescribeServices is for
    // `aws ecs wait services-stable` in ci.yml.
    const ecsServiceArn = `arn:aws:ecs:${this.region}:${this.account}:service/${ECS_CLUSTER_NAME}/${ECS_SERVICE_NAME}`;
    deployRole.addToPolicy(new iam.PolicyStatement({
      actions: ['ecs:UpdateService', 'ecs:DescribeServices'],
      resources: [ecsServiceArn],
    }));

    new cdk.CfnOutput(this, 'GithubActionsDeployRoleArnOutput', {
      value: deployRole.roleArn,
    });

    new cdk.CfnOutput(this, 'EcrRepositoryUriOutput', {
      value: repository.repositoryUri,
    });
  }
}
