using Amazon.DynamoDBv2.Model;
using TuracoChorus.Adapters.DynamoDb.Consent;

namespace TuracoChorus.Adapters.DynamoDb.Consent.Tests;

public class DynamoDbConsentItemMapperTests
{
    [Fact]
    public void ToConsentRecord_NullItem_MeansNeverDecided()
    {
        var record = DynamoDbConsentItemMapper.ToConsentRecord("user-1", item: null);

        Assert.Equal("user-1", record.UserId);
        Assert.False(record.Granted);
        Assert.Null(record.GrantedAt);
    }

    [Fact]
    public void ToConsentRecord_EmptyItem_MeansNeverDecided()
    {
        var record = DynamoDbConsentItemMapper.ToConsentRecord("user-1", []);

        Assert.False(record.Granted);
        Assert.Null(record.GrantedAt);
    }

    [Fact]
    public void ToConsentRecord_GrantedItem_ReadsGrantedAt()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"] = new AttributeValue { S = "user-1" },
            ["granted"] = new AttributeValue { BOOL = true },
            ["grantedAt"] = new AttributeValue { S = "2026-08-24T14:30:00.0000000+00:00" },
        };

        var record = DynamoDbConsentItemMapper.ToConsentRecord("user-1", item);

        Assert.True(record.Granted);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T14:30:00.0000000+00:00"), record.GrantedAt);
    }

    [Fact]
    public void ToConsentRecord_RevokedItem_ReadsGrantedAt()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"] = new AttributeValue { S = "user-1" },
            ["granted"] = new AttributeValue { BOOL = false },
            ["grantedAt"] = new AttributeValue { S = "2026-08-24T14:30:00.0000000+00:00" },
        };

        var record = DynamoDbConsentItemMapper.ToConsentRecord("user-1", item);

        Assert.False(record.Granted);
        Assert.Equal(DateTimeOffset.Parse("2026-08-24T14:30:00.0000000+00:00"), record.GrantedAt);
    }

    [Fact]
    public void ToConsentRecord_ItemMissingGrantedAt_ReturnsNull()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"] = new AttributeValue { S = "user-1" },
            ["granted"] = new AttributeValue { BOOL = false },
        };

        var record = DynamoDbConsentItemMapper.ToConsentRecord("user-1", item);

        Assert.False(record.Granted);
        Assert.Null(record.GrantedAt);
    }

    [Fact]
    public void ToPutItem_Granted_IncludesGrantedAt()
    {
        var grantedAt = new DateTimeOffset(2026, 8, 24, 14, 30, 0, TimeSpan.Zero);
        var item = DynamoDbConsentItemMapper.ToPutItem("user-1", granted: true, grantedAt);

        Assert.Equal("user-1", item["userId"].S);
        Assert.True(item["granted"].BOOL);
        Assert.Equal(grantedAt.ToString("O"), item["grantedAt"].S);
    }

    [Fact]
    public void ToPutItem_Revoked_IncludesGrantedAt()
    {
        var grantedAt = new DateTimeOffset(2026, 8, 24, 14, 30, 0, TimeSpan.Zero);
        var item = DynamoDbConsentItemMapper.ToPutItem("user-1", granted: false, grantedAt);

        Assert.False(item["granted"].BOOL);
        Assert.Equal(grantedAt.ToString("O"), item["grantedAt"].S);
    }
}
