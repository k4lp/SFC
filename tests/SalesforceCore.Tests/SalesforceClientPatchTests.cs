using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.Tests;

public class SalesforceClientPatchTests
{
    private sealed class StubTokenProvider : ITokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("access-token");
        public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("https://example.my.salesforce.com");
        public Task RevokeTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty)
            };
            return Task.FromResult(response);
        }
    }

    private static SalesforceClient CreateClient()
    {
        var httpClient = new HttpClient(new NoContentHandler());
        var tokenProvider = new StubTokenProvider();
        var options = Options.Create(new SalesforceOptions
        {
            ApiVersion = "v60.0",
            HttpTimeout = TimeSpan.FromSeconds(5)
        });

        return new SalesforceClient(httpClient, tokenProvider, options, NullLogger<SalesforceClient>.Instance);
    }

    [Fact]
    public async Task PatchAsync_NoContent_ReturnsEmptyJsonObject()
    {
        var client = CreateClient();

        var result = await client.PatchAsync("/sobjects/Account/001xxx", new { Name = "Acme" });

        result.Should().BeOfType<JsonObject>();
        ((JsonObject)result).Count.Should().Be(0);
    }

    [Fact]
    public async Task PatchAsync_GenericJsonNode_NoContent_ReturnsEmptyJsonObject()
    {
        var client = CreateClient();

        var result = await client.PatchAsync<JsonNode>("/sobjects/Account/001xxx", new { Name = "Acme" });

        result.Should().BeOfType<JsonObject>();
        ((JsonObject)result).Count.Should().Be(0);
    }
}

