using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using System.Text.Json;

namespace SalesforceCore.Tests;

/// <summary>
/// Unit tests for CompositeService batching behavior.
/// Verifies that large record sets are correctly chunked into 25-record batches.
/// </summary>
public class CompositeServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly IOptions<SalesforceOptions> _options;
    private readonly Mock<ILogger<CompositeService>> _mockLogger;
    private readonly CompositeService _service;
    private int _postAsyncCallCount;

    public CompositeServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _options = Options.Create(new SalesforceOptions { ApiVersion = "v62.0" });
        _mockLogger = new Mock<ILogger<CompositeService>>();
        _postAsyncCallCount = 0;

        // Setup mock to track calls and return successful responses
        _mockClient.Setup(c => c.PostAsync<CompositeResponse>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string endpoint, object payload, CancellationToken ct) =>
            {
                _postAsyncCallCount++;
                var request = payload as CompositeRequest;
                var response = new CompositeResponse();

                // Generate successful responses for each sub-request
                if (request != null)
                {
                    foreach (var subRequest in request.CompositeSubRequests)
                    {
                        response.CompositeSubResponses.Add(new CompositeSubResponse
                        {
                            ReferenceId = subRequest.ReferenceId,
                            HttpStatusCode = 201,
                            Body = JsonSerializer.SerializeToNode(new { id = $"001{Guid.NewGuid():N}".Substring(0, 18), success = true })
                        });
                    }
                }

                return response;
            });

        _service = new CompositeService(_mockClient.Object, _options, _mockLogger.Object);
    }

    [Fact]
    public async Task CreateRecordsAsync_EmptyList_ShouldNotCallApi()
    {
        // Arrange
        var records = new List<Dictionary<string, object?>>();

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().BeEmpty();
        _postAsyncCallCount.Should().Be(0);
    }

    [Fact]
    public async Task CreateRecordsAsync_LessThan25Records_ShouldMakeSingleCall()
    {
        // Arrange
        var records = CreateTestRecords(10);

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(10);
        _postAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateRecordsAsync_Exactly25Records_ShouldMakeSingleCall()
    {
        // Arrange
        var records = CreateTestRecords(25);

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(25);
        _postAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateRecordsAsync_26Records_ShouldMakeTwoCalls()
    {
        // Arrange
        var records = CreateTestRecords(26);

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(26);
        _postAsyncCallCount.Should().Be(2); // 25 + 1
    }

    [Fact]
    public async Task CreateRecordsAsync_55Records_ShouldMakeThreeCalls()
    {
        // Arrange: 55 records = 25 + 25 + 5
        var records = CreateTestRecords(55);

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(55);
        _postAsyncCallCount.Should().Be(3);
    }

    [Fact]
    public async Task CreateRecordsAsync_100Records_ShouldMakeFourCalls()
    {
        // Arrange: 100 records = 25 + 25 + 25 + 25
        var records = CreateTestRecords(100);

        // Act
        var results = await _service.CreateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(100);
        _postAsyncCallCount.Should().Be(4);
    }

    [Fact]
    public async Task UpdateRecordsAsync_55Records_ShouldMakeThreeCalls()
    {
        // Arrange: 55 records with Ids
        var records = CreateTestRecordsWithIds(55);

        // Act
        var results = await _service.UpdateRecordsAsync("Account", records);

        // Assert
        results.Should().HaveCount(55);
        _postAsyncCallCount.Should().Be(3);
    }

    [Fact]
    public async Task DeleteRecordsAsync_55Records_ShouldMakeThreeCalls()
    {
        // Arrange
        var ids = Enumerable.Range(1, 55).Select(i => $"001{i:D15}").ToList();

        // Act
        var results = await _service.DeleteRecordsAsync("Account", ids);

        // Assert
        results.Should().HaveCount(55);
        _postAsyncCallCount.Should().Be(3);
    }

    [Fact]
    public async Task UpsertRecordsAsync_WithExternalId_ShouldBatchCorrectly()
    {
        // Arrange
        var records = Enumerable.Range(1, 55)
            .Select(i => new Dictionary<string, object?>
            {
                { "External_Id__c", $"EXT{i:D5}" },
                { "Name", $"Account {i}" }
            })
            .ToList();

        // Act
        var results = await _service.UpsertRecordsAsync("Account", "External_Id__c", records);

        // Assert
        results.Should().HaveCount(55);
        _postAsyncCallCount.Should().Be(3);
    }

    [Fact]
    public async Task UpdateRecordsAsync_MissingId_ShouldThrowException()
    {
        // Arrange: Record without Id
        var records = new List<Dictionary<string, object?>>
        {
            new() { { "Name", "Test Account" } } // No Id!
        };

        // Act & Assert
        var act = () => _service.UpdateRecordsAsync("Account", records);
        await act.Should().ThrowAsync<SalesforceCore.Models.Errors.SalesforceValidationException>()
            .WithMessage("*Id*");
    }

    [Fact]
    public async Task ExecuteAsync_TooManySubRequests_ShouldThrowException()
    {
        // Arrange: Create request with more than 25 sub-requests directly
        var request = new CompositeRequest();
        for (int i = 0; i < 30; i++)
        {
            request.CompositeSubRequests.Add(new CompositeSubRequest
            {
                Method = "POST",
                Url = "/services/data/v62.0/sobjects/Account",
                ReferenceId = $"ref_{i}",
                Body = new { Name = $"Account {i}" }
            });
        }

        // Act & Assert
        var act = () => _service.ExecuteAsync(request);
        await act.Should().ThrowAsync<SalesforceCore.Models.Errors.SalesforceException>()
            .WithMessage("*exceeds maximum*25*");
    }

    [Fact]
    public async Task CreateBatch_ShouldBuildCorrectRequest()
    {
        // Arrange
        var builder = _service.CreateBatch();

        // Act
        var results = await builder
            .Create("Account", new Dictionary<string, object?> { { "Name", "Test 1" } })
            .Create("Account", new Dictionary<string, object?> { { "Name", "Test 2" } })
            .Update("Account", "001xxx", new Dictionary<string, object?> { { "Name", "Updated" } })
            .Delete("Account", "001yyy")
            .Query("SELECT Id FROM Account LIMIT 1")
            .ExecuteAsync();

        // Assert
        results.Should().HaveCount(5);
        _postAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateBatch_WithAllOrNone_ShouldSetFlag()
    {
        // Arrange
        CompositeRequest? capturedRequest = null;
        _mockClient.Setup(c => c.PostAsync<CompositeResponse>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((_, req, _) => capturedRequest = req as CompositeRequest)
            .ReturnsAsync(new CompositeResponse());

        var builder = _service.CreateBatch();

        // Act
        await builder
            .WithAllOrNone()
            .Create("Account", new Dictionary<string, object?> { { "Name", "Test" } })
            .ExecuteAsync();

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.AllOrNone.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRecordsAsync_AllOrNone_ShouldBePropagatedToEachBatch()
    {
        // Arrange
        var capturedRequests = new List<CompositeRequest>();
        _mockClient.Setup(c => c.PostAsync<CompositeResponse>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((_, req, _) =>
            {
                if (req is CompositeRequest cr)
                    capturedRequests.Add(cr);
            })
            .ReturnsAsync(new CompositeResponse());

        var records = CreateTestRecords(55);

        // Act
        await _service.CreateRecordsAsync("Account", records, allOrNone: true);

        // Assert
        capturedRequests.Should().HaveCount(3);
        capturedRequests.All(r => r.AllOrNone).Should().BeTrue();
    }

    private static List<Dictionary<string, object?>> CreateTestRecords(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Dictionary<string, object?>
            {
                { "Name", $"Account {i}" },
                { "Website", $"https://account{i}.example.com" }
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> CreateTestRecordsWithIds(int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new Dictionary<string, object?>
            {
                { "Id", $"001{i:D15}" },
                { "Name", $"Updated Account {i}" }
            })
            .ToList();
    }
}
