using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Query;
using Xunit;

namespace SalesforceCore.Tests.V22;

public enum V221Stage { Open, [SalesforceValue("Closed Won")] ClosedWon }

[SalesforceObject("Opportunity")]
public class V221Opp
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public V221Stage StageName { get; set; }
    public V221Stage? ForecastStage { get; set; }
    public double? Probability { get; set; }
    public float? Score { get; set; }
}

/// <summary>Regression tests for defects found when revalidating the 2.2.0 LINQ release.</summary>
public class LinqRevalidationV221Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V221Opp> Q => _ex.Query<V221Opp>();
    private const string Cols = "SELECT Id, Name, StageName, ForecastStage, Probability, Score FROM Opportunity";

    [Fact] public async Task NonNullableEnum_SendsPicklistValue_NotOrdinal()
    {
        await Q.Where(o => o.StageName == V221Stage.ClosedWon).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (StageName = 'Closed Won')");
        await Q.Where(o => V221Stage.Open != o.StageName).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (StageName != 'Open')");
        var captured = V221Stage.ClosedWon;
        await Q.Where(o => o.StageName == captured && o.ForecastStage == V221Stage.Open).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE ((StageName = 'Closed Won') AND (ForecastStage = 'Open'))");
    }

    [Fact] public async Task EnumArrayContains_BecomesInList()
    {
        var stages = new[] { V221Stage.Open, V221Stage.ClosedWon };
        await Q.Where(o => stages.Contains(o.StageName)).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE StageName IN ('Open', 'Closed Won')");
        await Q.Where(o => !stages.Contains(o.StageName)).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE StageName NOT IN ('Open', 'Closed Won')");
    }

    [Fact] public async Task FloatingPoint_NeverUsesExponentNotation()
    {
        await Q.Where(o => o.Probability > 0.0000001).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (Probability > 0.0000001)");
        await Q.Where(o => o.Probability < 1e20).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (Probability < 100000000000000000000)");
        await Q.Where(o => o.Score >= 0.25f).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (Score >= 0.25)");
        var nan = double.NaN;
        var act = () => Q.Where(o => o.Probability > nan).ToListAsync();
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact] public async Task FieldToFieldComparison_Throws_InsteadOfInvalidSoql()
    {
        var act = () => Q.Where(o => o.Name == o.Id).ToListAsync();
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*two fields*");
        _ex.Calls.Should().BeEmpty();
    }

    [Fact] public async Task InlineSemiJoin_Works()
    {
        await _ex.Query<V22Account>().Where(a => _ex.Query<V22Contact>().Where(c => c.Email != null).Select(c => c.AccountId).Contains(a.Id)).ToListAsync();
        _ex.Last.Should().EndWith("FROM Account WHERE Id IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        await _ex.Query<V22Account>().Where(a => !_ex.Query<V22Contact>().Select(c => c.AccountId).Contains(a.Id)).ToListAsync();
        _ex.Last.Should().EndWith("FROM Account WHERE Id NOT IN (SELECT AccountId FROM Contact)");
    }

    [Fact] public void RowSetOperatorsAfterPaging_Throw()
    {
        var checks = new Action[]
        {
            () => Q.Take(5).Where(o => o.Name == "x").ToList(),
            () => Q.Skip(2).Where(o => o.Name == "x").ToList(),
            () => Q.Take(5).OrderBy(o => o.Name).ToList(),
            () => Q.Skip(3).Count(o => o.Name == "x"),
            () => Q.Take(3).Any(o => o.Name == "x"),
            () => Q.Take(3).First(o => o.Name == "x"),
        };
        foreach (var check in checks)
        {
            check.Should().Throw<NotSupportedException>().WithMessage("*after Take/Skip*");
        }

        _ex.Calls.Should().BeEmpty();
    }

    [Fact] public async Task PagingAfterFilterAndOrder_StillTranslates()
    {
        await Q.Where(o => o.Name == "x").OrderBy(o => o.Name).Skip(2).Take(3).ToListAsync();
        _ex.Last.Should().Be($"{Cols} WHERE (Name = 'x') ORDER BY Name ASC LIMIT 3 OFFSET 2");
        _ex.TotalSize = 10;
        (await Q.Where(o => o.Name == "x").Skip(3).CountAsync()).Should().Be(7);
        (await Q.Skip(1).Take(2).FirstOrDefaultAsync()).Should().BeNull();
    }

    [Fact] public async Task AggregatesAfterPaging_Throw()
    {
        var sum = () => _ex.Query<V22Account>().OrderByDescending(a => a.AnnualRevenue).Take(10).SumAsync(a => a.AnnualRevenue);
        await sum.Should().ThrowAsync<NotSupportedException>().WithMessage("*Take/Skip*");
        var max = () => _ex.Query<V22Account>().Skip(5).MaxAsync(a => a.AnnualRevenue);
        await max.Should().ThrowAsync<NotSupportedException>();
        _ex.Calls.Should().BeEmpty();
    }

    [Fact] public async Task OffsetAbove2000_ThrowsBeforeSending_ButCountStillWorks()
    {
        var act = () => Q.Skip(2001).ToListAsync();
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*2000*");
        _ex.Calls.Should().BeEmpty();
        await Q.Skip(2000).ToListAsync();
        _ex.Last.Should().EndWith("OFFSET 2000");
        _ex.TotalSize = 2500;
        (await Q.Skip(2400).CountAsync()).Should().Be(100);
    }
}
