using TuracoChorus.Adapters.DynamoDb;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus.Configuration;

/// <summary>
/// Reads <see cref="DynamoDbLogDataSourceOptions"/> from config, including the one genuinely
/// polymorphic piece — <c>Dimensions</c>, a list of <see cref="DimensionSource"/> (either
/// <see cref="DirectAttributeSource"/> or <see cref="LookupSource"/>) — which has no built-in
/// IConfiguration binding support, so each entry is read manually via a "Type" discriminator.
/// </summary>
internal static class DynamoDbLogDataSourceOptionsReader
{
    private const string Prefix = "DynamoDb:LogData";

    public static DynamoDbLogDataSourceOptions Read(IConfiguration configuration) => new(
        TableName: RequireString(configuration, $"{Prefix}:TableName"),
        PartitionKeyAttribute: RequireString(configuration, $"{Prefix}:PartitionKeyAttribute"),
        PartitionKeyValueTemplate: RequireString(configuration, $"{Prefix}:PartitionKeyValueTemplate"),
        DateAttribute: RequireString(configuration, $"{Prefix}:DateAttribute"),
        Dimensions: ReadDimensions(configuration),
        SortKeyAttribute: configuration[$"{Prefix}:SortKeyAttribute"],
        EntrySortKeyPrefix: configuration[$"{Prefix}:EntrySortKeyPrefix"]);

    private static IReadOnlyList<DimensionConfig> ReadDimensions(IConfiguration configuration)
    {
        var result = new List<DimensionConfig>();
        foreach (var child in configuration.GetSection($"{Prefix}:Dimensions").GetChildren())
        {
            var path = $"{Prefix}:Dimensions:{child.Key}";
            var name = RequireString(configuration, $"{path}:Name");
            var type = RequireString(configuration, $"{path}:Type");

            DimensionSource source = type switch
            {
                "Direct" => new DirectAttributeSource(RequireField(configuration, path, type, "AttributeName")),
                "Lookup" => new LookupSource(
                    IdAttributeName: RequireField(configuration, path, type, "IdAttributeName"),
                    LookupPartitionKeyValueTemplate: RequireField(configuration, path, type, "LookupPartitionKeyValueTemplate"),
                    LookupSortKeyValueTemplate: RequireField(configuration, path, type, "LookupSortKeyValueTemplate"),
                    LookupNameAttribute: RequireField(configuration, path, type, "LookupNameAttribute"),
                    LookupTableName: configuration[$"{path}:LookupTableName"]),
                _ => throw new InvalidOperationException(
                    $"{path} has an unrecognized \"Type\" value \"{type}\" — must be \"Direct\" or \"Lookup\".")
            };

            result.Add(new DimensionConfig(name, source));
        }

        return result;
    }

    private static string RequireField(IConfiguration configuration, string path, string type, string field)
        => configuration[$"{path}:{field}"] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{path} has Type \"{type}\" but is missing \"{field}\".");
}
