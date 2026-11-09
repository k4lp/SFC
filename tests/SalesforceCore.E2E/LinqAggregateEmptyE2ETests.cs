using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Query;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Round 5: stacked LINQ operators feeding aggregates when nothing matches. Salesforce answers an aggregate query
/// over zero rows with one AggregateResult whose aliases are null (SOQL and SOSL Reference, "Aggregate Functions"),
/// and COUNT() with totalSize 0 and no records.
/// </summary>
public class LinqAggregateEmptyE2ETests
{
    private static void EmptyAggregate(FakeSalesforce fake, params string[] aliases)
    {
        var row = new JsonObject { ["attributes"] = new JsonObject { ["type"] = "AggregateResult" } };
        foreach (var a in aliases) row[a] = null;
        fake.QueryRows.Add(row);
    }

    private static string LastSoql(FakeSalesforce fake) =>
        Uri.UnescapeDataString(fake.Requests.Last(r => r.Contains("/query")).Split("q=")[1]).Replace('+', ' ');

    [Fact]
    public async Task StackedWhere_WithMultiSelectContains_ThenNullableMin_OverNoRows_ReturnsNull()
    {
        await using var h = await E2EHost.StartAsync();
        EmptyAggregate(h.Fake, "min");
        var typed = h.Get<ITypedDataService>();

        var min = await typed.Query<RtDeal>()
            .Where(d => d.Amount > 100m)
            .Where(d => d.Stage == RtStage.ClosedWon || d.Hobbies!.Contains("Golf"))
            .MinAsync(d => d.CloseDate);

        min.Should().BeNull();
        LastSoql(h.Fake).Should().Contain("MIN(CloseDate) min").And.Contain("StageName = 'Closed Won'").And.Contain("Hobbies__c INCLUDES ('Golf')");
    }

    [Fact]
    public async Task Sum_OverNoRows_IsZero_AndCountIsZero()
    {
        await using var h = await E2EHost.StartAsync();
        EmptyAggregate(h.Fake, "sum");
        var typed = h.Get<ITypedDataService>();

        var sum = await typed.Query<RtDeal>().Where(d => d.IsWon).SumAsync(d => d.Amount);
        sum.Should().Be(0m);

        h.Fake.QueryRows.Clear();
        (await typed.CountAsync<RtDeal>(d => d.Name == "nobody")).Should().Be(0);
        (await typed.AnyAsync<RtDeal>(d => d.Name == "nobody")).Should().BeFalse();
    }
}
