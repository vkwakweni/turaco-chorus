import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as ecr from 'aws-cdk-lib/aws-ecr';

const GITHUB_REPO = 'vkwakweni/turaco-chorus';

// Lets GitHub Actions assume an AWS role via OIDC (short-lived credentials per
// workflow run) instead of storing long-lived AWS access keys as repo secrets.
// References the GitHub OIDC provider already registered in this account by
// loggers-world's own GithubOidcStack — IAM only allows one provider per
// issuer URL per account, so this stack must not declare a second one.
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
          // only the main branch (i.e. a merge/push to main) can assume this role.
          // GitHub's sub claim sometimes appends immutable owner/repo IDs after a
          // literal "@" (e.g. "owner@123/repo@456"). The wildcard only expands
          // after that literal "@", which real GitHub names can never contain
          // themselves, so a look-alike account/repo name can't match this.
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

    new cdk.CfnOutput(this, 'GithubActionsDeployRoleArnOutput', {
      value: deployRole.roleArn,
    });

    new cdk.CfnOutput(this, 'EcrRepositoryUriOutput', {
      value: repository.repositoryUri,
    });
  }
}
