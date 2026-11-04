using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

[Flags]
public enum WcInterests { None = 0, Golf = 1, [SalesforceCore.Attributes.SalesforceValue("Fine Dining")] FineDining = 2 }

/// <summary>
/// Contract tests against the fake Salesforce server: the exact bytes SFC puts on the wire for each data type and
/// header, per the REST API, Bulk API 2.0 and SOQL references. Spec-checked, not run against a live org.
/// </summary>
public class WireComplianceE2ETests
{
    [Fact]
    public async Task CreateRecord_SendsRestWireFormatForEveryType()
    {
        await using var h = await E2EHost.StartAsync();
        var id = await h.Get<IDataService>().CreateRecordAsync("Account", new Dictionary<string, object?>
        {
            ["Name"] = "O'Brien",
            ["Active__c"] = true,
            ["Hobbies__c"] = new[] { "Golf", "Fine Dining" },
            ["Interests__c"] = WcInterests.Golf | WcInterests.FineDining,
            ["AnnualRevenue"] = 12345678901234567.89m,
            ["Score__c"] = 0.1234567890123456,
            ["CloseDate"] = new DateOnly(2024, 2, 29),
            ["Start__c"] = new DateTimeOffset(2024, 1, 2, 8, 34, 5, 6, TimeSpan.FromHours(5.5)),
            ["Opens__c"] = new TimeOnly(9, 30),
            ["Blob__c"] = new byte[] { 72, 105 },
            ["Empty__c"] = null
        });

        var body = h.Fake.Records[id];
        body["Hobbies__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining");
        body["Interests__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining");
        body["AnnualRevenue"]!.ToJsonString().Should().Be("12345678901234567.89");
        body["Score__c"]!.ToJsonString().Should().Be("0.1234567890123456");
        body["CloseDate"]!.GetValue<string>().Should().Be("2024-02-29");
        body["Start__c"]!.GetValue<string>().Should().Be("2024-01-02T03:04:05.006Z");
        body["Opens__c"]!.GetValue<string>().Should().Be("09:30:00.000Z");
        body["Blob__c"]!.GetValue<string>().Should().Be("SGk=");
    }

    [Fact]
    public async Task Query_SendsQueryOptionsAndCallOptionsHeaders_OnEveryPage()
    {
        await using var h = await E2EHost.StartAsync(new()
        {
            ["Salesforce:QueryBatchSize"] = "500",
            ["Salesforce:ClientName"] = "SFC-Tests"
        });
        for (var i = 0; i < 5; i++) h.Fake.QueryRows.Add(new JsonObject { ["Id"] = $"001{i:D15}" });

        var page = await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        await h.Get<IDataService>().QueryNextAsync(page.NextRecordsUrl!);

        var queryCalls = h.Fake.SforceHeaders.Where(x => x.Line.Contains("/query")).ToList();
        queryCalls.Should().HaveCountGreaterThanOrEqualTo(2);
        queryCalls.Should().OnlyContain(x => x.Headers["Sforce-Query-Options"] == "batchSize=500");
        queryCalls.Should().OnlyContain(x => x.Headers["Sforce-Call-Options"] == "client=SFC-Tests");
    }

    [Fact]
    public async Task Query_WithoutOptions_SendsNoSforceQueryOrCallHeaders()
    {
        await using var h = await E2EHost.StartAsync();
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        h.Fake.SforceHeaders.Where(x => x.Line.Contains("/query"))
            .Should().OnlyContain(x => !x.Headers.ContainsKey("Sforce-Query-Options") && !x.Headers.ContainsKey("Sforce-Call-Options"));
    }

    [Fact]
    public async Task Query_LongerThan100kCharacters_IsRejectedBeforeSending()
    {
        await using var h = await E2EHost.StartAsync();
        var soql = "SELECT Id FROM Account WHERE Name IN (" + string.Join(",", Enumerable.Range(0, 12000).Select(i => $"'n{i:D5}'")) + ")";
        var before = h.Fake.QueryRequests;
        var act = () => h.Get<IDataService>().QueryAsync(soql);
        await act.Should().ThrowAsync<ArgumentException>();
        h.Fake.QueryRequests.Should().Be(before);
    }

    [Fact]
    public async Task BulkIngest_UsesLfLineEnding_AndInvariantTypedCells()
    {
        await using var h = await E2EHost.StartAsync();
        await h.Get<IBulkService>().InsertAsync("Account", new[]
        {
            new Dictionary<string, object?>
            {
                ["Name"] = "a,b", ["Amount__c"] = 1234.5m, ["Close__c"] = new DateOnly(2024, 2, 29),
                ["Hobbies__c"] = new[] { "Golf", "Fine Dining" }, ["Active__c"] = false
            }
        });

        h.Fake.LastIngestJobRequest!["lineEnding"]!.GetValue<string>().Should().Be("LF");
        h.Fake.LastBulkUpload.Should().NotContain("\r\n", "the job declares lineEnding=LF; CRLF data is rejected");
        h.Fake.LastBulkUpload.Should().Contain("\"a,b\",1234.5,2024-02-29,Golf;Fine Dining,false\n");
    }
}
