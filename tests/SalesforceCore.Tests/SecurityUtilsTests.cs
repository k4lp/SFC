using SalesforceCore.Utilities;
using Xunit;
using FluentAssertions;

namespace SalesforceCore.Tests;

public class SecurityUtilsTests
{
    [Theory]
    [InlineData("001000000000001", true)]
    [InlineData("001000000000001AAA", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("001", false)]
    [InlineData("001000000000001AAAA", false)]
    public void IsValidSalesforceId_ShouldValidateCorrectly(string? id, bool expected)
    {
        var result = SecurityUtils.IsValidSalesforceId(id);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Account", true)]
    [InlineData("Custom_Object__c", true)]
    [InlineData("My_Object_123__c", true)]
    [InlineData("Custom_Metadata__mdt", true)]
    [InlineData("Platform_Event__e", true)]
    [InlineData("Big_Object__b", true)]
    [InlineData("Invalid Object!", false)]
    [InlineData("DROP TABLE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidObjectName_ShouldValidateCorrectly(string? name, bool expected)
    {
        var result = SecurityUtils.IsValidObjectName(name);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Name", "Name")]
    [InlineData("Test's Value", "Test''s Value")]
    [InlineData("Test\\Value", "Test\\Value")]
    [InlineData("Line1\nLine2", "Line1\nLine2")] // Newlines are NOT escaped in SOQL string literals
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SanitizeForSoql_ShouldEscapeCorrectly(string? input, string expected)
    {
        var result = SecurityUtils.SanitizeForSoql(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("100%", "100\\%")]
    [InlineData("User_Name", "User\\_Name")]
    [InlineData("Normal", "Normal")]
    [InlineData("O'Reilly", "O''Reilly")]
    public void SanitizeSoqlLike_ShouldEscapeWildcards(string? input, string expected)
    {
        var result = SecurityUtils.SanitizeSoqlLike(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Account.Name", true)]
    [InlineData("Account.Owner.Name", true)]
    [InlineData("Custom_Field__c", true)]
    [InlineData("Custom_Field__r", true)]
    [InlineData("Custom_Field__pc", true)]
    [InlineData("Invalid-Field", false)]
    [InlineData("Field; DROP TABLE", false)]
    [InlineData("Field' OR '1'='1", false)]
    public void IsValidFieldName_ShouldValidateCorrectly(string? name, bool expected)
    {
        var result = SecurityUtils.IsValidFieldName(name);
        result.Should().Be(expected);
    }

    [Fact]
    public void SanitizeFieldList_ShouldRemoveInvalidFields()
    {
        var fields = new[] { "Name", "Invalid Field!", "Account.Name", "DROP TABLE", "Industry" };

        var result = SecurityUtils.SanitizeFieldList(fields);

        result.Should().BeEquivalentTo(new[] { "Name", "Account.Name", "Industry" });
    }

    [Theory]
    [InlineData("SELECT Id FROM Account", true)]
    [InlineData("  select Id from Account", true)]
    [InlineData("SELECT Name FROM Account WHERE Name = 'Test''--safe'", true)]
    [InlineData("DELETE FROM Account", false)]
    [InlineData("SELECT Id FROM Account; DROP TABLE", false)]
    [InlineData("SELECT Id FROM Account -- comment", false)]
    [InlineData("SELECT Id FROM Account /* comment */", false)]
    [InlineData("SELECT Name FROM Account WHERE Name = 'Unclosed", false)]
    public void TryValidateSoqlQuery_ShouldValidateCorrectly(string soql, bool expected)
    {
        var result = SecurityUtils.TryValidateSoqlQuery(soql, out _);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>", "&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;")]
    [InlineData("Normal Text", "Normal Text")]
    [InlineData("<div class=\"test\">Content</div>", "&lt;div class=&quot;test&quot;&gt;Content&lt;/div&gt;")]
    public void HtmlEncode_ShouldEncodeCorrectly(string input, string expected)
    {
        var result = SecurityUtils.HtmlEncode(input);
        result.Should().Be(expected);
    }

    #region Advanced Injection Protection Tests

    [Fact]
    public void SanitizeSoql_ShouldRemoveNullBytes()
    {
        // Null byte injection attempt
        var malicious = "Smith\0' OR '1'='1";
        
        var result = SecurityUtils.SanitizeSoql(malicious);
        
        // Null bytes should be removed
        result.Should().NotContain("\0");
        // Quotes should still be escaped
        result.Should().Contain("''");
    }

    [Fact]
    public void SanitizeSoql_ShouldNormalizeUnicodeQuoteHomoglyphs()
    {
        // Unicode apostrophe/quote homoglyphs that could bypass naive filtering
        var testCases = new[]
        {
            ("Smithʼ OR 1=1", "Smith'' OR 1=1"),  // U+02BC Modifier Letter Apostrophe
            ("Testʹ--", "Test''--"),              // U+02B9 Modifier Letter Prime  
            ("Value' OR", "Value'' OR"),          // U+2019 Right Single Quotation Mark
            ("Data' AND", "Data'' AND"),          // U+2018 Left Single Quotation Mark
            ("Name' DROP", "Name'' DROP"),        // U+FF07 Fullwidth Apostrophe
        };

        foreach (var (input, expected) in testCases)
        {
            var result = SecurityUtils.SanitizeSoql(input);
            result.Should().Be(expected, because: $"Unicode quote in '{input}' should be normalized and escaped");
        }
    }

    [Fact]
    public void SanitizeSoql_ShouldRemoveControlCharacters()
    {
        // Control character injection
        var withControls = "Normal\x01Text\x1FHere";
        
        var result = SecurityUtils.SanitizeSoql(withControls);
        
        result.Should().Be("NormalTextHere");
    }

    [Fact]
    public void SanitizeSoql_ShouldPreserveValidWhitespace()
    {
        // Valid whitespace should be preserved
        var withWhitespace = "Line 1\nLine 2\r\nLine 3\tTabbed";
        
        var result = SecurityUtils.SanitizeSoql(withWhitespace);
        
        result.Should().Be(withWhitespace);
    }

    [Theory]
    [InlineData("' OR '1'='1", true)]
    [InlineData("1=1", true)]
    [InlineData("'; DROP TABLE", true)]
    [InlineData("UNION SELECT * FROM User", true)]
    [InlineData("/* comment */", true)]
    [InlineData("Normal search term", false)]
    [InlineData("John Smith", false)]
    [InlineData("100% Complete", false)]
    public void ContainsSuspiciousPatterns_ShouldDetectInjectionAttempts(string input, bool expected)
    {
        var result = SecurityUtils.ContainsSuspiciousPatterns(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ContainsSuspiciousPatterns_ShouldDetectUnicodeQuoteHomoglyphs()
    {
        // Unicode quote that could be used for injection
        var unicodeInjection = "Smithʼ OR 1=1"; // Using U+02BC
        
        var result = SecurityUtils.ContainsSuspiciousPatterns(unicodeInjection);
        
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(123, true, "123")]
    [InlineData(123.45, true, "123.45")]
    [InlineData("456", true, "456")]
    [InlineData("12.34", true, "12.34")]
    [InlineData("-50", true, "-50")]
    [InlineData("not a number", false, "")]
    [InlineData("123' OR '1'='1", false, "")]
    [InlineData(null, false, "")]
    public void TryValidateNumeric_ShouldPreventInjection(object? input, bool expectedValid, string expectedValue)
    {
        var result = SecurityUtils.TryValidateNumeric(input, out var sanitized);
        
        result.Should().Be(expectedValid);
        if (expectedValid)
        {
            sanitized.Should().Be(expectedValue);
        }
    }

    [Theory]
    [InlineData(true, true, "TRUE")]
    [InlineData(false, true, "FALSE")]
    [InlineData("true", true, "TRUE")]
    [InlineData("false", true, "FALSE")]
    [InlineData("1", true, "TRUE")]
    [InlineData("0", true, "FALSE")]
    [InlineData("yes", true, "TRUE")]
    [InlineData("no", true, "FALSE")]
    [InlineData("TRUE' OR '1'='1", false, "")]
    [InlineData("maybe", false, "")]
    [InlineData(null, false, "")]
    public void TryValidateBoolean_ShouldPreventInjection(object? input, bool expectedValid, string expectedValue)
    {
        var result = SecurityUtils.TryValidateBoolean(input, out var sanitized);
        
        result.Should().Be(expectedValid);
        if (expectedValid)
        {
            sanitized.Should().Be(expectedValue);
        }
    }

    [Fact]
    public void TryValidateDateTime_ShouldFormatCorrectly()
    {
        var dateTime = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        
        var result = SecurityUtils.TryValidateDateTime(dateTime, out var sanitized);
        
        result.Should().BeTrue();
        sanitized.Should().Be("2024-06-15T10:30:00.000Z");
    }

    [Fact]
    public void TryValidateDateTime_ShouldRejectInjectionInDateString()
    {
        var maliciousDate = "2024-01-01' OR '1'='1";
        
        var result = SecurityUtils.TryValidateDateTime(maliciousDate, out _);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateStringLength_ShouldRejectOverlyLongInput()
    {
        var longString = new string('A', 5000);
        
        var result = SecurityUtils.ValidateStringLength(longString);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void SanitizeSoqlWithLengthCheck_ShouldThrowOnOverlyLongInput()
    {
        var longString = new string('A', 5000);
        
        var act = () => SecurityUtils.SanitizeSoqlWithLengthCheck(longString);
        
        act.Should().Throw<ArgumentException>()
            .WithMessage("*exceeds maximum allowed length*");
    }

    [Theory]
    [InlineData(null, "NULL")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    [InlineData(123, "123")]
    [InlineData("Test's Value", "'Test''s Value'")]
    public void FormatSoqlValue_ShouldFormatCorrectly(object? input, string expected)
    {
        var result = SecurityUtils.FormatSoqlValue(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatSoqlValue_ShouldSanitizeSalesforceId()
    {
        var id = "001000000000001AAA";
        
        var result = SecurityUtils.FormatSoqlValue(id);
        
        result.Should().Be($"'{id}'");
    }

    [Fact]
    public void SanitizeSoql_ShouldHandleComplexInjectionAttempt()
    {
        // Complex multi-vector injection attempt
        var attack = "Smith\0ʼ OR \x01'1'='1' UNION SELECT * FROM User --";
        
        var result = SecurityUtils.SanitizeSoql(attack);
        
        // Should be safe - all vectors neutralized
        result.Should().NotContain("\0");           // No null bytes
        result.Should().NotContain("\x01");         // No control chars
        result.Should().NotContain("ʼ");            // Unicode normalized
        result.Should().Contain("''");              // Quotes escaped
    }

    [Theory]
    [InlineData("' OR '1'='1' --")]
    [InlineData("'; DELETE FROM Account; --")]
    [InlineData("' UNION SELECT Name FROM User WHERE '1'='1")]
    [InlineData("1' AND Name LIKE '%admin%' --")]
    public void SanitizeSoql_ShouldNeutralizeCommonInjectionPatterns(string malicious)
    {
        var result = SecurityUtils.SanitizeSoql(malicious);
        
        // All single quotes should be doubled
        var originalQuotes = malicious.Count(c => c == '\'');
        var resultDoubleQuotes = CountSubstring(result, "''");
        
        resultDoubleQuotes.Should().Be(originalQuotes, 
            because: "each single quote should become a double quote");
    }

    private static int CountSubstring(string text, string pattern)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(pattern, i, StringComparison.Ordinal)) != -1)
        {
            count++;
            i += pattern.Length;
        }
        return count;
    }

    #endregion
}
