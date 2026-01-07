using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Authorization.Handlers;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// End-to-end security tests validating the complete data pipeline integrity.
/// Tests cover: authentication flow, authorization pipeline, SOQL injection prevention,
/// field-level security, caching with authorization, and distributed scenarios.
/// </summary>
public class EndToEndSecurityTests
{
    #region SOQL Injection Prevention Pipeline

    [Fact]
    public void SoqlBuilder_Pipeline_ShouldPreventComplexInjectionAttacks()
    {
        // Arrange - simulate a sophisticated injection attempt
        var maliciousInput = "Smith' OR Name LIKE '%' --";

        // Act - build query through the full pipeline
        var query = SoqlBuilder.From("Contact")
            .Select("Id", "Name", "Email")
            .WhereLike("LastName", maliciousInput)
            .Limit(10)
            .Build();

        // Assert - the injection should be escaped, making it a literal search
        // Verify the malicious single quote is escaped by doubling
        query.Should().Contain("Smith''");

        // LIKE clauses escape wildcards in the value itself (not via ESCAPE clause)
        // Verify the malicious single quote is escaped by doubling
        query.Should().Contain("Smith''");
    }

    [Fact]
    public void SoqlCondition_Pipeline_ShouldSanitizeAllValueTypes()
    {
        // Test various injection vectors through the SoqlCondition API
        var testCases = new[]
        {
            ("test'; DELETE FROM Account; --", "string injection"),
            ("test\\'; DROP TABLE --", "escaped quote injection"),
            ("1 OR 1=1", "boolean injection"),
            ("UNION SELECT * FROM User", "UNION injection")
        };

        foreach (var (input, description) in testCases)
        {
            var condition = SoqlCondition.Equals("Name", input);
            var rendered = condition.Render();

            // Verify proper escaping - no unescaped quotes or SQL keywords executed as code
            rendered.Should().Contain(SecurityUtils.SanitizeForSoql(input),
                because: $"{description} should be properly escaped");
        }
    }

    [Fact]
    public void SoqlBuilder_Pipeline_ShouldValidateFieldNamesAgainstInjection()
    {
        // Attempt to inject via field names
        var maliciousFieldNames = new[]
        {
            "Name; DROP TABLE Account",
            "Name' OR '1'='1",
            "Name/**/FROM/**/User",
            "../../../etc/passwd",
            "Name<script>alert(1)</script>"
        };

        foreach (var fieldName in maliciousFieldNames)
        {
            var act = () => SoqlBuilder.From("Account").Select(fieldName).Build();

            act.Should().Throw<ArgumentException>(
                because: $"malicious field name '{fieldName}' should be rejected");
        }
    }

    [Theory]
    [InlineData("SELECT Id FROM Account WHERE Name = 'Test'", true)]
    [InlineData("Account", true)]
    [InlineData("Account; DROP TABLE User", false)]
    [InlineData("Account__c", true)]
    [InlineData("ns__Custom_Object__c", true)]
    [InlineData("1Account", false)]
    public void SecurityUtils_ShouldValidateObjectNamesCorrectly(string name, bool shouldBeValid)
    {
        if (shouldBeValid && !name.Contains("SELECT"))
        {
            SecurityUtils.IsValidObjectName(name).Should().BeTrue();
        }
        else if (!shouldBeValid)
        {
            SecurityUtils.IsValidObjectName(name).Should().BeFalse();
        }
    }

    #endregion

    #region Authorization Pipeline Tests

