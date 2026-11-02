using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace SalesforceCore.E2E.Harness;

/// <summary>
/// In-process fake of the Salesforce REST, Bulk 2.0, Composite and OAuth endpoints, served by ASP.NET Core TestServer.
/// Behaviour follows the documented Salesforce semantics: error-array bodies, INVALID_SESSION_ID 401s, nextRecordsUrl
/// paging, Sforce-Locator bulk paging, Sforce-Limit-Info headers, job state machine.
/// </summary>
public sealed class FakeSalesforce : IAsyncDisposable
{
    public const string InstanceUrl = "https://acme.my.salesforce.com";
    private readonly WebApplication _app;
    private int _tokenGeneration;

    // ---- knobs and observations -------------------------------------------------------------------
    public int TokenRequests;
    public TimeSpan TokenDelay = TimeSpan.Zero;
    public ConcurrentQueue<string> Requests { get; } = new();
    /// <summary>Status codes returned (once each, in order) before a POST to /sobjects is processed normally.</summary>
    public ConcurrentQueue<int> SObjectPostFailures { get; } = new();
    public int SObjectPostsProcessed;
    public ConcurrentDictionary<string, JsonObject> Records { get; } = new();
    public List<JsonObject> QueryRows { get; } = new();
    public int QueryPageSize = 2;
    /// <summary>Status codes returned (once each) before a GET /query is answered.</summary>
    public ConcurrentQueue<int> QueryFailures { get; } = new();
    /// <summary>Delay applied to the first rejected (401) request, to model a response that arrives after another
    /// request has already refreshed the token.</summary>
    public TimeSpan FirstUnauthorizedDelay = TimeSpan.Zero;
    private int _unauthorizedCount;
    public int QueryRequests;
    public string BulkQueryCsvPage1 = "Id,Name\n001A,One\n";
    public string BulkQueryCsvPage2 = "Id,Name\n001B,Two\n";
    public string? LastBulkUpload;
    public string BulkFinalState = "JobComplete";
    public string BulkErrorMessage = "";
    public Func<string, (string success, string failed, int processed, int failed2)>? BulkResultsFor;
    public string? LastCompositeBody;
    public Func<JsonObject, JsonObject>? CompositeResponder;

    public HttpMessageHandler Handler => _app.GetTestServer().CreateHandler();

    public void InvalidateSessions() => Interlocked.Increment(ref _tokenGeneration);

    private FakeSalesforce(WebApplication app) { _app = app; }

