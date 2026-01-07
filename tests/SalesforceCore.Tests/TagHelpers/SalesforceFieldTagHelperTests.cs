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

public class SalesforceFieldTagHelperTests
{
    private readonly Mock<ISchemaService> _mockSchemaService;
    private readonly SalesforceFieldTagHelper _tagHelper;

    public SalesforceFieldTagHelperTests()
    {
        _mockSchemaService = new Mock<ISchemaService>();
        _tagHelper = new SalesforceFieldTagHelper(_mockSchemaService.Object);
    }

    #region Permission Check Tests

    [Fact]
    public async Task ProcessAsync_WhenFieldUpdateable_ShouldNotModifyOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "Name";
        _tagHelper.Mode = AccessMode.Update;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Name", new SObjectField { Name = "Name", Updateable = true, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().NotContain(a => a.Name == "readonly");
        output.Attributes.Should().NotContain(a => a.Name == "disabled");
        output.TagName.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAsync_WhenFieldNotUpdateable_ShouldAddReadonly()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "CreatedDate";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Readonly;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "CreatedDate", new SObjectField { Name = "CreatedDate", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly" && a.Value.ToString() == "readonly");
    }

    [Fact]
    public async Task ProcessAsync_WhenFieldNotAccessible_ShouldAddReadonly()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "SecretField__c";
        _tagHelper.Mode = AccessMode.Read;
        _tagHelper.Behavior = DeniedBehavior.Readonly;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "SecretField__c", new SObjectField { Name = "SecretField__c", Accessible = false, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly");
    }

    #endregion

    #region Denied Behavior Tests

