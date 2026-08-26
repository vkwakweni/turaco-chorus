using Microsoft.Extensions.Configuration;
using TuracoChorus.Adapters.Cognito;
using TuracoChorus.Configuration;

namespace TuracoChorus.Tests.Configuration;

public class CognitoOptionsReaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> ValidValues() => new()
    {
        ["Cognito:UserPoolId"] = "pool-123",
        ["Cognito:Region"] = "us-east-1",
        ["Cognito:AppClientId"] = "client-abc",
        ["Cognito:TokenType"] = "AccessToken",
    };

    [Fact]
    public void Read_ReturnsOptions_WhenAllRequiredKeysPresent()
    {
        var options = CognitoOptionsReader.Read(Build(ValidValues()));

        Assert.Equal("pool-123", options.UserPoolId);
        Assert.Equal("us-east-1", options.Region);
        Assert.Equal("client-abc", options.AppClientId);
        Assert.Equal(CognitoTokenType.AccessToken, options.TokenType);
    }

    [Fact]
    public void Read_DefaultsUserIdClaimToSub_WhenOmitted()
    {
        var options = CognitoOptionsReader.Read(Build(ValidValues()));

        Assert.Equal("sub", options.UserIdClaim);
    }

    [Fact]
    public void Read_UsesConfiguredUserIdClaim_WhenPresent()
    {
        var values = ValidValues();
        values["Cognito:UserIdClaim"] = "custom_claim";

        var options = CognitoOptionsReader.Read(Build(values));

        Assert.Equal("custom_claim", options.UserIdClaim);
    }

    [Fact]
    public void Read_Throws_NamingTheMissingKey()
    {
        var values = ValidValues();
        values.Remove("Cognito:UserPoolId");

        var ex = Assert.Throws<InvalidOperationException>(() => CognitoOptionsReader.Read(Build(values)));
        Assert.Contains("Cognito:UserPoolId", ex.Message);
    }

    [Fact]
    public void Read_Throws_ListingValidTokenTypes_WhenInvalid()
    {
        var values = ValidValues();
        values["Cognito:TokenType"] = "RefreshToken";

        var ex = Assert.Throws<InvalidOperationException>(() => CognitoOptionsReader.Read(Build(values)));
        Assert.Contains("IdToken", ex.Message);
        Assert.Contains("AccessToken", ex.Message);
    }
}
