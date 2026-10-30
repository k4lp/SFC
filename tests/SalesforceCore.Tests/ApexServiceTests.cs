using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Apex; // Added missing namespace
using Moq;
using Xunit;
using FluentAssertions;

namespace SalesforceCore.Tests;

public class ApexServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ITokenProvider> _mockTokenProvider;
    private readonly Mock<IOptions<SalesforceOptions>> _mockOptions;
    private readonly Mock<ILogger<ApexService>> _mockLogger;
    private readonly ApexService _service;
    private readonly HttpClient _httpClient;

    public ApexServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockTokenProvider = new Mock<ITokenProvider>();
        _mockLogger = new Mock<ILogger<ApexService>>();
        
        var options = new SalesforceOptions();
        _mockOptions = new Mock<IOptions<SalesforceOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(options);

        // Setup generic HttpClient
        _httpClient = new HttpClient(new MockHttpMessageHandler()) 
        { 
            BaseAddress = new Uri("https://na1.salesforce.com") 
        };

        _mockTokenProvider.Setup(p => p.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-token");
        _mockTokenProvider.Setup(p => p.GetInstanceUrlAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://na1.salesforce.com");

        _service = new ApexService(_mockClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetAsync_ShouldCallApexEndpoint()
    {
        var expected = new JObject { ["ok"] = true };
        _mockClient.Setup(c => c.GetAsync<JObject>("/services/apexrest/my/apex/class", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _service.GetAsync<JObject>("/my/apex/class");

        result.Should().BeSameAs(expected);
        _mockClient.Verify(c => c.GetAsync<JObject>("/services/apexrest/my/apex/class", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("my/class", "/services/apexrest/my/class")]
    [InlineData("/services/apexrest/x", "/services/apexrest/x")]
    public async Task GetAsync_NormalisesPath(string input, string expected)
    {
        await _service.GetAsync<JObject>(input);
        _mockClient.Verify(c => c.GetAsync<JObject>(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("/a/%2e%2e/b")]
    [InlineData("/a/%2F/b")]
    [InlineData("a\\b")]
    [InlineData("https://evil.example/x")]
    [InlineData(" ")]
    public async Task GetAsync_RejectsTraversal(string path)
    {
        var act = () => _service.GetAsync<JObject>(path);
        await act.Should().ThrowAsync<ArgumentException>();
        _mockClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WithQueryParams_EscapesAndAppends()
    {
        await _service.GetAsync<JObject>("/c?x=1", new Dictionary<string, string> { ["a b"] = "c&d" });
        _mockClient.Verify(c => c.GetAsync<JObject>("/services/apexrest/c?x=1&a%20b=c%26d", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_WithNullOrEmptyQueryParams_DoesNotAppendQuestionMark()
    {
        await _service.GetAsync<JObject>("/c", (Dictionary<string, string>)null!);
        await _service.GetAsync<JObject>("/c", new Dictionary<string, string>());
        _mockClient.Verify(c => c.GetAsync<JObject>("/services/apexrest/c", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PostAsync_PassesBodyAndToken()
    {
        using var cts = new CancellationTokenSource();
        var body = new { a = 1 };
        await _service.PostAsync<JObject>("/p", body, cts.Token);
        _mockClient.Verify(c => c.PostAsync<JObject>("/services/apexrest/p", body, cts.Token), Times.Once);
    }
}

// Simple mock handler to prevent actual network calls
public class MockHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{}")
        });
    }
}
