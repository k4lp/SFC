using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json.Nodes;
using SalesforceCore.AspNetCore.Controllers;
using SalesforceCore.AspNetCore.ViewModels;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Security;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Configuration;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Authorization;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Redesigned Pipeline Integration Tests.
/// Validates the full MVC flow from Controller -> Services -> API Client.
/// </summary>
public class PipelineIntegrationTests
{
    // Real Services
    private readonly DataService _dataService;
    private readonly FieldLevelSecurityService _flsService;
    private readonly ConfigurationService _configService;
    private readonly MemoryCacheProvider _cacheProvider;

    // Mocks
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ISchemaService> _mockSchema;
    private readonly Mock<IBulkService> _mockBulk;
    private readonly Mock<IVisibilityService> _mockVisibility;
    
    // Config
    private readonly IOptions<SalesforceOptions> _options;
    private readonly IOptions<SalesforceMvcOptions> _mvcOptions;

    public PipelineIntegrationTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockSchema = new Mock<ISchemaService>();
        _mockBulk = new Mock<IBulkService>();
        _mockVisibility = new Mock<IVisibilityService>();

        _options = Options.Create(new SalesforceOptions { ApiVersion = "v62.0" });
        _mvcOptions = Options.Create(new SalesforceMvcOptions());

        _cacheProvider = new MemoryCacheProvider(
            new MemoryCache(new MemoryCacheOptions()),
            _options,
            NullLogger<MemoryCacheProvider>.Instance);

        // Setup Schema Defaults to prevent NREs
        SetupSchemaDefaults();

        // Initialize Real Services
        _configService = new ConfigurationService(
            _mockSchema.Object,
            _options,
            NullLogger<ConfigurationService>.Instance);

        _dataService = new DataService(
            _mockClient.Object,
            _mockSchema.Object,
            _mockBulk.Object,
            _cacheProvider,
            _options,
            NullLogger<DataService>.Instance);

