using TuracoChorus.Adapters.DynamoDb.Audit;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

internal static class DynamoDbAskAuditLoggerOptionsReader
{
    public static DynamoDbAskAuditLoggerOptions Read(IConfiguration configuration)
        => new(RequireString(configuration, "DynamoDb:Audit:TableName"));
}
