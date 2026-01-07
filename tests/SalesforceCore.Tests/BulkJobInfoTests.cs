using System.Text.Json;
using FluentAssertions;
using SalesforceCore.Models.Data;
using Xunit;

namespace SalesforceCore.Tests;

public class BulkJobInfoTests
{
    [Fact]
    public void BulkJobInfo_ShouldParseSalesforceDateTimeOffsets()
    {
        var json = @"{
  ""id"": ""750xx0000000001AAA"",
  ""operation"": ""query"",
  ""object"": ""Account"",
  ""createdById"": ""005xx0000000001AAA"",
  ""createdDate"": ""2026-01-07T16:43:34.000+0000"",
  ""systemModstamp"": ""2026-01-07T16:43:35.000+0000"",
  ""state"": ""JobComplete"",
  ""contentType"": ""CSV"",
  ""apiVersion"": 60.0,
  ""numberRecordsProcessed"": 1,
  ""numberRecordsFailed"": 0,
  ""totalProcessingTime"": 10,
  ""retries"": 0
}";

        var info = JsonSerializer.Deserialize<BulkJobInfo>(json);

        info.Should().NotBeNull();
        info!.CreatedDate.Should().Be(new DateTime(2026, 1, 7, 16, 43, 34, DateTimeKind.Utc));
        info.SystemModstamp.Should().Be(new DateTime(2026, 1, 7, 16, 43, 35, DateTimeKind.Utc));
    }
}