        _flsService = new FieldLevelSecurityService(_mockSchema.Object);
    }

    private void SetupSchemaDefaults()
    {
        _mockSchema.SetReturnsDefault(Task.FromResult<SObjectDescribe?>(new SObjectDescribe { Name = "Default" }));
        _mockSchema.SetReturnsDefault(Task.FromResult(new List<SObjectField>()));
        _mockSchema.SetReturnsDefault(Task.FromResult(new Dictionary<string, SObjectField>()));
        _mockSchema.SetReturnsDefault(Task.FromResult(new List<string>()));
        _mockSchema.SetReturnsDefault(Task.FromResult("Name"));

        // Default: Allow all fields through sanitization
        _mockSchema.Setup(s => s.SanitizeFieldListAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, IEnumerable<string> fields, CancellationToken ct) => fields?.ToList() ?? new List<string>());
        _mockSchema.Setup(s => s.SanitizeFieldListWithFlsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, IEnumerable<string> fields, CancellationToken ct) => fields?.ToList() ?? new List<string>());
    }

    private SalesforceController CreateController()
    {
        var controller = new SalesforceController(
            _mockSchema.Object,
            _dataService,
            _configService,
            _mockVisibility.Object,
            _options,
            _mvcOptions,
            NullLogger<SalesforceController>.Instance
        );

        // Setup Context (CRITICAL for MVC logic)
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "testuser") }));
        
        var routeData = new RouteData();
        var actionDescriptor = new ControllerActionDescriptor();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = routeData,
            ActionDescriptor = actionDescriptor
        };

        // Setup TempData
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());

        return controller;
    }

    [Fact]
    public async Task Create_ShouldSanitizeInput_AndCallApi()
    {
        // Arrange
        var controller = CreateController();
        var sObject = "Account";

        // Schema Setup
        var fields = new List<SObjectField>
        {
            new SObjectField { Name = "Name", Createable = true, Type = "string" },
            new SObjectField { Name = "Rating", Createable = false, Type = "picklist" }, // Read-only
            new SObjectField { Name = "AnnualRevenue", Createable = true, Type = "currency" }
        };

        _mockSchema.Setup(s => s.GetDescribeAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SObjectDescribe { Name = sObject, Createable = true, Label = "Account" });
        
        _mockSchema.Setup(s => s.GetCreateableFieldsAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields.Where(f => f.Createable).ToList());

        _mockVisibility.Setup(v => v.EvaluatePolicyAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // API Mock
        _mockClient.Setup(c => c.PostAsync<CreateResult>(
                It.Is<string>(u => u.Contains("/sobjects/Account")),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateResult { Id = "001RECORDID", Success = true });

        // Input
        var form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            { "Name", "Pipeline Corp" },
            { "Rating", "Hot" },        // Should be stripped
            { "AnnualRevenue", "1000" }
        });

        // Act
        var result = await controller.Create(sObject, form);

        // Assert
        var redirect = result.Should().BeOfType<RedirectToActionResult>().Subject;
        redirect.ActionName.Should().Be("Details");

        // Verify Payload was sanitized (Rating removed)
        _mockClient.Verify(c => c.PostAsync<CreateResult>(
            It.IsAny<string>(),
            It.Is<Dictionary<string, object?>>(payload => 
                payload.ContainsKey("Name") && 
                !payload.ContainsKey("Rating") &&
                payload.ContainsKey("AnnualRevenue")
            ),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_ShouldPreventSoqlInjection_AndPaginate()
    {
        // Arrange
        var controller = CreateController();
        var sObject = "Contact";
        var unsafeSearch = "Smith' OR '1'='1";

        // Schema Setup
        _mockSchema.Setup(s => s.GetDescribeAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SObjectDescribe { Name = sObject, Label = sObject, LabelPlural = sObject + "s" });

        _mockSchema.Setup(s => s.GetNameFieldAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync("LastName");

        _mockSchema.Setup(s => s.GetFieldMapAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField> { 
                { "Name", new SObjectField { Name = "Name" } },
                { "CreatedDate", new SObjectField { Name = "CreatedDate" } }
            });

        // API Mock
        var queryResult = new QueryResult { Records = new List<JsonObject>() };
        _mockClient.Setup(c => c.GetAsync<QueryResult>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResult);

        // Act
        var result = await controller.Index(sObject, q: unsafeSearch);

        // Assert
        result.Should().BeOfType<ViewResult>();

        // Verify correct escaping in SOQL
        _mockClient.Verify(c => c.GetAsync<QueryResult>(
            It.Is<string>(url => 
                // The URL is encoded, so we verify the decoded version or parts of it
                // Quotes should be escaped by doubling (SOQL escapes wildcards in value, not via ESCAPE keyword)
                Uri.UnescapeDataString(url).Contains("LIKE '%Smith'' OR ''1''=''1%'")
            ), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Details_ShouldHydrateLookups()
    {
        // Arrange
        var controller = CreateController();
        var sObject = "Opportunity";
        var id = "006000000000001AAA";
        var accountId = "001000000000001AAA";

        // Schema
        _mockSchema.Setup(s => s.GetDescribeAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SObjectDescribe { Name = sObject });

        var fields = new List<SObjectField>
        {
            new SObjectField { Name = "Id", Type = "id" },
            new SObjectField { Name = "Name", Type = "string" },
            new SObjectField { 
                Name = "AccountId", 
                Type = "reference", 
                ReferenceTo = new List<string> { "Account" }, 
                RelationshipName = "Account" 
            }
        };

        _mockSchema.Setup(s => s.GetQueryableFieldsAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields);
        _mockSchema.Setup(s => s.GetAccessibleFieldsAsync(sObject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields);

        // API Mocks
        // 1. Get Record
        _mockClient.Setup(c => c.GetAsync(It.Is<string>(u => u.Contains(id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonObject 
            {
                ["Id"] = id, 
                ["Name"] = "Deal", 
                ["AccountId"] = accountId 
            });

        // 2. Resolve Lookup (Account)
        _mockClient.Setup(c => c.GetAsync<QueryResult>(
                It.Is<string>(u => Uri.UnescapeDataString(u).Contains("SELECT Id, Name FROM Account") && u.Contains(accountId)), 
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult 
            {
                Records = new List<JsonObject> { new JsonObject { ["Id"] = accountId, ["Name"] = "Big Corp" } } 
            });

        // 3. Get Files (Empty)
        _mockClient.Setup(c => c.GetAsync<QueryResult>(
                It.Is<string>(u => u.Contains("ContentDocumentLink")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResult { Records = new List<JsonObject>() });

        // Act
        var result = await controller.Details(sObject, id);

        // Assert
        var viewResult = result.Should().BeOfType<ViewResult>().Subject;
        var model = viewResult.Model.Should().BeOfType<DetailsViewModel>().Subject;

        model.HydratedLookups.Should().ContainKey("AccountId");
        model.HydratedLookups["AccountId"].Should().Be("Big Corp");
    }
}
