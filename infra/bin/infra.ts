#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib/core';
import { TuracoChorusStack } from '../lib/infra-stack';
import { GithubOidcStack } from '../lib/github-oidc-stack';

const app = new cdk.App();
new TuracoChorusStack(app, 'TuracoChorusStack', {
  /* If you don't specify 'env', this stack will be environment-agnostic.
   * Account/Region-dependent features and context lookups will not work,
   * but a single synthesized template can be deployed anywhere. */

  /* Uncomment the next line to specialize this stack for the AWS Account
   * and Region that are implied by the current CLI configuration. */
  // env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION },

  /* Uncomment the next line if you know exactly what Account and Region you
   * want to deploy the stack to. */
  // env: { account: '123456789012', region: 'us-east-1' },

  /* For more information, see https://docs.aws.amazon.com/cdk/latest/guide/environments.html */
});

// Needs a concrete account/region to build the OIDC provider ARN it references;
// deployed manually/once, not via CI. Stack id is prefixed to differentiate it
// from other apps' own stacks of the same unprefixed name in this account.
new GithubOidcStack(app, 'TuracoChorusGithubOidcStack', {
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION },
});
