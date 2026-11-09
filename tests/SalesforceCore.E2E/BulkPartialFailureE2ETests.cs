using FluentAssertions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Round 5: Bulk API 2.0 ingest with partial failures, gzip responses and a session that expires while the job runs.
/// Result files follow the Bulk API 2.0 Developer Guide: successfulResults "sf__Id,sf__Created,…",
/// failedResults "sf__Id,sf__Error,…" (quoted CSV, error text may contain commas, quotes and line breaks).
/// </summary>
public class BulkPartialFailureE2ETests
{
    [Fact]
    public async Task BulkInsert_PartialFailures_WithGzipAndExpiredSession_MapsEveryRow()
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:EnableResponseCompression"] = "true" });
        h.Fake.GzipResponses = true;
        const string success = "sf__Id,sf__Created,Name\n001000000000001AAA,true,ok one\n001000000000002AAA,true,\"ok, \"\"two\"\"\"\n";
        const string failed = "sf__Id,sf__Error,Name\n,\"REQUIRED_FIELD_MISSING:Required fields are missing: [Name, \"\"Type\"\"]\nsecond line:--\",\"bad, one\"\n";
        h.Fake.BulkResultsFor = _ =>
        {
            h.Fake.InvalidateSessions(); // the token expires between upload and result download
            return (success, failed, 3, 1);
        };

        var result = await h.Get<IBulkService>().InsertAsync("Account", new[]
        {
            new Dictionary<string, object?> { ["Name"] = "ok one" },
            new Dictionary<string, object?> { ["Name"] = "ok, \"two\"" },
            new Dictionary<string, object?> { ["Name"] = "bad, one" },
        });

        result.SuccessfulRecords.Select(r => r.Id).Should().Equal("001000000000001AAA", "001000000000002AAA");
        result.SuccessfulRecords[1].OriginalData!["Name"].Should().Be("ok, \"two\"");
        result.FailedRecords.Should().ContainSingle();
        result.FailedRecords[0].Error.Should().Contain("REQUIRED_FIELD_MISSING").And.Contain("\"Type\"").And.Contain("second line");
        result.FailedRecords[0].OriginalData!["Name"].Should().Be("bad, one");
        result.IsSuccess.Should().BeFalse();
        h.Fake.LastAcceptEncoding.Should().Contain("gzip");
        h.Fake.TokenRequests.Should().BeGreaterThan(1, "the expired session was refreshed mid-job");
    }
}
