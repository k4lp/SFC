using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Tracking;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Comprehensive data pipeline integration tests that validate the full data flow
/// from query building through API calls, response parsing, mapping, and caching.
/// </summary>
public class DataPipelineIntegrationTests
{
    #region Query → API → Response Pipeline

    [Fact]
    public async Task FullQueryPipeline_ShouldBuildExecuteAndParseCorrectly()
    {
        // Arrange - Full pipeline from SoqlBuilder → DataService → ISalesforceClient → Response
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        // Setup schema
        mockSchema.Setup(s => s.GetQueryableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new() { Name = "Id", Type = "id" },
                new() { Name = "Name", Type = "string" },
                new() { Name = "Industry", Type = "picklist" },
                new() { Name = "AnnualRevenue", Type = "currency" }
            });

        mockSchema.Setup(s => s.SanitizeFieldListAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());
        mockSchema.Setup(s => s.SanitizeFieldListWithFlsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());

        // Capture the actual query sent
        string capturedUrl = "";
        mockClient.Setup(c => c.GetAsync<QueryResult>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((url, _) => capturedUrl = url)
            .ReturnsAsync(new QueryResult
            {
                TotalSize = 2,
                Done = true,
                Records = new List<JsonObject>
                {
                    JsonNode.Parse("{\"Id\":\"001AAA\",\"Name\":\"Acme Corp\",\"Industry\":\"Technology\",\"AnnualRevenue\":1000000}")!.AsObject(),
                    JsonNode.Parse("{\"Id\":\"001BBB\",\"Name\":\"Beta Inc\",\"Industry\":\"Finance\",\"AnnualRevenue\":5000000}")!.AsObject()
                }
            });

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act - Build and execute query using QueryPagedAsync
        var result = await dataService.QueryPagedAsync("Account",
            fields: new[] { "Id", "Name", "Industry", "AnnualRevenue" },
            filter: SoqlCondition.And(
                SoqlCondition.Equals("Industry", "Technology"),
                SoqlCondition.GreaterThan("AnnualRevenue", 100000)
            ),
            orderBy: "Name",
            pageSize: 10);

        // Assert - Full pipeline validation
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.Records.Should().HaveCount(2);

        // Verify query structure (URL-encoded)
        capturedUrl.Should().Contain("SELECT");
        capturedUrl.Should().Contain("FROM%20Account"); // %20 is URL-encoded space
        capturedUrl.Should().Contain("Industry");
        capturedUrl.Should().Contain("AnnualRevenue");
    }

    [Fact]
    public async Task QueryPipeline_ShouldHandlePagination()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        SetupDefaultSchema(mockSchema);

        // First page
        mockClient.SetupSequence(c => c.GetAsync<QueryResult>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult
            {
                TotalSize = 50,
                Done = false,
                NextRecordsUrl = "/services/data/v60.0/query/01gxx-2000",
                Records = Enumerable.Range(1, 25)
                    .Select(i => JsonNode.Parse($"{{\"Id\":\"001{i:D3}\",\"Name\":\"Record {i}\"}}")!.AsObject())
                    .ToList()
            })
            .ReturnsAsync(new QueryResult
            {
                TotalSize = 50,
                Done = true,
                Records = Enumerable.Range(26, 25)
                    .Select(i => JsonNode.Parse($"{{\"Id\":\"001{i:D3}\",\"Name\":\"Record {i}\"}}")!.AsObject())
                    .ToList()
            });

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act - Query with auto-pagination
        var allRecords = new List<JsonObject>();
        var result = await dataService.QueryAsync("SELECT Id, Name FROM Account");
        allRecords.AddRange(result.Records);

        while (!result.Done && !string.IsNullOrEmpty(result.NextRecordsUrl))
        {
            result = await dataService.QueryNextAsync(result.NextRecordsUrl);
            allRecords.AddRange(result.Records);
        }