    [Fact]
    public async Task ProcessAsync_WithDisableBehavior_ShouldAddDisabled()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.FieldName = "ReadOnlyField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Disable;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "ReadOnlyField", new SObjectField { Name = "ReadOnlyField", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "disabled" && a.Value.ToString() == "disabled");
    }

    [Fact]
    public async Task ProcessAsync_WithHideBehavior_ShouldSuppressOutput()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.FieldName = "HiddenField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Hide;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "HiddenField", new SObjectField { Name = "HiddenField", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_WithReadonlyWithClassBehavior_ShouldAddReadonlyAndClass()
    {
        // Arrange
        _tagHelper.ObjectName = "Lead";
        _tagHelper.FieldName = "ReadOnlyField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.ReadonlyWithClass;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "ReadOnlyField", new SObjectField { Name = "ReadOnlyField", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Lead", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly");
        output.Attributes.Should().Contain(a => a.Name == "class" && a.Value.ToString()!.Contains("sf-readonly"));
    }

    [Fact]
    public async Task ProcessAsync_WithDisableWithClassBehavior_ShouldAddDisabledAndClass()
    {
        // Arrange
        _tagHelper.ObjectName = "Opportunity";
        _tagHelper.FieldName = "DisabledField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.DisableWithClass;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "DisabledField", new SObjectField { Name = "DisabledField", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Opportunity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "disabled");
        output.Attributes.Should().Contain(a => a.Name == "class" && a.Value.ToString()!.Contains("sf-disabled"));
    }

    [Fact]
    public async Task ProcessAsync_WithExistingClass_ShouldAppendClass()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "ReadOnlyField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.ReadonlyWithClass;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "ReadOnlyField", new SObjectField { Name = "ReadOnlyField", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");
        output.Attributes.SetAttribute("class", "form-control");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        var classAttr = output.Attributes["class"];
        classAttr.Should().NotBeNull();
        classAttr!.Value.ToString().Should().Contain("form-control");
        classAttr.Value.ToString().Should().Contain("sf-readonly");
    }

    #endregion

    #region Different Element Types

    [Fact]
    public async Task ProcessAsync_OnSelectElement_ShouldWork()
    {
        // Arrange
        _tagHelper.ObjectName = "Case";
        _tagHelper.FieldName = "Status";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Disable;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Status", new SObjectField { Name = "Status", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Case", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("select");
        var output = CreateTagHelperOutput("select");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "disabled");
    }

    [Fact]
    public async Task ProcessAsync_OnTextareaElement_ShouldWork()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "Description";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Readonly;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Description", new SObjectField { Name = "Description", Updateable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("textarea");
        var output = CreateTagHelperOutput("textarea");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly");
    }

    #endregion

    #region Access Mode Tests

    [Fact]
    public async Task ProcessAsync_CreateMode_WhenCreateable_ShouldNotModify()
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

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().NotContain(a => a.Name == "readonly");
        output.Attributes.Should().NotContain(a => a.Name == "disabled");
    }

    [Fact]
    public async Task ProcessAsync_CreateMode_WhenNotCreateable_ShouldApplyBehavior()
    {
        // Arrange
        _tagHelper.ObjectName = "Contact";
        _tagHelper.FieldName = "Id";
        _tagHelper.Mode = AccessMode.Create;
        _tagHelper.Behavior = DeniedBehavior.Readonly;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "Id", new SObjectField { Name = "Id", Createable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly");
    }

    [Fact]
    public async Task ProcessAsync_DeleteMode_ShouldAlwaysAllow()
    {
        // Arrange - Delete is object-level only, field check should pass
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "AnyField";
        _tagHelper.Mode = AccessMode.Delete;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "AnyField", new SObjectField { Name = "AnyField", Updateable = false, Createable = false, Accessible = true, DeprecatedAndHidden = false } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert - Delete mode always passes for field checks
        output.Attributes.Should().NotContain(a => a.Name == "readonly");
        output.Attributes.Should().NotContain(a => a.Name == "disabled");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ProcessAsync_WithNoObjectName_ShouldNotModify()
    {
        // Arrange
        _tagHelper.ObjectName = null;
        _tagHelper.FieldName = "Name";

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().NotContain(a => a.Name == "readonly");
        output.Attributes.Should().NotContain(a => a.Name == "disabled");
    }

    [Fact]
    public async Task ProcessAsync_WithNoFieldName_ShouldNotModify()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = null;

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().NotContain(a => a.Name == "readonly");
        output.Attributes.Should().NotContain(a => a.Name == "disabled");
    }

    [Fact]
    public async Task ProcessAsync_DeprecatedField_ShouldApplyBehavior()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "OldField__c";
        _tagHelper.Mode = AccessMode.Read;
        _tagHelper.Behavior = DeniedBehavior.Hide;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
        {
            { "OldField__c", new SObjectField { Name = "OldField__c", Accessible = true, DeprecatedAndHidden = true } }
        };
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.TagName.Should().BeNull();
        output.Content.GetContent().Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_FieldNotFound_ShouldApplyBehavior()
    {
        // Arrange
        _tagHelper.ObjectName = "Account";
        _tagHelper.FieldName = "NonExistentField";
        _tagHelper.Mode = AccessMode.Update;
        _tagHelper.Behavior = DeniedBehavior.Readonly;

        var fieldMap = new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase);
        _mockSchemaService.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fieldMap);

        var context = CreateTagHelperContext("input");
        var output = CreateTagHelperOutput("input");

        // Act
        await _tagHelper.ProcessAsync(context, output);

        // Assert
        output.Attributes.Should().Contain(a => a.Name == "readonly");
    }

    #endregion

    #region Helper Methods

    private static TagHelperContext CreateTagHelperContext(string tagName)
    {
        return new TagHelperContext(
            tagName: tagName,
            allAttributes: new TagHelperAttributeList(),
            items: new Dictionary<object, object>(),
            uniqueId: Guid.NewGuid().ToString());
    }

    private static TagHelperOutput CreateTagHelperOutput(string tagName)
    {
        return new TagHelperOutput(
            tagName: tagName,
            attributes: new TagHelperAttributeList(),
            getChildContentAsync: (useCachedResult, encoder) =>
                Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    }

    #endregion
}
