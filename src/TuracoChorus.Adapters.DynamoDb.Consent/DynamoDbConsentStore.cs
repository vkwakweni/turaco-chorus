using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;

namespace TuracoChorus.Adapters.DynamoDb.Consent;

/// <summary>
/// Reads and writes a user's consent decision in TuracoChorusConsent — one row per user,
/// overwritten in place on every change. See artifacts/tech-stack.md's "Storage schemas" section.
/// </summary>
public sealed class DynamoDbConsentStore(
    DynamoDbConsentStoreOptions options, IAmazonDynamoDB client) : IConsentStore
{
    public async Task<ConsentRecord> GetConsentAsync(string userId)
    {
        var response = await client.GetItemAsync(new GetItemRequest
        {
            TableName = options.TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                [DynamoDbConsentItemMapper.UserIdAttribute] = new AttributeValue { S = userId },
            },
        });

        return DynamoDbConsentItemMapper.ToConsentRecord(userId, response.Item);
    }

    public async Task<ConsentRecord> SetConsentAsync(string userId, bool granted)
    {
        var grantedAt = DateTimeOffset.UtcNow;

        await client.PutItemAsync(new PutItemRequest
        {
            TableName = options.TableName,
            Item = DynamoDbConsentItemMapper.ToPutItem(userId, granted, grantedAt),
        });

        return new ConsentRecord(userId, granted, grantedAt);
    }
}
