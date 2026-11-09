using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

public enum RtStage { Open, [SalesforceValue("Closed Won")] ClosedWon }

[Flags]
public enum RtInterests { None = 0, Golf = 1, [SalesforceValue("Fine Dining")] FineDining = 2 }

[SalesforceObject("Opportunity")]
public class RtDeal
{
    [SalesforceField("Id")] public string? Id { get; set; }
    [SalesforceField("Name")] public string? Name { get; set; }
    [SalesforceField("Amount")] public decimal? Amount { get; set; }
    [SalesforceField("Probability")] public double? Probability { get; set; }
    [SalesforceField("Count__c")] public int? Count { get; set; }
    [SalesforceField("CloseDate")] public DateOnly? CloseDate { get; set; }
    [SalesforceField("Start__c")] public DateTimeOffset? Start { get; set; }
    [SalesforceField("Opens__c")] public TimeOnly? Opens { get; set; }
    [SalesforceField("IsWon")] public bool IsWon { get; set; }
    [SalesforceField("StageName")] public RtStage Stage { get; set; }
    [SalesforceField("Interests__c")] public RtInterests Interests { get; set; }
    [SalesforceField("Hobbies__c")] public List<string>? Hobbies { get; set; }
}

/// <summary>
/// Round 5: typed write → Salesforce-formatted read → LINQ filter on the same values, through the real pipeline.
/// The read side is shaped by the REST API Developer Guide / Object Reference formats (dates "yyyy-MM-dd", datetimes
/// "yyyy-MM-ddTHH:mm:ss.SSS+0000", times "HH:mm:ss.SSSZ", multipicklist "A;B", numbers as JSON numbers), not by
/// echoing what SFC sent.
/// </summary>
public class DataTypeRoundTripE2ETests
{
    private static RtDeal Sample() => new()
    {
        Name = "O’Brien & \"Co\" ünïcode",
        Amount = 12345678901234567.89m,
        Probability = 0.1234567890123456,
        Count = -42,
        CloseDate = new DateOnly(2024, 2, 29),
        Start = new DateTimeOffset(2024, 1, 2, 8, 34, 5, 6, TimeSpan.FromHours(5.5)),
        Opens = new TimeOnly(9, 30, 15, 250),
        IsWon = true,
        Stage = RtStage.ClosedWon,
        Interests = RtInterests.Golf | RtInterests.FineDining,
        Hobbies = new() { "Golf", "Fine Dining" },
    };

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("hi-IN")]
    [InlineData("ar-SA")]
    public async Task TypedWrite_ThenSalesforceShapedRead_ThenLinqFilter_RoundTripsEveryType(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            await using var h = await E2EHost.StartAsync();
            var typed = h.Get<ITypedDataService>();
            var deal = Sample();

            var id = await typed.CreateAsync(deal);
            var sent = h.Fake.Records[id];
            sent["StageName"]!.GetValue<string>().Should().Be("Closed Won");
            sent["Interests__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining");
            sent["Hobbies__c"]!.GetValue<string>().Should().Be("Golf;Fine Dining");
            sent["Start__c"]!.GetValue<string>().Should().Be("2024-01-02T03:04:05.006Z");
            sent["Opens__c"]!.GetValue<string>().Should().Be("09:30:15.250Z");
            sent["Amount"]!.ToJsonString().Should().Be("12345678901234567.89");
            sent["Id"]!.GetValue<string>().Should().Be(id, "the fake stamps the new Id; a null Id from the model must not have been sent over it");

            // What Salesforce returns for that record (Salesforce's own formats).
            h.Fake.QueryRows.Add(JsonNode.Parse($$"""
            {"attributes":{"type":"Opportunity","url":"/services/data/v62.0/sobjects/Opportunity/{{id}}"},
             "Id":"{{id}}","Name":"O’Brien & \"Co\" ünïcode","Amount":12345678901234567.89,"Probability":0.1234567890123456,
             "Count__c":-42,"CloseDate":"2024-02-29","Start__c":"2024-01-02T03:04:05.006+0000","Opens__c":"09:30:15.250Z",
             "IsWon":true,"StageName":"Closed Won","Interests__c":"Golf;Fine Dining","Hobbies__c":"Golf;Fine Dining"}
            """)!.AsObject());

            var start = deal.Start;
            var close = deal.CloseDate;
            var rows = await typed.GetAllAsync<RtDeal>(d =>
                d.Stage == RtStage.ClosedWon && d.CloseDate == close && d.Start == start && d.Amount == deal.Amount &&
                d.Name == deal.Name && d.IsWon && d.Count == -42);

            var soql = Uri.UnescapeDataString(h.Fake.Requests.Last(r => r.Contains("/query")).Split("q=")[1]).Replace('+', ' ');
            soql.Should().Contain("StageName = 'Closed Won'")
                .And.Contain("CloseDate = 2024-02-29")
                .And.Contain("Start__c = 2024-01-02T03:04:05")
                .And.Contain("Amount = 12345678901234567.89")
                .And.Contain("Name = 'O’Brien & \\\"Co\\\" ünïcode'")
                .And.Contain("Count__c = -42");

            rows.Should().ContainSingle();
            var back = rows[0];
            back.Should().BeEquivalentTo(deal, o => o.Excluding(d => d.Id));
            back.Id.Should().Be(id);
            back.Start!.Value.Offset.Should().Be(TimeSpan.Zero);
            back.Start.Should().Be(deal.Start);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
