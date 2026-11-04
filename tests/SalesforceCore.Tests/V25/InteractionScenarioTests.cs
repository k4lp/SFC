using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Query;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Tests.V22;
using Xunit;

namespace SalesforceCore.Tests.V25;

/// <summary>Scenario tests combining features fixed across 2.1-2.4 (LINQ x enums x paging, caches x nesting, writes x enums).</summary>
public class InteractionScenarioTests
{
    private static MemoryCacheProvider NewCache() => new(
        new MemoryCache(new MemoryCacheOptions()), Options.Create(new SalesforceOptions()), NullLogger<MemoryCacheProvider>.Instance);

    // ---- cache: nested GetOrCreateAsync ---------------------------------------------------------------------------

    [Fact]
    public async Task NestedGetOrCreate_ForManyOtherKeys_NeverDeadlocks()
    {
        // 2.4.0 used 32 hash-selected stripes; with 300 inner keys at least one shares the outer key's stripe
        // (probability of none: (31/32)^300 < 0.01%), and that inner wait never completed.
        var cache = NewCache();
        var outer = cache.GetOrCreateAsync<string>("layout_form_account_" + Guid.NewGuid(), async ct =>
        {
            for (var i = 0; i < 300; i++)
            {
                await cache.GetOrCreateAsync<string>($"describe_{i}_{Guid.NewGuid()}", _ => Task.FromResult<string?>("d"), cancellationToken: ct);
            }
            return "form";
        });
        (await Task.WhenAny(outer, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(outer);
        (await outer).Should().Be("form");
    }

    [Fact]
    public async Task GetOrCreate_IsSingleFlight_AcrossProviderInstances_AndCancelledWaiterLeavesNoLock()
    {
        var shared = new MemoryCache(new MemoryCacheOptions());
        MemoryCacheProvider P() => new(shared, Options.Create(new SalesforceOptions()), NullLogger<MemoryCacheProvider>.Instance);
        var key = "k" + Guid.NewGuid();
        var calls = 0;
        var gate = new TaskCompletionSource();
        async Task<string?> Factory(CancellationToken _) { Interlocked.Increment(ref calls); await gate.Task; return "v"; }

        var first = P().GetOrCreateAsync<string>(key, Factory);
        using var cts = new CancellationTokenSource();
        var cancelled = P().GetOrCreateAsync<string>(key, Factory, cancellationToken: cts.Token);
        var third = P().GetOrCreateAsync<string>(key, Factory);
        cts.Cancel();
        await FluentActions.Awaiting(() => cancelled).Should().ThrowAsync<OperationCanceledException>();
        gate.SetResult();

        (await first).Should().Be("v");
        (await third).Should().Be("v");
        calls.Should().Be(1, "scoped providers used to have their own locks, so each scope ran the factory");
        MemoryCacheProvider.KeyLocks.Count.Should().Be(0);
    }

    // ---- LINQ: enum aggregates, paging arithmetic -----------------------------------------------------------------

    [Fact]
    public async Task EnumAggregates_MapPicklistValues_InsteadOfSilentlyReturningFirstMember()
    {
        var ex = new RecordingExecutor();
        ex.Rows.Add(new JsonObject { ["StageName"] = "Closed Won" });
        ex.Rows.Add(new JsonObject { ["StageName"] = "Open" });
        (await ex.Query<V221Opp>().DistinctAsync(o => o.StageName)).Should().Equal(V221Stage.ClosedWon, V221Stage.Open);

        var max = new RecordingExecutor();
        max.Rows.Add(new JsonObject { ["max"] = "Closed Won" });
        (await max.Query<V221Opp>().MaxAsync(o => o.StageName)).Should().Be(V221Stage.ClosedWon);

        var unknown = new RecordingExecutor();
        unknown.Rows.Add(new JsonObject { ["StageName"] = "Negotiation" });
        await FluentActions.Awaiting(() => unknown.Query<V221Opp>().DistinctAsync(o => o.StageName))
            .Should().ThrowAsync<InvalidCastException>().WithMessage("*Negotiation*");
    }

    [Fact]
    public async Task CountAndAny_AfterTakeSkip_ApplyPagingArithmetically_WhilePredicatesStillThrow()
    {
        var ex = new RecordingExecutor { TotalSize = 10 };
        (await ex.Query<V221Opp>().Where(o => o.StageName == V221Stage.ClosedWon).Take(3).CountAsync()).Should().Be(3);
        ex.Last.Should().Be("SELECT COUNT() FROM Opportunity WHERE (StageName = 'Closed Won')");
        (await ex.Query<V221Opp>().Skip(8).Take(5).CountAsync()).Should().Be(2);
        (await ex.Query<V221Opp>().Skip(10).AnyAsync()).Should().BeFalse();
        (await ex.Query<V221Opp>().Skip(9).AnyAsync()).Should().BeTrue();
        await FluentActions.Awaiting(() => ex.Query<V221Opp>().Take(3).AllAsync(o => o.Name == "a")).Should().ThrowAsync<NotSupportedException>();
        await FluentActions.Awaiting(() => ex.Query<V221Opp>().Take(3).DistinctAsync(o => o.StageName)).Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task AllAsync_WithEnumAndSemiJoin_NegatesIntoValidSoql()
    {
        var ex = new RecordingExecutor { TotalSize = 0 };
        (await ex.Query<V221Opp>().AllAsync(o => o.StageName == V221Stage.ClosedWon)).Should().BeTrue();
        ex.Last.Should().Be("SELECT COUNT() FROM Opportunity WHERE NOT (StageName = 'Closed Won')");
        await ex.Query<V22Account>().AllAsync(a => ex.Query<V22Contact>().Select(c => c.AccountId).Contains(a.Id));
        ex.Last.Should().Be("SELECT COUNT() FROM Account WHERE Id NOT IN (SELECT AccountId FROM Contact)");
    }

    // ---- write paths: enums and floating point -------------------------------------------------------------------

    [Fact]
    public void RequestJson_WritesEnumsAsPicklistValues_AndTypedResponsesReadThemBack()
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?> { ["StageName"] = V221Stage.ClosedWon, ["N"] = (V221Stage?)null },
            SalesforceClient.JsonOptions);
        json.Should().Be("{\"StageName\":\"Closed Won\",\"N\":null}");
        JsonSerializer.Deserialize<V221Opp>("{\"StageName\":\"Closed Won\",\"ForecastStage\":1}", SalesforceClient.ReadOptions)!
            .Should().BeEquivalentTo(new { StageName = V221Stage.ClosedWon, ForecastStage = (V221Stage?)V221Stage.ClosedWon });
        // Library enums with their own [JsonConverter] keep it.
        JsonSerializer.Serialize(Models.Data.BulkOperation.upsert, SalesforceClient.JsonOptions).Should().Be("\"upsert\"");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e30)]
    public void BulkCsv_UnrepresentableDouble_ThrowsArgumentException_NotOverflow(double value)
        => FluentActions.Invoking(() => BulkService.FormatCsvValue(value)).Should().Throw<ArgumentException>();

    [Fact]
    public void BulkCsv_FormatsEnumsAndTinyDoubles_WithoutExponent()
    {
        BulkService.FormatCsvValue(V221Stage.ClosedWon).Should().Be("Closed Won");
        BulkService.FormatCsvValue(1e-7).Should().Be("0.0000001");
    }
}
