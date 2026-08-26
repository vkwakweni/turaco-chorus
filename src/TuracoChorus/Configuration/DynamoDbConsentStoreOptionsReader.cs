using TuracoChorus.Adapters.DynamoDb.Consent;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

internal static class DynamoDbConsentStoreOptionsReader
{
    public static DynamoDbConsentStoreOptions Read(IConfiguration configuration)
        => new(RequireString(configuration, "DynamoDb:Consent:TableName"));
}
