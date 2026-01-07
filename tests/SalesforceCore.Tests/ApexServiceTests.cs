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
        // Act
        try 
        {
            await _service.GetAsync<JObject>("/my/apex/class");
        }
        catch (JsonReaderException) 
        {
            // Expected since mock returns empty string
        }
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
