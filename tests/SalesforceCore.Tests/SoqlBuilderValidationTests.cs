using System;
using FluentAssertions;
using SalesforceCore.Services.Query;
using Xunit;

namespace SalesforceCore.Tests;

public class SoqlBuilderValidationTests
{
    [Fact]
    public void WhereDateLiteral_WithNBasedLiteral_ShouldThrow()
    {
        var builder = SoqlBuilder.From("Account");

        Action act = () => builder.WhereDateLiteral("CreatedDate", DateLiteral.LAST_N_DAYS);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DateLiteralN*");
    }

    [Fact]
    public void DateLiteralCondition_WithNBasedLiteral_ShouldThrow()
    {
        Action act = () => SoqlCondition.DateLiteral("CreatedDate", DateLiteral.N_DAYS_AGO);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DateLiteralN*");
    }

    [Fact]
    public void WhereIncludes_EmptyValues_ShouldRenderAlwaysFalse()
    {
        var soql = SoqlBuilder.From("Contact")
            .Select("Id")
            .WhereIncludes("Languages", Array.Empty<string>())
            .Build();

        soql.Should().Contain("Id = NULL");
    }

    [Fact]
    public void WhereExcludes_EmptyValues_ShouldRenderAlwaysTrue()
    {
        var soql = SoqlBuilder.From("Contact")
            .Select("Id")
            .WhereExcludes("Languages", Array.Empty<string>())
            .Build();

        soql.Should().Contain("Id != NULL");
    }

    [Fact]
    public void SoqlCondition_IncludesEmpty_ShouldRenderAlwaysFalse()
    {
        var condition = SoqlCondition.Includes("Languages", Array.Empty<string>());

        condition.Render().Should().Be("Id = NULL");
    }

    [Fact]
    public void SoqlCondition_ExcludesEmpty_ShouldRenderAlwaysTrue()
    {
        var condition = SoqlCondition.Excludes("Languages", Array.Empty<string>());

        condition.Render().Should().Be("Id != NULL");
    }

    #region DateBetween Tests

    [Fact]
    public void WhereDateBetween_ShouldWrapInParentheses()
    {
        var soql = SoqlBuilder.From("Account")
            .Select("Id")
            .WhereDateBetween("CreatedDate", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 
                              new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc))
            .Build();

        // Should be wrapped in parentheses for correct precedence
        soql.Should().Contain("(CreatedDate >=");
        soql.Should().Contain("AND CreatedDate <=");
    }

    [Fact]
    public void WhereDateBetween_WithInvertedDates_ShouldSwap()
    {
        // Arrange - end date is before start date (inverted)
        var startDate = new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        var soql = SoqlBuilder.From("Account")
            .Select("Id")
            .WhereDateBetween("CreatedDate", startDate, endDate)
            .Build();

        // Should swap dates: the earlier date should come first in the query
        var jan1Index = soql.IndexOf("2024-01-01");
        var dec31Index = soql.IndexOf("2024-12-31");
        
        // The first date in the SOQL should be January (the earlier date after swap)
        jan1Index.Should().BeLessThan(dec31Index);
    }

    [Fact]
    public void SoqlCondition_DateBetween_ShouldWrapInParentheses()
    {
        var condition = SoqlCondition.DateBetween("CreatedDate", 
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        var rendered = condition.Render();
        
        rendered.Should().StartWith("(");
        rendered.Should().EndWith(")");
    }

    [Fact]
    public void SoqlCondition_DateBetween_WithInvertedDates_ShouldSwap()
    {
        // Inverted: Dec 31 before Jan 1
        var condition = SoqlCondition.DateBetween("CreatedDate",
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var rendered = condition.Render();
        
        // After swap, Jan 1 should appear before Dec 31 in the output
        var jan1Index = rendered.IndexOf("2024-01-01");
        var dec31Index = rendered.IndexOf("2024-12-31");
        
        jan1Index.Should().BeLessThan(dec31Index);
    }

    #endregion
}