        // Assert
        allRecords.Should().HaveCount(50);
        result.Done.Should().BeTrue();
    }

    #endregion

    #region CRUD Pipeline Tests

    [Fact]
    public async Task CreatePipeline_ShouldCallApiWithData()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        // Setup schema - returns list of createable fields
        mockSchema.Setup(s => s.GetCreateableFieldsAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new() { Name = "FirstName" },
                new() { Name = "LastName" },
                new() { Name = "Email" }
            });

        string? capturedEndpoint = null;
        mockClient.Setup(c => c.PostAsync<CreateResult>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((endpoint, _, _) =>
            {
                capturedEndpoint = endpoint;
            })
            .ReturnsAsync(new CreateResult { Id = "003xxx", Success = true });

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act
        var inputData = new Dictionary<string, object?>
        {
            ["FirstName"] = "John",
            ["LastName"] = "Doe",
            ["Email"] = "john@example.com"
        };

        var result = await dataService.CreateRecordAsync("Contact", inputData);

        // Assert
        result.Should().NotBeNullOrEmpty();
        capturedEndpoint.Should().Contain("/sobjects/Contact");
    }

    [Fact]
    public async Task UpdatePipeline_ShouldCallPatchWithData()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        // Setup schema - returns list of updateable fields
        mockSchema.Setup(s => s.GetUpdateableFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new() { Name = "Name" },
                new() { Name = "Industry" },
                new() { Name = "Rating" }
            });

        string? capturedEndpoint = null;
        mockClient.Setup(c => c.PatchAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((endpoint, _, _) => capturedEndpoint = endpoint)
            .ReturnsAsync(JsonNode.Parse("{}")!);

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act - Only update Name and Industry
        var updates = new Dictionary<string, object?>
        {
            ["Name"] = "Updated Name",
            ["Industry"] = "Healthcare"
        };

        await dataService.UpdateRecordAsync("Account", "001ABCDEFGHIJKL", updates);

        // Assert
        capturedEndpoint.Should().NotBeNull();
        capturedEndpoint.Should().Contain("/sobjects/Account/001ABCDEFGHIJKL");
    }

    [Fact]
    public async Task DeletePipeline_ShouldValidateIdAndCallApi()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        string? capturedEndpoint = null;
        mockClient.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((endpoint, _) => capturedEndpoint = endpoint)
            .Returns(Task.CompletedTask);

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act
        await dataService.DeleteRecordAsync("Account", "001ABCDEFGHIJKL");

        // Assert
        capturedEndpoint.Should().Contain("/sobjects/Account/001ABCDEFGHIJKL");
    }

    #endregion

    #region Mapping Pipeline Tests

    [Fact]
    public void MappingPipeline_ShouldTransformSalesforceToModel()
    {
        // Arrange - JSON from Salesforce API
        var salesforceJson = JsonNode.Parse(@"{
            ""Id"": ""001xxx"",
            ""Name"": ""Test Account"",
            ""Industry"": ""Technology"",
            ""AnnualRevenue"": 1500000.50,
            ""NumberOfEmployees"": 100,
            ""CreatedDate"": ""2024-01-15T10:30:00.000+0000"",
            ""Owner"": {
                ""Id"": ""005xxx"",
                ""Name"": ""John Smith""
            }
        }");

        // Act
        var model = SalesforceMapper.FromSalesforce<TestAccountModel>(salesforceJson!);

        // Assert
        model.Should().NotBeNull();
        model.Id.Should().Be("001xxx");
        model.Name.Should().Be("Test Account");
        model.Industry.Should().Be("Technology");
        model.Revenue.Should().Be(1500000.50m);
        model.EmployeeCount.Should().Be(100);
        model.OwnerName.Should().Be("John Smith");
    }

    [Fact]
    public void MappingPipeline_ShouldTransformModelToSalesforce()
    {
        // Arrange
        var model = new TestAccountModel
        {
            Id = "001xxx",
            Name = "New Account",
            Industry = "Finance",
            Revenue = 2500000m,
            EmployeeCount = 250
        };

        // Act
        var salesforcePayload = SalesforceMapper.ToSalesforceDictionary(model);

        // Assert
        salesforcePayload.Should().ContainKey("Name");
        salesforcePayload["Name"].Should().Be("New Account");
        salesforcePayload.Should().ContainKey("Industry");
        salesforcePayload.Should().ContainKey("AnnualRevenue");
        salesforcePayload["AnnualRevenue"].Should().Be(2500000m);
    }

    [Fact]
    public void MappingPipeline_ShouldHandleNullsAndMissingFields()
    {
        // Arrange - Sparse JSON response
        var sparseJson = JsonNode.Parse(@"{
            ""Id"": ""001xxx"",
            ""Name"": ""Sparse Account""
        }");

        // Act
        var model = SalesforceMapper.FromSalesforce<TestAccountModel>(sparseJson!);

        // Assert
        model.Id.Should().Be("001xxx");
        model.Name.Should().Be("Sparse Account");
        model.Industry.Should().BeNull();
        model.Revenue.Should().BeNull();
        model.EmployeeCount.Should().BeNull();
        model.OwnerName.Should().BeNull();
    }

    #endregion

    #region Caching Pipeline Tests

    [Fact]
    public async Task CachingPipeline_ShouldCacheSchemaMetadata()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new SalesforceOptions { SchemaCacheDuration = TimeSpan.FromMinutes(10) });
        var cacheProvider = new MemoryCacheProvider(memoryCache, options, NullLogger<MemoryCacheProvider>.Instance);

        int apiCallCount = 0;
        mockClient.Setup(c => c.GetAsync<SObjectDescribe>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => apiCallCount++)
            .ReturnsAsync(new SObjectDescribe { Name = "Account", Label = "Account" });

        var schemaService = new SchemaService(mockClient.Object, cacheProvider, options, NullLogger<SchemaService>.Instance);

        // Act - Multiple calls should only hit API once
        await schemaService.GetDescribeAsync("Account");
        await schemaService.GetDescribeAsync("Account");
        await schemaService.GetDescribeAsync("Account");

        // Assert
        apiCallCount.Should().Be(1, "schema should be cached after first call");
    }

    [Fact]
    public async Task CachingPipeline_ShouldHandleCacheExpiration()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new SalesforceOptions { SchemaCacheDuration = TimeSpan.FromMilliseconds(50) });
        var cacheProvider = new MemoryCacheProvider(memoryCache, options, NullLogger<MemoryCacheProvider>.Instance);

        int apiCallCount = 0;
        mockClient.Setup(c => c.GetAsync<SObjectDescribe>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => apiCallCount++)
            .ReturnsAsync(new SObjectDescribe { Name = "Account", Label = "Account" });

        var schemaService = new SchemaService(mockClient.Object, cacheProvider, options, NullLogger<SchemaService>.Instance);

        // Act
        await schemaService.GetDescribeAsync("Account");
        await Task.Delay(100); // Wait for cache to expire
        await schemaService.GetDescribeAsync("Account");

        // Assert
        apiCallCount.Should().Be(2, "cache should have expired");
    }

    #endregion

    #region Change Tracking Pipeline Tests

    [Fact]
    public void ChangeTrackerPipeline_ShouldDetectModifications()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var entity = new TestAccountModel
        {
            Id = "001xxx",
            Name = "Original Name",
            Industry = "Technology"
        };

        tracker.Track(entity, EntityState.Unchanged);

        // Act - Modify the entity
        entity.Name = "Modified Name";
        entity.Industry = "Healthcare";

        var changes = tracker.GetChanges(entity);

        // Assert
        changes.Should().HaveCount(2);
        changes.Should().Contain(c => c.FieldName == "Name");
        changes.Should().Contain(c => c.FieldName == "Industry");

        var nameChange = changes.First(c => c.FieldName == "Name");
        nameChange.OriginalValue.Should().Be("Original Name");
        nameChange.CurrentValue.Should().Be("Modified Name");
    }

    [Fact]
    public void ChangeTrackerPipeline_ShouldGetModifiedFieldsForUpdate()
    {
        // Arrange
        var tracker = new ChangeTracker();
        var entity = new TestAccountModel
        {
            Id = "001xxx",
            Name = "Original Name",
            Industry = "Technology",
            Revenue = 1000000m
        };

        tracker.Track(entity, EntityState.Unchanged);

        // Act - Modify some fields
        entity.Name = "Updated Name";
        entity.Revenue = 2000000m;
        // Industry stays the same

        var modifiedFields = tracker.GetModifiedFields(entity);

        // Assert
        modifiedFields.Should().ContainKey("Name");
        modifiedFields.Should().ContainKey("AnnualRevenue");
        modifiedFields.Should().NotContainKey("Industry");
        modifiedFields["Name"].Should().Be("Updated Name");
        modifiedFields["AnnualRevenue"].Should().Be(2000000m);
    }

    #endregion

    #region Error Handling Pipeline Tests

    [Fact]
    public async Task ErrorPipeline_ShouldPropagateSalesforceErrors()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = CreateCacheProvider();
        var options = CreateOptions();

        SetupDefaultSchema(mockSchema);

        mockClient.Setup(c => c.PostAsync<CreateResult>(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Models.Errors.SalesforceException(
                "REQUIRED_FIELD_MISSING",
                "Required fields are missing: [LastName]",
                400));

        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act & Assert
        var act = async () => await dataService.CreateRecordAsync("Contact", new Dictionary<string, object?>
        {
            ["FirstName"] = "John"
            // Missing required LastName
        });

        await act.Should().ThrowAsync<Models.Errors.SalesforceException>()
            .WithMessage("*REQUIRED_FIELD_MISSING*");
    }

    #endregion

    #region Helper Methods

    private static MemoryCacheProvider CreateCacheProvider()
    {
        return new MemoryCacheProvider(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SalesforceOptions()),
            NullLogger<MemoryCacheProvider>.Instance);
    }

    private static IOptions<SalesforceOptions> CreateOptions()
    {
        return Options.Create(new SalesforceOptions { ApiVersion = "v60.0" });
    }

    private static void SetupDefaultSchema(Mock<ISchemaService> mockSchema)
    {
        mockSchema.Setup(s => s.GetQueryableFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new() { Name = "Id", Type = "id" },
                new() { Name = "Name", Type = "string" }
            });

        mockSchema.Setup(s => s.GetCreateableFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField>
            {
                new() { Name = "FirstName", Type = "string", Createable = true },
                new() { Name = "LastName", Type = "string", Createable = true }
            });

        mockSchema.Setup(s => s.SanitizeFieldListAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());
        mockSchema.Setup(s => s.SanitizeFieldListWithFlsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());
    }

    #endregion

    #region Test Models

    [Attributes.SalesforceObject("Account")]
    private class TestAccountModel
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Industry { get; set; }

        [Attributes.SalesforceField("AnnualRevenue")]
        public decimal? Revenue { get; set; }

        [Attributes.SalesforceField("NumberOfEmployees")]
        public int? EmployeeCount { get; set; }

        [Attributes.SalesforceField("Owner.Name")]
        public string? OwnerName { get; set; }
    }

    #endregion
}
