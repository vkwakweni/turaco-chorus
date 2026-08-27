import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';

export class TuracoChorusStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // TODO: switch to RemovalPolicy.RETAIN before an official/production deployment —
    // DESTROY is only appropriate while there's no real user data in these tables yet.
    const consentTable = new dynamodb.Table(this, 'TuracoChorusConsent', {
      partitionKey: { name: 'userId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const auditTable = new dynamodb.Table(this, 'TuracoChorusAskAudit', {
      partitionKey: { name: 'userId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    new cdk.CfnOutput(this, 'ConsentTableNameOutput', {
      value: consentTable.tableName,
    });

    new cdk.CfnOutput(this, 'AuditTableNameOutput', {
      value: auditTable.tableName,
    });
  }
}
