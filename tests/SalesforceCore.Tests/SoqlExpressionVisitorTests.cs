using Xunit;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Query;

namespace SalesforceCore.Tests;

/// <summary>
/// Unit tests for SoqlExpressionVisitor - LINQ to SOQL translation.
/// </summary>
public class SoqlExpressionVisitorTests
{
    #region Test Models

    [SalesforceObject("Account")]
    public class Account
    {
        public string? Id { get; set; }
        public string? Name { get; set; }

        [SalesforceField("Account_Number__c")]
        public string? AccountNumber { get; set; }

        [SalesforceField("AnnualRevenue")]
        public decimal? Revenue { get; set; }

        [SalesforceField("IsActive__c")]
        public bool IsActive { get; set; }

        [SalesforceField("CreatedDate")]
        public DateTimeOffset? CreatedDate { get; set; }

        [SalesforceField("NumberOfEmployees")]
        public int? EmployeeCount { get; set; }
    }

    #endregion

    [Fact]
    public void Translate_Equals_ShouldGenerateEqualsClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == "Acme");

        // Assert
        result.Should().Be("(Name = 'Acme')");
    }

    [Fact]
    public void Translate_NotEquals_ShouldGenerateNotEqualsClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name != "Acme");

        // Assert
        result.Should().Be("(Name != 'Acme')");
    }

    [Fact]
    public void Translate_GreaterThan_ShouldGenerateGreaterThanClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Revenue > 1000000);

        // Assert
        result.Should().Be("(AnnualRevenue > 1000000)");
    }

    [Fact]
    public void Translate_LessThanOrEqual_ShouldGenerateLessThanOrEqualClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.EmployeeCount <= 100);

        // Assert
        result.Should().Be("(NumberOfEmployees <= 100)");
    }

    [Fact]
    public void Translate_And_ShouldGenerateAndClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == "Acme" && a.IsActive == true);

        // Assert
        result.Should().Be("((Name = 'Acme') AND (IsActive__c = TRUE))");
    }

    [Fact]
    public void Translate_Or_ShouldGenerateOrClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == "Acme" || a.Name == "Beta");

        // Assert
        result.Should().Be("((Name = 'Acme') OR (Name = 'Beta'))");
    }

    [Fact]
    public void Translate_ComplexExpression_ShouldGenerateCorrectClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(
            a => (a.Name == "Acme" || a.Name == "Beta") && a.Revenue > 500000);

        // Assert
        result.Should().Contain("AND");
        result.Should().Contain("OR");
        result.Should().Contain("AnnualRevenue > 500000");
    }

    [Fact]
    public void Translate_NullEquals_ShouldGenerateNullCheck()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == null);

        // Assert
        result.Should().Be("(Name = NULL)");
    }

    [Fact]
    public void Translate_NullNotEquals_ShouldGenerateNotNullCheck()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name != null);

        // Assert
        result.Should().Be("(Name != NULL)");
    }

    [Fact]
    public void Translate_Boolean_ShouldGenerateBooleanValue()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.IsActive == true);

        // Assert
        result.Should().Be("(IsActive__c = TRUE)");
    }

    [Fact]
    public void Translate_BooleanFalse_ShouldGenerateFalseValue()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.IsActive == false);

        // Assert
        result.Should().Be("(IsActive__c = FALSE)");
    }

    [Fact]
    public void Translate_BooleanMember_ShouldGenerateEqualsTrue()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.IsActive);

        // Assert
        result.Should().Be("IsActive__c = TRUE");
    }

    [Fact]
    public void Translate_BooleanNegation_ShouldGenerateNotEqualsTrue()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => !a.IsActive);

        // Assert
        result.Should().Be("NOT (IsActive__c = TRUE)");
    }

    [Fact]
    public void Translate_StringContains_ShouldGenerateLikeClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.Contains("Acme"));

        // Assert
        result.Should().Be("Name LIKE '%Acme%'");
    }

    [Fact]
    public void Translate_StringStartsWith_ShouldGenerateLikeClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.StartsWith("Acme"));

        // Assert
        result.Should().Be("Name LIKE 'Acme%'");
    }

    [Fact]
    public void Translate_StringEndsWith_ShouldGenerateLikeClause()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.EndsWith("Inc"));

        // Assert
        result.Should().Be("Name LIKE '%Inc'");
    }

    [Fact]
    public void Translate_LocalVariable_ShouldUseVariableValue()
    {
        // Arrange
        var searchName = "Acme Corporation";

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == searchName);

        // Assert
        result.Should().Be("(Name = 'Acme Corporation')");
    }

    [Fact]
    public void Translate_LocalVariableList_ShouldGenerateInClause()
    {
        // Arrange
        var validNames = new List<string> { "Acme", "Beta", "Gamma" };

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => validNames.Contains(a.Name!));

        // Assert
        result.Should().Be("Name IN ('Acme', 'Beta', 'Gamma')");
    }

    [Fact]
    public void Translate_EmptyList_ShouldGenerateFalsePredicate()
    {
        // Arrange
        var emptyNames = new List<string>();

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => emptyNames.Contains(a.Name!));

        // Assert
        result.Should().Be("(Id = NULL)");
    }

    [Fact]
    public void Translate_ContainsConstantItem_ShouldThrow()
    {
        // Act
        Action act = () => SoqlExpressionVisitor.Translate<Account>(a => new[] { "A", "B" }.Contains("C"));

        // Assert
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Translate_MappedField_ShouldUseSalesforceFieldName()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.AccountNumber == "ACC001");

        // Assert
        result.Should().Be("(Account_Number__c = 'ACC001')");
    }

    [Fact]
    public void Translate_DecimalComparison_ShouldFormatCorrectly()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Revenue >= 1500000.50m);

        // Assert
        result.Should().Be("(AnnualRevenue >= 1500000.50)");
    }

    [Fact]
    public void Translate_DateTime_ShouldFormatCorrectly()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate > date);

        // Assert
        result.Should().Contain("2024-06-15T00:00:00Z");
    }

    [Fact]
    public void Translate_DateTimeOffset_WithOffset_ShouldNormalizeToUtc()
    {
        // Arrange
        // 01:00 at -05:00 is 06:00 UTC
        var date = new DateTimeOffset(2024, 6, 15, 1, 0, 0, TimeSpan.FromHours(-5));

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.CreatedDate > date);

        // Assert
        result.Should().Contain("2024-06-15T06:00:00Z");
    }

    [Fact]
    public void Translate_MultipleConditions_ShouldChainWithAnd()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(
            a => a.Name == "Acme" && a.IsActive == true && a.Revenue > 1000000);

        // Assert
        result.Should().Contain("Name = 'Acme'");
        result.Should().Contain("IsActive__c = TRUE");
        result.Should().Contain("AnnualRevenue > 1000000");
        result.Should().Contain("AND");
    }

    [Fact]
    public void Translate_StringWithSpecialChars_ShouldEscapeCorrectly()
    {
        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name == "Acme's \"Best\" Corp");

        // Assert
        // Should escape single quotes for SOQL
        result.Should().Contain("Acme''s");
    }

    [Fact]
    public void ToSoqlWhere_ExtensionMethod_ShouldWork()
    {
        // Arrange
        System.Linq.Expressions.Expression<Func<Account, bool>> predicate = a => a.Name == "Test";

        // Act
        var result = predicate.ToSoqlWhere();

        // Assert
        result.Should().Be("(Name = 'Test')");
    }

    #region LIKE Wildcard Escaping Tests

    [Fact]
    public void Translate_ContainsWithWildcardPercent_ShouldEscape()
    {
        // Arrange - user input contains % which is a SOQL wildcard
        var search = "100%";

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.Contains(search));

        // Assert - % should be escaped as \%
        result.Should().Contain("100\\%");
    }

    [Fact]
    public void Translate_ContainsWithWildcardUnderscore_ShouldEscape()
    {
        // Arrange - user input contains _ which is a single-char wildcard in SOQL
        var search = "test_value";

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.Contains(search));

        // Assert - _ should be escaped as \_
        result.Should().Contain("test\\_value");
    }

    [Fact]
    public void Translate_ContainsWithBackslash_ShouldEscape()
    {
        // Arrange - user input contains backslash
        var search = "path\\file";

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.Contains(search));

        // Assert - backslash should be escaped as \\
        result.Should().Contain("path\\\\file");
    }

    [Fact]
    public void Translate_StartsWithWildcardChars_ShouldEscape()
    {
        // Arrange
        var search = "50%_discount";

        // Act
        var result = SoqlExpressionVisitor.Translate<Account>(a => a.Name!.StartsWith(search));

        // Assert
        result.Should().Contain("50\\%\\_discount");
    }

    #endregion
}
