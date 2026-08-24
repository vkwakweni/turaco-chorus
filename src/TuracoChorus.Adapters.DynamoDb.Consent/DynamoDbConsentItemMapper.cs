using Amazon.DynamoDBv2.Model;
using TuracoChorus.Core.Models;

namespace TuracoChorus.Adapters.DynamoDb.Consent;

/// <summary>
/// Pure mapping between TuracoChorusConsent's item shape and ConsentRecord — no AWS calls.
/// See artifacts/tech-stack.md's "Storage schemas" section for the item shape.
/// </summary>
public static class DynamoDbConsentItemMapper
{
    public const string UserIdAttribute = "userId";
    public const string GrantedAttribute = "granted";
    public const string GrantedAtAttribute = "grantedAt";

    /// <summary>A missing/empty item means the user never made a consent decision.</summary>
    public static ConsentRecord ToConsentRecord(string userId, Dictionary<string, AttributeValue>? item)
    {
        if (item is not { Count: > 0 })
        {
            return new ConsentRecord(userId, Granted: false, GrantedAt: null);
        }

        var granted = item.TryGetValue(GrantedAttribute, out var grantedAttr) && grantedAttr.BOOL is true;
        var grantedAt = item.TryGetValue(GrantedAtAttribute, out var grantedAtAttr) && grantedAtAttr.S is { } s
            ? DateTimeOffset.Parse(s)
            : (DateTimeOffset?)null;

        return new ConsentRecord(userId, granted, grantedAt);
    }

    /// <summary>Builds the item to write. grantedAt is always set — see the "timestamp of the last decision" note in domain-interfaces-and-objects.md.</summary>
    public static Dictionary<string, AttributeValue> ToPutItem(string userId, bool granted, DateTimeOffset grantedAt)
        => new()
        {
            [UserIdAttribute] = new AttributeValue { S = userId },
            [GrantedAttribute] = new AttributeValue { BOOL = granted },
            [GrantedAtAttribute] = new AttributeValue { S = grantedAt.ToString("O") },
        };
}
