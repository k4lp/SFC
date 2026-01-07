using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using SalesforceCore.Services.Query;
using Xunit;

namespace SalesforceCore.Tests;

public class SoqlHavingConditionTests
{
    [Fact]
    public void Having_AggregateCondition_ShouldRenderHavingClause()
    {
        var soql = SoqlBuilder.From("Opportunity")
            .Select("StageName")
            .GroupBy("StageName")
            .Having(SoqlAggregate.Count().GreaterThan(5))
            .Build();

        soql.Should().Contain("HAVING COUNT() > 5");
    }

    [Fact]
    public void Having_CompoundAggregateCondition_ShouldRenderCombinedClause()
    {
        var condition = SoqlAggregateCondition.And(
            SoqlAggregate.Sum("Amount").GreaterThan(1000),
            SoqlAggregate.CountDistinct("Id").GreaterThan(1));

        var soql = SoqlBuilder.From("Opportunity")
            .Select("StageName")
            .GroupBy("StageName")
            .Having(condition)
            .Build();

        soql.Should().Contain("HAVING (SUM(Amount) > 1000) AND (COUNT_DISTINCT(Id) > 1)");
    }

    [Fact]
    public void Having_RawClause_ShouldRequireExplicitUnsafeOptIn()
    {
        var act = () =>
        {
            var builder = SoqlBuilder.From("Account")
                .Select("Id")
                .GroupBy("Id");
            InvokeUnsafeHaving(builder, "COUNT() > 1", allowUnsafe: false);
        };

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Raw HAVING clauses are unsafe*");
    }

    [Fact]
    public void Having_RawClause_WithUnsafeOptIn_ShouldRenderClause()
    {
        var builder = SoqlBuilder.From("Account")
            .Select("Id")
            .GroupBy("Id");
        InvokeUnsafeHaving(builder, "COUNT() > 1", allowUnsafe: true);
        var soql = builder.Build();

        soql.Should().Contain("HAVING COUNT() > 1");
    }

    [Fact]
    public void AggregateCountDistinct_ShouldRequireField()
    {
        var act = () => SoqlAggregate.CountDistinct("").GreaterThan(1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*requires a field name*");
    }

    private static void InvokeUnsafeHaving(SoqlBuilder builder, string condition, bool allowUnsafe)
    {
        var method = typeof(SoqlBuilder).GetMethod("Having", new[] { typeof(string), typeof(bool) });
        if (method == null)
        {
            throw new InvalidOperationException("Unable to locate raw Having(string, bool) overload.");
        }

        try
        {
            method.Invoke(builder, new object?[] { condition, allowUnsafe });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }
}
