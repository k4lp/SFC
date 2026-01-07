using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Caching;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Tests;

public class SchemaServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ICacheProvider> _mockCache;
    private readonly Mock<IOptions<SalesforceOptions>> _mockOptions;
    private readonly Mock<ILogger<SchemaService>> _mockLogger;
    private readonly SchemaService _service;

    public SchemaServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockCache = new Mock<ICacheProvider>();
        _mockLogger = new Mock<ILogger<SchemaService>>();

        var options = new SalesforceOptions();
        _mockOptions = new Mock<IOptions<SalesforceOptions>>();
        _mockOptions.Setup(o => o.Value).Returns(options);

        _service = new SchemaService(
            _mockClient.Object,
            _mockCache.Object,
            _mockOptions.Object,
            _mockLogger.Object);
    }

    #region GetAccessibleFieldsAsync Tests

    [Fact]
    public async Task GetAccessibleFieldsAsync_ShouldReturnOnlyAccessibleFields()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Id", Accessible = true, DeprecatedAndHidden = false, Type = "id" },
                new SObjectField { Name = "Name", Accessible = true, DeprecatedAndHidden = false, Type = "string" },
                new SObjectField { Name = "Secret__c", Accessible = false, DeprecatedAndHidden = false, Type = "string" },
                new SObjectField { Name = "OldField__c", Accessible = true, DeprecatedAndHidden = true, Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetAccessibleFieldsAsync("Account");

        // Assert
        result.Should().HaveCount(2);
        result.Select(f => f.Name).Should().Contain("Id");
        result.Select(f => f.Name).Should().Contain("Name");
        result.Select(f => f.Name).Should().NotContain("Secret__c");
        result.Select(f => f.Name).Should().NotContain("OldField__c");
    }

    [Fact]
    public async Task GetAccessibleFieldsAsync_ShouldExcludeNonQueryableTypes()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Id", Accessible = true, DeprecatedAndHidden = false, Type = "id" },
                new SObjectField { Name = "BillingAddress", Accessible = true, DeprecatedAndHidden = false, Type = "address" },
                new SObjectField { Name = "Location__c", Accessible = true, DeprecatedAndHidden = false, Type = "location" }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetAccessibleFieldsAsync("Account");

        // Assert
        result.Should().HaveCount(1);
        result.Select(f => f.Name).Should().Contain("Id");
        result.Select(f => f.Name).Should().NotContain("BillingAddress");
        result.Select(f => f.Name).Should().NotContain("Location__c");
    }

    #endregion

    #region SanitizeFieldListWithFlsAsync Tests

    [Fact]
    public async Task SanitizeFieldListWithFlsAsync_ShouldRemoveInaccessibleFields()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Id", Accessible = true, DeprecatedAndHidden = false, Type = "id" },
                new SObjectField { Name = "Name", Accessible = true, DeprecatedAndHidden = false, Type = "string" },
                new SObjectField { Name = "Secret__c", Accessible = false, DeprecatedAndHidden = false, Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        var requestedFields = new[] { "Id", "Name", "Secret__c" };

        // Act
        var result = await _service.SanitizeFieldListWithFlsAsync("Account", requestedFields);

        // Assert
        result.Should().Contain("Id");
        result.Should().Contain("Name");
        result.Should().NotContain("Secret__c");
    }

    [Fact]
    public async Task SanitizeFieldListWithFlsAsync_ShouldAlwaysIncludeId()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Id", Accessible = true, DeprecatedAndHidden = false, Type = "id" },
                new SObjectField { Name = "Name", Accessible = true, DeprecatedAndHidden = false, Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        var requestedFields = new[] { "Name" }; // Not requesting Id

        // Act
        var result = await _service.SanitizeFieldListWithFlsAsync("Account", requestedFields);

        // Assert
        result.Should().Contain("Id"); // Id should be added automatically
        result.Should().Contain("Name");
    }

    [Fact]
    public async Task SanitizeFieldListWithFlsAsync_ShouldHandleRelationshipFields()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Contact",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Id", Accessible = true, DeprecatedAndHidden = false, Type = "id" },
                new SObjectField { Name = "AccountId", Accessible = true, DeprecatedAndHidden = false, Type = "reference" }
            }
        };

        SetupDescribeCache("Contact", describe);

        var requestedFields = new[] { "Id", "AccountId", "Account.Name" };

        // Act
        var result = await _service.SanitizeFieldListWithFlsAsync("Contact", requestedFields);

        // Assert
        result.Should().Contain("Id");
        result.Should().Contain("AccountId");
        result.Should().NotContain("Account.Name"); // Base field is AccountId, relationship notation removed
    }

    #endregion

    #region GetPicklistValuesAsync Tests

    [Fact]
    public async Task GetPicklistValuesAsync_ShouldReturnActiveValuesOnly()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Opportunity",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "StageName",
                    Type = "picklist",
                    RestrictedPicklist = false,
                    DependentPicklist = false,
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "Prospecting", Label = "Prospecting", Active = true, DefaultValue = true },
                        new PicklistEntry { Value = "Qualification", Label = "Qualification", Active = true, DefaultValue = false },
                        new PicklistEntry { Value = "OldStage", Label = "Old Stage", Active = false, DefaultValue = false }
                    }
                }
            }
        };

        SetupDescribeCache("Opportunity", describe);

        // Act
        var result = await _service.GetPicklistValuesAsync("Opportunity", "StageName");

        // Assert
        result.Values.Should().HaveCount(2);
        result.Values.Select(v => v.Value).Should().Contain("Prospecting");
        result.Values.Select(v => v.Value).Should().Contain("Qualification");
        result.Values.Select(v => v.Value).Should().NotContain("OldStage");
        result.DefaultValue.Should().Be("Prospecting");
    }

    [Fact]
    public async Task GetPicklistValuesAsync_ShouldIndicateRestrictedPicklist()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Case",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "Status",
                    Type = "picklist",
                    RestrictedPicklist = true,
                    DependentPicklist = false,
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "New", Label = "New", Active = true }
                    }
                }
            }
        };

        SetupDescribeCache("Case", describe);

        // Act
        var result = await _service.GetPicklistValuesAsync("Case", "Status");

        // Assert
        result.IsRestricted.Should().BeTrue();
    }

    [Fact]
    public async Task GetPicklistValuesAsync_ForNonPicklistField_ShouldReturnEmptyResult()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Name", Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetPicklistValuesAsync("Account", "Name");

        // Assert
        result.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPicklistValuesAsync_ForNonExistentField_ShouldReturnEmptyResult()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Name", Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetPicklistValuesAsync("Account", "NonExistent");

        // Assert
        result.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPicklistValuesAsync_DependentPicklist_ShouldBuildDependencyMap()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Lead",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "Country",
                    Type = "picklist",
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "USA", Label = "USA", Active = true },
                        new PicklistEntry { Value = "Canada", Label = "Canada", Active = true }
                    }
                },
                new SObjectField
                {
                    Name = "State",
                    Type = "picklist",
                    DependentPicklist = true,
                    ControllerName = "Country",
                    PicklistValues = new List<PicklistEntry>
                    {
                        // ValidFor is Base64 encoded bitmap - "gA==" = 0x80 = bit 0 set (USA)
                        new PicklistEntry { Value = "CA", Label = "California", Active = true, ValidFor = "gA==" },
                        // "QA==" = 0x40 = bit 1 set (Canada)
                        new PicklistEntry { Value = "ON", Label = "Ontario", Active = true, ValidFor = "QA==" }
                    }
                }
            }
        };

        SetupDescribeCache("Lead", describe);

        // Act
        var result = await _service.GetPicklistValuesAsync("Lead", "State");

        // Assert
        result.IsDependentPicklist.Should().BeTrue();
        result.ControllerName.Should().Be("Country");
        result.DependencyMap.Should().ContainKey("USA");
        result.DependencyMap.Should().ContainKey("Canada");
        result.DependencyMap["USA"].Should().Contain("CA");
        result.DependencyMap["Canada"].Should().Contain("ON");
    }

    #endregion

    #region GetDependentPicklistValuesAsync Tests

    [Fact]
    public async Task GetDependentPicklistValuesAsync_ShouldFilterByControllingValue()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Lead",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "Country",
                    Type = "picklist",
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "USA", Label = "USA", Active = true },
                        new PicklistEntry { Value = "Canada", Label = "Canada", Active = true }
                    }
                },
                new SObjectField
                {
                    Name = "State",
                    Type = "picklist",
                    DependentPicklist = true,
                    ControllerName = "Country",
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "CA", Label = "California", Active = true, ValidFor = "gA==" },
                        new PicklistEntry { Value = "NY", Label = "New York", Active = true, ValidFor = "gA==" },
                        new PicklistEntry { Value = "ON", Label = "Ontario", Active = true, ValidFor = "QA==" }
                    }
                }
            }
        };

        SetupDescribeCache("Lead", describe);

        // Act
        var usaStates = await _service.GetDependentPicklistValuesAsync("Lead", "State", "USA");
        var canadaStates = await _service.GetDependentPicklistValuesAsync("Lead", "State", "Canada");

        // Assert
        usaStates.Select(s => s.Value).Should().Contain("CA");
        usaStates.Select(s => s.Value).Should().Contain("NY");
        usaStates.Select(s => s.Value).Should().NotContain("ON");

        canadaStates.Select(s => s.Value).Should().Contain("ON");
        canadaStates.Select(s => s.Value).Should().NotContain("CA");
        canadaStates.Select(s => s.Value).Should().NotContain("NY");
    }

    [Fact]
    public async Task GetDependentPicklistValuesAsync_NonDependentPicklist_ShouldReturnAllValues()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Opportunity",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "StageName",
                    Type = "picklist",
                    DependentPicklist = false,
                    PicklistValues = new List<PicklistEntry>
                    {
                        new PicklistEntry { Value = "Prospecting", Label = "Prospecting", Active = true },
                        new PicklistEntry { Value = "Closed Won", Label = "Closed Won", Active = true }
                    }
                }
            }
        };

        SetupDescribeCache("Opportunity", describe);

        // Act
        var result = await _service.GetDependentPicklistValuesAsync("Opportunity", "StageName", "AnyValue");

        // Assert
        result.Should().HaveCount(2); // Returns all values since not dependent
    }

    #endregion

    #region GetRelationshipMetadataAsync Tests

    [Fact]
    public async Task GetRelationshipMetadataAsync_ShouldReturnLookupMetadata()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Contact",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "AccountId",
                    Type = "reference",
                    RelationshipName = "Account",
                    ReferenceTo = new List<string> { "Account" },
                    PolymorphicForeignKey = false,
                    WriteRequiresMasterRead = false,
                    InlineHelpText = "The account this contact belongs to"
                }
            }
        };

        SetupDescribeCache("Contact", describe);

        // Act
        var result = await _service.GetRelationshipMetadataAsync("Contact", "AccountId");

        // Assert
        result.Should().NotBeNull();
        result!.FieldName.Should().Be("AccountId");
        result.RelationshipName.Should().Be("Account");
        result.ReferenceTo.Should().Be("Account");
        result.IsPolymorphic.Should().BeFalse();
        result.IsLookup.Should().BeTrue();
        result.InlineHelpText.Should().Be("The account this contact belongs to");
    }

    [Fact]
    public async Task GetRelationshipMetadataAsync_PolymorphicLookup_ShouldIndicatePolymorphic()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Task",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "WhatId",
                    Type = "reference",
                    RelationshipName = "What",
                    ReferenceTo = new List<string> { "Account", "Opportunity", "Case", "Campaign" },
                    PolymorphicForeignKey = true
                }
            }
        };

        SetupDescribeCache("Task", describe);

        // Act
        var result = await _service.GetRelationshipMetadataAsync("Task", "WhatId");

        // Assert
        result.Should().NotBeNull();
        result!.IsPolymorphic.Should().BeTrue();
        result.ReferenceToTypes.Should().HaveCount(4);
        result.ReferenceToTypes.Should().Contain("Account");
        result.ReferenceToTypes.Should().Contain("Opportunity");
    }

    [Fact]
    public async Task GetRelationshipMetadataAsync_MasterDetail_ShouldIndicateNotLookup()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "OpportunityLineItem",
            Fields = new List<SObjectField>
            {
                new SObjectField
                {
                    Name = "OpportunityId",
                    Type = "reference",
                    RelationshipName = "Opportunity",
                    ReferenceTo = new List<string> { "Opportunity" },
                    WriteRequiresMasterRead = true // Master-detail relationship
                }
            }
        };

        SetupDescribeCache("OpportunityLineItem", describe);

        // Act
        var result = await _service.GetRelationshipMetadataAsync("OpportunityLineItem", "OpportunityId");

        // Assert
        result.Should().NotBeNull();
        result!.IsLookup.Should().BeFalse(); // Master-detail, not lookup
    }

    [Fact]
    public async Task GetRelationshipMetadataAsync_NonLookupField_ShouldReturnNull()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            Fields = new List<SObjectField>
            {
                new SObjectField { Name = "Name", Type = "string" }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetRelationshipMetadataAsync("Account", "Name");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetChildRelationshipsAsync Tests

    [Fact]
    public async Task GetChildRelationshipsAsync_ShouldReturnNonDeprecatedRelationships()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            ChildRelationships = new List<ChildRelationship>
            {
                new ChildRelationship
                {
                    RelationshipName = "Contacts",
                    ChildSObject = "Contact",
                    Field = "AccountId",
                    DeprecatedAndHidden = false
                },
                new ChildRelationship
                {
                    RelationshipName = "Opportunities",
                    ChildSObject = "Opportunity",
                    Field = "AccountId",
                    DeprecatedAndHidden = false
                },
                new ChildRelationship
                {
                    RelationshipName = "OldRelationship",
                    ChildSObject = "OldObject",
                    Field = "AccountId",
                    DeprecatedAndHidden = true
                },
                new ChildRelationship
                {
                    RelationshipName = null, // No relationship name
                    ChildSObject = "SomeObject",
                    Field = "AccountId",
                    DeprecatedAndHidden = false
                }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetChildRelationshipsAsync("Account");

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.RelationshipName).Should().Contain("Contacts");
        result.Select(r => r.RelationshipName).Should().Contain("Opportunities");
        result.Select(r => r.RelationshipName).Should().NotContain("OldRelationship");
    }

    #endregion

    #region GetRecordTypesAsync Tests

    [Fact]
    public async Task GetRecordTypesAsync_ShouldReturnAvailableRecordTypes()
    {
        // Arrange
        var describe = new SObjectDescribe
        {
            Name = "Account",
            RecordTypeInfos = new List<RecordTypeInfo>
            {
                new RecordTypeInfo
                {
                    RecordTypeId = "012xxx1",
                    Name = "Business Account",
                    DeveloperName = "Business_Account",
                    Available = true,
                    DefaultRecordTypeMapping = true
                },
                new RecordTypeInfo
                {
                    RecordTypeId = "012xxx2",
                    Name = "Person Account",
                    DeveloperName = "Person_Account",
                    Available = true,
                    DefaultRecordTypeMapping = false
                },
                new RecordTypeInfo
                {
                    RecordTypeId = "012xxx3",
                    Name = "Unavailable Type",
                    DeveloperName = "Unavailable",
                    Available = false,
                    DefaultRecordTypeMapping = false
                }
            }
        };

        SetupDescribeCache("Account", describe);

        // Act
        var result = await _service.GetRecordTypesAsync("Account");

        // Assert
        result.Should().HaveCount(2);
        result.Select(r => r.Name).Should().Contain("Business Account");
        result.Select(r => r.Name).Should().Contain("Person Account");
        result.Select(r => r.Name).Should().NotContain("Unavailable Type");
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Fact]
    public async Task InvalidateCacheAsync_WithObjectName_ShouldRemoveObjectCache()
    {
        // Act
        await _service.InvalidateCacheAsync("Account");

        // Assert
        _mockCache.Verify(c => c.RemoveAsync("Schema_Account"), Times.Once);
    }

    [Fact]
    public async Task InvalidateCacheAsync_WithoutObjectName_ShouldRemoveGlobalDescribe()
    {
        // Act
        await _service.InvalidateCacheAsync();

        // Assert
        _mockCache.Verify(c => c.RemoveAsync("GlobalDescribe"), Times.Once);
    }

    #endregion

    #region Helper Methods

    private void SetupDescribeCache(string objectName, SObjectDescribe describe)
    {
        _mockCache.Setup(c => c.GetOrCreateAsync<SObjectDescribe>(
                $"Schema_{objectName}",
                It.IsAny<Func<CancellationToken, Task<SObjectDescribe?>>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);
    }

    #endregion
}
