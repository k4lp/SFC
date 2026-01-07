using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Tests;

public class CompositeGraphTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ILogger<CompositeService>> _mockLogger;
    private readonly CompositeService _service;

    public CompositeGraphTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockLogger = new Mock<ILogger<CompositeService>>();
        var options = Options.Create(new SalesforceOptions { ApiVersion = "v60.0" });
        _service = new CompositeService(_mockClient.Object, options, _mockLogger.Object);
    }

    [Fact]
    public async Task ExecuteGraphAsync_ShouldCallCompositeGraphEndpoint()
    {
        // Arrange
        var request = new CompositeGraphRequest();
        var graph = new GraphDefinition { GraphId = "graph1" };
        graph.CompositeRequest.Add(new CompositeSubRequest
        {
            ReferenceId = "ref1",
            Method = "POST",
            Url = "/services/data/v60.0/sobjects/Account",
            Body = new { Name = "Graph Account" }
        });
        request.Graphs.Add(graph);

        var expectedResponse = new CompositeGraphResponse();
        expectedResponse.Graphs.Add(new GraphResult
        {
            GraphId = "graph1",
            IsSuccessful = true
        });

        _mockClient.Setup(c => c.PostAsync<CompositeGraphResponse>(
                It.Is<string>(s => s.Contains("/composite/graph")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.ExecuteGraphAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Graphs.Should().HaveCount(1);
        result.Graphs[0].IsSuccessful.Should().BeTrue();
        _mockClient.Verify(c => c.PostAsync<CompositeGraphResponse>(
            It.Is<string>(s => s.Contains("composite/graph")),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteGraphAsync_WithBuilder_ShouldBuildCorrectRequest()
    {
        // Arrange
        var builder = _service.CreateGraphBuilder();
        var graphBuilder = builder.StartGraph("graph1");
        graphBuilder.Add(new CompositeSubRequest
        {
            ReferenceId = "ref1",
            Method = "POST",
            Url = "/services/data/v60.0/sobjects/Contact",
            Body = new { LastName = "Graph Contact" }
        });

        var expectedResponse = new CompositeGraphResponse();
        
        _mockClient.Setup(c => c.PostAsync<CompositeGraphResponse>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        await _service.ExecuteGraphAsync(builder);

        // Assert
        _mockClient.Verify(c => c.PostAsync<CompositeGraphResponse>(
            It.IsAny<string>(),
            It.Is<CompositeGraphRequest>(req => 
                req.Graphs.Count == 1 && 
                req.Graphs[0].GraphId == "graph1" &&
                req.Graphs[0].CompositeRequest.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
