using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.Attributes;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Layout;
using SalesforceCore.Services.Metadata;
using Xunit;

namespace SalesforceCore.E2E;

public enum R4Stage { Open, [SalesforceValue("Closed Won")] ClosedWon }

/// <summary>
/// Round 4: scenarios that combine features fixed in 2.1-2.4 (retries x idempotency x tokens, bulk x paging x size x
/// gzip x cancellation, batching x chunk limits x failure indexes x culture, caching x layout x concurrency).
/// </summary>
public class Round4InteractionE2ETests
{
    private static string BigCsv(int bytes, string header = "Id,Name\n")
    {
        var sb = new StringBuilder(header);
        var i = 0;
        while (sb.Length < bytes) sb.Append("001").Append((i++).ToString("D12")).Append(",Name with some padding text ").Append(i).Append('\n');
        return sb.ToString();
    }

    // ---- bulk results larger than MaxResponseContentBufferSize (10 MB) -------------------------------------------

    [Fact]
    public async Task BulkQuery_PageLargerThan10MB_WithGzip_AndExpiredSession_ReturnsEveryRow()
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:EnableResponseCompression"] = "true" });
        h.Fake.GzipResponses = true;
        h.Fake.BulkQueryCsvPage1 = BigCsv(12 * 1024 * 1024);
        h.Fake.BulkQueryCsvPage2 = "Id,Name\n001LAST,Last\n";
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account"); // warm token
        h.Fake.InvalidateSessions();

        var csv = await h.Get<IBulkService>().QueryAsync("SELECT Id, Name FROM Account");

