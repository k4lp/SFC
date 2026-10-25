using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Data;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V2;

/// <summary>DataService/ApexService through the real DI + HTTP pipeline against a fake org.</summary>
public class DataAndApexEndToEndTests
{
    private static JsonObject Acc(int i) => SalesforceJson.Record("Account", $"001{i:D15}", ("Name", $"Acme {i}"));

    [Fact]
    public async Task QueryAllAsyncEnumerable_FollowsNextRecordsUrl()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { Acc(1), Acc(2) }, "/services/data/v62.0/query/01gXX-2000", 3)));
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/01gXX-2000", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { Acc(3) }, null, 3)));

        var names = new List<string>();
        await foreach (var r in host.Get<IDataService>().QueryAllAsyncEnumerable("SELECT Id, Name FROM Account"))
            names.Add(r["Name"]!.GetValue<string>());

        names.Should().Equal("Acme 1", "Acme 2", "Acme 3");
        host.Server.Requests.First().Query("q").Should().Be("SELECT Id, Name FROM Account");
        host.Server.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAllAsync_CollectsEveryPage()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { Acc(1) }, "/services/data/v62.0/query/01gYY-1", 2)));
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/01gYY-1", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { Acc(2) }, null, 2)));

        var all = await host.Get<IDataService>().QueryAllAsync("SELECT Id FROM Account");
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryNextAsync_KeepsServerProvidedVersion()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v58.0/query/01gZZ-2000", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { Acc(9) })));

        var page = await host.Get<IDataService>().QueryNextAsync("/services/data/v58.0/query/01gZZ-2000");
        page.Records.Should().ContainSingle();
    }

    [Theory]
    [InlineData("/services/data/v62.0/query/../../../secur/frontdoor.jsp")]
    [InlineData("/services/data/v62.0/query\\..\\x")]
    [InlineData("//evil.example.com/services/data")]
    public async Task QueryNextAsync_RejectsTraversal(string url)
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<IDataService>().QueryNextAsync(url);
        await act.Should().ThrowAsync<Exception>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUpdateDelete_SendPayloadUnfiltered()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Post, "/services/data/v62.0/sobjects/Account/?", _ => FakeSalesforceServer.Json(HttpStatusCode.Created, SalesforceJson.Created("001000000000001AAA")));
        host.Server.On(HttpMethod.Patch, "/services/data/v62.0/sobjects/Account/001000000000001AAA", _ => FakeSalesforceServer.Empty());
        host.Server.On(HttpMethod.Delete, "/services/data/v62.0/sobjects/Account/001000000000001AAA", _ => FakeSalesforceServer.Empty());
        var data = host.Get<IDataService>();

        var id = await data.CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "Acme", ["Secret__c"] = "x" });
        await data.UpdateRecordAsync("Account", id, new Dictionary<string, object?> { ["Name"] = "Acme 2" });
        await data.DeleteRecordAsync("Account", id);

        id.Should().Be("001000000000001AAA");
        var create = host.Server.Requests[0].Json!.AsObject();
        create["Secret__c"]!.GetValue<string>().Should().Be("x", "2.0 does not drop fields; Salesforce enforces FLS");
        host.Server.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Post, HttpMethod.Patch, HttpMethod.Delete);
    }

    [Fact]
    public async Task Create_FieldSecurityError_IsTyped()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.Error(HttpMethod.Post, ".*/sobjects/Account/?", HttpStatusCode.BadRequest, "INVALID_FIELD_FOR_INSERT_UPDATE", "Unable to create/update fields: Secret__c", "Secret__c");
        var act = () => host.Get<IDataService>().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Secret__c"] = "x" });
        await act.Should().ThrowAsync<SalesforceFieldSecurityException>();
    }

    [Fact]
    public async Task GetRecord_InvalidFieldName_IsRejectedLocally()
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<IDataService>().GetRecordAsync("Account", "001000000000001AAA", new[] { "Name", "Id FROM User --" });
        await act.Should().ThrowAsync<ArgumentException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apex_Get_And_Post_UseApexRestRoot()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/apexrest/orders/42", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, new { id = 42 }));
        host.Server.On(HttpMethod.Post, "/services/apexrest/orders", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, new { ok = true }));
        var apex = host.Get<IApexService>();

        (await apex.GetAsync("orders/42"))["id"]!.GetValue<int>().Should().Be(42);
        (await apex.PostAsync("/orders", new { qty = 3 }))["ok"]!.GetValue<bool>().Should().BeTrue();
        host.Server.Requests[1].Json!["qty"]!.GetValue<int>().Should().Be(3);
    }

    [Theory]
    [InlineData("../data/v62.0/sobjects/User")]
    [InlineData("orders//x")]
    [InlineData("https://evil.example.com/x")]
    public async Task Apex_RejectsPathEscapes(string path)
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<IApexService>().GetAsync(path);
        await act.Should().ThrowAsync<ArgumentException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Apex_ErrorIsTyped()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.Error(HttpMethod.Get, "/services/apexrest/boom", HttpStatusCode.InternalServerError, "APEX_ERROR", "System.NullPointerException");
        var act = () => host.Get<IApexService>().GetAsync("boom");
        (await act.Should().ThrowAsync<SalesforceException>()).Which.ErrorCode.Should().Be("APEX_ERROR");
    }
}