    public static async Task<FakeSalesforce> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        var fake = new FakeSalesforce(app);
        fake.Map(app);
        await app.StartAsync();
        return fake;
    }

    private string CurrentToken => $"00Dtoken!gen{Volatile.Read(ref _tokenGeneration)}";

    private static Task Error(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(new JsonArray(new JsonObject { ["message"] = message, ["errorCode"] = code }).ToJsonString());
    }

    private static Task Json(HttpContext ctx, JsonNode node, int status = 200)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json;charset=UTF-8";
        ctx.Response.Headers["Sforce-Limit-Info"] = "api-usage=25/15000";
        return ctx.Response.WriteAsync(node.ToJsonString());
    }

    private void Map(WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            Requests.Enqueue($"{ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString}");
            if (ctx.Request.Path.StartsWithSegments("/services/oauth2"))
            {
                await next();
                return;
            }

            var auth = ctx.Request.Headers.Authorization.ToString();
            if (auth != $"Bearer {CurrentToken}")
            {
                if (Interlocked.Increment(ref _unauthorizedCount) == 1 && FirstUnauthorizedDelay > TimeSpan.Zero)
                    await Task.Delay(FirstUnauthorizedDelay);
                await Error(ctx, 401, "INVALID_SESSION_ID", "Session expired or invalid");
                return;
            }

            await next();
        });

        app.MapPost("/services/oauth2/token", async ctx =>
        {
            Interlocked.Increment(ref TokenRequests);
            if (TokenDelay > TimeSpan.Zero) await Task.Delay(TokenDelay);
            await Json(ctx, new JsonObject
            {
                ["access_token"] = CurrentToken,
                ["instance_url"] = InstanceUrl,
                ["token_type"] = "Bearer",
                ["issued_at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            });
        });

        const string v = "/services/data/{ver}";

        app.MapPost(v + "/sobjects/{obj}", async (HttpContext ctx, string obj) =>
        {
            if (SObjectPostFailures.TryDequeue(out var status))
            {
                // Simulates a gateway error AFTER the record was committed (what makes POST retries unsafe).
                Interlocked.Increment(ref SObjectPostsProcessed);
                await Error(ctx, status, "SERVER_UNAVAILABLE", "upstream failure");
                return;
            }

            Interlocked.Increment(ref SObjectPostsProcessed);
            var body = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            var id = "001" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            body["Id"] = id;
            Records[id] = body;
            await Json(ctx, new JsonObject { ["id"] = id, ["success"] = true, ["errors"] = new JsonArray() }, 201);
        });

        app.MapGet(v + "/query", async ctx => await QueryPage(ctx, 0));
        app.MapGet(v + "/queryAll", async ctx => await QueryPage(ctx, 0));
        app.MapGet(v + "/query/{cursor}", async (HttpContext ctx, string cursor) =>
            await QueryPage(ctx, int.Parse(cursor.Split('-')[1])));

        // ---- Bulk 2.0 ingest
        var jobs = new ConcurrentDictionary<string, JsonObject>();
        app.MapPost(v + "/jobs/ingest", async ctx =>
        {
            var req = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            var id = "750" + jobs.Count.ToString("D12");
            var job = new JsonObject { ["id"] = id, ["state"] = "Open", ["object"] = req["object"]?.GetValue<string>(), ["operation"] = req["operation"]?.GetValue<string>(), ["contentType"] = "CSV" };
            jobs[id] = job;
            await Json(ctx, job.DeepClone());
        });
        app.MapPut(v + "/jobs/ingest/{id}/batches", async (HttpContext ctx, string id) =>
        {
            using var r = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            LastBulkUpload = await r.ReadToEndAsync();
            ctx.Response.StatusCode = 201;
        });
        app.MapMethods(v + "/jobs/ingest/{id}", new[] { "PATCH" }, async (HttpContext ctx, string id) =>
        {
            var req = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            var job = jobs[id];
            var requested = req["state"]!.GetValue<string>();
            job["state"] = requested == "UploadComplete" ? BulkFinalState : requested;
            if (requested == "UploadComplete")
            {
                var (s, f, processed, failed) = BulkResultsFor?.Invoke(LastBulkUpload ?? "") ?? ("sf__Id,sf__Created\n", "sf__Id,sf__Error\n", 0, 0);
                job["numberRecordsProcessed"] = processed;
                job["numberRecordsFailed"] = failed;
                job["_s"] = s; job["_f"] = f;
                if (BulkFinalState == "Failed") job["errorMessage"] = BulkErrorMessage;
            }
            await Json(ctx, Public(job));
        });
        app.MapGet(v + "/jobs/ingest/{id}", (HttpContext ctx, string id) => Json(ctx, Public(jobs[id])));
        app.MapGet(v + "/jobs/ingest/{id}/successfulResults", (HttpContext ctx, string id) => Csv(ctx, jobs[id]["_s"]!.GetValue<string>()));
        app.MapGet(v + "/jobs/ingest/{id}/failedResults", (HttpContext ctx, string id) => Csv(ctx, jobs[id]["_f"]!.GetValue<string>()));

        // ---- Bulk 2.0 query with Sforce-Locator paging
        app.MapPost(v + "/jobs/query", ctx => Json(ctx, new JsonObject { ["id"] = "750000000000QQQ", ["state"] = "UploadComplete", ["operation"] = "query" }));
        app.MapGet(v + "/jobs/query/750000000000QQQ", ctx => Json(ctx, new JsonObject { ["id"] = "750000000000QQQ", ["state"] = "JobComplete", ["operation"] = "query", ["numberRecordsProcessed"] = 2 }));
        app.MapGet(v + "/jobs/query/750000000000QQQ/results", async ctx =>
        {
            var locator = ctx.Request.Query["locator"].ToString();
            ctx.Response.Headers["Sforce-NumberOfRecords"] = "1";
            ctx.Response.Headers["Sforce-Locator"] = string.IsNullOrEmpty(locator) ? "MTAwMDA" : "null";
            await Csv(ctx, string.IsNullOrEmpty(locator) ? BulkQueryCsvPage1 : BulkQueryCsvPage2);
        });

        app.MapPost(v + "/composite", async ctx =>
        {
            var body = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            LastCompositeBody = body.ToJsonString();
            var resp = CompositeResponder?.Invoke(body) ?? DefaultComposite(body);
            await Json(ctx, resp);
        });
    }

    private static JsonObject DefaultComposite(JsonObject body)
    {
        var arr = new JsonArray();
        foreach (var sub in body["compositeRequest"]!.AsArray())
        {
            arr.Add(new JsonObject
            {
                ["referenceId"] = sub!["referenceId"]!.GetValue<string>(),
                ["httpStatusCode"] = 201,
                ["httpHeaders"] = new JsonObject(),
                ["body"] = new JsonObject { ["id"] = "001" + Guid.NewGuid().ToString("N")[..12], ["success"] = true, ["errors"] = new JsonArray() }
            });
        }
        return new JsonObject { ["compositeResponse"] = arr };
    }

    private static JsonObject Public(JsonObject job)
    {
        var copy = (JsonObject)job.DeepClone();
        copy.Remove("_s"); copy.Remove("_f");
        return copy;
    }

    private static Task Csv(HttpContext ctx, string csv)
    {
        ctx.Response.ContentType = "text/csv";
        return ctx.Response.WriteAsync(csv);
    }

    private Task QueryPage(HttpContext ctx, int start)
    {
        Interlocked.Increment(ref QueryRequests);
        if (QueryFailures.TryDequeue(out var failure))
            return Error(ctx, failure, "SERVER_UNAVAILABLE", "try again");
        if (ctx.Request.Query.TryGetValue("q", out var q) && q.ToString().Contains("Bogus__c"))
            return Error(ctx, 400, "INVALID_FIELD", "No such column 'Bogus__c' on entity 'Account'.");
        var page = QueryRows.Skip(start).Take(QueryPageSize).Select(r => (JsonNode)r.DeepClone()).ToArray();
        var next = start + QueryPageSize;
        var done = next >= QueryRows.Count;
        var result = new JsonObject
        {
            ["totalSize"] = QueryRows.Count,
            ["done"] = done,
            ["records"] = new JsonArray(page)
        };
        if (!done) result["nextRecordsUrl"] = $"/services/data/v62.0/query/01gCURSOR-{next}";
        return Json(ctx, result);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
