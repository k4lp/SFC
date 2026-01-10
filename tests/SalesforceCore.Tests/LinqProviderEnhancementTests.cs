using System;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Query;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Tests for LINQ provider enhancements: DateTime member mapping and locking clauses.
/// </summary>
public class LinqProviderEnhancementTests
{
    [SalesforceObject("Account")]
    public class Account
    {
        public string? Id { get; set; }

        [SalesforceField("Name")]
        public string? Name { get; set; }

        [SalesforceField("CreatedDate")]
        public DateTime CreatedDate { get; set; }

        [SalesforceField("LastModifiedDate")]
        public DateTime? LastModifiedDate { get; set; }
    }

    #region DateTime Member Translation Tests

    [Fact]
    public void Translate_DateTimeYear_ShouldGenerateCalendarYear()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate.Year == 2024);
        result.Should().Contain("CALENDAR_YEAR(CreatedDate)");
        result.Should().Contain("2024");
    }

    [Fact]
    public void Translate_DateTimeMonth_ShouldGenerateCalendarMonth()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate.Month == 12);
        result.Should().Contain("CALENDAR_MONTH(CreatedDate)");
        result.Should().Contain("12");
    }

    [Fact]
    public void Translate_DateTimeDay_ShouldGenerateDayInMonth()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate.Day > 15);
        result.Should().Contain("DAY_IN_MONTH(CreatedDate)");
        result.Should().Contain("15");
    }

    [Fact]
    public void Translate_DateTimeHour_ShouldGenerateHourInDay()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate.Hour >= 9);
        result.Should().Contain("HOUR_IN_DAY(CreatedDate)");
    }

    [Fact]
    public void Translate_DateTimeDayOfYear_ShouldGenerateDayInYear()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate.DayOfYear == 100);
        result.Should().Contain("DAY_IN_YEAR(CreatedDate)");
        result.Should().Contain("100");
    }

    [Fact]
    public void Translate_CombinedDateConditions_ShouldGenerateAndClause()
    {
        var result = SoqlExpressionVisitor.Translate<Account>(
            a => a.CreatedDate.Year == 2024 && a.CreatedDate.Month > 6);

        result.Should().Contain("CALENDAR_YEAR(CreatedDate)");
        result.Should().Contain("CALENDAR_MONTH(CreatedDate)");
        result.Should().Contain("AND");
    }

    #endregion

    #region Nullable DateTime Tests

    [SalesforceObject("Contact")]
    public class Contact
    {
        public string? Id { get; set; }

        [SalesforceField("LastModifiedDate")]
        public DateTime? LastModifiedDate { get; set; }

        [SalesforceField("Birthday")]
        public DateTimeOffset? Birthday { get; set; }
    }

    [Fact]
    public void Translate_NullableDateTimeYear_ShouldGenerateCalendarYear()
    {
        // Nullable DateTime property access should work the same
        var result = SoqlExpressionVisitor.Translate<Contact>(c => c.LastModifiedDate!.Value.Year == 2024);
        result.Should().Contain("CALENDAR_YEAR");
    }

    [Fact]
    public void Translate_NullableDateTimeOffsetMonth_ShouldGenerateCalendarMonth()
    {
        // Nullable DateTimeOffset property access should work
        var result = SoqlExpressionVisitor.Translate<Contact>(c => c.Birthday!.Value.Month == 6);
        result.Should().Contain("CALENDAR_MONTH");
    }

    [Fact]
    public void Translate_DateTimeLiteralComparison_ShouldFormatUtc()
    {
        // Ensure DateTime literals are properly formatted
        var testDate = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate > testDate);
        result.Should().Contain("2024-01-15T10:30:00Z");
    }

    [Fact]
    public void Translate_DateTimeOffsetWithOffset_ShouldNormalizeToUtc()
    {
        // Non-UTC DateTimeOffset should be converted to UTC
        // 14:00 at +05:30 (IST) = 08:30 UTC
        var testDate = new DateTimeOffset(2024, 1, 15, 14, 0, 0, TimeSpan.FromHours(5.5));
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate > testDate);
        result.Should().Contain("2024-01-15T08:30:00Z");
    }

    #endregion
}
