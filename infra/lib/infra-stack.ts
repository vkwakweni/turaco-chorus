import * as cdk from 'aws-cdk-lib/core';
import { Construct } from 'constructs';
import * as dynamodb from 'aws-cdk-lib/aws-dynamodb';

export class TuracoChorusStack extends cdk.Stack {
  public readonly consentTable: dynamodb.Table;
  public readonly auditTable: dynamodb.Table;

  constructor(scope: Construct, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    this.consentTable = new dynamodb.Table(this, 'TuracoChorusConsent', {
      partitionKey: { name: 'userId', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    this.auditTable = new dynamodb.Table(this, 'TuracoChorusAskAudit', {
      partitionKey: { name: 'userId', type: dynamodb.AttributeType.STRING },
      sortKey: { name: 'timestamp', type: dynamodb.AttributeType.STRING },
      billingMode: dynamodb.BillingMode.PAY_PER_REQUEST,
      removalPolicy: cdk.RemovalPolicy.RETAIN,
    });

    new cdk.CfnOutput(this, 'ConsentTableNameOutput', {
      value: this.consentTable.tableName,
    });

    new cdk.CfnOutput(this, 'AuditTableNameOutput', {
      value: this.auditTable.tableName,
    });
  }
}
