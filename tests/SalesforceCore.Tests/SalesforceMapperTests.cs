using Xunit;
using FluentAssertions;
using System.Text.Json.Nodes;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;

namespace SalesforceCore.Tests;

/// <summary>
/// Unit tests for SalesforceMapper - attribute-based mapping between C# objects and Salesforce.
/// </summary>
public class SalesforceMapperTests
{
    #region Test Models

    [SalesforceObject("Account")]
    public class AccountModel
    {
        public string? Id { get; set; }

        [SalesforceField("Name", Required = true, MaxLength = 255)]
        public string? Name { get; set; }

        [SalesforceField("Account_Number__c")]
        public string? AccountNumber { get; set; }

        [SalesforceField("AnnualRevenue", Precision = 18, Scale = 2)]
        public decimal? Revenue { get; set; }

        [SalesforceField("IsActive__c")]
        public bool IsActive { get; set; }

        [SalesforceField("CreatedDate", ReadOnly = true)]
        public DateTimeOffset? CreatedDate { get; set; }

        [SalesforceIgnore]
        public string? ComputedValue { get; set; }

        [SalesforceExternalId]
        [SalesforceField("External_ID__c")]
        public string? ExternalId { get; set; }
    }

    public class SimpleModel
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [SalesforceObject("Custom_Object__c")]
    public class CustomObjectModel
    {
        [SalesforceId]
        public string? RecordId { get; set; }

        public string? Description { get; set; }
    }

    public enum LeadStatus
    {
        [SalesforceValue("Open - Not Contacted")]
        OpenNotContacted,

        [SalesforceValue("Closed Won")]
        ClosedWon,

        Plain
    }

    [SalesforceObject("Lead")]
    public class LeadModel
    {
        [SalesforceField("Status")]
        public LeadStatus Status { get; set; }
    }

    #endregion

    [Fact]
    public void GetObjectName_WithAttribute_ShouldReturnAttributeName()
    {
        // Act
        var result = SalesforceMapper.GetObjectName<AccountModel>();

        // Assert
        result.Should().Be("Account");
    }

    [Fact]
    public void GetObjectName_WithoutAttribute_ShouldReturnTypeName()
    {
        // Act
        var result = SalesforceMapper.GetObjectName<SimpleModel>();

        // Assert
        result.Should().Be("SimpleModel");
    }

    [Fact]
    public void GetObjectName_CustomObject_ShouldReturnAttributeName()
    {
        // Act
        var result = SalesforceMapper.GetObjectName<CustomObjectModel>();

        // Assert
        result.Should().Be("Custom_Object__c");
    }

