using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Offline end-to-end tests: real SalesforceClient against a fake Salesforce HTTP endpoint.
/// </summary>
public class SalesforceClientEndToEndTests
{
    internal sealed class FakeTokenProvider : ITokenProvider
    {
        public string? Token = "tok-1";
        public string? Instance = "https://acme.my.salesforce.com";
        public string? RefreshResult = "tok-2";
        public int RefreshCalls;
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult(Token);
        public Task<string?> GetInstanceUrlAsync(CancellationToken ct = default) => Task.FromResult(Instance);
        public Task RevokeTokenAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> RefreshTokenAsync(CancellationToken ct = default) { RefreshCalls++; return Task.FromResult(RefreshResult); }
        public Task<bool> IsAuthenticatedAsync(CancellationToken ct = default) => Task.FromResult(Token != null);
    }

    internal sealed record Captured(HttpMethod Method, Uri Uri, string? Auth, string? Body, string? ContentType, string? Accept);

    internal sealed class FakeSalesforce : HttpMessageHandler
    {
        public readonly List<Captured> Requests = new();
        public readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? Default;
        public Exception? Throw;

        public FakeSalesforce Enqueue(HttpStatusCode code, string? body = null, string mediaType = "application/json")
        {
            Responses.Enqueue(_ => new HttpResponseMessage(code) { Content = new StringContent(body ?? "", Encoding.UTF8, mediaType) });
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (Throw != null) throw Throw;
            Requests.Add(new Captured(r.Method, r.RequestUri!, r.Headers.Authorization?.Parameter,
                r.Content == null ? null : await r.Content.ReadAsStringAsync(ct),
                r.Content?.Headers.ContentType?.MediaType, string.Join(",", r.Headers.Accept)));
            if (Responses.Count > 0) return Responses.Dequeue()(r);
            return Default?.Invoke(r) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private readonly FakeSalesforce _http = new();
    private readonly FakeTokenProvider _tokens = new();
    private SalesforceClient Client(Action<SalesforceOptions>? cfg = null)
    {
        var o = new SalesforceOptions();
        cfg?.Invoke(o);
        return new SalesforceClient(new HttpClient(_http), _tokens, Options.Create(o), NullLogger<SalesforceClient>.Instance);
    }

    // ---------- URL building ----------
    [Fact]
    public async Task Get_BuildsVersionedDataUrl_AndSendsBearer()
    {
        var c = Client();
        await c.GetAsync("/sobjects/Account/001");
        _http.Requests.Single().Uri.AbsoluteUri.Should().Be($"https://acme.my.salesforce.com/services/data/{c.ApiVersion}/sobjects/Account/001");
        _http.Requests.Single().Auth.Should().Be("tok-1");
    }

    [Fact]
    public async Task Get_RelativePathWithoutSlash_IsNormalised()
    {
        var c = Client();
        await c.GetAsync("limits");
        _http.Requests.Single().Uri.AbsolutePath.Should().EndWith("/limits");
        _http.Requests.Single().Uri.AbsolutePath.Should().StartWith("/services/data/");
    }

    [Fact]
    public async Task Get_ServicesPath_IsNotReprefixed()
    {
        var c = Client();
        await c.GetAsync("/services/apexrest/foo");
        _http.Requests.Single().Uri.AbsolutePath.Should().Be("/services/apexrest/foo");
    }

    [Fact]
    public async Task InstanceUrl_TrailingSlash_DoesNotDoubleSlash()
    {
        _tokens.Instance = "https://acme.my.salesforce.com/";
        var c = Client();
        await c.GetAsync("/limits");
        _http.Requests.Single().Uri.AbsoluteUri.Should().NotContain(".com//");
    }

    [Fact]
    public void BuildApiUrl_BeforeAuth_Throws()
    {
        var c = Client();
        c.Invoking(x => x.BuildApiUrl("/x")).Should().Throw<InvalidOperationException>();
        c.Invoking(x => x.InstanceUrl).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task InstanceUrl_IsAvailableAfterFirstCall()
    {
        var c = Client();
        await c.GetAsync("/limits");
        c.InstanceUrl.Should().Be("https://acme.my.salesforce.com");
    }

    [Fact]
    public async Task ApiVersion_FromOptions_IsUsed()
    {
        var c = Client(o => o.ApiVersion = "v99.0");
        await c.GetAsync("/limits");
        _http.Requests.Single().Uri.AbsolutePath.Should().Be("/services/data/v99.0/limits");
    }

    // ---------- Serialization ----------
    [Fact]
    public async Task GetGeneric_DeserializesQueryResult()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":2,"done":false,"nextRecordsUrl":"/services/data/v60.0/query/01g-2000","records":[{"Id":"001A"},{"Id":"001B"}]}""");
        var r = await Client().GetAsync<QueryResult>("/query?q=x");
        r.TotalSize.Should().Be(2);
        r.Done.Should().BeFalse();
        r.NextRecordsUrl.Should().Contain("01g-2000");
        r.Records.Select(x => x["Id"]!.GetValue<string>()).Should().Equal("001A", "001B");
    }

