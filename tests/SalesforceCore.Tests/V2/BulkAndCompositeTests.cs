using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V2;

public class BulkEndToEndTests
{
    private const string JobId = "7505g00000AbCdEAAV";

    private static object Job(string state) => new { id = JobId, operation = "insert", @object = "Account", state };

    [Fact]
    public async Task IngestLifecycle_UsesBulk2Verbs()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Post, "/services/data/v62.0/jobs/ingest/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, Job("Open")));
        host.Server.On(HttpMethod.Put, $"/services/data/v62.0/jobs/ingest/{JobId}/batches", _ => FakeSalesforceServer.Empty(HttpStatusCode.Created));
        host.Server.On(HttpMethod.Patch, $"/services/data/v62.0/jobs/ingest/{JobId}", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, Job("UploadComplete")));
        host.Server.On(HttpMethod.Get, $"/services/data/v62.0/jobs/ingest/{JobId}/successfulResults", _ => FakeSalesforceServer.Text(HttpStatusCode.OK, "\"sf__Id\",\"Name\"\n\"001\",\"A\"\n", "text/csv"));
        var bulk = host.Get<IBulkService>();

        var job = await bulk.CreateJobAsync(new CreateBulkJobRequest { ObjectName = "Account", Operation = BulkOperation.insert });
        await bulk.UploadJobDataAsync(job.Id, "Name\nA\n");
        var closed = await bulk.CloseJobAsync(job.Id);
        var ok = await bulk.GetSuccessfulResultsAsync(job.Id);

        closed.State.Should().Be(BulkJobState.UploadComplete);
        ok.Should().Contain("sf__Id");
        var upload = host.Server.Requests[1];
        upload.ContentType.Should().StartWith("text/csv");
        upload.Body.Should().Be("Name\nA\n");
        host.Server.Requests[2].Json!["state"]!.GetValue<string>().Should().Be("UploadComplete");
    }

    [Fact]
    public async Task Abort_UsesPatch()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Patch, $".*/jobs/ingest/{JobId}", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, Job("Aborted")));
        (await host.Get<IBulkService>().AbortJobAsync(JobId)).State.Should().Be(BulkJobState.Aborted);
    }

    [Theory]
    [InlineData("../../sobjects/User")]
    [InlineData("750?x=1")]
    [InlineData("")]
    public async Task InvalidJobIds_AreRejected_BeforeAnyRequest(string jobId)
    {
        await using var host = SalesforceTestHost.Create();
        var bulk = host.Get<IBulkService>();
        await FluentActions.Awaiting(() => bulk.GetJobAsync(jobId)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => bulk.DeleteJobAsync(jobId)).Should().ThrowAsync<ArgumentException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryResults_EscapesLocator()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, $".*/jobs/query/{JobId}/results", _ => FakeSalesforceServer.Text(HttpStatusCode.OK, "Id\n1\n", "text/csv"));
        await host.Get<IBulkService>().GetQueryResultsAsync(JobId, "abc&maxRecords=1", 10);
        var r = host.Server.Requests.Single();
        r.Query("locator").Should().Be("abc&maxRecords=1");
        r.Query("maxRecords").Should().Be("10");
    }
}

public class CompositeGraphBuilderV2Tests
{
    [Fact]
    public void UsesConfiguredApiVersion()
    {
        var req = new SalesforceCore.Models.Data.CompositeGraphBuilder("58").StartGraph("g")
            .Create("Account", new Dictionary<string, object?> { ["Name"] = "A" }, "acc").Build();
        req.Graphs.Single().CompositeRequest.Single().Url.Should().Be("/services/data/v58.0/sobjects/Account");
    }

    [Fact]
    public void Update_AcceptsReferenceIds()
    {
        var req = new SalesforceCore.Models.Data.CompositeGraphBuilder().StartGraph()
            .Create("Account", new Dictionary<string, object?> { ["Name"] = "A" }, "acc")
            .Update("Account", "@{acc.id}", new Dictionary<string, object?> { ["Name"] = "B" }).Build();
        req.Graphs[0].CompositeRequest[1].Url.Should().EndWith("/sobjects/Account/@{acc.id}");
        req.Graphs[0].CompositeRequest[1].Method.Should().Be("PATCH");
    }

    [Theory]
    [InlineData("Account/../User", "001000000000001AAA")]
    [InlineData("Account", "001/../../x")]
    public void InvalidSegments_Throw(string obj, string id)
    {
        var b = new SalesforceCore.Models.Data.CompositeGraphBuilder().StartGraph();
        FluentActions.Invoking(() => b.Delete(obj, id)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithoutGraph_Throws()
        => FluentActions.Invoking(() => new SalesforceCore.Models.Data.CompositeGraphBuilder().Build()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void InvalidApiVersion_Throws()
        => FluentActions.Invoking(() => new SalesforceCore.Models.Data.CompositeGraphBuilder("vNext")).Should().Throw<Exception>();
}
