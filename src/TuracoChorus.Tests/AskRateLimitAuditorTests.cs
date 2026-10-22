using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using TuracoChorus.Core.Fakes;
using TuracoChorus.Core.Models;
using TuracoChorus.RateLimiting;
using Xunit;

namespace TuracoChorus.Tests;

public sealed class AskRateLimitAuditorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Rig
    {
        public FakeIdentityVerifier Verifier { get; } = new();
        public FakeAuditLogger Audit { get; } = new();

        public Rig() => Verifier.Register("valid-token", "user-1");

        public AskRateLimitAuditor Auditor => new(
            Verifier, Audit, new FixedTimeProvider(Now), NullLogger<AskRateLimitAuditor>.Instance);
    }

    private static HttpContext RejectedContext(string? authorization, string? body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/ask";
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        context.Request.Body = new MemoryStream(body is null ? [] : Encoding.UTF8.GetBytes(body));
        return context;
    }

    [Fact]
    public async Task RecordAsync_WithAValidCredential_WritesOneRateLimitedEntry()
    {
        var rig = new Rig();

        await rig.Auditor.RecordAsync(
            RejectedContext("Bearer valid-token", """{"question":"How active was I last week?"}"""),
            CancellationToken.None);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal("How active was I last week?", entry.QueryText);
        Assert.Equal(AuditOutcome.RateLimited, entry.Outcome);
        Assert.Null(entry.ConsentGranted);
        Assert.Null(entry.AggregatedDataSent);
        Assert.Equal(Now, entry.Timestamp);
    }

    [Fact]
    public async Task RecordAsync_MatchesTheQuestionPropertyIgnoringCase()
    {
        var rig = new Rig();

        await rig.Auditor.RecordAsync(
            RejectedContext("Bearer valid-token", """{"Question":"capitalised"}"""),
            CancellationToken.None);

        Assert.Equal("capitalised", Assert.Single(rig.Audit.Entries).QueryText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer not-a-registered-token")]
    [InlineData("Bearer ")]
    public async Task RecordAsync_WithoutAValidCredential_WritesNothingAndDoesNotThrow(string? authorization)
    {
        var rig = new Rig();

        await rig.Auditor.RecordAsync(
            RejectedContext(authorization, """{"question":"hello"}"""),
            CancellationToken.None);

        Assert.Equal(0, rig.Audit.AttemptCount);
    }

    [Fact]
    public async Task RecordAsync_WhenTheVerifierThrows_WritesNothingAndDoesNotThrow()
    {
        var audit = new FakeAuditLogger();
        var auditor = new AskRateLimitAuditor(
            new ThrowingVerifier(), audit, new FixedTimeProvider(Now), NullLogger<AskRateLimitAuditor>.Instance);

        await auditor.RecordAsync(RejectedContext("Bearer valid-token", """{"question":"hello"}"""), CancellationToken.None);

        Assert.Equal(0, audit.AttemptCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"other":"x"}""")]
    [InlineData("""{"question":42}""")]
    [InlineData("""{"question":null}""")]
    [InlineData("""["question"]""")]
    public async Task RecordAsync_WithAMissingOrUnusableBody_StillAuditsTheAttemptWithAnEmptyQuestion(string? body)
    {
        var rig = new Rig();

        await rig.Auditor.RecordAsync(RejectedContext("Bearer valid-token", body), CancellationToken.None);

        var entry = Assert.Single(rig.Audit.Entries);
        Assert.Equal("user-1", entry.UserId);
        Assert.Equal(string.Empty, entry.QueryText);
        Assert.Equal(AuditOutcome.RateLimited, entry.Outcome);
    }

    [Fact]
    public async Task RecordAsync_WithABodyOverTheCap_StillAuditsTheAttemptWithAnEmptyQuestion()
    {
        var rig = new Rig();
        var oversized = $$"""{"question":"{{new string('x', AskRateLimitAuditor.MaxBodyBytes)}}"}""";

        await rig.Auditor.RecordAsync(RejectedContext("Bearer valid-token", oversized), CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(rig.Audit.Entries).QueryText);
    }

    [Fact]
    public async Task RecordAsync_WithABodyJustUnderTheCap_KeepsTheQuestion()
    {
        var rig = new Rig();
        var question = new string('x', AskRateLimitAuditor.MaxBodyBytes - 20);
        var body = $$"""{"question":"{{question}}"}""";
        Assert.True(Encoding.UTF8.GetByteCount(body) <= AskRateLimitAuditor.MaxBodyBytes);

        await rig.Auditor.RecordAsync(RejectedContext("Bearer valid-token", body), CancellationToken.None);

        Assert.Equal(question, Assert.Single(rig.Audit.Entries).QueryText);
    }

    [Fact]
    public async Task RecordAsync_WhenTheAuditWriteThrows_IsSwallowedSoTheCallerStillGetsItsResponse()
    {
        var rig = new Rig();
        rig.Audit.ExceptionToThrow = new TimeoutException("audit table down");

        await rig.Auditor.RecordAsync(
            RejectedContext("Bearer valid-token", """{"question":"hello"}"""),
            CancellationToken.None);

        Assert.Equal(1, rig.Audit.AttemptCount);
    }

    [Fact]
    public async Task RecordAsync_TwoRejectionsFromTheSameCaller_WriteTwoEntries()
    {
        // Documents the accepted trade-off: no de-duplication, the per-address limit is the only bound.
        var rig = new Rig();

        await rig.Auditor.RecordAsync(RejectedContext("Bearer valid-token", """{"question":"a"}"""), CancellationToken.None);
        await rig.Auditor.RecordAsync(RejectedContext("Bearer valid-token", """{"question":"b"}"""), CancellationToken.None);

        Assert.Equal(["a", "b"], rig.Audit.Entries.Select(e => e.QueryText));
    }

    private sealed class ThrowingVerifier : TuracoChorus.Core.Ports.IIdentityVerifier
    {
        public Task<string> VerifyIdentityAsync(string rawCredential)
            => throw new InvalidOperationException("identity provider unreachable");
    }
}
