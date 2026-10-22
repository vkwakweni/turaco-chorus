using Amazon;
using Amazon.DynamoDBv2;

namespace TuracoChorus.Adapters.DynamoDb.Consent.Tests;

/// <summary>
/// Real read/write round-trip against a real, installer-owned DynamoDB table — no fake client.
/// Excluded from the default test run (this project's own `VSTestTestCaseFilter` keeps a plain
/// `dotnet test` green without credentials); run with `dotnet test --filter "Category=Integration"`.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DynamoDbConsentStoreIntegrationTests
{
    private const string TestUserId = "integration-test-consent-user";

    [Fact]
    public async Task SetThenGetConsentAsync_AgainstRealTable_RoundTrips()
    {
        var region = RequireEnv("TURACO_TEST_AWS_REGION");
        var options = new DynamoDbConsentStoreOptions(
            TableName: RequireEnv("TURACO_TEST_DYNAMODB_CONSENT_TABLE_NAME"));

        using var client = new AmazonDynamoDBClient(
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) });
        var consentStore = new DynamoDbConsentStore(options, client);

        var granted = await consentStore.SetConsentAsync(TestUserId, granted: true);
        Assert.True(granted.Granted);
        Assert.NotNull(granted.GrantedAt);

        var readBack = await consentStore.GetConsentAsync(TestUserId);
        Assert.Equal(TestUserId, readBack.UserId);
        Assert.True(readBack.Granted);
        Assert.Equal(granted.GrantedAt, readBack.GrantedAt);

        var revoked = await consentStore.SetConsentAsync(TestUserId, granted: false);
        Assert.False(revoked.Granted);
        Assert.NotNull(revoked.GrantedAt);
    }

    private static string RequireEnv(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Integration test requires environment variable \"{name}\" to be set.");
}
