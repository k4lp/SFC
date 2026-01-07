using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Tests;

public class ReplicationServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ILogger<ReplicationService>> _mockLogger;
    private readonly ReplicationService _service;

    public ReplicationServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockLogger = new Mock<ILogger<ReplicationService>>();
        _service = new ReplicationService(_mockClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetUpdatedAsync_ShouldCallGetUpdatedEndpoint()
    {
        // Arrange
        var objectName = "Account";
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;
        var expectedResponse = new UpdatedRecordsResult
        {
            Ids = new List<string> { "001xxx", "001yyy" },
            LatestDateCovered = end.ToString("o")
        };

        _mockClient.Setup(c => c.GetAsync<UpdatedRecordsResult>(
                It.Is<string>(s => s.Contains($"/sobjects/{objectName}/updated")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.GetUpdatedAsync(objectName, start, end);

        // Assert
        result.Ids.Should().HaveCount(2);
        _mockClient.Verify(c => c.GetAsync<UpdatedRecordsResult>(
            It.Is<string>(s => s.Contains($"start=") && s.Contains($"end=")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDeletedAsync_ShouldCallGetDeletedEndpoint()
    {
        // Arrange
        var objectName = "Contact";
        var start = DateTime.UtcNow.AddHours(-1);
        var end = DateTime.UtcNow;
        var expectedResponse = new DeletedRecordsResult
        {
            DeletedRecords = new List<DeletedRecordInfo>
            {
                new DeletedRecordInfo { Id = "003xxx", DeletedDate = DateTime.UtcNow.ToString("o") }
            },
            LatestDateCovered = end.ToString("o")
        };

        _mockClient.Setup(c => c.GetAsync<DeletedRecordsResult>(
                It.Is<string>(s => s.Contains($"/sobjects/{objectName}/deleted")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.GetDeletedAsync(objectName, start, end);

        // Assert
        result.DeletedRecords.Should().HaveCount(1);
        _mockClient.Verify(c => c.GetAsync<DeletedRecordsResult>(
            It.Is<string>(s => s.Contains($"start=") && s.Contains($"end=")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
