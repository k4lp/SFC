using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.Tests.V26;

[Flags]
public enum Interests { None = 0, Golf = 1, [SalesforceValue("Fine Dining")] FineDining = 2, Travel = 4 }

public enum Rating { Hot, [SalesforceValue("Very Warm")] VeryWarm }

public class MatrixRecord
{
    public string? Id { get; set; }
    public bool IsActive__c { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Rating? Rating { get; set; }
    public string[]? Hobbies__c { get; set; }
    public List<string>? Tags__c { get; set; }
    public Interests Interests__c { get; set; }
    public decimal? AnnualRevenue { get; set; }
    public decimal? Discount__c { get; set; }
    public double? Score__c { get; set; }
    public int? NumberOfEmployees { get; set; }
    public long? Big__c { get; set; }
    public DateOnly? CloseDate { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTimeOffset? LastModifiedDate { get; set; }
    public TimeOnly? Opens__c { get; set; }
    public string? OwnerId { get; set; }
    public SalesforceAddress? BillingAddress { get; set; }
    public SalesforceLocation? Location__c { get; set; }
    public byte[]? Body { get; set; }
    public string? Secret__c { get; set; }
    public JsonNode? AnyValue__c { get; set; }
}

/// <summary>
/// One row per Salesforce field type (Object Reference, "Field Types"; REST API Developer Guide, "Valid Date and
/// DateTime Formats"; SOQL and SOSL Reference, "Date Formats and Date Literals"; Bulk API 2.0, "Valid Date Format in
/// Records"). Checked against the specs, not a live org.
/// </summary>
public class DataTypeMatrixTests
{
    // Shape of a REST "Get a record" response body (REST API Developer Guide sample + compound fields).
    private const string DocSampleRecord = """
    {
      "attributes": { "type": "Account", "url": "/services/data/v62.0/sobjects/Account/001D000000IRFmaIAH" },
      "Id": "001D000000IRFmaIAH",
      "IsActive__c": true,
      "Name": "O\u2019Brien & \"Sons\"",
      "Description": "line1\nline2",
      "Rating": "Very Warm",
      "Hobbies__c": "Golf;Fine Dining",
      "Tags__c": "a;b;c",
      "Interests__c": "Golf;Travel",
      "AnnualRevenue": 12345678901234567.89,
      "Discount__c": 12.5,
      "Score__c": 0.1234567890123456,
      "NumberOfEmployees": 250,
      "Big__c": 9007199254740993,
      "CloseDate": "2024-02-29",
      "CreatedDate": "2024-01-02T03:04:05.000+0000",
      "LastModifiedDate": "2024-01-02T03:04:05.123+0000",
      "Opens__c": "09:30:00.000Z",
      "OwnerId": "005D0000001AbCdIAK",
      "BillingAddress": { "city": "San Francisco", "country": "US", "countryCode": "US", "geocodeAccuracy": "Address",
                          "latitude": 37.793846, "longitude": -122.394837, "postalCode": "94105", "state": "CA",
                          "stateCode": "CA", "street": "1 Market St\nSuite 300" },
      "Location__c": { "latitude": 37.7938, "longitude": -122.3948 },
      "Body": "SGVsbG8=",
      "Secret__c": "****1234",
      "AnyValue__c": "free text"
    }
    """;

    [Fact]
    public void Read_DocSampleRecord_EveryFieldType()
    {
        var r = SalesforceMapper.FromSalesforce<MatrixRecord>(JsonNode.Parse(DocSampleRecord)!);

        r.Id.Should().Be("001D000000IRFmaIAH");
        r.IsActive__c.Should().BeTrue();
        r.Name.Should().Be("O\u2019Brien & \"Sons\"");
        r.Description.Should().Be("line1\nline2");
        r.Rating.Should().Be(Rating.VeryWarm);
        r.Hobbies__c.Should().Equal("Golf", "Fine Dining");
        r.Tags__c.Should().Equal("a", "b", "c");
        r.Interests__c.Should().Be(Interests.Golf | Interests.Travel);
        r.AnnualRevenue.Should().Be(12345678901234567.89m, "currency must not lose digits through double");
        r.Discount__c.Should().Be(12.5m);
        r.Score__c.Should().Be(0.1234567890123456);
        r.NumberOfEmployees.Should().Be(250);
        r.Big__c.Should().Be(9007199254740993L);
        r.CloseDate.Should().Be(new DateOnly(2024, 2, 29));
        r.CreatedDate.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        r.CreatedDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        r.LastModifiedDate.Should().Be(new DateTimeOffset(2024, 1, 2, 3, 4, 5, 123, TimeSpan.Zero));
        r.Opens__c.Should().Be(new TimeOnly(9, 30));
        r.OwnerId.Should().Be("005D0000001AbCdIAK");
        r.BillingAddress!.City.Should().Be("San Francisco");
        r.BillingAddress.Street.Should().Be("1 Market St\nSuite 300");
        r.BillingAddress.Latitude.Should().Be(37.793846m);
        r.BillingAddress.GeocodeAccuracy.Should().Be("Address");
        r.Location__c!.Longitude.Should().Be(-122.3948m);
        r.Body.Should().Equal("Hello"u8.ToArray());
        r.Secret__c.Should().Be("****1234", "encryptedstring comes back masked unless the user can view encrypted data");
        r.AnyValue__c!.GetValue<string>().Should().Be("free text");
    }

    [Fact]
    public void Read_NullsForEveryType()
    {
        var json = new JsonObject();
        foreach (var p in typeof(MatrixRecord).GetProperties()) json[p.Name] = null;
        json["Interests__c"] = null;
        var act = () => SalesforceMapper.FromSalesforce<MatrixRecord>(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void Write_TypedRecord_ProducesRestWireValues()
    {
        var rec = new MatrixRecord
        {
            IsActive__c = true,
            Name = "x",
            Rating = Rating.VeryWarm,
            Hobbies__c = new[] { "Golf", "Fine Dining" },
            Tags__c = new() { "a", "b" },
            Interests__c = Interests.Golf | Interests.FineDining,
            AnnualRevenue = 12345678901234567.89m,
            Score__c = 0.1234567890123456,
            CloseDate = new DateOnly(2024, 2, 29),
            CreatedDate = new DateTime(2024, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            LastModifiedDate = new DateTimeOffset(2024, 1, 2, 8, 34, 5, 6, TimeSpan.FromHours(5.5)),
            Opens__c = new TimeOnly(9, 30, 0, 250),
            BillingAddress = new SalesforceAddress { City = "SF" },
            Location__c = new SalesforceLocation { Latitude = 1 },
            Body = "Hello"u8.ToArray()
        };

        var body = SalesforceMapper.ToSalesforceDictionary(rec);
        var json = JsonNode.Parse(JsonSerializer.Serialize(body, SalesforceClient.JsonOptions))!.AsObject();

        json["IsActive__c"]!.GetValue<bool>().Should().BeTrue();
        json["Rating"]!.GetValue<string>().Should().Be("Very Warm");
        json["Hobbies__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining", "a JSON array is rejected for multipicklist");
        json["Tags__c"]!.GetValue<string>().Should().Be("a;b");
        json["Interests__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining");
        json["AnnualRevenue"]!.ToJsonString().Should().Be("12345678901234567.89");
        json["Score__c"]!.ToJsonString().Should().Be("0.1234567890123456");
        json["CloseDate"]!.GetValue<string>().Should().Be("2024-02-29");
        json["CreatedDate"]!.GetValue<string>().Should().Be("2024-01-02T03:04:05.006Z");
        json["LastModifiedDate"]!.GetValue<string>().Should().Be("2024-01-02T03:04:05.006Z");
        json["Opens__c"]!.GetValue<string>().Should().Be("09:30:00.250Z");
        json.ContainsKey("BillingAddress").Should().BeFalse("compound fields are read-only; write the components");
        json.ContainsKey("Location__c").Should().BeFalse();
        json["Body"]!.GetValue<string>().Should().Be("SGVsbG8=", "base64 fields are base64 strings in REST JSON");
    }

    [Theory]
    [InlineData(null, "NULL")]
    [InlineData(true, "TRUE")]
    [InlineData(false, "FALSE")]
    [InlineData(42, "42")]
    [InlineData(-9007199254740993L, "-9007199254740993")]
    [InlineData("O'Brien", @"'O\'Brien'")]
    [InlineData("a\\b", @"'a\\b'")]
    [InlineData("q\"t", "'q\\\"t'")]
    [InlineData("l1\nl2\r\t\b\f", @"'l1\nl2\r\t\b\f'")]
    [InlineData("O\u2019Brien", "'O\u2019Brien'")]
    public void SoqlLiteral_Scalars(object? value, string expected)
        => SalesforceWireFormat.SoqlLiteral(value).Should().Be(expected);

    [Fact]
    public void SoqlLiteral_TypedValues()
    {
        SalesforceWireFormat.SoqlLiteral(1234.5m).Should().Be("1234.5");
        SalesforceWireFormat.SoqlLiteral(0.1234567890123456789).Should().Be("0.12345678901234568");
        SalesforceWireFormat.SoqlLiteral(1e-7).Should().Be("0.0000001");
        SalesforceWireFormat.SoqlLiteral(1e20).Should().Be("100000000000000000000");
        SalesforceWireFormat.SoqlLiteral(0.1f).Should().Be("0.1");
        SalesforceWireFormat.SoqlLiteral(new DateOnly(2024, 2, 29)).Should().Be("2024-02-29");
        SalesforceWireFormat.SoqlLiteral(new DateTime(2024, 1, 2, 3, 4, 5, 999, DateTimeKind.Utc)).Should().Be("2024-01-02T03:04:05Z");
        SalesforceWireFormat.SoqlLiteral(new DateTimeOffset(2024, 1, 2, 8, 34, 5, TimeSpan.FromHours(5.5))).Should().Be("2024-01-02T03:04:05Z");
        SalesforceWireFormat.SoqlLiteral(Rating.VeryWarm).Should().Be("'Very Warm'");
        SalesforceWireFormat.SoqlLiteral(new[] { "a", "b'c" }).Should().Be(@"('a', 'b\'c')");
        SalesforceWireFormat.SoqlLiteral(new object?[] { 1, null, true }).Should().Be("(1, NULL, TRUE)");
    }

    [Fact]
    public void SoqlLiteral_IsCultureInvariant()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            SalesforceWireFormat.SoqlLiteral(1234.5m).Should().Be("1234.5");
            SalesforceWireFormat.SoqlLiteral(1.5).Should().Be("1.5");
            SalesforceWireFormat.CsvValue(1.5f).Should().Be("1.5");
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void NonFiniteNumbers_AreRejected()
    {
        ((Action)(() => SalesforceWireFormat.FormatNumber(double.NaN))).Should().Throw<ArgumentException>();
        ((Action)(() => SalesforceWireFormat.CsvValue(double.PositiveInfinity))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BulkCsvCells_EveryType()
    {
        SalesforceWireFormat.CsvValue(null).Should().BeEmpty();
        SalesforceWireFormat.CsvValue(true).Should().Be("true");
        SalesforceWireFormat.CsvValue(12345678901234567.89m).Should().Be("12345678901234567.89");
        SalesforceWireFormat.CsvValue(1e-7).Should().Be("0.0000001");
        SalesforceWireFormat.CsvValue(new DateOnly(2024, 2, 29)).Should().Be("2024-02-29");
        SalesforceWireFormat.CsvValue(new DateTime(2024, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc)).Should().Be("2024-01-02T03:04:05.006Z");
        SalesforceWireFormat.CsvValue(new TimeOnly(9, 30)).Should().Be("09:30:00.000Z");
        SalesforceWireFormat.CsvValue(Rating.VeryWarm).Should().Be("Very Warm");
        SalesforceWireFormat.CsvValue(Interests.Golf | Interests.Travel).Should().Be("Golf;Travel");
        SalesforceWireFormat.CsvValue(new[] { "a", "b" }).Should().Be("a;b");
        ((Action)(() => SalesforceWireFormat.CsvValue(new byte[] { 1 }))).Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData("2024-01-02T03:04:05.000+0000")]
    [InlineData("2024-01-02T03:04:05.000Z")]
    [InlineData("2024-01-02T03:04:05Z")]
    [InlineData("2024-01-02T08:34:05.000+05:30")]
    [InlineData("2024-01-02T08:34:05.000+0530")]
    [InlineData("2024-01-01T22:04:05.000-0500")]
    public void DateTime_ParsesEveryDocumentedForm(string text)
    {
        SalesforceWireFormat.TryParseDateTime(text, out var v).Should().BeTrue();
        v.UtcDateTime.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }

    [Fact]
    public void DateTime_Unspecified_IsTreatedAsUtc_Local_IsConverted()
    {
        SalesforceWireFormat.FormatDateTime(new DateTime(2024, 1, 2, 3, 4, 5)).Should().Be("2024-01-02T03:04:05.000Z");
        var local = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local);
        SalesforceWireFormat.FormatDateTime(local).Should().Be(local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("001D000000IRFma", "001D000000IRFmaIAH")] // REST API Developer Guide sample id
    [InlineData("00Q000000000000", "00Q000000000000EAA")] // Q (upper) at position 3 sets bit 2
    [InlineData("001D000000IRFmaIAH", "001D000000IRFmaIAH")]
    public void Id_15To18(string id, string expected)
        => SalesforceWireFormat.To18CharId(id).Should().Be(expected);

    [Fact]
    public void Id_Equality_RespectsCaseOf15CharIds()
    {
        SalesforceWireFormat.IdEquals("001D000000IRFma", "001D000000IRFmaIAH").Should().BeTrue();
        SalesforceWireFormat.IdEquals("001D000000IRFma", "001d000000IRFma").Should().BeFalse();
        ((Action)(() => SalesforceWireFormat.To18CharId("bad"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MultiPicklist_RoundTrip_Flags()
    {
        var text = SalesforceWireFormat.FormatMultiPicklist(Interests.FineDining | Interests.Travel);
        text.Should().Be("Fine Dining;Travel");
        SalesforceWireFormat.ParseFlags(text, typeof(Interests)).Should().Be(Interests.FineDining | Interests.Travel);
        SalesforceWireFormat.SplitMultiPicklist("").Should().BeEmpty();
        SalesforceWireFormat.SplitMultiPicklist(null).Should().BeEmpty();
        ((Action)(() => SalesforceWireFormat.ParseFlags("Golf;Bogus", typeof(Interests)))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Time_ParsesWithAndWithoutZ()
    {
        SalesforceWireFormat.TryParseTime("09:30:00.250Z", out var a).Should().BeTrue();
        a.Should().Be(new TimeOnly(9, 30, 0, 250));
        SalesforceWireFormat.TryParseTime("09:30:00", out var b).Should().BeTrue();
        b.Should().Be(new TimeOnly(9, 30));
    }

    [Fact]
    public void TypedJson_ReadsPlus0000Offsets()
    {
        var dto = JsonSerializer.Deserialize<Dto>("""{"CreatedDate":"2024-01-02T03:04:05.000+0000","D":"2024-02-29","T":"09:30:00.000Z"}""", SalesforceClient.ReadOptions)!;
        dto.CreatedDate.Should().Be(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        dto.D.Should().Be(new DateOnly(2024, 2, 29));
        dto.T.Should().Be(new TimeOnly(9, 30));
    }

    private sealed class Dto { public DateTime CreatedDate { get; set; } public DateOnly D { get; set; } public TimeOnly T { get; set; } }
}
