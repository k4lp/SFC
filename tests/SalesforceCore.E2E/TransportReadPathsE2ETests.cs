using System.IO.Compression;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Files;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>Round 5: decompression cap and error redaction on every response read path (buffered JSON, raw Apex, streamed download).</summary>
public class TransportReadPathsE2ETests
{
    private const string Echo = "Authorization: Bearer 00Dtoken!gen0 leaked";

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

    private static IEnumerable<Exception> Flatten(Exception? e) { for (; e != null; e = e.InnerException) yield return e; }

    public static TheoryData<string> Paths => new() { "query", "apex", "download" };

    private static Func<Task> Call(E2EHost h, string path) => path switch
    {
        "query" => () => h.Get<IDataService>().QueryAsync("SELECT Id FROM Account"),
        "apex" => () => h.Get<IApexService>().GetNodeAsync("/items"),
        _ => () => h.Get<IFileService>().DownloadAsync("068000000000001AAA"),
    };

    private static bool Matches(HttpContext ctx, string path) => path switch
    {
        "query" => ctx.Request.Path.Value!.Contains("/query"),
        "apex" => ctx.Request.Path.Value!.StartsWith("/services/apexrest/"),
        _ => ctx.Request.Path.Value!.EndsWith("/VersionData"),
    };

    [Theory, MemberData(nameof(Paths))]
    public async Task GzipBomb_IsStoppedAtTheCap_OnEveryReadPath(string path)
    {
        await using var h = await E2EHost.StartAsync(new()
        {
            ["Salesforce:EnableResponseCompression"] = "true",
            ["Salesforce:MaxDecompressedResponseBytes"] = (1024 * 1024).ToString(),
            ["Salesforce:MaxResponseContentBufferSize"] = (512L * 1024 * 1024).ToString(),
        });
        var bomb = GzipOfZeros(64 * 1024 * 1024);
        h.Fake.Intercept = async ctx =>
        {
            if (!Matches(ctx, path)) return false;
            ctx.Response.ContentType = path == "download" ? "application/octet-stream" : "application/json";
            ctx.Response.Headers.ContentEncoding = "gzip";
            await ctx.Response.Body.WriteAsync(bomb);
            return true;
        };

        var ex = await Call(h, path).Should().ThrowAsync<Exception>();
        Flatten(ex.Which).Should().Contain(e => e is InvalidDataException && e.Message.Contains("MaxDecompressedResponseBytes"));
    }

    [Theory, MemberData(nameof(Paths))]
    public async Task ErrorBodyEchoingTheToken_IsRedacted_OnEveryReadPath(string path)
    {
        await using var h = await E2EHost.StartAsync();
        h.Fake.Intercept = async ctx =>
        {
            if (!Matches(ctx, path)) return false;
            ctx.Response.StatusCode = 502;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.WriteAsync(Echo);
            return true;
        };

        var ex = await Call(h, path).Should().ThrowAsync<Exception>();
        foreach (var e in Flatten(ex.Which)) e.Message.Should().NotContain("00Dtoken!gen0");
        ex.Which.ToString().Should().NotContain("00Dtoken!gen0");
    }
}
