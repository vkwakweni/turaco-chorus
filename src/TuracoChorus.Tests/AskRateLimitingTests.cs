using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using TuracoChorus.RateLimiting;
using Xunit;

namespace TuracoChorus.Tests;

public sealed class AskRateLimitingTests
{
    private static HttpContext ContextFor(string path, string ip)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    private static bool TryAcquire(System.Threading.RateLimiting.PartitionedRateLimiter<HttpContext> limiter, HttpContext context)
    {
        using var lease = limiter.AttemptAcquire(context);
        return lease.IsAcquired;
    }

    [Fact]
    public void Ask_AllowsUpToThePerIpLimit_ThenRejectsTheSameAddress()
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 3, GlobalPerDay: 100));

        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.False(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
    }

    [Fact]
    public void Ask_CountsEachAddressSeparately()
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 1, GlobalPerDay: 100));

        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.False(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.2")));
    }

    [Fact]
    public void Ask_GlobalDailyCapAppliesAcrossAddresses()
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 100, GlobalPerDay: 3));

        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.2")));
        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.3")));
        Assert.False(TryAcquire(limiter, ContextFor("/ask", "203.0.113.4")));
    }

    [Fact]
    public void Ask_PathMatchIgnoresCase()
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 1, GlobalPerDay: 100));

        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.False(TryAcquire(limiter, ContextFor("/ASK", "203.0.113.1")));
    }

    [Theory]
    [InlineData("/stats")]
    [InlineData("/consent")]
    public void OtherRoutes_AreNeverLimited(string path)
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 1, GlobalPerDay: 1));

        for (var i = 0; i < 20; i++)
        {
            Assert.True(TryAcquire(limiter, ContextFor(path, "203.0.113.1")));
        }
    }

    [Fact]
    public void AskRequestsThatAreRejected_DoNotUseUpOtherRoutes()
    {
        var limiter = AskRateLimiting.Create(new AskRateLimitOptions(PerIpPerMinute: 1, GlobalPerDay: 1));

        Assert.True(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.False(TryAcquire(limiter, ContextFor("/ask", "203.0.113.1")));
        Assert.True(TryAcquire(limiter, ContextFor("/stats", "203.0.113.1")));
    }
}

public sealed class AskRateLimitOptionsReaderTests
{
    private static IConfiguration ConfigWith(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Read_WithNothingSet_UsesTheDefaults()
    {
        var options = AskRateLimitOptionsReader.Read(ConfigWith());

        Assert.Equal(new AskRateLimitOptions(), options);
        Assert.Equal(10, options.PerIpPerMinute);
        Assert.Equal(500, options.GlobalPerDay);
    }

    [Fact]
    public void Read_WithValuesSet_UsesThem()
    {
        var options = AskRateLimitOptionsReader.Read(ConfigWith(
            ("RateLimiting:Ask:PerIpPerMinute", "4"),
            ("RateLimiting:Ask:GlobalPerDay", "60")));

        Assert.Equal(4, options.PerIpPerMinute);
        Assert.Equal(60, options.GlobalPerDay);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void Read_WithAnInvalidValue_ThrowsNamingTheKey(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AskRateLimitOptionsReader.Read(ConfigWith(("RateLimiting:Ask:PerIpPerMinute", value))));

        Assert.Contains("RateLimiting:Ask:PerIpPerMinute", ex.Message);
    }
}