        csv.Length.Should().BeGreaterThan(12 * 1024 * 1024, "2.4.0 buffered each page under the 10 MB limit and threw");
        csv.Should().EndWith("001LAST,Last\n");
        csv.Split('\n').Count(l => l == "Id,Name").Should().Be(1);
    }

    [Fact]
    public async Task BulkInsert_SuccessFileLargerThan10MB_IsRead_SoCallerDoesNotRetryAndDuplicate()
    {
        await using var h = await E2EHost.StartAsync();
        var success = BigCsv(11 * 1024 * 1024, "sf__Id,sf__Created,Name\n").Replace(",Name with", ",true,Name with");
        var rows = success.Count(c => c == '\n') - 1;
        h.Fake.BulkResultsFor = _ => (success, "sf__Id,sf__Error,Name\n", rows, 0);

        var result = await h.Get<IBulkService>().InsertAsync("Account", new[] { new Dictionary<string, object?> { ["Name"] = "x" } });

        result.SuccessfulRecords.Should().HaveCount(rows);
        h.Fake.AbortedJobs.Should().BeEmpty("a completed job must not be 'aborted' when reading its results fails");
    }

    // ---- cancellation in the middle of bulk jobs ------------------------------------------------------------------

    [Fact]
    public async Task BulkIngest_CancelledWhileProcessing_AbortsTheJob()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.BulkFinalState = "InProgress";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var act = () => h.Get<IBulkService>().UpdateAsync("Account",
            new[] { new Dictionary<string, object?> { ["Id"] = "001000000000001AAA", ["Name"] = "x" } }, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Fake.AbortedJobs.Should().ContainSingle("2.4.0 sent the abort with the already-cancelled token, so it never left");
    }

    [Fact]
    public async Task BulkQuery_CancelledWhileRunning_AbortsTheJob_ButCompletedJobIsNeverAborted()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.QueryJobState = "InProgress";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var act = () => h.Get<IBulkService>().QueryAsync("SELECT Id FROM Account", cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Fake.AbortedJobs.Should().ContainSingle();

        await using var h2 = await E2EHost.StartAsync();
        (await h2.Get<IBulkService>().QueryAsync("SELECT Id FROM Account")).Should().Contain("001B");
        h2.Fake.AbortedJobs.Should().BeEmpty();
    }

    // ---- enums written by every write path -------------------------------------------------------------------------

    [Fact]
    public async Task EnumValues_InRecordDictionaries_AreSentAsPicklistValues_OnRestAndBulk()
    {
        await using var h = await E2EHost.StartAsync();
        var id = await h.Get<IDataService>().CreateRecordAsync("Opportunity",
            new Dictionary<string, object?> { ["Name"] = "o", ["StageName"] = R4Stage.ClosedWon });
        h.Fake.Records[id]["StageName"]!.GetValue<string>().Should().Be("Closed Won", "2.4.0 sent the ordinal 1");

        await h.Get<IBulkService>().InsertAsync("Opportunity", new[] { new Dictionary<string, object?> { ["Name"] = "o", ["StageName"] = R4Stage.ClosedWon } });
        h.Fake.LastBulkUpload.Should().Be("Name,StageName\no,Closed Won\n", "2.4.0 wrote the member name ClosedWon");
    }

    // ---- sObject Collections limits x caller-chosen bulkThreshold x failure index ------------------------------------

    [Fact]
    public async Task BatchCreate_AboveCollectionsLimit_WithHighThreshold_ChunksBy200_AndKeepsInputIndexes()
    {
        await using var h = await E2EHost.StartAsync();
        var recs = Enumerable.Range(0, 450)
            .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?> { ["Name"] = i is 7 or 401 ? "bad" + i : "n" + i }).ToList();

        var result = await h.Get<IDataService>().BatchCreateAsync("Account", recs, bulkThreshold: 1000);

        h.Fake.CollectionCallSizes.Should().Equal(200, 200, 50);
        result.SuccessCount.Should().Be(448);
        result.FailedRecords.Select(f => f.Index).Should().Equal(7, 401);
        result.UsedBulkApi.Should().BeFalse();
    }

    [Fact]
    public async Task BatchUpdate_And_BatchDelete_AboveCollectionsLimit_AreChunked()
    {
        await using var h = await E2EHost.StartAsync();
        var ids = Enumerable.Range(0, 250).Select(i => (i == 230 ? "bad" : "001") + i.ToString("D15")).ToList();

        var upd = await h.Get<IDataService>().BatchUpdateAsync("Account",
            ids.Select(id => (IDictionary<string, object?>)new Dictionary<string, object?> { ["Id"] = id, ["Name"] = id.StartsWith("bad") ? "bad" : "ok" }), bulkThreshold: 1000);
        var del = await h.Get<IDataService>().BatchDeleteAsync("Account", ids, bulkThreshold: 1000);

        h.Fake.CollectionCallSizes.Should().Equal(200, 50, 200, 50);
        upd.FailedRecords.Should().ContainSingle().Which.Index.Should().Be(230);
        del.FailedRecords.Should().ContainSingle().Which.Index.Should().Be(230);
        del.SuccessCount.Should().Be(249);
    }

    [Fact]
    public async Task CompositeUpsert_NumericExternalId_IsCultureInvariant()
    {
        await using var h = await E2EHost.StartAsync();
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            await h.Get<IDataService>().BatchUpsertAsync("Account", "Num__c",
                new[] { (IDictionary<string, object?>)new Dictionary<string, object?> { ["Num__c"] = 1234.5m, ["Name"] = "a" } });
        }
        finally { CultureInfo.CurrentCulture = prev; }
        h.Fake.UpsertUrls.Should().ContainSingle().Which.Should().EndWith("/Num__c/1234.5", "2.4.0 sent 1234,5 under de-DE");
    }

    [Fact]
    public async Task CompositeUpsert_Cancelled_Propagates_InsteadOfReportingFailuresAndSendingMoreChunks()
    {
        await using var h = await E2EHost.StartAsync();
        using var cts = new CancellationTokenSource();
        h.Fake.CompositeResponder = body => { cts.Cancel(); return DefaultLike(body); };
        var recs = Enumerable.Range(0, 60).Select(i => (IDictionary<string, object?>)new Dictionary<string, object?> { ["Ext__c"] = "e" + i, ["Name"] = "n" });

        var act = () => h.Get<IDataService>().BatchUpsertAsync("Account", "Ext__c", recs, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Fake.Requests.Count(r => r.EndsWith("/composite")).Should().Be(1);
    }

    private static JsonObject DefaultLike(JsonObject body) => new()
    {
        ["compositeResponse"] = new JsonArray(body["compositeRequest"]!.AsArray().Select(s => (JsonNode)new JsonObject
        {
            ["referenceId"] = s!["referenceId"]!.GetValue<string>(), ["httpStatusCode"] = 200, ["httpHeaders"] = new JsonObject(),
            ["body"] = new JsonObject { ["id"] = "001000000000009AAA", ["created"] = false }
        }).ToArray())
    };

    // ---- resilience: per-attempt timeout on GET is retried, POST still never replayed --------------------------------

    [Fact]
    public async Task SlowQueryAttempt_TimesOut_AndIsRetried()
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:PerAttemptTimeout"] = "00:00:00.300" });
        h.Fake.QueryRows.Add(new JsonObject { ["Id"] = "001A" });
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account"); // warm token
        h.Fake.QueryDelayOnce = TimeSpan.FromSeconds(3);

        var result = await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");

        result.Records.Should().ContainSingle("2.4.0 surfaced the first attempt's timeout instead of retrying the GET");
    }

    // ---- caches: nested GetOrCreate (layout -> describe) and single-flight across request scopes ----------------------

    [Fact]
    public async Task Describe_RequestedByManyScopesAtOnce_IsFetchedOnce()
    {
        await using var h = await E2EHost.StartAsync();
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account").ContinueWith(_ => { });
        h.Fake.DescribeDelay = TimeSpan.FromMilliseconds(200);
        var before = h.Fake.DescribeRequests;

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var s = h.Root.CreateAsyncScope();
            (await s.ServiceProvider.GetRequiredService<ISchemaService>().GetDescribeAsync("Account")).Should().NotBeNull();
        }));

        (h.Fake.DescribeRequests - before).Should().Be(1, "2.4.0 locks were per scope, so each request scope described again");
    }

    [Fact]
    public async Task LayoutForms_ForManyObjects_NeverHang_OnNestedCacheCalls()
    {
        await using var h = await E2EHost.StartAsync();
        var layout = h.Get<ILayoutDescriptorService>();
        var work = Task.WhenAll(Enumerable.Range(0, 40).Select(i => layout.GetFormAsync("Account", (Models.Layout.FormMode)(i % 2), "012" + i.ToString("D15"))));
        (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(work);
        await work;
    }
}