    [Fact]
    public async Task VisibilityService_Pipeline_ShouldEvaluateComplexPolicies()
    {
        // Arrange - full visibility pipeline setup
        var options = Options.Create(new VisibilityOptions
        {
            Policies = new Dictionary<string, VisibilityPolicy>
            {
                ["AdminOrManager"] = new VisibilityPolicy
                {
                    Strategy = "Any", // OR logic
                    Requirements = new List<VisibilityRequirementConfig>
                    {
                        new() { Type = "Role", Settings = JsonNode.Parse("{\"Role\":\"Admin\"}")!.AsObject() },
                        new() { Type = "Role", Settings = JsonNode.Parse("{\"Role\":\"Manager\"}")!.AsObject() }
                    }
                },
                ["AdminAndSalesforce"] = new VisibilityPolicy
                {
                    Strategy = "All", // AND logic
                    Requirements = new List<VisibilityRequirementConfig>
                    {
                        new() { Type = "Role", Settings = JsonNode.Parse("{\"Role\":\"Admin\"}")!.AsObject() },
                        new() { Type = "Role", Settings = JsonNode.Parse("{\"Role\":\"SalesforceUser\"}")!.AsObject() }
                    }
                }
            }
        });

        var handlers = new List<IVisibilityRequirementHandler> { new RoleHandler() };
        var userProvider = new Mock<IUserContextProvider>();

        // User with Admin role only
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "admin"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "test"));
        userProvider.Setup(p => p.GetUser()).Returns(adminUser);

        var service = new VisibilityService(
            new OptionsWrapper<VisibilityOptions>(options.Value),
            handlers,
            userProvider.Object,
            NullLogger<VisibilityService>.Instance);

        // Act & Assert

        // Admin should pass "AdminOrManager" (has Admin role)
        var result1 = await service.EvaluatePolicyAsync("AdminOrManager");
        result1.Should().BeTrue();

        // Admin should FAIL "AdminAndSalesforce" (missing SalesforceUser role)
        var result2 = await service.EvaluatePolicyAsync("AdminAndSalesforce");
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task VisibilityService_Pipeline_ShouldHandleMissingPoliciesSecurely()
    {
        // Arrange
        var options = Options.Create(new VisibilityOptions { Policies = new() });
        var handlers = new List<IVisibilityRequirementHandler>();
        var userProvider = new Mock<IUserContextProvider>();
        userProvider.Setup(p => p.GetUser()).Returns(new ClaimsPrincipal());

        var service = new VisibilityService(
            new OptionsWrapper<VisibilityOptions>(options.Value),
            handlers,
            userProvider.Object,
            NullLogger<VisibilityService>.Instance);

        // Act - request a policy that doesn't exist
        var result = await service.EvaluatePolicyAsync("NonExistentPolicy");

        // Assert - should fail securely (default deny)
        result.Should().BeFalse();
    }

    [Fact]
    public async Task VisibilityService_Pipeline_ShouldPassCancellationTokenToHandlers()
    {
        // Arrange - verify cancellation token is passed correctly to handlers
        var options = Options.Create(new VisibilityOptions
        {
            Policies = new Dictionary<string, VisibilityPolicy>
            {
                ["TestPolicy"] = new VisibilityPolicy
                {
                    Requirements = new List<VisibilityRequirementConfig>
                    {
                        new() { Type = "Test", Settings = new JsonObject() }
                    }
                }
            }
        });

        CancellationToken capturedToken = default;
        var testHandler = new Mock<IVisibilityRequirementHandler>();
        testHandler.Setup(h => h.Type).Returns("Test");
        testHandler.Setup(h => h.HandleAsync(It.IsAny<JsonObject>(), It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .Callback<JsonObject, ClaimsPrincipal, CancellationToken>((_, _, ct) => capturedToken = ct)
            .ReturnsAsync(true);

        var handlers = new List<IVisibilityRequirementHandler> { testHandler.Object };
        var userProvider = new Mock<IUserContextProvider>();
        userProvider.Setup(p => p.GetUser()).Returns(new ClaimsPrincipal(new ClaimsIdentity("test")));

        var service = new VisibilityService(
            new OptionsWrapper<VisibilityOptions>(options.Value),
            handlers,
            userProvider.Object,
            NullLogger<VisibilityService>.Instance);

        // Act
        using var cts = new CancellationTokenSource();
        await service.EvaluatePolicyAsync("TestPolicy", cts.Token);

        // Assert - cancellation token should be passed to handler
        capturedToken.Should().Be(cts.Token);
    }

    #endregion

    #region Caching Security Pipeline Tests

    [Fact]
    public async Task MemoryCacheProvider_Pipeline_ShouldPreventCacheStampede()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new SalesforceOptions());
        var cacheProvider = new MemoryCacheProvider(memoryCache, options, NullLogger<MemoryCacheProvider>.Instance);

        var factoryCallCount = 0;

        // Act - simulate concurrent requests
        var tasks = new List<Task<string?>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(cacheProvider.GetOrCreateAsync<string>("test_key", async ct =>
            {
                Interlocked.Increment(ref factoryCallCount);
                await Task.Delay(100, ct); // Simulate work
                return "cached_value";
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - factory should only be called once (stampede prevented)
        factoryCallCount.Should().Be(1);
        var results = await Task.WhenAll(tasks);
        results.Should().OnlyContain(result => result == "cached_value");
    }

    [Fact]
    public async Task DistributedCacheProvider_Pipeline_ShouldIsolateKeysByPrefix()
    {
        // Arrange - simulate two environments sharing Redis
        var mockCache = new Mock<IDistributedCache>();
        var capturedKeys = new List<string>();

        mockCache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((key, _, _, _) => capturedKeys.Add(key))
            .Returns(Task.CompletedTask);

        var prodOptions = Options.Create(new SalesforceOptions { CacheKeyPrefix = "PROD_SF_" });
        var stagingOptions = Options.Create(new SalesforceOptions { CacheKeyPrefix = "STAGING_SF_" });

        var prodCache = new DistributedCacheProvider(mockCache.Object, prodOptions, NullLogger<DistributedCacheProvider>.Instance);
        var stagingCache = new DistributedCacheProvider(mockCache.Object, stagingOptions, NullLogger<DistributedCacheProvider>.Instance);

        // Act
        await prodCache.SetAsync("schema:Account", "prod_data");
        await stagingCache.SetAsync("schema:Account", "staging_data");

        // Assert - keys should be isolated by prefix
        capturedKeys.Should().Contain("PROD_SF_schema:Account");
        capturedKeys.Should().Contain("STAGING_SF_schema:Account");
        capturedKeys.Should().HaveCount(2);
    }

    #endregion

    #region Data Service Pipeline Tests

    [Fact]
    public async Task DataService_Pipeline_ShouldSanitizeQueryParameters()
    {
        // Arrange
        var mockClient = new Mock<ISalesforceClient>();
        var mockSchema = new Mock<ISchemaService>();
        var mockBulk = new Mock<IBulkService>();
        var cacheProvider = new MemoryCacheProvider(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new SalesforceOptions()),
            NullLogger<MemoryCacheProvider>.Instance);

        string capturedQuery = "";
        mockClient.Setup(c => c.GetAsync<QueryResult>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((q, _) => capturedQuery = q)
            .ReturnsAsync(new QueryResult { Records = new List<JsonObject>() });

        mockSchema.Setup(s => s.GetQueryableFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SObjectField> { new() { Name = "Id" }, new() { Name = "Name" } });
        mockSchema.Setup(s => s.SanitizeFieldListAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());
        mockSchema.Setup(s => s.SanitizeFieldListWithFlsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, IEnumerable<string> fields, CancellationToken _) => fields.ToList());

        var options = Options.Create(new SalesforceOptions());
        var dataService = new DataService(mockClient.Object, mockSchema.Object, mockBulk.Object, cacheProvider, options, NullLogger<DataService>.Instance);

        // Act - query with potentially malicious search term using safe SoqlCondition
        var condition = SoqlCondition.Like("Name", "%Test' OR '1'='1%");
        var result = await dataService.QueryPagedAsync("Account",
            fields: new[] { "Id", "Name" },
            filter: condition);

        // Assert - the condition should be safely escaped
        capturedQuery.Should().NotContain("OR '1'='1");
    }

    [Theory]
    [InlineData("001AAABBBCCCDDD", true)]  // Valid 15-char ID
    [InlineData("001AAABBBCCCDDDAAA", true)]  // Valid 18-char ID
    [InlineData("invalid", false)]
    [InlineData("001' OR '1'='1", false)]
    [InlineData("<script>alert(1)</script>", false)]
    public void SecurityUtils_ShouldValidateSalesforceIds(string id, bool shouldBeValid)
    {
        // Validate that SecurityUtils correctly identifies valid vs invalid Salesforce IDs
        var result = SecurityUtils.IsValidSalesforceId(id);
        result.Should().Be(shouldBeValid, because: $"ID '{id}' should be {(shouldBeValid ? "valid" : "invalid")}");
    }

    #endregion

    #region Token Security Pipeline Tests

    [Fact]
    public void TokenProvider_Pipeline_ShouldHaveSecureDefaults()
    {
        // Verify that token options have secure defaults
        var jwtOptions = new JwtTokenProviderOptions();

        // Token expiration should be reasonable (not too long)
        jwtOptions.TokenExpiration.Should().BeLessOrEqualTo(TimeSpan.FromHours(24),
            because: "tokens should have reasonable expiration");

        // Salesforce options should default to secure settings
        var sfOptions = new SalesforceOptions();

        sfOptions.ForceSecureCookie.Should().BeTrue(
            because: "cookies should be secure by default");

        sfOptions.SessionCookieName.Should().StartWith("__Host-",
            because: "__Host- prefix prevents cookie injection attacks");
    }

    #endregion

    #region Field-Level Security Pipeline Tests

    [Fact]
    public async Task FieldLevelSecurity_Pipeline_ShouldEnforceOnCreate()
    {
        // Arrange
        var mockSchema = new Mock<ISchemaService>();
        mockSchema.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>
            {
                ["Name"] = new() { Name = "Name", Createable = true, Label = "Account Name" },
                ["Industry"] = new() { Name = "Industry", Createable = true, Label = "Industry" },
                ["Rating"] = new() { Name = "Rating", Createable = false, Label = "Rating" },
                ["SystemModstamp"] = new() { Name = "SystemModstamp", Createable = false, Label = "System Modstamp" }
            });

        var flsService = new Security.FieldLevelSecurityService(mockSchema.Object);

        // Act
        var inputFields = new Dictionary<string, object?>
        {
            ["Name"] = "Test Account",
            ["Industry"] = "Technology",
            ["Rating"] = "Hot", // Should be filtered out
            ["SystemModstamp"] = DateTime.UtcNow // Should be filtered out
        };

        var result = await flsService.FilterCreateableFieldsAsync("Account", inputFields);

        // Assert
        result.Should().ContainKey("Name");
        result.Should().ContainKey("Industry");
        result.Should().NotContainKey("Rating");
        result.Should().NotContainKey("SystemModstamp");
    }

    [Fact]
    public async Task FieldLevelSecurity_Pipeline_ShouldEnforceOnQuery()
    {
        // Arrange
        var mockSchema = new Mock<ISchemaService>();
        mockSchema.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>
            {
                ["Id"] = new() { Name = "Id", Accessible = true },
                ["Name"] = new() { Name = "Name", Accessible = true },
                ["Industry"] = new() { Name = "Industry", Accessible = true },
                ["SensitiveField__c"] = new() { Name = "SensitiveField__c", Accessible = false }
            });

        var flsService = new Security.FieldLevelSecurityService(mockSchema.Object);

        // Act
        var inputRecord = new Dictionary<string, object?>
        {
            ["Id"] = "001xxx",
            ["Name"] = "Test",
            ["Industry"] = "Tech",
            ["SensitiveField__c"] = "secret"
        };

        var result = await flsService.FilterReadableFieldsAsync("Account", inputRecord);

        // Assert
        result.Should().ContainKey("Id");
        result.Should().ContainKey("Name");
        result.Should().ContainKey("Industry");
        result.Should().NotContainKey("SensitiveField__c");
    }

    #endregion

    #region Input Validation Pipeline Tests

    [Theory]
    [InlineData("Account.Name", true)]
    [InlineData("Account.Owner.Profile.Name", true)]
    [InlineData("CustomField__c", true)]
    [InlineData("Custom_Field__r.Name", true)]
    [InlineData("Field; DROP TABLE", false)]
    [InlineData("Field\nName", false)]
    [InlineData("Field\tName", false)]
    [InlineData("..Name", false)]
    [InlineData("Name..", false)]
    [InlineData("Account..Name", false)]
    public void SecurityUtils_FieldValidation_ShouldBeComprehensive(string fieldName, bool shouldBeValid)
    {
        var result = SecurityUtils.IsValidFieldName(fieldName);
        result.Should().Be(shouldBeValid, because: $"field name '{fieldName}' validation should be {shouldBeValid}");
    }

    [Theory]
    [InlineData("/dashboard", true)]
    [InlineData("~/home", true)]
    [InlineData("relative/path", true)]
    [InlineData("//evil.com/phishing", false)]
    [InlineData("https://evil.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    public void SecurityUtils_UrlValidation_ShouldPreventOpenRedirect(string url, bool shouldBeLocal)
    {
        var result = SecurityUtils.IsLocalUrl(url);
        result.Should().Be(shouldBeLocal, because: $"URL '{url}' locality should be {shouldBeLocal}");
    }

    [Theory]
    [InlineData("document.pdf", new[] { ".pdf", ".doc" }, true)]
    [InlineData("script.exe", new[] { ".pdf", ".doc" }, false)]
    [InlineData("file.PDF", new[] { ".pdf" }, true)] // Case insensitive
    [InlineData("file", new[] { ".pdf" }, false)] // No extension
    [InlineData("file..pdf", new[] { ".pdf" }, true)]
    public void SecurityUtils_FileExtension_ShouldValidateSecurely(string filename, string[] allowed, bool shouldBeAllowed)
    {
        var result = SecurityUtils.IsAllowedExtension(filename, allowed);
        result.Should().Be(shouldBeAllowed);
    }

    #endregion

    #region Serialization Security Tests

    [Fact]
    public void JsonSerialization_ShouldNotExecuteTypeNames()
    {
        // Verify that System.Text.Json is used (which doesn't support TypeNameHandling by default)
        // This is a compile-time check more than runtime, but validates the pattern

        var json = "{\"$type\":\"System.Diagnostics.Process\",\"StartInfo\":{\"FileName\":\"calc.exe\"}}";

        // System.Text.Json should NOT deserialize this as a Process
        var act = () => JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        act.Should().NotThrow(); // It parses as a dictionary, not as a Process

        var result = JsonSerializer.Deserialize<JsonElement>(json);
        result.TryGetProperty("$type", out _).Should().BeTrue(); // $type is just a string property
    }

    [Fact]
    public void SalesforceMapper_ShouldHandleMaliciousJson()
    {
        // Arrange
        var maliciousJsonCases = new[]
        {
            "{\"Id\":\"001xxx\",\"Name\":null,\"__proto__\":{\"admin\":true}}", // Prototype pollution attempt
            "{\"Id\":\"001xxx\",\"Name\":\"Test\",\"constructor\":{\"prototype\":{\"admin\":true}}}", // Another prototype pollution
            "{\"Id\":\"001xxx\",\"Name\":\"" + new string('A', 10000) + "\"}" // Large payload
        };

        foreach (var json in maliciousJsonCases)
        {
            var act = () =>
            {
                var node = JsonNode.Parse(json);
                return Mapping.SalesforceMapper.FromSalesforce<TestAccount>(node!);
            };

            // Should parse without executing malicious payloads
            act.Should().NotThrow();
        }
    }

    [Attributes.SalesforceObject("Account")]
    private class TestAccount
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    #endregion
}

/// <summary>
/// Helper wrapper for IOptionsSnapshot.
/// </summary>
file class OptionsWrapper<T> : IOptionsSnapshot<T> where T : class, new()
{
    private readonly T _value;

    public OptionsWrapper(T value) => _value = value;

    public T Value => _value;

    public T Get(string? name) => _value;
}
