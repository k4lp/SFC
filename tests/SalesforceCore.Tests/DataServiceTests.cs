using System.Web;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Caching;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Utilities;

namespace SalesforceCore.Tests;

public class DataServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ISchemaService> _mockSchema;
    private readonly Mock<IBulkService> _mockBulkService;
    private readonly Mock<ICacheProvider> _mockCache;
    private readonly Mock<IOptions<SalesforceOptions>> _mockOptions;
    private readonly Mock<ILogger<DataService>> _mockLogger;
    private readonly DataService _service;

    public DataServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockSchema = new Mock<ISchemaService>();
        _mockBulkService = new Mock<IBulkService>();
        _mockCache = new Mock<ICacheProvider>();
        _mockLogger = new Mock<ILogger<DataService>>();

        var options = new SalesforceOptions();
        _mockOptions = new Mock<IOptions<SalesforceOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(options);

        _service = new DataService(
            _mockClient.Object,
            _mockSchema.Object,
            _mockBulkService.Object,
            _mockCache.Object,
            _mockOptions.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task QueryAsync_ShouldCallCorrectEndpoint()
    {
        // Arrange
        var soql = "SELECT Id, Name FROM Account";
        var expectedResponse = new QueryResult
        {
            TotalSize = 1,
            Done = true,
            Records = new List<JsonObject> { new JsonObject { ["Id"] = "001xxx", ["Name"] = "Test Account" } }
        };

        _mockClient.Setup(c => c.GetAsync<QueryResult>(
                It.Is<string>(s => s.Contains("/query")),
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
    public async Task GetRecordAsync_ShouldCallSObjectEndpoint()
    {
        // Arrange
        var id = "001xxx";
        var expectedResponse = new JsonObject { ["Id"] = "001xxx", ["Name"] = "Test" };

        _mockClient.Setup(c => c.GetAsync(
                It.Is<string>(s => s.Contains($"/sobjects/Account/{id}")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.GetRecordAsync("Account", id);

        // Assert
        result["Id"]?.ToString().Should().Be(id);
    }

    [Fact]
    public async Task CreateRecordAsync_ShouldFilterFieldsAndCallPost()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "Name", "New Account" },
            { "ReadOnlyField", "ShouldBeIgnored" }
        };

        // Mock schema to return only "Name" as createable
        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> 
            { 
                new SObjectField { Name = "Name", Createable = true } 
            });

        var expectedResult = new CreateResult { Id = "001new", Success = true };

        _mockClient.Setup(c => c.PostAsync<CreateResult>(
                It.Is<string>(s => s.Contains("/sobjects/Account")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var id = await _service.CreateRecordAsync("Account", data);

        // Assert
        id.Should().Be("001new");
        
        // Verify only Name was sent
        _mockClient.Verify(c => c.PostAsync<CreateResult>(
            It.IsAny<string>(),
            It.Is<Dictionary<string, object?>>(d => d.ContainsKey("Name") && !d.ContainsKey("ReadOnlyField")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRecordAsync_ShouldFilterFieldsAndCallPatch()
    {
        // Arrange
        var id = "001xxx";
        var data = new Dictionary<string, object?>
        {
            { "Name", "Updated Account" },
            { "CreatedDate", "2023-01-01" } // Read only
        };

        _mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> 
            { 
                new SObjectField { Name = "Name", Updateable = true } 
            });

        _mockClient.Setup(c => c.PatchAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonObject());

        // Act
        await _service.UpdateRecordAsync("Account", id, data);

        // Assert
        _mockClient.Verify(c => c.PatchAsync(
            It.Is<string>(s => s.Contains($"sobjects/Account/{id}")),
            It.Is<Dictionary<string, object?>>(d => d.ContainsKey("Name") && !d.ContainsKey("CreatedDate")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteRecordAsync_ShouldCallDelete()
    {
        // Arrange
        var id = "001xxx";

        _mockClient.Setup(c => c.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.DeleteRecordAsync("Account", id);

        // Assert
        _mockClient.Verify(c => c.DeleteAsync(
            It.Is<string>(s => s.Contains($"sobjects/Account/{id}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #region Upsert Tests

    [Fact]
    public async Task UpsertRecordAsync_ShouldCallPatchWithExternalId()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "Name", "Upserted Account" },
            { "Industry", "Technology" }
        };

        _mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new SObjectField { Name = "Name", Updateable = true },
                new SObjectField { Name = "Industry", Updateable = true }
            });

        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new SObjectField { Name = "Name", Createable = true },
                new SObjectField { Name = "Industry", Createable = true }
            });

        var response = new JsonObject { ["id"] = "001newxxx", ["created"] = true };
        _mockClient.Setup(c => c.PatchAsync<JsonNode>(
                It.Is<string>(s => s.Contains("/sobjects/Account/External_Id__c/EXT-123")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _service.UpsertRecordAsync("Account", "External_Id__c", "EXT-123", data);

        // Assert
        result.Success.Should().BeTrue();
        result.Id.Should().Be("001newxxx");
        result.Created.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertRecordAsync_WhenUpdating_ShouldReturnCreatedFalse()
    {
        // Arrange
        var data = new Dictionary<string, object?> { { "Name", "Updated Account" } };

        _mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Updateable = true } });
        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Createable = true } });

        var response = new JsonObject { ["id"] = "001existing", ["created"] = false };
        _mockClient.Setup(c => c.PatchAsync<JsonNode>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _service.UpsertRecordAsync("Account", "External_Id__c", "EXT-456", data);

        // Assert
        result.Created.Should().BeFalse();
        result.Id.Should().Be("001existing");
    }

    [Fact]
    public async Task UpsertRecordAsync_ShouldExcludeExternalIdFieldFromPayload()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "External_Id__c", "EXT-789" } // Should be excluded from payload
        };

        _mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new SObjectField { Name = "Name", Updateable = true },
                new SObjectField { Name = "External_Id__c", Updateable = true }
            });
        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new SObjectField { Name = "Name", Createable = true },
                new SObjectField { Name = "External_Id__c", Createable = true }
            });

        var response = new JsonObject { ["id"] = "001xxx", ["created"] = true };
        _mockClient.Setup(c => c.PatchAsync<JsonNode>(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        await _service.UpsertRecordAsync("Account", "External_Id__c", "EXT-789", data);

        // Assert - verify External_Id__c is not in payload
        _mockClient.Verify(c => c.PatchAsync<JsonNode>(
            It.IsAny<string>(),
            It.Is<Dictionary<string, object?>>(d => !d.ContainsKey("External_Id__c") && d.ContainsKey("Name")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpsertRecordAsync_WithEmptySObject_ShouldThrowArgumentException()
    {
        // Arrange
        var data = new Dictionary<string, object?> { { "Name", "Test" } };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertRecordAsync("", "External_Id__c", "EXT-123", data));
    }

    [Fact]
    public async Task UpsertRecordAsync_WithEmptyExternalIdField_ShouldThrowArgumentException()
    {
        // Arrange
        var data = new Dictionary<string, object?> { { "Name", "Test" } };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertRecordAsync("Account", "", "EXT-123", data));
    }

    [Fact]
    public async Task UpsertRecordAsync_WithEmptyExternalIdValue_ShouldThrowArgumentException()
    {
        // Arrange
        var data = new Dictionary<string, object?> { { "Name", "Test" } };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpsertRecordAsync("Account", "External_Id__c", "", data));
    }

    #endregion

    #region Batch Create Tests

    [Fact]
    public async Task BatchCreateAsync_WithSmallList_ShouldUseCompositeApi()
    {
        // Arrange
        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "Name", "Account 1" } },
            new Dictionary<string, object?> { { "Name", "Account 2" } }
        };

        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Createable = true } });

        var compositeResponse = new JsonArray
        {
            new JsonObject { ["id"] = "001xxx1", ["success"] = true, ["errors"] = new JsonArray() },
            new JsonObject { ["id"] = "001xxx2", ["success"] = true, ["errors"] = new JsonArray() }
        };

        _mockClient.Setup(c => c.PostAsync<JsonArray>(
                It.Is<string>(s => s.Contains("/composite/sobjects")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(compositeResponse);

        // Act
        var result = await _service.BatchCreateAsync("Account", records);

        // Assert
        result.UsedBulkApi.Should().BeFalse();
        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(0);
        result.SuccessfulIds.Should().Contain("001xxx1");
        result.SuccessfulIds.Should().Contain("001xxx2");
    }

    [Fact]
    public async Task BatchCreateAsync_WithLargeList_ShouldUseBulkApi()
    {
        // Arrange
        var records = Enumerable.Range(1, 250)
            .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?> { { "Name", $"Account {i}" } })
            .ToList();

        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Createable = true } });

        var bulkResult = new BulkJobResults
        {
            SuccessfulRecords = Enumerable.Range(1, 250)
                .Select(i => new BulkRecordResult { Id = $"001xxx{i}", Success = true })
                .ToList(),
            FailedRecords = new List<BulkRecordResult>()
        };

        _mockBulkService.Setup(b => b.InsertAsync(
                "Account",
                It.IsAny<IEnumerable<Dictionary<string, object?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        // Act
        var result = await _service.BatchCreateAsync("Account", records);

        // Assert
        result.UsedBulkApi.Should().BeTrue();
        result.SuccessCount.Should().Be(250);
        result.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task BatchCreateAsync_WithCustomThreshold_ShouldRespectThreshold()
    {
        // Arrange
        var records = Enumerable.Range(1, 60)
            .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?> { { "Name", $"Account {i}" } })
            .ToList();

        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Createable = true } });

        var bulkResult = new BulkJobResults
        {
            SuccessfulRecords = records.Select((_, i) => new BulkRecordResult { Id = $"001xxx{i}", Success = true }).ToList(),
            FailedRecords = new List<BulkRecordResult>()
        };

        _mockBulkService.Setup(b => b.InsertAsync(
                "Account",
                It.IsAny<IEnumerable<Dictionary<string, object?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        // Act - use threshold of 50
        var result = await _service.BatchCreateAsync("Account", records, bulkThreshold: 50);

        // Assert - should use Bulk API since 60 > 50
        result.UsedBulkApi.Should().BeTrue();
        _mockBulkService.Verify(b => b.InsertAsync(
            "Account",
            It.IsAny<IEnumerable<Dictionary<string, object?>>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BatchCreateAsync_WithEmptyList_ShouldReturnEmptyResult()
    {
        // Arrange
        var records = new List<IDictionary<string, object?>>();

        // Act
        var result = await _service.BatchCreateAsync("Account", records);

        // Assert
        result.SuccessCount.Should().Be(0);
        result.FailureCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task BatchCreateAsync_WithPartialFailure_ShouldReportErrors()
    {
        // Arrange
        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "Name", "Valid Account" } },
            new Dictionary<string, object?> { { "Name", null } } // Will fail validation
        };

        _mockSchema.Setup(s => s.GetCreateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Createable = true } });

        var compositeResponse = new JsonArray
        {
            new JsonObject { ["id"] = "001xxx1", ["success"] = true, ["errors"] = new JsonArray() },
            new JsonObject
            {
                ["success"] = false,
                ["errors"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["statusCode"] = "REQUIRED_FIELD_MISSING",
                        ["message"] = "Name is required",
                        ["fields"] = new JsonArray { "Name" }
                    }
                }
            }
        };

        _mockClient.Setup(c => c.PostAsync<JsonArray>(
                It.Is<string>(s => s.Contains("/composite/sobjects")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(compositeResponse);

        // Act
        var result = await _service.BatchCreateAsync("Account", records);

        // Assert
        result.SuccessCount.Should().Be(1);
        result.FailureCount.Should().Be(1);
        result.AllSucceeded.Should().BeFalse();
        result.FailedRecords.Should().HaveCount(1);
        result.FailedRecords[0].Index.Should().Be(1);
        result.FailedRecords[0].Message.Should().Contain("Name is required");
    }

    #endregion

    #region Batch Update Tests

    [Fact]
    public async Task BatchUpdateAsync_ShouldIncludeIdField()
    {
        // Arrange
        var records = new List<IDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "Id", "001xxx1" }, { "Name", "Updated 1" } },
            new Dictionary<string, object?> { { "Id", "001xxx2" }, { "Name", "Updated 2" } }
        };

        _mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new SObjectField { Name = "Name", Updateable = true } });

        var compositeResponse = new JsonArray
        {
            new JsonObject { ["id"] = "001xxx1", ["success"] = true, ["errors"] = new JsonArray() },
            new JsonObject { ["id"] = "001xxx2", ["success"] = true, ["errors"] = new JsonArray() }
        };

        _mockClient.Setup(c => c.PatchAsync<JsonArray>(
                It.Is<string>(s => s.Contains("/composite/sobjects")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(compositeResponse);

        // Act
        var result = await _service.BatchUpdateAsync("Account", records);

        // Assert
        result.SuccessCount.Should().Be(2);
        result.UsedBulkApi.Should().BeFalse();
    }

    #endregion

    #region Batch Delete Tests

    [Fact]
    public async Task BatchDeleteAsync_WithSmallList_ShouldUseCompositeApi()
    {
        // Arrange
        var ids = new[] { "001xxx1", "001xxx2", "001xxx3" };

        var compositeResponse = new JsonArray
        {
            new JsonObject { ["id"] = "001xxx1", ["success"] = true, ["errors"] = new JsonArray() },
            new JsonObject { ["id"] = "001xxx2", ["success"] = true, ["errors"] = new JsonArray() },
            new JsonObject { ["id"] = "001xxx3", ["success"] = true, ["errors"] = new JsonArray() }
        };

        _mockClient.Setup(c => c.DeleteAsync<JsonArray>(
                It.Is<string>(s => s.Contains("/composite/sobjects")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(compositeResponse);

        // Act
        var result = await _service.BatchDeleteAsync("Account", ids);

        // Assert
        result.UsedBulkApi.Should().BeFalse();
        result.SuccessCount.Should().Be(3);
    }

    [Fact]
    public async Task BatchDeleteAsync_WithEmptyList_ShouldReturnEmptyResult()
    {
        // Arrange
        var ids = Array.Empty<string>();

        // Act
        var result = await _service.BatchDeleteAsync("Account", ids);

        // Assert
        result.SuccessCount.Should().Be(0);
        result.TotalCount.Should().Be(0);
    }

    #endregion

    #region Polymorphic Lookup Tests

    [Fact]
    public async Task ResolvePolymorphicTypeAsync_WithKnownPrefix_ShouldResolveFromStaticMap()
    {
        // Arrange - "001" is the standard Account prefix
        var accountId = "001000000000001AAA";

        // Act
        var result = await _service.ResolvePolymorphicTypeAsync(accountId);

        // Assert
        result.Should().Be("Account");
        // Should not query EntityDefinition for known prefixes
        _mockCache.Verify(c => c.GetOrCreateAsync<Dictionary<string, string>>(
            It.IsAny<string>(),
            It.IsAny<Func<CancellationToken, Task<Dictionary<string, string>?>>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolvePolymorphicTypeAsync_WithContactPrefix_ShouldResolveContact()
    {
        // Arrange - "003" is the standard Contact prefix
        var contactId = "003000000000001AAA";

        // Act
        var result = await _service.ResolvePolymorphicTypeAsync(contactId);

        // Assert
        result.Should().Be("Contact");
    }

    [Fact]
    public async Task ResolvePolymorphicTypeAsync_WithCustomObjectPrefix_ShouldQueryEntityDefinition()
    {
        // Arrange - "a0A" is a custom object prefix
        var customId = "a0A000000000001AAA";
        var prefixMap = new Dictionary<string, string> { { "a0A", "Custom_Object__c" } };

        _mockCache.Setup(c => c.GetOrCreateAsync<Dictionary<string, string>>(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<Dictionary<string, string>?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixMap);

        // Act
        var result = await _service.ResolvePolymorphicTypeAsync(customId);

        // Assert
        result.Should().Be("Custom_Object__c");
    }

    [Fact]
    public async Task ResolvePolymorphicTypeAsync_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var invalidId = "xx";

        // Act
        var result = await _service.ResolvePolymorphicTypeAsync(invalidId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolvePolymorphicTypeAsync_WithEmptyId_ShouldReturnNull()
    {
        // Act
        var result = await _service.ResolvePolymorphicTypeAsync("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task BatchResolvePolymorphicTypesAsync_ShouldResolveMultipleIds()
    {
        // Arrange
        var ids = new[]
        {
            "001000000000001AAA", // Account
            "003000000000001AAA", // Contact
            "006000000000001AAA"  // Opportunity
        };

        // Act
        var result = await _service.BatchResolvePolymorphicTypesAsync(ids);

        // Assert
        result.Should().HaveCount(3);
        result["001000000000001AAA"].Should().Be("Account");
        result["003000000000001AAA"].Should().Be("Contact");
        result["006000000000001AAA"].Should().Be("Opportunity");
    }

    [Fact]
    public async Task BatchResolvePolymorphicTypesAsync_WithMixedKnownAndUnknown_ShouldResolveKnownAndQueryUnknown()
    {
        // Arrange
        var ids = new[]
        {
            "001000000000001AAA", // Account (known)
            "a0B000000000001AAA"  // Custom object (unknown)
        };

        var prefixMap = new Dictionary<string, string> { { "a0B", "Another_Custom__c" } };

        _mockCache.Setup(c => c.GetOrCreateAsync<Dictionary<string, string>>(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<Dictionary<string, string>?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(prefixMap);

        // Act
        var result = await _service.BatchResolvePolymorphicTypesAsync(ids);

        // Assert
        result["001000000000001AAA"].Should().Be("Account");
        result["a0B000000000001AAA"].Should().Be("Another_Custom__c");
    }

    [Fact]
    public async Task BatchResolvePolymorphicTypesAsync_WithEmptyList_ShouldReturnEmptyDictionary()
    {
        // Act
        var result = await _service.BatchResolvePolymorphicTypesAsync(Array.Empty<string>());

        // Assert
        result.Should().BeEmpty();
    }

    #endregion
}
