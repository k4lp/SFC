using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Xunit;
using FluentAssertions;

namespace SalesforceCore.Tests;

/// <summary>
/// Unit tests for CSV parsing using CsvHelper.
/// Validates that RFC 4180 compliance is maintained for Salesforce Bulk API compatibility.
/// </summary>
public class BulkServiceCsvTests
{
    /// <summary>
    /// Helper method to parse CSV using CsvHelper (same configuration as BulkService).
    /// </summary>
    private static List<string[]> ParseCsvRows(string csv)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.None
        };

        var rows = new List<string[]>();
        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, config);

        // Read header
        csvReader.Read();
        csvReader.ReadHeader();
        var headers = csvReader.HeaderRecord ?? Array.Empty<string>();
        rows.Add(headers);

        // Read data rows
        while (csvReader.Read())
        {
            var row = new string[headers.Length];
            for (int i = 0; i < headers.Length; i++)
            {
                row[i] = csvReader.GetField(i) ?? string.Empty;
            }
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public void ParseCsvRows_SimpleCase_ShouldParseCorrectly()
    {
        // Arrange
        var csv = "Name,Age\nJohn,30\nJane,25";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new[] { "Name", "Age" });
        result[1].Should().BeEquivalentTo(new[] { "John", "30" });
        result[2].Should().BeEquivalentTo(new[] { "Jane", "25" });
    }

    [Fact]
    public void ParseCsvRows_QuotedComma_ShouldPreserveCommaInField()
    {
        // Arrange: Field contains a comma inside quotes
        var csv = "Name,Desc\n\"Widget, Small\",10";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(2);
        result[1][0].Should().Be("Widget, Small");
        result[1][1].Should().Be("10");
    }

    [Fact]
    public void ParseCsvRows_QuotedNewline_ShouldPreserveNewlineInField()
    {
        // Arrange: Field contains a newline inside quotes
        var csv = "Name,Desc\nItem1,\"Line1\nLine2\"";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(2);
        result[1][0].Should().Be("Item1");
        result[1][1].Should().Be("Line1\nLine2");
    }

    [Fact]
    public void ParseCsvRows_EscapedQuote_ShouldParseDoubledQuotes()
    {
        // Arrange: Escaped quotes (doubled quotes "" within quoted fields)
        var csv = "Name\n\"John \"\"The Rock\"\" Smith\"";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(2);
        result[1][0].Should().Be("John \"The Rock\" Smith");
    }

    [Fact]
    public void ParseCsvRows_CrlfLineEndings_ShouldHandleCorrectly()
    {
        // Arrange: Windows-style CRLF line endings
        var csv = "Name,Age\r\nJohn,30\r\nJane,25";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new[] { "Name", "Age" });
        result[1].Should().BeEquivalentTo(new[] { "John", "30" });
        result[2].Should().BeEquivalentTo(new[] { "Jane", "25" });
    }

    [Fact]
    public void ParseCsvRows_EmptyFields_ShouldPreserveEmptyStrings()
    {
        // Arrange: Empty fields
        var csv = "A,B,C\n1,,3\n,2,";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);
        result[1].Should().BeEquivalentTo(new[] { "1", "", "3" });
        result[2].Should().BeEquivalentTo(new[] { "", "2", "" });
    }

    [Fact]
    public void ParseCsvRows_QuotedFieldWithCrLf_ShouldPreserveNewlines()
    {
        // Arrange: Quoted field with CRLF inside
        var csv = "Name,Address\nJohn,\"123 Main St\r\nApt 4\r\nNew York\"";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(2);
        result[1][1].Should().Be("123 Main St\r\nApt 4\r\nNew York");
    }

    [Fact]
    public void ParseCsvRows_ComplexCase_ShouldParseCorrectly()
    {
        // Arrange: Complex case with multiple edge cases
        var csv = "Id,Name,Description,Notes\n" +
                  "001,\"Acme, Inc.\",\"A company\nwith multiple lines\",Simple\n" +
                  "002,Test,\"Has \"\"quotes\"\" inside\",\"Also, commas\"";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);

        // Row 1
        result[1][0].Should().Be("001");
        result[1][1].Should().Be("Acme, Inc.");
        result[1][2].Should().Be("A company\nwith multiple lines");
        result[1][3].Should().Be("Simple");

        // Row 2
        result[2][0].Should().Be("002");
        result[2][1].Should().Be("Test");
        result[2][2].Should().Be("Has \"quotes\" inside");
        result[2][3].Should().Be("Also, commas");
    }

    [Fact]
    public void ParseCsvRows_SingleColumn_ShouldParseCorrectly()
    {
        // Arrange
        var csv = "Name\nJohn\nJane";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new[] { "Name" });
        result[1].Should().BeEquivalentTo(new[] { "John" });
        result[2].Should().BeEquivalentTo(new[] { "Jane" });
    }

    [Fact]
    public void ParseCsvRows_UnicodeContent_ShouldPreserveUnicode()
    {
        // Arrange: Unicode characters
        var csv = "Name,City\n日本語,東京\nПривет,Москва";

        // Act
        var result = ParseCsvRows(csv);

        // Assert
        result.Should().HaveCount(3);
        result[1].Should().BeEquivalentTo(new[] { "日本語", "東京" });
        result[2].Should().BeEquivalentTo(new[] { "Привет", "Москва" });
    }

    [Fact]
    public void CsvHelper_RFC4180Compliance_HandlesAllEdgeCases()
    {
        // This test validates that CsvHelper correctly handles all RFC 4180 requirements
        // which is critical for Salesforce Bulk API 2.0 compatibility

        // RFC 4180 Requirements:
        // 1. Each record is located on a separate line
        // 2. The last record may or may not have an ending line break
        // 3. There may be an optional header line
        // 4. Fields may be enclosed in double quotes
        // 5. Fields containing line breaks, double quotes, or commas must be enclosed in double quotes
        // 6. Double quotes within a quoted field must be escaped by doubling them

        var csv = "sf__Id,sf__Created,Name,Description\n" +
                  "001Xx000001ABC,true,\"Test Record\",\"Line1\nLine2\"\n" +
                  "001Xx000001DEF,false,\"Acme, Corp\",\"Has \"\"quotes\"\"\"\n" +
                  "001Xx000001GHI,true,Simple,Normal";

        var result = ParseCsvRows(csv);

        result.Should().HaveCount(4);
        result[0].Should().BeEquivalentTo(new[] { "sf__Id", "sf__Created", "Name", "Description" });
        result[1][2].Should().Be("Test Record");
        result[1][3].Should().Be("Line1\nLine2");
        result[2][2].Should().Be("Acme, Corp");
        result[2][3].Should().Be("Has \"quotes\"");
        result[3][2].Should().Be("Simple");
    }
}
