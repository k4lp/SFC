using System.Globalization;
using FluentAssertions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

public class BulkCompositeE2ETests
{
    [Fact]
    public async Task BulkQuery_FollowsSforceLocator_AndKeepsOneHeader()
    {
        await using var h = await E2EHost.StartAsync();
        var csv = await h.Get<IBulkService>().QueryAsync("SELECT Id, Name FROM Account");
        csv.Should().Be("Id,Name\n001A,One\n001B,Two\n", "2.2.x returned only the first locator page");
    }

    [Fact]
    public async Task BulkUpload_IsCultureInvariant_AndQuotesSpecialCharacters()
    {
        await using var h = await E2EHost.StartAsync();
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await h.Get<IBulkService>().InsertAsync("Account", new[]
            {
                new Dictionary<string, object?>
                {
                    ["Name"] = "Acme, \"Inc\"\nLine2",
                    ["AnnualRevenue"] = 1234.5m,
                    ["Rating__c"] = 0.25,
                    ["Since__c"] = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                    ["Day__c"] = new DateOnly(2024, 1, 2),
                    ["Active__c"] = true,
                    ["Blank__c"] = null
                }
            });
        }
        finally { CultureInfo.CurrentCulture = prev; }

        h.Fake.LastBulkUpload.Should().Be(
            "Name,AnnualRevenue,Rating__c,Since__c,Day__c,Active__c,Blank__c\r\n" +
            "\"Acme, \"\"Inc\"\"\nLine2\",1234.5,0.25,2024-01-02T03:04:05.000Z,2024-01-02,true,\r\n");
    }

    [Fact]
    public async Task BulkJob_InFailedState_Throws_InsteadOfEmptySuccess()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.BulkFinalState = "Failed";
        h.Fake.BulkErrorMessage = "InvalidBatch : Field name not found : Bogus__c";
        var act = () => h.Get<IBulkService>().InsertAsync("Account", new[] { new Dictionary<string, object?> { ["Bogus__c"] = "x" } });
        (await act.Should().ThrowAsync<SalesforceException>()).Which.Message.Should().Contain("Field name not found");
    }

    [Fact]
    public async Task BatchCreate_ViaBulk_ReportsFailureIndex_IntoCallerInput()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.BulkResultsFor = _ => (
            "sf__Id,sf__Created,Name\n001X,true,a\n001Y,true,b\n",
            "sf__Id,sf__Error,Name\n,REQUIRED_FIELD_MISSING:Required fields are missing,c\n", 3, 1);
        var recs = new[] { "a", "b", "c" }.Select(n => (IDictionary<string, object?>)new Dictionary<string, object?> { ["Name"] = n }).ToList();

        var result = await h.Get<IDataService>().BatchCreateAsync("Account", recs, bulkThreshold: 1);

        result.UsedBulkApi.Should().BeTrue();
        result.SuccessCount.Should().Be(2);
        result.FailedRecords.Should().ContainSingle().Which.Index.Should().Be(2, "2.2.x reported the failure-file row number (1)");
    }

    [Fact]
    public async Task Composite_AllOrNone_Over25_Throws_BeforeSendingAnything()
    {
        await using var h = await E2EHost.StartAsync();
        var many = Enumerable.Range(0, 30).Select(i => new Dictionary<string, object?> { ["Name"] = "n" + i });
        var act = () => h.Get<ICompositeService>().CreateRecordsAsync("Account", many, allOrNone: true);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*allOrNone*");
        h.Fake.Requests.Should().NotContain(r => r.EndsWith("/composite"));
    }

    [Fact]
    public async Task Composite_AllOrNone_Within25_IsOneAtomicCall_AndWithoutAllOrNoneStillChunks()
    {
        await using var h = await E2EHost.StartAsync();
        var composite = h.Get<ICompositeService>();
        (await composite.CreateRecordsAsync("Account", Enumerable.Range(0, 25).Select(i => new Dictionary<string, object?> { ["Name"] = "n" + i }), allOrNone: true))
            .Should().HaveCount(25);
        h.Fake.LastCompositeBody.Should().Contain("\"allOrNone\":true");
        (await composite.CreateRecordsAsync("Account", Enumerable.Range(0, 30).Select(i => new Dictionary<string, object?> { ["Name"] = "n" + i })))
            .Should().HaveCount(30);
        h.Fake.Requests.Count(r => r.EndsWith("/composite")).Should().Be(3);
    }
}
