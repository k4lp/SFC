using System.Web;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using SalesforceCore.Services.Tooling;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Tests;

public class ToolingServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<IOptions<SalesforceOptions>> _mockOptions;
    private readonly Mock<ILogger<ToolingService>> _mockLogger;
    private readonly ToolingService _service;

    public ToolingServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockLogger = new Mock<ILogger<ToolingService>>();
        
        var options = new SalesforceOptions();
        _mockOptions = new Mock<IOptions<SalesforceOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(options);

        _service = new ToolingService(_mockClient.Object, _mockOptions.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteAnonymousAsync_ShouldCallCorrectEndpoint()
    {
        // Arrange
        var apex = "System.debug('Hello World');";
        var expectedResponse = new ExecuteAnonymousResult
        {
            Compiled = true,
            Success = true
        };

        _mockClient.Setup(c => c.GetAsync<ExecuteAnonymousResult>(
                It.Is<string>(s => s.Contains("/tooling/executeAnonymous")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.ExecuteAnonymousAsync(apex);

        // Assert
        result.Success.Should().BeTrue();
        result.Compiled.Should().BeTrue();
        _mockClient.Verify(c => c.GetAsync<ExecuteAnonymousResult>(
            It.Is<string>(s => HttpUtility.UrlDecode(s).Contains("anonymousBody=" + apex)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_ShouldCallToolingQueryEndpoint()
    {
        // Arrange
        var soql = "SELECT Id, Name FROM ApexClass";
        var expectedResponse = new QueryResult
        {
            TotalSize = 1,
            Done = true,
            Records = new List<JsonObject> { JsonNode.Parse("{ \"Id\": \"01pxxx\", \"Name\": \"MyClass\" }")!.AsObject() }
        };

        _mockClient.Setup(c => c.GetAsync<QueryResult>(
                It.Is<string>(s => s.Contains("/tooling/query")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.QueryAsync(soql);

        // Assert
        result.TotalSize.Should().Be(1);
        _mockClient.Verify(c => c.GetAsync<QueryResult>(
            It.Is<string>(s => HttpUtility.UrlDecode(s).Contains("q=" + soql)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetApexClassAsync_ShouldQueryApexClass()
    {
        // Arrange - use valid 15-character Salesforce ID format
        var id = "01pxx0000001234";

        _mockClient.Setup(c => c.GetAsync<QueryResult>(
                It.Is<string>(s => s.Contains("/tooling/query")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                TotalSize = 1,
                Done = true,
                Records = new List<JsonObject> { new JsonObject { ["Id"] = id, ["Name"] = "MyClass" } }
            });

        // Act
        var result = await _service.GetApexClassAsync(id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
    }
}