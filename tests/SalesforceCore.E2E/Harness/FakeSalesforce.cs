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
    /// <summary>Sforce-* request headers per request ("METHOD path" -> header -> value).</summary>
    public ConcurrentQueue<(string Line, Dictionary<string, string> Headers)> SforceHeaders { get; } = new();
    public JsonObject? LastIngestJobRequest;
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
    public int DescribeRequests;
    public int GlobalDescribeRequests;
    public ConcurrentQueue<int> DescribeFailures { get; } = new();
    public byte[] FileBytes = Enumerable.Range(0, 11 * 1024 * 1024).Select(i => (byte)(i % 251)).ToArray();
    public bool GzipResponses;
    public string? LastAcceptEncoding;
    public string? LastGrantType;
    public Func<JsonObject, JsonObject>? CompositeResponder;
    /// <summary>Bodies of every sObject Collections call (POST/PATCH) and the id lists of DELETE calls.</summary>
    public ConcurrentQueue<int> CollectionCallSizes { get; } = new();
    public ConcurrentQueue<string> AbortedJobs { get; } = new();
    public string QueryJobState = "JobComplete";
    public TimeSpan DescribeDelay = TimeSpan.Zero;
    /// <summary>Delay applied once to the next GET /query (models one slow attempt).</summary>
    public TimeSpan QueryDelayOnce = TimeSpan.Zero;
    public ConcurrentQueue<string> UpsertUrls { get; } = new();

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
            SforceHeaders.Enqueue(($"{ctx.Request.Method} {ctx.Request.Path}", ctx.Request.Headers
                .Where(h => h.Key.StartsWith("Sforce-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)));
            LastAcceptEncoding = ctx.Request.Headers.AcceptEncoding.ToString();
            if (GzipResponses && LastAcceptEncoding.Contains("gzip") && !ctx.Request.Path.StartsWithSegments("/services/oauth2"))
            {
                var original = ctx.Response.Body;
                await using var buffer = new MemoryStream();
                ctx.Response.Body = buffer;
                ctx.Response.Headers.ContentEncoding = "gzip";
                var authOk = ctx.Request.Headers.Authorization.ToString() == $"Bearer {CurrentToken}";
                if (!authOk) { ctx.Response.Body = original; ctx.Response.Headers.Remove("Content-Encoding"); await Error(ctx, 401, "INVALID_SESSION_ID", "Session expired or invalid"); return; }
                await next();
                ctx.Response.Body = original;
                await using (var gz = new System.IO.Compression.GZipStream(original, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                {
                    await gz.WriteAsync(buffer.ToArray());
                }
                return;
            }
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
            var form = await ctx.Request.ReadFormAsync();
            LastGrantType = form["grant_type"];
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
            LastIngestJobRequest = (JsonObject)req.DeepClone();
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
            if (requested == "Aborted") AbortedJobs.Enqueue(id);
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
        app.MapGet(v + "/jobs/query/750000000000QQQ", ctx => Json(ctx, new JsonObject { ["id"] = "750000000000QQQ", ["state"] = QueryJobState, ["operation"] = "query", ["numberRecordsProcessed"] = 2 }));
        app.MapMethods(v + "/jobs/query/{id}", new[] { "PATCH" }, async (HttpContext ctx, string id) =>
        {
            var req = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            if (req["state"]?.GetValue<string>() == "Aborted") { AbortedJobs.Enqueue(id); QueryJobState = "Aborted"; }
            await Json(ctx, new JsonObject { ["id"] = id, ["state"] = QueryJobState });
        });
        app.MapGet(v + "/jobs/query/750000000000QQQ/results", async ctx =>
        {
            var locator = ctx.Request.Query["locator"].ToString();
            ctx.Response.Headers["Sforce-NumberOfRecords"] = "1";
            ctx.Response.Headers["Sforce-Locator"] = string.IsNullOrEmpty(locator) ? "MTAwMDA" : "null";
            await Csv(ctx, string.IsNullOrEmpty(locator) ? BulkQueryCsvPage1 : BulkQueryCsvPage2);
        });

        app.MapGet(v + "/sobjects", async ctx =>
        {
            Interlocked.Increment(ref GlobalDescribeRequests);
            await Json(ctx, new JsonObject { ["encoding"] = "UTF-8", ["maxBatchSize"] = 200, ["sobjects"] = new JsonArray(
                new JsonObject { ["name"] = "Account", ["label"] = "Account", ["queryable"] = true, ["custom"] = false },
                new JsonObject { ["name"] = "Invoice__c", ["label"] = "Invoice", ["queryable"] = true, ["custom"] = true }) });
        });
        app.MapGet(v + "/sobjects/{obj}/describe", async (HttpContext ctx, string obj) =>
        {
            Interlocked.Increment(ref DescribeRequests);
            if (DescribeDelay > TimeSpan.Zero) await Task.Delay(DescribeDelay);
            if (DescribeFailures.TryDequeue(out var st)) { await Error(ctx, st, st == 404 ? "NOT_FOUND" : "SERVER_UNAVAILABLE", "describe failed"); return; }
            if (obj != "Account") { await Error(ctx, 404, "NOT_FOUND", "The requested resource does not exist"); return; }
            await Json(ctx, Describe());
        });
        app.MapGet(v + "/sobjects/ContentVersion/{id}/VersionData", async (HttpContext ctx, string id) =>
        {
            ctx.Response.ContentType = "application/octet-stream";
            await ctx.Response.Body.WriteAsync(FileBytes);
        });
        app.MapGet("/services/apexrest/echo/{kind}", async (HttpContext ctx, string kind) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(kind switch
            {
                "object" => "{\"accountName\":\"Acme\",\"total\":12.5}",
                "array" => "[1,2,3]",
                "string" => "\"ok\"",
                _ => ""
            });
        });
        async Task Collections(HttpContext ctx)
        {
            JsonArray results;
            if (ctx.Request.Method == "DELETE")
            {
                var ids = ctx.Request.Query["ids"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                CollectionCallSizes.Enqueue(ids.Length);
                if (ids.Length > 200) { await Error(ctx, 400, "EXCEEDED_ID_LIMIT", "Too many ids: limit 200"); return; }
                results = new JsonArray(ids.Select(id => (JsonNode)Result(id, id.StartsWith("bad"))).ToArray());
            }
            else
            {
                var body = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
                LastCompositeBody = body.ToJsonString();
                var recs = body["records"]!.AsArray();
                CollectionCallSizes.Enqueue(recs.Count);
                if (recs.Count > 200) { await Error(ctx, 400, "EXCEEDED_ID_LIMIT", "record limit is 200"); return; }
                results = new JsonArray(recs.Select(r =>
                {
                    var bad = r!["Name"]?.ToString().StartsWith("bad") == true;
                    var id = r["Id"]?.ToString() ?? "001" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                    if (!bad) Records[id] = (JsonObject)r.DeepClone();
                    return (JsonNode)Result(id, bad);
                }).ToArray());
            }
            await Json(ctx, results);

            static JsonObject Result(string id, bool bad) => bad
                ? new JsonObject { ["success"] = false, ["errors"] = new JsonArray(new JsonObject { ["statusCode"] = "FIELD_CUSTOM_VALIDATION_EXCEPTION", ["message"] = "bad record", ["fields"] = new JsonArray() }) }
                : new JsonObject { ["id"] = id, ["success"] = true, ["errors"] = new JsonArray() };
        }
        app.MapMethods(v + "/composite/sobjects", new[] { "POST", "PATCH", "DELETE" }, Collections);

        app.MapPost(v + "/composite", async ctx =>
        {
            var body = (JsonObject)(await JsonNode.ParseAsync(ctx.Request.Body))!;
            LastCompositeBody = body.ToJsonString();
            foreach (var sub in body["compositeRequest"]?.AsArray() ?? new JsonArray()) UpsertUrls.Enqueue(sub!["url"]!.GetValue<string>());
            var resp = CompositeResponder?.Invoke(body) ?? DefaultComposite(body);
            await Json(ctx, resp);
        });
    }

    public static JsonObject Describe() => new()
    {
        ["name"] = "Account", ["label"] = "Account", ["queryable"] = true, ["createable"] = true, ["updateable"] = true,
        ["fields"] = new JsonArray(
            new JsonObject { ["name"] = "Id", ["label"] = "Account ID", ["type"] = "id", ["nillable"] = false, ["createable"] = false, ["updateable"] = false },
            new JsonObject { ["name"] = "Name", ["label"] = "Account <Name> & \"Co\"", ["type"] = "string", ["length"] = 255, ["nillable"] = false, ["createable"] = true, ["updateable"] = true, ["inlineHelpText"] = "<script>alert(1)</script>" },
            new JsonObject { ["name"] = "Industry", ["label"] = "Industry", ["type"] = "picklist", ["nillable"] = true, ["createable"] = true, ["updateable"] = true,
                ["picklistValues"] = new JsonArray(
                    new JsonObject { ["value"] = "Tech\"<b>", ["label"] = "Tech <b>", ["active"] = true, ["defaultValue"] = false },
                    new JsonObject { ["value"] = "Old", ["label"] = "Old", ["active"] = false, ["defaultValue"] = false }) },
            new JsonObject { ["name"] = "AnnualRevenue", ["label"] = "Revenue", ["type"] = "currency", ["nillable"] = true, ["createable"] = true, ["updateable"] = true, ["precision"] = 18, ["scale"] = 2 })
    };

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

    private async Task QueryPage(HttpContext ctx, int start)
    {
        var delay = QueryDelayOnce;
        if (delay > TimeSpan.Zero) { QueryDelayOnce = TimeSpan.Zero; await Task.Delay(delay, ctx.RequestAborted); }
        await QueryPageCore(ctx, start);
    }

    private Task QueryPageCore(HttpContext ctx, int start)
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
