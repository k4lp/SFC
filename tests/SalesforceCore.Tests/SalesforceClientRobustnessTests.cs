using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.Tests;

public class SalesforceClientRobustnessTests
{
    private sealed class LazyInstanceUrlTokenProvider : ITokenProvider
    {
        private string? _instanceUrl;

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            // Simulate providers that only populate instance_url when a token is acquired.
            _instanceUrl ??= "https://example.my.salesforce.com";
            return Task.FromResult<string?>("access-token");
        }

        public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_instanceUrl);

        public Task RevokeTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class RotatingTokenProvider : ITokenProvider
    {
        public int RefreshCalls { get; private set; }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("token-1");

        public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("https://example.my.salesforce.com");

        public Task RevokeTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult<string?>("token-2");
        }

        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastAccessToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastAccessToken = request.Headers.Authorization?.Parameter;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private int _callCount;
        public List<string?> AccessTokens { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AccessTokens.Add(request.Headers.Authorization?.Parameter);
            _callCount++;

            if (_callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class NoContentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty)
            });
        }
    }

    private static SalesforceClient CreateClient(HttpMessageHandler handler, ITokenProvider tokenProvider)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new SalesforceOptions
        {
            ApiVersion = "v60.0",
            HttpTimeout = TimeSpan.FromSeconds(5)
        });

        return new SalesforceClient(httpClient, tokenProvider, options, NullLogger<SalesforceClient>.Instance);
    }

    [Fact]
    public async Task GetAsync_FirstCall_TokenProviderPopulatesInstanceUrlDuringGetAccessToken_Succeeds()
    {
        var handler = new CapturingHandler();
        var client = CreateClient(handler, new LazyInstanceUrlTokenProvider());

        var result = await client.GetAsync("/limits");

        result.Should().BeOfType<JsonObject>();
        handler.LastAccessToken.Should().Be("access-token");
        handler.LastRequestUri.Should().NotBeNull();
        handler.LastRequestUri!.ToString().Should().Be("https://example.my.salesforce.com/services/data/v60.0/limits");
    }

    [Fact]
    public async Task GetAsync_401_RefreshToken_RetriesWithNewToken()
    {
        var tokenProvider = new RotatingTokenProvider();
        var handler = new SequenceHandler();
        var client = CreateClient(handler, tokenProvider);

        var result = await client.GetAsync("/limits");

        result.Should().BeOfType<JsonObject>();
        tokenProvider.RefreshCalls.Should().Be(1);
        handler.AccessTokens.Should().HaveCount(2);
        handler.AccessTokens[0].Should().Be("token-1");
        handler.AccessTokens[1].Should().Be("token-2");
    }

    [Fact]
    public async Task PutAsync_NoContent_ReturnsEmptyJsonObject()
    {
        var client = CreateClient(new NoContentHandler(), new LazyInstanceUrlTokenProvider());

        var result = await client.PutAsync("/sobjects/Account/001xxx", new { Name = "Acme" });

        result.Should().BeOfType<JsonObject>();
        ((JsonObject)result).Count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_GenericJsonNode_NoContent_ReturnsEmptyJsonObject()
    {
        var client = CreateClient(new NoContentHandler(), new LazyInstanceUrlTokenProvider());

        var result = await client.DeleteAsync<JsonNode>("/sobjects/Account/001xxx");

        result.Should().BeOfType<JsonObject>();
        ((JsonObject)result).Count.Should().Be(0);
    }
}

