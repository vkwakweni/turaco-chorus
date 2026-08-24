using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.DynamoDb.Audit;

/// <summary>
/// Appends one row per /ask call to TuracoChorusAskAudit — write-only, no read side.
/// See artifacts/tech-stack.md's "Storage schemas" section for the item shape.
/// </summary>
public sealed class DynamoDbAskAuditLogger(
    DynamoDbAskAuditLoggerOptions options, IAmazonDynamoDB client) : IAuditLogger
{
    public Task RecordAuditEntryAsync(AuditEntry entry) => client.PutItemAsync(new PutItemRequest
    {
        TableName = options.TableName,
        Item = DynamoDbAuditItemMapper.ToPutItem(entry),
    });
}
