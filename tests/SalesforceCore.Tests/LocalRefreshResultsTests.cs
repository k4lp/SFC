using FluentAssertions;
using SalesforceCore.AspNetCore.Extensions;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>Unreleased review: the per-session refresh-reuse record stays bounded under unique-session churn.</summary>
public class LocalRefreshResultsTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static AspNetCoreTokenProvider.RefreshSnapshot Snap(string t) => new(t, "rt-" + t, "https://acme.my.salesforce.com");

    [Fact]
    public void UniqueSessionChurn_WithinTtl_IsCappedAndKeepsTheNewestEntries()
    {
        var time = new ManualTime();
        var r = new AspNetCoreTokenProvider.LocalRefreshResults(time, maxEntries: 1000, ttl: TimeSpan.FromMinutes(2));
        for (var i = 0; i < 50_000; i++)
        {
            time.Now = time.Now.AddMilliseconds(1);
            r.Set("s" + i, "old" + i, Snap("new" + i));
        }

        r.Count.Should().BeLessThanOrEqualTo(1000);
        r.TryGet("s49999", "old49999", out var newest).Should().BeTrue("the session refreshed last is still served to its queued requests");
        newest.AccessToken.Should().Be("new49999");
        r.TryGet("s0", "old0", out _).Should().BeFalse();
    }

    [Fact]
    public void ExpiredEntries_AreNotServed_AndAreRemovedOnLaterActivity()
    {
        var time = new ManualTime();
        var r = new AspNetCoreTokenProvider.LocalRefreshResults(time, maxEntries: 1000, ttl: TimeSpan.FromMinutes(2));
        for (var i = 0; i < 500; i++) r.Set("s" + i, "old", Snap("t" + i));

        time.Now = time.Now.AddMinutes(3);
        r.TryGet("s1", "old", out _).Should().BeFalse("older than the TTL");
        r.Count.Should().Be(0, "a lookup after the TTL sweeps expired tokens out of memory");
    }

    [Fact]
    public void Entry_IsOnlyServedForTheSameRejectedToken()
    {
        var r = new AspNetCoreTokenProvider.LocalRefreshResults();
        r.Set("s", "stale-A", Snap("fresh"));
        r.TryGet("s", "stale-B", out _).Should().BeFalse();
        r.TryGet("other", "stale-A", out _).Should().BeFalse();
        r.TryGet("s", "stale-A", out var hit).Should().BeTrue();
        hit.RefreshToken.Should().Be("rt-fresh");
    }
}
