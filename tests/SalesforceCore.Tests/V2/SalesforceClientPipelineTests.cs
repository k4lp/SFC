using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.Extensions;
using SalesforceCore.Http;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V2;

/// <summary>End-to-end tests of the transport through the real DI pipeline.</summary>
public class SalesforceClientPipelineTests
{
    [Fact]
    public async Task SendAsync_ReturnsStatusHeadersAndBody()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/limits", _ =>
            FakeSalesforceServer.Json(HttpStatusCode.OK, new { DailyApiRequests = new { Max = 15000, Remaining = 14000 } },
                new Dictionary<string, string> { ["Sforce-Request-Id"] = "req-42", ["ETag"] = "\"abc\"" }));

        var response = await host.Get<ISalesforceClient>().SendAsync(SalesforceRequest.Get("/limits"));

        response.Status.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
        response.RequestId.Should().Be("req-42");
        response.GetHeader("etag").Should().Be("\"abc\"");
        response.AsJson()["DailyApiRequests"]!["Max"]!.GetValue<int>().Should().Be(15000);
        host.Server.Requests.Single().Bearer.Should().Be("tok-1");
    }

    [Fact]
    public async Task SendAsync_ThrowOnErrorFalse_ReturnsErrorResponse()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.Error(HttpMethod.Get, ".*/sobjects/Account/001000000000001AAA", HttpStatusCode.NotFound, "NOT_FOUND", "gone");

        var response = await host.Get<ISalesforceClient>().SendAsync(new SalesforceRequest(HttpMethod.Get, "/sobjects/Account/001000000000001AAA") { ThrowOnError = false });

        response.Status.Should().Be(404);
        response.IsSuccess.Should().BeFalse();
        response.Content.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task SendAsync_CustomHeadersAndTextBody_AreSent()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Put, ".*/jobs/ingest/750/batches", _ => FakeSalesforceServer.Empty(HttpStatusCode.Created));

        await host.Get<ISalesforceClient>().SendAsync(new SalesforceRequest(HttpMethod.Put, "/jobs/ingest/750/batches")
        {
            TextBody = "Name\nAcme",
            ContentType = "text/csv"
        }.WithHeader("Sforce-Auto-Assign", "FALSE"));

        var req = host.Server.Requests.Single();
        req.ContentType.Should().Be("text/csv");
        req.Body.Should().Be("Name\nAcme");
        req.Headers["Sforce-Auto-Assign"].Should().Be("FALSE");
    }

    [Fact]
    public async Task SendAsync_BinaryBody_IsSentWithContentType()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Post, ".*/upload", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, new { ok = true }));
        await host.Get<ISalesforceClient>().SendAsync(new SalesforceRequest(HttpMethod.Post, "/upload") { BinaryBody = new byte[] { 65, 66 }, ContentType = "application/pdf" });
        host.Server.Requests.Single().ContentType.Should().Be("application/pdf");
        host.Server.Requests.Single().Body.Should().Be("AB");
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Host")]
    public async Task SendAsync_ManagedHeaders_CannotBeOverridden(string header)
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<ISalesforceClient>().SendAsync(SalesforceRequest.Get("/limits").WithHeader(header, "x"));
        await act.Should().ThrowAsync<ArgumentException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public void SalesforceRequest_RejectsAbsoluteUrls_SoTokensNeverLeaveTheInstance()
    {
        FluentActions.Invoking(() => SalesforceRequest.Get("https://evil.example.com/steal")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => SalesforceRequest.Get(" ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce_AndReplaysTheSameBody()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Post, ".*/sobjects/Account", r => r.Bearer == "tok-1"
            ? FakeSalesforceServer.Json(HttpStatusCode.Unauthorized, SalesforceJson.Errors(("INVALID_SESSION_ID", "expired", Array.Empty<string>())))
            : FakeSalesforceServer.Json(HttpStatusCode.Created, SalesforceJson.Created("001NEW")));

        var result = await host.Get<ISalesforceClient>().PostAsync("/sobjects/Account", new { Name = "Acme" });

        result["id"]!.GetValue<string>().Should().Be("001NEW");
        host.Tokens.RefreshCount.Should().Be(1);
        host.Server.Requests.Should().HaveCount(2);
        host.Server.Requests[1].Bearer.Should().Be("tok-2");
        host.Server.Requests[1].Body.Should().Be(host.Server.Requests[0].Body);
    }

    [Fact]
    public async Task Unauthorized_Twice_ThrowsAuthException()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(null, ".*", _ => FakeSalesforceServer.Empty(HttpStatusCode.Unauthorized));
        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        (await act.Should().ThrowAsync<SalesforceAuthException>()).Which.TokenExpired.Should().BeTrue();
        host.Tokens.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task NetworkFailure_IsSalesforceConnectionException()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.FailWith = new HttpRequestException("connection reset");
        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        var ex = await act.Should().ThrowAsync<SalesforceConnectionException>();
        ex.Which.IsTransient.Should().BeTrue();
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task ErrorResponses_AreTyped_WithRequestIdAndRetryAfter()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, ".*/limits", _ =>
        {
            var r = FakeSalesforceServer.Json((HttpStatusCode)429, SalesforceJson.Errors(("REQUEST_LIMIT_EXCEEDED", "slow down", Array.Empty<string>())),
                new Dictionary<string, string> { ["Sforce-Request-Id"] = "rid-9" });
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
            return r;
        });

        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        var ex = (await act.Should().ThrowAsync<SalesforceRateLimitException>()).Which;
        ex.RetryAfter.Should().Be(TimeSpan.FromSeconds(3));
        ex.RequestId.Should().Be("rid-9");
        ex.HttpStatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Retries_TransientFailures_WhenConfigured()
    {
        await using var host = SalesforceTestHost.Create(o => { o.MaxRetries = 2; o.RetryBaseDelay = TimeSpan.FromMilliseconds(1); });
        host.Server.On(HttpMethod.Get, ".*/limits", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, new { ok = true }));
        host.Server.On(HttpMethod.Get, ".*/limits", _ => FakeSalesforceServer.Empty(HttpStatusCode.ServiceUnavailable), times: 2);

        var result = await host.Get<ISalesforceClient>().GetAsync("/limits");

        result["ok"]!.GetValue<bool>().Should().BeTrue();
        host.Server.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task NoRetries_WhenMaxRetriesIsZero()
    {
        await using var host = SalesforceTestHost.Create(o => o.MaxRetries = 0);
        host.Server.On(HttpMethod.Get, ".*/limits", _ => FakeSalesforceServer.Empty(HttpStatusCode.ServiceUnavailable));
        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        await act.Should().ThrowAsync<SalesforceServerException>();
        host.Server.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ApiUsageHandler_TracksSforceLimitInfo()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, ".*/limits", _ => FakeSalesforceServer.Json(HttpStatusCode.OK, new { },
            new Dictionary<string, string> { ["Sforce-Limit-Info"] = "api-usage=250/15000" }));

        await host.Get<ISalesforceClient>().GetAsync("/limits");

        var usage = host.Get<ISalesforceApiUsage>();
        usage.Used.Should().Be(250);
        usage.Max.Should().Be(15000);
        usage.Remaining.Should().Be(14750);
        usage.UsageRatio.Should().BeApproximately(250d / 15000, 1e-9);
        usage.LastUpdated.Should().NotBeNull();
    }

    [Fact]
    public async Task ConfiguredApiVersion_IsUsed_AndNormalised()
    {
        await using var host = SalesforceTestHost.Create(o => o.ApiVersion = "58");
        host.Server.Get(".*", new { });
        var client = host.Get<ISalesforceClient>();
        client.ApiVersion.Should().Be("v58.0");
        await client.GetAsync("/sobjects");
        host.Server.Requests.Single().Path.Should().Be("/services/data/v58.0/sobjects");
    }

    private sealed class StampHandler : DelegatingHandler
    {
        public static int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", "corr-1");
            return base.SendAsync(request, cancellationToken);
        }
    }

    [Fact]
    public async Task AddSalesforceHttpHandler_PlugsIntoPipeline()
    {
        await using var host = SalesforceTestHost.Create(services: s => s.AddSalesforceHttpHandler<StampHandler>());
        host.Server.Get(".*", new { });
        await host.Get<ISalesforceClient>().GetAsync("/limits");
        host.Server.Requests.Single().Headers["X-Correlation-Id"].Should().Be("corr-1");
        StampHandler.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Streams_AndBytes_AreReturned()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, ".*/VersionData", _ => FakeSalesforceServer.Text(HttpStatusCode.OK, "PDFDATA", "application/pdf"));
        var client = host.Get<ISalesforceClient>();
        (await client.GetBytesAsync("/sobjects/ContentVersion/068000000000001AAA/VersionData")).Should().Equal("PDFDATA"u8.ToArray());
        await using var stream = await client.GetStreamAsync("/sobjects/ContentVersion/068000000000001AAA/VersionData");
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).Should().Be("PDFDATA");
    }

    [Fact]
    public async Task Stream_ErrorStatus_ThrowsTyped()
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<ISalesforceClient>().GetStreamAsync("/sobjects/ContentVersion/068000000000001AAA/VersionData");
        await act.Should().ThrowAsync<SalesforceNotFoundException>();
    }

    [Fact]
    public async Task EmptyBodies_ReturnEmptyJson()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(null, ".*", _ => FakeSalesforceServer.Empty());
        var c = host.Get<ISalesforceClient>();
        (await c.PatchAsync("/sobjects/Account/001000000000001AAA", new { Name = "x" })).Should().BeOfType<JsonObject>();
        (await c.PutAsync<JsonArray>("/x", new { })).Should().BeEmpty();
        (await c.DeleteAsync<JsonObject>("/sobjects/Account/001000000000001AAA")).Should().BeEmpty();
        (await c.GetRawAsync("/x", "text/csv")).Should().BeEmpty();
    }

    [Fact]
    public async Task MissingToken_ThrowsAuthException_WithoutCallingSalesforce()
    {
        await using var host = SalesforceTestHost.Create();
        host.Tokens.AccessToken = null;
        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        await act.Should().ThrowAsync<SalesforceAuthException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UntrustedInstanceUrl_IsRejected_BeforeSendingToken()
    {
        await using var host = SalesforceTestHost.Create();
        host.Tokens.InstanceUrl = "https://attacker.example.com";
        var act = () => host.Get<ISalesforceClient>().GetAsync("/limits");
        await act.Should().ThrowAsync<SalesforceAuthException>();
        host.Server.Requests.Should().BeEmpty();
    }
}
