using Microsoft.AspNetCore.Http;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>Round 5: end-to-end workflows where 2.7.0 security guards interact with legitimate data and raw Apex payloads.</summary>
public class Round5WorkflowE2ETests
{
    [Theory]
    [InlineData("https://shop.example.com/p/1")]
    [InlineData("urn:sku://a/b")]
    [InlineData("SKU-1.2/ä/日本")]
    [InlineData("v1..2")]
    public async Task Upsert_LegitimateExternalIdValues_ReachSalesforceEncodedAsOneSegment(string externalId)
    {
        await using var h = await E2EHost.StartAsync();
        string? rawTarget = null;
        h.Fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.Contains("/sobjects/Account/Ext__c/")) return false;
            rawTarget = Uri.UnescapeDataString(ctx.Request.Path.Value!);
            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"id\":\"001000000000001AAA\",\"success\":true,\"created\":true,\"errors\":[]}");
            return true;
        };
        var data = h.Get<IDataService>();

        await data.UpsertRecordAsync("Account", "Ext__c", externalId, new Dictionary<string, object?> { ["Name"] = "x" });

        rawTarget.Should().NotBeNull();
        rawTarget!.Should().EndWith("/sobjects/Account/Ext__c/" + externalId);
    }

    [Theory]
    [InlineData("[{\"Id\":\"a\"},{\"Id\":\"b\"}]", 2)]
    [InlineData("[]", 0)]
    public async Task ApexRawGet_ArrayResponse_IsReturnedNotThrown(string body, int count)
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.StartsWith("/services/apexrest/")) return false;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body);
            return true;
        };
        var apex = h.Get<IApexService>();

        var node = await apex.GetNodeAsync("/items");

        node.Should().BeOfType<JsonArray>().Which.Count.Should().Be(count);
        var obj = () => apex.GetAsync("/items");
        (await obj.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("GetNodeAsync");
    }
}
