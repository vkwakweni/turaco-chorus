using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Models;
using TuracoChorus.Core.Ports;
using Xunit;

namespace TuracoChorus.Tests;

/// <summary>
/// Drives the real pipeline (rate limiter, then <c>OnRejected</c>, then the auditor) against the fake
/// adapters, with a limit of one <c>/ask</c> per minute so the second request is always rejected.
/// </summary>
public sealed class AskRateLimitAuditingEndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .UseSetting("UseFakeAdapters", "true")
            .UseSetting("RateLimiting:Ask:PerIpPerMinute", "1")
            .UseSetting("RateLimiting:Ask:GlobalPerDay", "1000"));

    public void Dispose() => _factory.Dispose();

    private HttpClient ClientFor(string? token)
    {
        ((FakeIdentityVerifier)_factory.Services.GetRequiredService<IIdentityVerifier>()).Register("valid-token", "user-1");
        var client = _factory.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private FakeAuditLogger Audit => (FakeAuditLogger)_factory.Services.GetRequiredService<IAuditLogger>();

    [Fact]
    public async Task SecondAsk_FromAValidCaller_Gets429WithRetryAfter_AndIsAuditedAsRateLimited()
    {
        var client = ClientFor("valid-token");

        var first = await client.PostAsJsonAsync("/ask", new { question = "first" });
        var second = await client.PostAsJsonAsync("/ask", new { question = "second" });

        // The first request was admitted (consent defaults to off, so it is a 403 denial).
        Assert.Equal(HttpStatusCode.Forbidden, first.StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"));
        Assert.Contains("Too many requests", await second.Content.ReadAsStringAsync());

        Assert.Equal(2, Audit.Entries.Count);
        Assert.Equal(AuditOutcome.Denied, Audit.Entries[0].Outcome);
        var rateLimited = Audit.Entries[1];
        Assert.Equal(AuditOutcome.RateLimited, rateLimited.Outcome);
        Assert.Equal("user-1", rateLimited.UserId);
        Assert.Equal("second", rateLimited.QueryText);
        Assert.Null(rateLimited.ConsentGranted);
    }

    [Fact]
    public async Task SecondAsk_WithoutACredential_Gets429_AndWritesNoRateLimitedEntry()
    {
        var client = ClientFor(token: null);

        await client.PostAsJsonAsync("/ask", new { question = "first" });
        var second = await client.PostAsJsonAsync("/ask", new { question = "second" });

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.DoesNotContain(Audit.Entries, e => e.Outcome == AuditOutcome.RateLimited);
        Assert.Equal(0, Audit.AttemptCount);
    }

    [Fact]
    public async Task Ask_WhenTheDataSourceFails_Returns500_AndAuditsFailed()
    {
        var client = ClientFor("valid-token");
        await _factory.Services.GetRequiredService<IConsentStore>().SetConsentAsync("user-1", granted: true);
        // Nothing is seeded in FakeLogDataSource, so fetching stats throws.

        var response = await client.PostAsJsonAsync("/ask", new { question = "will this fail?" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = Assert.Single(Audit.Entries);
        Assert.Equal(AuditOutcome.Failed, entry.Outcome);
        Assert.Equal("will this fail?", entry.QueryText);
        Assert.True(entry.ConsentGranted);
        Assert.Null(entry.AggregatedDataSent);
    }

    [Fact]
    public async Task StatsAndConsent_AreNeverRateLimited_OrAuditedAsRateLimited()
    {
        var client = ClientFor("valid-token");

        for (var i = 0; i < 5; i++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/consent")).StatusCode);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.GetAsync("/stats")).StatusCode);
        }

        Assert.DoesNotContain(Audit.Entries, e => e.Outcome == AuditOutcome.RateLimited);
    }
}
