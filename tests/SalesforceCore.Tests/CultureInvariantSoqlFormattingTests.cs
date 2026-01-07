using System.Globalization;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Query;
using SalesforceCore.Services.Query;
using Xunit;

namespace SalesforceCore.Tests;

public class CultureInvariantSoqlFormattingTests
{
    [SalesforceObject("Account")]
    private class Account
    {
        [SalesforceField("AnnualRevenue")]
        public decimal Revenue { get; set; }
    }

    [Fact]
    public void SoqlBuilder_ShouldFormatDecimalUsingInvariantCulture()
    {
        WithCulture("fr-FR", () =>
        {
            var soql = new SoqlBuilder("Account")
                .Select("Id")
                .WhereGreaterThan("AnnualRevenue", 1.23m)
                .Build();

            soql.Should().Contain("1.23");
            soql.Should().NotContain("1,23");
        });
    }

    [Fact]
    public void SoqlCondition_ShouldFormatDecimalUsingInvariantCulture()
    {
        WithCulture("fr-FR", () =>
        {
            var soql = new SoqlBuilder("Account")
                .Select("Id")
                .WhereCondition(SoqlCondition.GreaterThan("AnnualRevenue", 1.23m))
                .Build();

            soql.Should().Contain("AnnualRevenue > 1.23");
            soql.Should().NotContain("1,23");
        });
    }

    [Fact]
    public void SoqlExpressionVisitor_ShouldFormatDecimalUsingInvariantCulture()
    {
        WithCulture("fr-FR", () =>
        {
            var where = SoqlExpressionVisitor.Translate<Account>(a => a.Revenue > 1.23m);
            where.Should().Contain("1.23");
            where.Should().NotContain("1,23");
        });
    }

    private static void WithCulture(string cultureName, Action action)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}

