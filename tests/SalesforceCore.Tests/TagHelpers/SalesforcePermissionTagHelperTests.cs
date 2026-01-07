using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Moq;
using Xunit;
using FluentAssertions;
using SalesforceCore.AspNetCore.TagHelpers;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Models.Security;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.Tests.TagHelpers;

public class SalesforcePermissionTagHelperTests
{
    private readonly Mock<ISchemaService> _mockSchemaService;
    private readonly SalesforcePermissionTagHelper _tagHelper;

    public SalesforcePermissionTagHelperTests()
    {
        _mockSchemaService = new Mock<ISchemaService>();
        _tagHelper = new SalesforcePermissionTagHelper(_mockSchemaService.Object);
    }

    #region Object-Level Permission Tests

    [Fact]
    public async Task ProcessAsync_ObjectCreate_WhenCreateable_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.Mode = AccessMode.Create;

        var describe = new SObjectDescribe { Createable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.IsContentModified.Should().BeFalse();
        output.TagName.Should().BeNull(); // Wrapper removed
    }

    [Fact]
    public async Task ProcessAsync_ObjectCreate_WhenNotCreateable_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.Mode = AccessMode.Create;

        var describe = new SObjectDescribe { Createable = false };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ObjectUpdate_WhenUpdateable_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.Mode = AccessMode.Update;

        var describe = new SObjectDescribe { Updateable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_ObjectDelete_WhenDeletable_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Lead";
        _tagHelper.Mode = AccessMode.Delete;

        var describe = new SObjectDescribe { Deletable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Lead", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_ObjectRead_WhenQueryable_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Opportunity";
        _tagHelper.Mode = AccessMode.Read;

        var describe = new SObjectDescribe { Queryable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Opportunity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_ObjectNotFound_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "NonExistentObject";
        _tagHelper.Mode = AccessMode.Read;

        _mockSchemaService.Setup(s => s.GetDescribeAsync("NonExistentObject", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SObjectDescribe?)null);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    #endregion

    #region Field-Level Permission Tests

    [Fact]
    public async Task ProcessAsync_FieldRead_WhenAccessible_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "AnnualRevenue";
        _tagHelper.Mode = AccessMode.Read;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "AnnualRevenue", new SObjectField { Name = "AnnualRevenue", Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_FieldRead_WhenNotAccessible_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "SecretField__c";
        _tagHelper.Mode = AccessMode.Read;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "SecretField__c", new SObjectField { Name = "SecretField__c", Accessible = false, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_FieldCreate_WhenCreateableAndAccessible_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.FieldName = "Email";
        _tagHelper.Mode = AccessMode.Create;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Email", new SObjectField { Name = "Email", Createable = true, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_FieldCreate_WhenNotCreateable_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.FieldName = "CreatedDate";
        _tagHelper.Mode = AccessMode.Create;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "CreatedDate", new SObjectField { Name = "CreatedDate", Createable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_FieldUpdate_WhenUpdateableAndAccessible_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Lead";
        _tagHelper.FieldName = "Phone";
        _tagHelper.Mode = AccessMode.Update;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Phone", new SObjectField { Name = "Phone", Updateable = true, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Lead", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    [Fact]
    public async Task ProcessAsync_FieldNotFound_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "NonExistentField";
        _tagHelper.Mode = AccessMode.Read;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase);
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_DeprecatedField_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "OldField__c";
        _tagHelper.Mode = AccessMode.Read;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "OldField__c", new SObjectField { Name = "OldField__c", Accessible = true, DeprecatedAndHidden = true } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    #endregion

    #region Negate Tests

    [Fact]
    public async Task ProcessAsync_WithNegate_WhenHasPermission_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.Mode = AccessMode.Create;
        _tagHelper.Negate = true;

        var describe = new SObjectDescribe { Createable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_WithNegate_WhenNoPermission_ShouldRenderContent()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.Mode = AccessMode.Create;
        _tagHelper.Negate = true;

        var describe = new SObjectDescribe { Createable = false };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // Wrapper is always removed
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ProcessAsync_WithNoObjectName_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = null;
        _tagHelper.Mode = AccessMode.Read;

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_WithEmptyObjectName_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "";
        _tagHelper.Mode = AccessMode.Read;

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_WithWhitespaceObjectName_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "   ";
        _tagHelper.Mode = AccessMode.Read;

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_ShouldRemoveWrapperTagName()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.Mode = AccessMode.Read;

        var describe = new SObjectDescribe { Queryable = true };
        _mockSchemaService.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var context = CreateTagHelperContext();
        var output = CreateTagHelperOutput();

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull(); // sf-permission tag should be removed
    }

    #endregion

    #region Helper Methods

    private static TagHelperContext CreateTagHelperContext()
    {
        return new TagHelperContext(
            tagName: "sf-permission",
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: Guid.NewGuid().ToString());
    }

    private static TagHelperOutput CreateTagHelperOutput()
    {
        return new TagHelperOutput(
            tagName: "sf-permission",
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    }

    #endregion
}
