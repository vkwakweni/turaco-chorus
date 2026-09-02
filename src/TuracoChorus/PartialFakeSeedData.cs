using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;
using static TuracoChorus.Configuration.ConfigReading;

namespace TuracoChorus;

/// <summary>
/// Registers exactly one test credential/user so a partial-fake deployment (see
/// AdapterRegistration's "UseFakeIdentityVerifier"/"UseFakeLogDataSource") is actually usable.
/// Distinct from <see cref="DevSeedData"/>: that path is DEBUG-only and tied to the all-fake
/// "UseFakeAdapters" switch; this one runs in Release too, since a real deployment — not just
/// local dev — can run with the identity/log-data ports faked. Consent/Audit/Insight stay real
/// in this mode, so nothing here touches those ports.
/// </summary>
internal static class PartialFakeSeedData
{
    public static void Seed(WebApplication app)
    {
        var credential = RequireString(app.Configuration, "FakeAuth:TestCredential");
        var userId = RequireString(app.Configuration, "FakeAuth:TestUserId");

        var identityVerifier = (FakeIdentityVerifier)app.Services.GetRequiredService<IIdentityVerifier>();
        identityVerifier.Register(credential, userId);

        var logDataSource = (FakeLogDataSource)app.Services.GetRequiredService<ILogDataSource>();
        logDataSource.Seed(userId, new AggregateStats(
            SourceId: userId,
            Range: new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)),
            TotalEntries: 3,
            Dimensions:
            [
                new Dimension("category", [new DimensionBucket("mood", 3)]),
                new Dimension("date", [new DimensionBucket("2026-01-15", 3)])
            ]));
    }
}
