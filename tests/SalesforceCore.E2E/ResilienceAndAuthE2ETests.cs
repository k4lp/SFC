using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Http;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>Transport behaviour through the real DI pipeline (resilience handler, token provider, error mapping).</summary>
public class ResilienceAndAuthE2ETests
{
    [Fact]
    public async Task Create_IsNotReplayed_AfterGatewayError_SoNoDuplicateRecord()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.SObjectPostFailures.Enqueue(502); // fake commits the record, then fails at the "gateway"

        var act = () => h.Get<IDataService>().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "B" });

        await act.Should().ThrowAsync<SalesforceException>();
        h.Fake.SObjectPostsProcessed.Should().Be(1, "a POST must not be retried on 5xx: 2.2.x replayed it and created a duplicate");
    }

    [Fact]
    public async Task Create_IsRetried_On429()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.SObjectPostFailures.Enqueue(429);
        var id = await h.Get<IDataService>().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "B" });
        id.Should().StartWith("001");
    }

    [Fact]
    public async Task Query_IsRetried_On503()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.QueryRows.Add(new JsonObject { ["Id"] = "001A" });
        h.Fake.QueryFailures.Enqueue(503);
        var result = await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        result.Records.Should().HaveCount(1);
        h.Fake.QueryRequests.Should().Be(2);
    }

    [Fact]
    public async Task ParallelRequests_AfterSessionExpiry_RefreshOnce()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.QueryRows.Add(new JsonObject { ["Id"] = "1" });
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        var before = h.Fake.TokenRequests;

        h.Fake.InvalidateSessions();
        h.Fake.TokenDelay = TimeSpan.FromMilliseconds(50);
        await Task.WhenAll(Enumerable.Range(0, 25).Select(async i =>
        {
            await using var scope = h.Root.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IDataService>().QueryAsync("SELECT Id FROM Account");
        }));

        (h.Fake.TokenRequests - before).Should().Be(1);
    }

    [Fact]
    public async Task Late401_AfterAnotherRequestRefreshed_ReusesNewToken()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.QueryRows.Add(new JsonObject { ["Id"] = "1" });
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        var before = h.Fake.TokenRequests;

        h.Fake.InvalidateSessions();
        h.Fake.FirstUnauthorizedDelay = TimeSpan.FromMilliseconds(400); // this 401 arrives after the refresh below
        var slow = Task.Run(async () =>
        {
            await using var s = h.Root.CreateAsyncScope();
            await s.ServiceProvider.GetRequiredService<IDataService>().QueryAsync("SELECT Id FROM Account");
        });
        await Task.Delay(100);
        await using (var s = h.Root.CreateAsyncScope())
        {
            await s.ServiceProvider.GetRequiredService<IDataService>().QueryAsync("SELECT Id FROM Account");
        }
        await slow;

        (h.Fake.TokenRequests - before).Should().Be(1, "2.2.x re-authenticated again for every late 401");
    }

    [Fact]
    public async Task ErrorArrayBody_MapsToTypedException_WithCode()
    {
        await using var h = await E2EHost.StartAsync();
        var act = () => h.Get<IDataService>().QueryAsync("SELECT Bogus__c FROM Account");
        var ex = (await act.Should().ThrowAsync<SalesforceException>()).Which;
        ex.ErrorCode.Should().Be("INVALID_FIELD");
        ex.Message.Should().Contain("Bogus__c");
        ex.HttpStatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SforceLimitInfo_IsTracked()
    {
        await using var h = await E2EHost.StartAsync();
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        var usage = h.Get<ISalesforceApiUsage>();
        usage.Used.Should().Be(25);
        usage.Max.Should().Be(15000);
    }

    [Fact]
    public async Task Cancellation_PropagatesAsOperationCanceled()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.TokenDelay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = () => h.Get<IDataService>().QueryAsync("SELECT Id FROM Account", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task QueryMore_FollowsNextRecordsUrl_ForListAndStream()
    {
        await using var h = await E2EHost.StartAsync();
        for (var i = 0; i < 5; i++) h.Fake.QueryRows.Add(new JsonObject { ["Id"] = $"00{i}" });
        var data = h.Get<IDataService>();

        (await data.QueryAllAsync("SELECT Id FROM Account")).Select(r => r["Id"]!.GetValue<string>())
            .Should().Equal("000", "001", "002", "003", "004");
        var streamed = new List<string>();
        await foreach (var r in data.QueryAllAsyncEnumerable("SELECT Id FROM Account")) streamed.Add(r["Id"]!.GetValue<string>());
        streamed.Should().HaveCount(5);
        h.Fake.Requests.Should().Contain(r => r.Contains("/query/01gCURSOR-2")).And.Contain(r => r.Contains("/query/01gCURSOR-4"));
    }
}