    [Fact]
    public void ToSalesforceDictionary_ShouldMapAllFields()
    {
        // Arrange
        var model = new AccountModel
        {
            Id = "001xxx",
            Name = "Test Account",
            AccountNumber = "ACC001",
            Revenue = 1000000.50m,
            IsActive = true,
            CreatedDate = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero),
            ComputedValue = "Should be ignored",
            ExternalId = "EXT001"
        };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model);

        // Assert
        result.Should().ContainKey("Id");
        result.Should().ContainKey("Name");
        result.Should().ContainKey("Account_Number__c");
        result.Should().ContainKey("AnnualRevenue");
        result.Should().ContainKey("IsActive__c");
        result.Should().ContainKey("External_ID__c");

        // Should NOT contain ignored field
        result.Should().NotContainKey("ComputedValue");

        // Check values
        result["Name"].Should().Be("Test Account");
        result["Account_Number__c"].Should().Be("ACC001");
        result["AnnualRevenue"].Should().Be(1000000.50m);
        result["IsActive__c"].Should().Be(true);
    }

    [Fact]
    public void ToSalesforceDictionary_ForCreate_ShouldExcludeIdAndReadOnlyFields()
    {
        // Arrange
        var model = new AccountModel
        {
            Id = "001xxx",
            Name = "Test Account",
            CreatedDate = DateTimeOffset.Now,
            ExternalId = "EXT001"
        };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model, forCreate: true);

        // Assert
        result.Should().NotContainKey("Id");
        result.Should().NotContainKey("CreatedDate"); // ReadOnly
        result.Should().ContainKey("Name");
        result.Should().ContainKey("External_ID__c");
    }

    [Fact]
    public void ToSalesforceDictionary_ForUpdate_ShouldExcludeReadOnlyFields()
    {
        // Arrange
        var model = new AccountModel
        {
            Id = "001xxx",
            Name = "Updated Name",
            CreatedDate = DateTimeOffset.Now
        };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model, forUpdate: true);

        // Assert
        result.Should().ContainKey("Id"); // Include Id for reference
        result.Should().ContainKey("Name");
        result.Should().NotContainKey("CreatedDate"); // ReadOnly
    }

    [Fact]
    public void FromSalesforce_ShouldMapFromJsonObject()
    {
        // Arrange
        var json = new JsonObject
        {
            ["Id"] = "001xxx",
            ["Name"] = "Test Account",
            ["Account_Number__c"] = "ACC001",
            ["AnnualRevenue"] = 1500000.75,
            ["IsActive__c"] = true,
            ["External_ID__c"] = "EXT001"
        };

        // Act
        var result = SalesforceMapper.FromSalesforce<AccountModel>(json);

        // Assert
        result.Id.Should().Be("001xxx");
        result.Name.Should().Be("Test Account");
        result.AccountNumber.Should().Be("ACC001");
        result.Revenue.Should().Be(1500000.75m);
        result.IsActive.Should().BeTrue();
        result.ExternalId.Should().Be("EXT001");
    }

    [Fact]
    public void FromSalesforce_List_ShouldMapMultipleRecords()
    {
        // Arrange
        var records = new List<JsonObject>
        {
            new JsonObject { ["Id"] = "001xxx", ["Name"] = "Account 1" },
            new JsonObject { ["Id"] = "002xxx", ["Name"] = "Account 2" },
            new JsonObject { ["Id"] = "003xxx", ["Name"] = "Account 3" }
        };

        // Act
        var result = SalesforceMapper.FromSalesforce<AccountModel>(records);

        // Assert
        result.Should().HaveCount(3);
        result[0].Name.Should().Be("Account 1");
        result[1].Name.Should().Be("Account 2");
        result[2].Name.Should().Be("Account 3");
    }

    [Fact]
    public void GetFieldName_WithAttribute_ShouldReturnMappedName()
    {
        // Act
        var result = SalesforceMapper.GetFieldName<AccountModel>("AccountNumber");

        // Assert
        result.Should().Be("Account_Number__c");
    }

    [Fact]
    public void GetFieldName_WithoutAttribute_ShouldReturnPropertyName()
    {
        // Act
        var result = SalesforceMapper.GetFieldName<SimpleModel>("Name");

        // Assert
        result.Should().Be("Name");
    }

    [Fact]
    public void GetQueryableFields_ShouldReturnAllNonIgnoredFields()
    {
        // Act
        var result = SalesforceMapper.GetQueryableFields<AccountModel>().ToList();

        // Assert
        result.Should().Contain("Id");
        result.Should().Contain("Name");
        result.Should().Contain("Account_Number__c");
        result.Should().Contain("AnnualRevenue");
        result.Should().Contain("IsActive__c");
        result.Should().Contain("CreatedDate");
        result.Should().Contain("External_ID__c");

        // Should NOT contain ignored field
        result.Should().NotContain("ComputedValue");
    }

    [Fact]
    public void GetIdFieldName_ShouldReturnIdField()
    {
        // Act
        var result = SalesforceMapper.GetIdFieldName<AccountModel>();

        // Assert
        result.Should().Be("Id");
    }

    [Fact]
    public void GetIdFieldName_WithCustomIdAttribute_ShouldReturnCustomField()
    {
        // Act
        var result = SalesforceMapper.GetIdFieldName<CustomObjectModel>();

        // Assert
        result.Should().Be("RecordId");
    }

    [Fact]
    public void GetExternalIdFieldName_ShouldReturnExternalIdField()
    {
        // Act
        var result = SalesforceMapper.GetExternalIdFieldName<AccountModel>();

        // Assert
        result.Should().Be("External_ID__c");
    }

    [Fact]
    public void GetExternalIdFieldName_WhenNone_ShouldReturnNull()
    {
        // Act
        var result = SalesforceMapper.GetExternalIdFieldName<SimpleModel>();

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ToSalesforceDictionary_DateTime_ShouldFormatCorrectly()
    {
        // Arrange
        var model = new AccountModel
        {
            CreatedDate = new DateTimeOffset(2024, 6, 15, 14, 30, 45, 123, TimeSpan.Zero)
        };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model, includeReadOnly: true);

        // Assert
        result["CreatedDate"].Should().Be("2024-06-15T14:30:45.123Z");
    }

    [Fact]
    public void ToSalesforceDictionary_DateTimeOffset_WithOffset_ShouldNormalizeToUtc()
    {
        // Arrange
        var model = new AccountModel
        {
            CreatedDate = new DateTimeOffset(2024, 6, 15, 14, 30, 45, 123, TimeSpan.FromHours(-5))
        };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model, includeReadOnly: true);

        // Assert
        result["CreatedDate"].Should().Be("2024-06-15T19:30:45.123Z");
    }

    [Fact]
    public void FromSalesforce_NullableFields_ShouldHandleNulls()
    {
        // Arrange
        var json = new JsonObject
        {
            ["Id"] = "001xxx",
            ["Name"] = null,
            ["AnnualRevenue"] = null
        };

        // Act
        var result = SalesforceMapper.FromSalesforce<AccountModel>(json);

        // Assert
        result.Id.Should().Be("001xxx");
        result.Name.Should().BeNull();
        result.Revenue.Should().BeNull();
    }

    [Fact]
    public void ClearCache_ShouldResetMetadata()
    {
        // Arrange - trigger metadata caching
        SalesforceMapper.GetObjectName<AccountModel>();

        // Act
        SalesforceMapper.ClearCache();

        // Assert - should not throw, cache should be empty and rebuild
        var result = SalesforceMapper.GetObjectName<AccountModel>();
        result.Should().Be("Account");
    }

    [Fact]
    public void ToSalesforceDictionary_Enum_ShouldUseSalesforceValueAttribute()
    {
        // Arrange
        var model = new LeadModel { Status = LeadStatus.ClosedWon };

        // Act
        var result = SalesforceMapper.ToSalesforceDictionary(model);

        // Assert
        result["Status"].Should().Be("Closed Won");
    }

    [Fact]
    public void FromSalesforce_Enum_ShouldUseSalesforceValueAttribute()
    {
        // Arrange
        var json = new JsonObject
        {
            ["Status"] = "Open - Not Contacted"
        };

        // Act
        var result = SalesforceMapper.FromSalesforce<LeadModel>(json);

        // Assert
        result.Status.Should().Be(LeadStatus.OpenNotContacted);
    }

    [Fact]
    public void FromSalesforce_Enum_ShouldFallbackToEnumName()
    {
        // Arrange
        var json = new JsonObject
        {
            ["Status"] = "Plain"
        };

        // Act
        var result = SalesforceMapper.FromSalesforce<LeadModel>(json);

        // Assert
        result.Status.Should().Be(LeadStatus.Plain);
    }
}
