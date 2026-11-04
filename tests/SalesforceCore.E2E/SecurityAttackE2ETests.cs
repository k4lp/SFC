using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Reports;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>2.7.0: hostile-server and malicious-input scenarios through the real DI pipeline.</summary>
public class SecurityAttackE2ETests
{
    private static int ApiCalls(FakeSalesforce fake) => fake.Requests.Count(r => r.Contains("/services/data/") || r.Contains("/services/apexrest/"));

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("http://acme.my.salesforce.com")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://localhost")]
    [InlineData("https://acme.my.salesforce.com.evil.com")]
    [InlineData("https://acme.my.salesforce.com@evil.com")]
    [InlineData("https://evil.com/#.salesforce.com")]
    [InlineData("https://salesforce.com")]
    [InlineData("javascript:alert(1)")]
    public async Task MaliciousInstanceUrl_FromTokenEndpoint_TokenIsNeverSentToIt(string hostile)
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.TokenInstanceUrlOverride = hostile;
        var data = h.Get<IDataService>();

        var act = () => data.QueryAsync("SELECT Id FROM Account");

        await act.Should().ThrowAsync<Exception>();
        ApiCalls(h.Fake).Should().Be(0, "no API request may reach a host that is not a trusted Salesforce instance");
    }

    [Theory]
    [InlineData("00O/../../apexrest/Admin")]
    [InlineData("00O%2e%2e%2fapexrest")]
    [InlineData("00O000000000001/../x")]
    [InlineData("00O000000000001\r\nX-Evil: 1")]
    public async Task ReportIdPathTraversal_IsRejectedBeforeSending(string reportId)
    {
        await using var h = await E2EHost.StartAsync();
        var reports = h.Get<IReportService>();

        var act = () => reports.RunReportAsync(reportId);

        await act.Should().ThrowAsync<ArgumentException>();
        ApiCalls(h.Fake).Should().Be(0);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(" .. ")]
    public async Task UpsertExternalIdDotSegments_AreRejectedBeforeSending(string externalId)
    {
        await using var h = await E2EHost.StartAsync();
        var data = h.Get<IDataService>();

        var act = () => data.UpsertRecordAsync("Account", "Ext__c", externalId, new Dictionary<string, object?> { ["Name"] = "x" });

        await act.Should().ThrowAsync<ArgumentException>();
        ApiCalls(h.Fake).Should().Be(0);
    }

    [Fact]
    public async Task UpsertExternalIdWithEncodedTraversal_IsRejectedBeforeSending()
    {
        // "a/../../b" escapes to a%2F..%2F..%2Fb; servers or proxies that decode %2F would traverse, so it is refused.
        await using var h = await E2EHost.StartAsync();
        var data = h.Get<IDataService>();

        var act = () => data.UpsertRecordAsync("Account", "Ext__c", "a/../../b", new Dictionary<string, object?> { ["Name"] = "x" });

        await act.Should().ThrowAsync<ArgumentException>();
        ApiCalls(h.Fake).Should().Be(0);
    }

    [Fact]
    public async Task CompositeUpdateWithTraversalId_IsRejectedBeforeSending()
    {
        await using var h = await E2EHost.StartAsync();
        var composite = h.Get<ICompositeService>();

        var act = () => composite.UpdateRecordsAsync("Account", new[]
        {
            new Dictionary<string, object?> { ["Id"] = "../../apexrest/Admin", ["Name"] = "x" }
        });

        await act.Should().ThrowAsync<ArgumentException>();
        ApiCalls(h.Fake).Should().Be(0);
    }

    [Fact]
    public async Task GzipBomb_IsCutOffAtTheDecompressedLimit()
    {
        await using var h = await E2EHost.StartAsync(new()
        {
            ["Salesforce:EnableResponseCompression"] = "true",
            ["Salesforce:MaxDecompressedResponseBytes"] = (1024 * 1024).ToString(),
            ["Salesforce:MaxResponseContentBufferSize"] = (512L * 1024 * 1024).ToString(),
        });
        var bomb = GzipOfZeros(64 * 1024 * 1024); // ~64 KB on the wire, 64 MB decompressed
        h.Fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.Contains("/query")) return false;
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers.ContentEncoding = "gzip";
            await ctx.Response.Body.WriteAsync(bomb);
            return true;
        };
        var data = h.Get<IDataService>();

        var act = () => data.QueryAsync("SELECT Id FROM Account");

        var ex = await act.Should().ThrowAsync<Exception>();
        Flatten(ex.Which).Should().Contain(e => e is InvalidDataException && e.Message.Contains("MaxDecompressedResponseBytes"));
    }

    [Fact]
    public async Task DeeplyNestedJson_FromHostileServer_FailsCleanly()
    {
        await using var h = await E2EHost.StartAsync();
        var nested = new string('[', 100_000) + new string(']', 100_000);
        h.Fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.Contains("/query")) return false;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(nested);
            return true;
        };
        var data = h.Get<IDataService>();

        var act = () => data.QueryAsync("SELECT Id FROM Account");

        await act.Should().ThrowAsync<Exception>(); // a JsonException (depth 64), never a stack overflow
    }

    [Fact]
    public async Task HostileErrorBody_EchoingTheToken_IsNotPutInTheExceptionVerbatim()
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.Contains("/query")) return false;
            var auth = ctx.Request.Headers.Authorization.ToString();
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync($"proxy failure, request header was {auth} access_token=00Dabc!SECRETSECRETSECRET");
            return true;
        };
        var data = h.Get<IDataService>();

        var act = () => data.QueryAsync("SELECT Id FROM Account");

        var ex = await act.Should().ThrowAsync<Exception>();
        foreach (var e in Flatten(ex.Which))
        {
            e.Message.Should().NotContain("00Dtoken!gen");
            e.Message.Should().NotContain("SECRETSECRETSECRET");
        }
    }

    [Fact]
    public async Task ClientNameWithCrLf_FailsOptionsValidationAtStartup()
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:ClientName"] = "app\r\nX-Injected: 1" });

        var act = () => h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");

        await act.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
        ApiCalls(h.Fake).Should().Be(0);
    }

    private static byte[] GzipOfZeros(int size)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < size / chunk.Length; i++) gz.Write(chunk);
        }
        return ms.ToArray();
    }

    private static IEnumerable<Exception> Flatten(Exception? e)
    {
        for (; e != null; e = e.InnerException) yield return e;
    }
}
