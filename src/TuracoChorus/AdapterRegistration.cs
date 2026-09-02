using System.Runtime.CompilerServices;
using Amazon;
using Amazon.DynamoDBv2;
using TuracoChorus.Adapters.Claude;
using TuracoChorus.Adapters.Cognito;
using TuracoChorus.Adapters.DynamoDb;
using TuracoChorus.Adapters.DynamoDb.Audit;
using TuracoChorus.Adapters.DynamoDb.Consent;
using TuracoChorus.Adapters.Gemini;
using TuracoChorus.Configuration;
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Ports;
using static TuracoChorus.Configuration.ConfigReading;

[assembly: InternalsVisibleTo("TuracoChorus.Tests")]

namespace TuracoChorus;

/// <summary>
/// Registers the five port implementations. "UseFakeAdapters" is the all-fake shortcut (a
/// no-credentials demo run — every port fake, nothing below this method runs). Independent of
/// that, "UseFakeIdentityVerifier"/"UseFakeLogDataSource" let just those two ports run fake while
/// Consent/Audit/Insight stay real — for a deployment deliberately kept independent of whichever
/// upstream application would otherwise supply real Cognito/DynamoDB values (see
/// artifacts/ecs-deployment.md's "per-port fake/real split"). Every config read here happens
/// eagerly, before the app finishes building, so a missing/malformed value fails the process at
/// startup rather than on the first request.
/// </summary>
internal static class AdapterRegistration
{
    public static void AddPortAdapters(this WebApplicationBuilder builder)
    {
        if (builder.Configuration.GetValue<bool>("UseFakeAdapters"))
        {
            builder.Services.AddSingleton<IIdentityVerifier, FakeIdentityVerifier>();
            builder.Services.AddSingleton<IConsentStore, FakeConsentStore>();
            builder.Services.AddSingleton<ILogDataSource, FakeLogDataSource>();
            builder.Services.AddSingleton<IInsightEngine, FakeInsightEngine>();
            builder.Services.AddSingleton<IAuditLogger, FakeAuditLogger>();
            return;
        }

        builder.Services.AddHttpClient();

        var region = RequireString(builder.Configuration, "Aws:Region");
        builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient(
            new AmazonDynamoDBConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(region) }));

        if (builder.Configuration.GetValue<bool>("UseFakeIdentityVerifier"))
        {
            builder.Services.AddSingleton<IIdentityVerifier, FakeIdentityVerifier>();
        }
        else
        {
            builder.Services.AddSingleton(CognitoOptionsReader.Read(builder.Configuration));
            builder.Services.AddSingleton<IIdentityVerifier, CognitoIdentityVerifier>();
        }

        if (builder.Configuration.GetValue<bool>("UseFakeLogDataSource"))
        {
            builder.Services.AddSingleton<ILogDataSource, FakeLogDataSource>();
        }
        else
        {
            builder.Services.AddSingleton(DynamoDbLogDataSourceOptionsReader.Read(builder.Configuration));
            builder.Services.AddSingleton<ILogDataSource, DynamoDbLogDataSource>();
        }

        builder.Services.AddSingleton(DynamoDbConsentStoreOptionsReader.Read(builder.Configuration));
        builder.Services.AddSingleton<IConsentStore, DynamoDbConsentStore>();

        builder.Services.AddSingleton(DynamoDbAskAuditLoggerOptionsReader.Read(builder.Configuration));
        builder.Services.AddSingleton<IAuditLogger, DynamoDbAskAuditLogger>();

        AddInsightEngine(builder);
    }

    private static void AddInsightEngine(WebApplicationBuilder builder)
    {
        switch (InsightProviderReader.Read(builder.Configuration))
        {
            case InsightProvider.Claude:
                var claudeOptions = ClaudeInsightEngineOptionsReader.Read(builder.Configuration);
                builder.Services.AddSingleton<IInsightEngine>(sp => new ClaudeInsightEngine(
                    claudeOptions, sp.GetRequiredService<IHttpClientFactory>().CreateClient()));
                break;
            case InsightProvider.Gemini:
                var geminiOptions = GeminiInsightEngineOptionsReader.Read(builder.Configuration);
                builder.Services.AddSingleton<IInsightEngine>(sp => new GeminiInsightEngine(
                    geminiOptions, sp.GetRequiredService<IHttpClientFactory>().CreateClient()));
                break;
        }
    }
}
