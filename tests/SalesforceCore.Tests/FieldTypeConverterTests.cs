using FluentAssertions;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Utilities;
using Xunit;

namespace SalesforceCore.Tests;

public class FieldTypeConverterTests
{
    [Fact]
    public void ConvertToApiValue_EncryptedString_ReturnsDBNull()
    {
        var field = new SObjectField { Type = "encryptedstring", Name = "Secret" };
        var result = FieldTypeConverter.ConvertToApiValue(field, "some value");
        result.Should().Be(DBNull.Value);
    }

    [Fact]
    public void ConvertToApiValue_NillableEmpty_ReturnsNull()
    {
        var field = new SObjectField { Type = "string", Name = "Optional", Nillable = true };
        var result = FieldTypeConverter.ConvertToApiValue(field, "");
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertToApiValue_NotNillableEmpty_ReturnsEmptyString()
    {
        var field = new SObjectField { Type = "string", Name = "Required", Nillable = false };
        var result = FieldTypeConverter.ConvertToApiValue(field, "");
        result.Should().Be(string.Empty);
    }

    [Theory]
    [InlineData("boolean", "true", true)]
    [InlineData("boolean", "false", false)]
    [InlineData("boolean", "1", true)]
    [InlineData("boolean", "0", false)] // Standard bool parsing might not handle "0" as false unless custom logic, checking implementation
    [InlineData("boolean", "on", true)]
    [InlineData("boolean", "yes", true)]
    public void ConvertToApiValue_Boolean_ParsesCorrectly(string type, string input, bool expected)
    {
        var field = new SObjectField { Type = type, Name = "Flag" };
        var result = FieldTypeConverter.ConvertToApiValue(field, input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ConvertToApiValue_Double_ParsesCorrectly()
    {
        var field = new SObjectField { Type = "double", Name = "Amount" };
        var result = FieldTypeConverter.ConvertToApiValue(field, "123.45");
        result.Should().Be(123.45);
    }

    [Fact]
    public void ConvertToApiValue_Percent_ParsesCorrectly()
    {
        // Salesforce expects percent as actual number (e.g. 50.0 for 50%), input logic might vary but implementation says it keeps it as double
        var field = new SObjectField { Type = "percent", Name = "Rate" };
        var result = FieldTypeConverter.ConvertToApiValue(field, "50.5");
        result.Should().Be(50.5);
    }

    [Fact]
    public void ConvertToApiValue_Date_ParsesCorrectly()
    {
        var field = new SObjectField { Type = "date", Name = "Birthdate" };
        // Assuming input is localized or ISO
        var result = FieldTypeConverter.ConvertToApiValue(field, "2023-01-01");
        result.Should().Be("2023-01-01");
    }

    [Fact]
    public void FormatForDisplay_Currency_FormatsCorrectly()
    {
        var field = new SObjectField { Type = "currency", Name = "Price", Scale = 2 };
        var result = FieldTypeConverter.FormatForDisplay(field, 1234.56);
        
        // Check for common currency formats
        var validFormats = new[] { "1,234.56", "1.234,56", "1234.56" };
        validFormats.Any(f => result.Contains(f)).Should().BeTrue($"result '{result}' should contain one of the valid number formats");
    }

    [Fact]
    public void ConvertToInputValue_Date_FormatsForInput()
    {
        var field = new SObjectField { Type = "date", Name = "Date" };
        var date = new DateTime(2023, 12, 31);
        var result = FieldTypeConverter.ConvertToInputValue(field, date);
        result.Should().Be("2023-12-31");
    }
}
