using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using SalesforceCore.Services.Reports;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Tests;

public class ReportServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ILogger<ReportService>> _mockLogger;
    private readonly ReportService _service;

    public ReportServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockLogger = new Mock<ILogger<ReportService>>();
        _service = new ReportService(_mockClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ListReportsAsync_ShouldCallAnalyticsEndpoint()
    {
        // Arrange
        var expectedResponse = JsonNode.Parse("{ \"reports\": [ { \"id\": \"00Oxxx\", \"name\": \"My Report\" } ] }")
            ?? new JsonObject();

        var tcs = new TaskCompletionSource<JsonNode>();
        tcs.SetResult(expectedResponse);
        _mockClient.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains("/analytics/reports")),
                It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        // Act
        var result = await _service.ListReportsAsync();

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("00Oxxx");
    }

    [Fact]
    public async Task RunReportAsync_ShouldCallRunEndpoint()
    {
        // Arrange
        var reportId = "00Oxxx";
        var expectedResponse = JsonNode.Parse("{ \"reportMetadata\": { \"name\": \"Test\" } }")
            ?? new JsonObject();

        var tcsRunReport = new TaskCompletionSource<JsonNode>();
        tcsRunReport.SetResult(expectedResponse);
        _mockClient.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains($"/analytics/reports/{reportId}")),
                It.IsAny<CancellationToken>()))
            .Returns(tcsRunReport.Task);

        // Act
        var result = await _service.RunReportAsync(reportId);

        // Assert
        result.Should().NotBeNull();
        _mockClient.Verify(c => c.GetAsync(
            It.Is<string>(s => s.Contains("includeDetails=true")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunReportWithFiltersAsync_ShouldPostFilters()
    {
        // Arrange
        var reportId = "00Oxxx";
        var filters = new List<ReportFilter>
        {
            new ReportFilter { Column = "ACCOUNT_NAME", Operator = "equals", Value = "Acme" }
        };
        var expectedResponse = JsonNode.Parse("{ \"reportMetadata\": { \"name\": \"Filtered\" } }")
            ?? new JsonObject();

        var tcsRunReportWithFilters = new TaskCompletionSource<JsonNode>();
        tcsRunReportWithFilters.SetResult(expectedResponse);
        _mockClient.Setup(c => c.PostAsync(
                It.Is<string>(s => s.Contains($"/analytics/reports/{reportId}")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Returns(tcsRunReportWithFilters.Task);

        // Act
        var result = await _service.RunReportWithFiltersAsync(reportId, filters);

        // Assert
        result.Should().NotBeNull();
        _mockClient.Verify(c => c.PostAsync(
            It.Is<string>(s => s.Contains(reportId)),
            It.Is<object>(o => o != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
