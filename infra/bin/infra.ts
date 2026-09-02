#!/usr/bin/env node
import * as cdk from 'aws-cdk-lib/core';
import { TuracoChorusStack } from '../lib/infra-stack';
import { TuracoChorusComputeStack } from '../lib/compute-stack';
import { GithubOidcStack } from '../lib/github-oidc-stack';

const app = new cdk.App();

// Pinned to a concrete account/region (rather than left environment-agnostic) because
// TuracoChorusComputeStack below needs to reference this stack's tables across stacks —
// cross-stack resource references require both stacks to share the same explicit env.
// This doesn't change actual deploy behaviour: it's only ever been deployed to this one
// account/region anyway.
const dataStack = new TuracoChorusStack(app, 'TuracoChorusStack', {
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION },
});

// Needs a concrete account/region: Vpc.fromLookup performs a real context lookup against the
// account at synth time, and can't defer to "deploy anywhere" the way TuracoChorusStack can.
// Kept as its own stack (not merged into TuracoChorusStack) so tearing down compute — an EC2
// instance, ASG, ECS cluster — never risks the DynamoDB tables living in the data stack; see
// artifacts/ecs-deployment.md.
new TuracoChorusComputeStack(app, 'TuracoChorusComputeStack', {
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION },
  consentTable: dataStack.consentTable,
  auditTable: dataStack.auditTable,
});

// Needs a concrete account/region to build the OIDC provider ARN it references;
// deployed manually/once, not via CI. Stack id is prefixed to differentiate it
// from other apps' own stacks of the same unprefixed name in this account.
new GithubOidcStack(app, 'TuracoChorusGithubOidcStack', {
  env: { account: process.env.CDK_DEFAULT_ACCOUNT, region: process.env.CDK_DEFAULT_REGION },
});