    [Fact]
    public async Task GetGeneric_TypedQueryResult_DeserializesCamelCaseWireFormat()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":1,"done":true,"records":[{"Id":"001A"}]}""");
        var r = await Client().GetAsync<QueryResult<JsonObject>>("/query?q=x");
        r.TotalSize.Should().Be(1);
        r.Done.Should().BeTrue();
        r.Records.Should().HaveCount(1);
    }

    [Fact]
    public async Task Post_SerializesPayloadAsJson_AndOmitsNulls()
    {
        _http.Enqueue(HttpStatusCode.Created, """{"id":"001NEW","success":true,"errors":[]}""");
        var res = await Client().PostAsync("/sobjects/Account", new Dictionary<string, object?> { ["Name"] = "Acme", ["Phone"] = null });
        var req = _http.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.ContentType.Should().Be("application/json");
        var body = JsonNode.Parse(req.Body!)!.AsObject();
        body["Name"]!.GetValue<string>().Should().Be("Acme");
        res["id"]!.GetValue<string>().Should().Be("001NEW");
    }

    [Fact]
    public async Task Post_UnicodePayload_RoundTrips()
    {
        await Client().PostAsync("/sobjects/Account", new { Name = "Ünïcødé – 日本 😀" });
        JsonNode.Parse(_http.Requests.Single().Body!)!["Name"]!.GetValue<string>().Should().Be("Ünïcødé – 日本 😀");
    }

    [Fact]
    public async Task Patch_NoContent_ReturnsEmptyObject()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        var r = await Client().PatchAsync("/sobjects/Account/001", new { Name = "x" });
        r.Should().BeOfType<JsonObject>();
        _http.Requests.Single().Method.Should().Be(HttpMethod.Patch);
    }

    [Fact]
    public async Task PatchGeneric_NoContent_ReturnsDefaultForPoco()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        var r = await Client().PatchAsync<QueryResult>("/x", new { });
        r.Should().BeNull();
    }

    [Fact]
    public async Task PatchGeneric_NoContent_JsonArray()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().PatchAsync<JsonArray>("/x", new { })).Should().BeEmpty();
    }

    [Fact]
    public async Task Put_NoContent_ReturnsEmptyObject()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().PutAsync("/x", new { })).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task PutGeneric_NoContent_JsonObject()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().PutAsync<JsonObject>("/x", new { })).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_NoContent_Succeeds()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        await Client().DeleteAsync("/sobjects/Account/001");
        _http.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task DeleteGeneric_NoContent_JsonNode()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().DeleteAsync<JsonNode>("/x")).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task DeleteGeneric_WithBody_Deserializes()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":0,"done":true,"records":[]}""");
        (await Client().DeleteAsync<QueryResult>("/x")).Done.Should().BeTrue();
    }

    [Fact]
    public async Task Post_CreatedWithEmptyBody_JsonNode_DoesNotThrow()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().PostAsync("/services/apexrest/fire", new { })).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task PostGeneric_EmptyBody_ReturnsDefault()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().PostAsync<QueryResult>("/x", new { })).Should().BeNull();
    }

    [Fact]
    public async Task Get_EmptyBody_JsonNode_ReturnsEmptyObject()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        (await Client().GetAsync("/x")).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task Get_ArrayResponse_ParsesAsArray()
    {
        _http.Enqueue(HttpStatusCode.OK, """[{"version":"60.0"},{"version":"61.0"}]""");
        (await Client().GetAsync("/services/data")).AsArray().Should().HaveCount(2);
    }

    // ---------- Error mapping ----------
    [Fact]
    public async Task Error_ArrayFormat_SingleError_MapsCodeFieldsStatus()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"Required fields are missing: [Name]","errorCode":"REQUIRED_FIELD_MISSING","fields":["Name"]}]""");
        var ex = await Client().Invoking(c => c.PostAsync("/sobjects/Account", new { })).Should().ThrowAsync<SalesforceException>();
        ex.Which.ErrorCode.Should().Be("REQUIRED_FIELD_MISSING");
        ex.Which.HttpStatusCode.Should().Be(400);
        ex.Which.Fields.Should().Contain("Name");
    }

    [Fact]
    public async Task Error_ArrayFormat_MultipleErrors_AreJoined()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"a","errorCode":"E1","fields":["F1"]},{"message":"b","errorCode":"E2","fields":["F2"]}]""");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.Message.Should().Contain("[E1] a").And.Contain("[E2] b");
        ex.Which.ErrorCode.Should().Be("E1");
        ex.Which.Fields.Should().BeEquivalentTo(new[] { "F1", "F2" });
    }

    [Fact]
    public async Task Error_EmptyArray_FallsBackToStatusMessage()
    {
        _http.Enqueue(HttpStatusCode.NotFound, "[]");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Error_OAuthObjectFormat_UsesErrorDescription()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"authentication failure"}""");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.Message.Should().Be("authentication failure");
        ex.Which.ErrorCode.Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Error_ObjectWithMessage_UsesMessageAndCode()
    {
        _http.Enqueue(HttpStatusCode.Forbidden, """{"message":"nope","errorCode":"INSUFFICIENT_ACCESS"}""");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.Message.Should().Be("nope");
        ex.Which.ErrorCode.Should().Be("INSUFFICIENT_ACCESS");
        ex.Which.HttpStatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Error_HtmlBody_IsReportedRaw()
    {
        _http.Enqueue(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.HttpStatusCode.Should().Be(502);
        ex.Which.RawResponse.Should().Contain("bad gateway");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Resource not found")]
    [InlineData(HttpStatusCode.Forbidden, "Access denied")]
    [InlineData(HttpStatusCode.BadRequest, "Invalid request")]
    [InlineData(HttpStatusCode.InternalServerError, "Salesforce server error")]
    public async Task Error_EmptyBody_UsesDefaultMessage(HttpStatusCode code, string msg)
    {
        _http.Enqueue(code);
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.Message.Should().Be(msg);
        ex.Which.HttpStatusCode.Should().Be((int)code);
    }

    [Fact]
    public async Task NetworkFailure_IsWrappedInSalesforceException()
    {
        _http.Throw = new HttpRequestException("connection refused");
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceException>();
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _http.Throw = new TaskCanceledException();
        await Client().Invoking(c => c.GetAsync("/x", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- Auth / token flows ----------
    [Fact]
    public async Task NoToken_ThrowsAuthException_WithoutHttpCall()
    {
        _tokens.Token = null;
        await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceAuthException>();
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_AndRetriesWithNewToken()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK, """{"ok":true}""");
        var r = await Client().GetAsync("/x");
        r["ok"]!.GetValue<bool>().Should().BeTrue();
        _http.Requests.Select(x => x.Auth).Should().Equal("tok-1", "tok-2");
        _tokens.RefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_Twice_ThrowsTokenExpired_NoInfiniteLoop()
    {
        _http.Default = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var ex = await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceAuthException>();
        ex.Which.TokenExpired.Should().BeTrue();
        _http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Unauthorized_RefreshReturnsNull_ThrowsTokenExpired()
    {
        _tokens.RefreshResult = null;
        _http.Enqueue(HttpStatusCode.Unauthorized);
        await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceAuthException>();
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Unauthorized_OnPost_ResendsSamePayload()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.Created, """{"id":"1"}""");
        await Client().PostAsync("/sobjects/Account", new { Name = "A" });
        _http.Requests.Should().HaveCount(2);
        _http.Requests[1].Body.Should().Be(_http.Requests[0].Body);
    }

    [Fact]
    public async Task Unauthorized_OnGetBytes_RefreshesAndRetries()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized);
        _http.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        (await Client().GetBytesAsync("/sobjects/ContentVersion/068/VersionData")).Should().Equal(1, 2, 3);
        _tokens.RefreshCalls.Should().Be(1);
    }

    [Fact]
    public async Task Unauthorized_OnGetStream_RefreshesAndRetries()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK, "hello", "text/plain");
        await using var s = await Client().GetStreamAsync("/x");
        (await new StreamReader(s).ReadToEndAsync()).Should().Be("hello");
        _http.Requests[1].Auth.Should().Be("tok-2");
    }

    [Fact]
    public async Task Unauthorized_OnGetRaw_RefreshesAndRetries()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK, "<xml/>", "application/xml");
        (await Client().GetRawAsync("/x", "application/xml")).Should().Be("<xml/>");
        _http.Requests[1].Accept.Should().Contain("application/xml");
    }

    [Fact]
    public async Task Unauthorized_OnPutRaw_RefreshesAndRetries()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK);
        await Client().PutRawAsync("/jobs/ingest/750/batches", "Name\nA", "text/csv");
        _http.Requests.Should().HaveCount(2);
        _http.Requests[1].Body.Should().Be("Name\nA");
        _http.Requests[1].ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task MissingInstanceUrl_RecoveredViaRefresh()
    {
        var lazy = new LazyProvider();
        var c = new SalesforceClient(new HttpClient(_http), lazy, Options.Create(new SalesforceOptions()), NullLogger<SalesforceClient>.Instance);
        await c.GetAsync("/x");
        _http.Requests.Single().Uri.Host.Should().Be("lazy.my.salesforce.com");
        _http.Requests.Single().Auth.Should().Be("refreshed");
    }

    [Fact]
    public async Task MissingInstanceUrl_Unrecoverable_ThrowsAuth()
    {
        _tokens.Instance = null;
        _tokens.RefreshResult = null;
        await Client().Invoking(c => c.GetAsync("/x")).Should().ThrowAsync<SalesforceAuthException>();
    }

    [Fact]
    public async Task GetAccessToken_DelegatesToProvider()
        => (await Client().GetAccessTokenAsync()).Should().Be("tok-1");

    private sealed class LazyProvider : ITokenProvider
    {
        private string? _inst;
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default) => Task.FromResult<string?>("initial");
        public Task<string?> GetInstanceUrlAsync(CancellationToken ct = default) => Task.FromResult(_inst);
        public Task RevokeTokenAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> RefreshTokenAsync(CancellationToken ct = default) { _inst = "https://lazy.my.salesforce.com"; return Task.FromResult<string?>("refreshed"); }
        public Task<bool> IsAuthenticatedAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    // ---------- Binary / raw ----------
    [Fact]
    public async Task GetBytes_ReturnsBody()
    {
        _http.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9, 8 }) });
        (await Client().GetBytesAsync("/x")).Should().Equal(9, 8);
    }

    [Fact]
    public async Task GetBytes_Error_Throws()
    {
        _http.Enqueue(HttpStatusCode.NotFound, """[{"message":"gone","errorCode":"NOT_FOUND"}]""");
        (await Client().Invoking(c => c.GetBytesAsync("/x")).Should().ThrowAsync<SalesforceException>()).Which.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetStream_Error_Throws()
    {
        _http.Enqueue(HttpStatusCode.InternalServerError);
        await Client().Invoking(c => c.GetStreamAsync("/x")).Should().ThrowAsync<SalesforceException>();
    }

    [Fact]
    public async Task GetRaw_SetsAcceptHeader()
    {
        _http.Enqueue(HttpStatusCode.OK, "a,b", "text/csv");
        (await Client().GetRawAsync("/jobs/query/750/results", "text/csv")).Should().Be("a,b");
        _http.Requests.Single().Accept.Should().Be("text/csv");
    }

    [Fact]
    public async Task PutRaw_Error_Throws()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"bad csv","errorCode":"INVALIDJOB"}]""");
        await Client().Invoking(c => c.PutRawAsync("/x", "a", "text/csv")).Should().ThrowAsync<SalesforceException>();
    }

    // ---------- Pagination flow (client-level) ----------
    [Fact]
    public async Task Pagination_FollowNextRecordsUrl_UntilDone()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":false,"nextRecordsUrl":"/services/data/v60.0/query/01g-1","records":[{"Id":"1"}]}""")
             .Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":false,"nextRecordsUrl":"/services/data/v60.0/query/01g-2","records":[{"Id":"2"}]}""")
             .Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":true,"records":[{"Id":"3"}]}""");
        var c = Client();
        var all = new List<string>();
        var page = await c.GetAsync<QueryResult>("/query?q=SELECT+Id+FROM+Account");
        all.AddRange(page.Records.Select(r => r["Id"]!.GetValue<string>()));
        while (!page.Done) { page = await c.GetAsync<QueryResult>(page.NextRecordsUrl!); all.AddRange(page.Records.Select(r => r["Id"]!.GetValue<string>())); }
        all.Should().Equal("1", "2", "3");
        _http.Requests[1].Uri.AbsolutePath.Should().Be("/services/data/v60.0/query/01g-1");
    }

    [Fact]
    public async Task ConcurrentRequests_AllSucceed()
    {
        var c = Client();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => c.GetAsync($"/sobjects/Account/{i}")));
        lock (_http.Requests) { _http.Requests.Count.Should().Be(50); }
    }
}
