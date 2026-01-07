using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Models.Authorization;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Layout;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Layout;
using SalesforceCore.Services.Metadata;
using Xunit;

namespace SalesforceCore.Tests;

public class LayoutDescriptorServiceTests
{
    private readonly Mock<ISchemaService> _schemaServiceMock;
    private readonly Mock<IPermissionService> _permissionServiceMock;
    private readonly Mock<IVisibilityService> _visibilityServiceMock;
    private readonly Mock<IUserContextProvider> _userProviderMock;
    private readonly Mock<ICacheProvider> _cacheProviderMock;
    private readonly Mock<IDynamicUiConfigProvider> _configProviderMock;
    private readonly Mock<ILogger<LayoutDescriptorService>> _loggerMock;
    private readonly DynamicUiOptions _options;
    private readonly LayoutDescriptorService _service;

    public LayoutDescriptorServiceTests()
    {
        _schemaServiceMock = new Mock<ISchemaService>();
        _permissionServiceMock = new Mock<IPermissionService>();
        _visibilityServiceMock = new Mock<IVisibilityService>();
        _userProviderMock = new Mock<IUserContextProvider>();
        _cacheProviderMock = new Mock<ICacheProvider>();
        _configProviderMock = new Mock<IDynamicUiConfigProvider>();
        _loggerMock = new Mock<ILogger<LayoutDescriptorService>>();
        _options = new DynamicUiOptions
        {
            BypassCache = true,
            Navigation = new NavigationConfig
            {
                DefaultObjects = new List<string> { "Account", "Contact" }
            }
        };
        _configProviderMock.Setup(c => c.Current).Returns(_options);

        _service = new LayoutDescriptorService(
            _schemaServiceMock.Object,
            _permissionServiceMock.Object,
            _visibilityServiceMock.Object,
            _userProviderMock.Object,
            _cacheProviderMock.Object,
            Options.Create(_options),
            _configProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetNavigationAsync_ReturnsNavigationDescriptor_WithDefaultObjects()
    {
        // Arrange
        SetupAccountPermissions();
        SetupContactPermissions();

        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContactDescribe());

        // Act
        var result = await _service.GetNavigationAsync();

        // Assert
        result.Should().NotBeNull();
        result.MainItems.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetFormAsync_ReturnsFormDescriptor_ForCreateMode()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>());

        // Act
        var result = await _service.GetFormAsync("Account", FormMode.Create);

        // Assert
        result.Should().NotBeNull();
        result.ObjectName.Should().Be("Account");
        result.Mode.Should().Be(FormMode.Create);
        result.Fields.Should().NotBeEmpty();
        result.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetFormAsync_ReturnsFormDescriptor_ForEditMode()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>());

        // Act
        var result = await _service.GetFormAsync("Account", FormMode.Edit);

        // Assert
        result.Should().NotBeNull();
        result.Mode.Should().Be(FormMode.Edit);
        result.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task GetFormAsync_ReturnsFormDescriptor_ForViewMode()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>());

        // Act
        var result = await _service.GetFormAsync("Account", FormMode.View);

        // Assert
        result.Should().NotBeNull();
        result.Mode.Should().Be(FormMode.View);
        result.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task GetListAsync_ReturnsListDescriptor_WithColumns()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetNameFieldAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");

        // Act
        var result = await _service.GetListAsync("Account");

        // Assert
        result.Should().NotBeNull();
        result.ObjectName.Should().Be("Account");
        result.Columns.Should().NotBeEmpty();
        result.RowActions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetDetailAsync_ReturnsDetailDescriptor_WithSectionsAndActions()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>());
        _schemaServiceMock
            .Setup(s => s.GetChildRelationshipsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChildRelationship>());

        // Act
        var result = await _service.GetDetailAsync("Account");

        // Assert
        result.Should().NotBeNull();
        result.ObjectName.Should().Be("Account");
        result.Sections.Should().NotBeEmpty();
        result.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAvailableActionsAsync_ReturnsCreateAction_ForListContext()
    {
        // Arrange
        SetupAccountPermissions(canCreate: true);

        // Act
        var result = await _service.GetAvailableActionsAsync("Account", UiActionContext.List);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(a => a.ActionType == "create");
    }

    [Fact]
    public async Task GetAvailableActionsAsync_DoesNotReturnCreateAction_WhenNotCreateable()
    {
        // Arrange
        SetupAccountPermissions(canCreate: false);

        // Act
        var result = await _service.GetAvailableActionsAsync("Account", UiActionContext.List);

        // Assert
        result.Should().NotContain(a => a.ActionType == "create");
    }

    [Fact]
    public async Task GetAvailableActionsAsync_ReturnsEditAndDeleteActions_ForDetailContext()
    {
        // Arrange
        SetupAccountPermissions(canUpdate: true, canDelete: true);

        // Act
        var result = await _service.GetAvailableActionsAsync("Account", UiActionContext.Detail);

        // Assert
        result.Should().Contain(a => a.ActionType == "edit");
        result.Should().Contain(a => a.ActionType == "delete");
    }

    [Fact]
    public async Task GetAvailableActionsAsync_ReturnsSaveAndCancelActions_ForFormContext()
    {
        // Arrange
        SetupAccountPermissions();

        // Act
        var result = await _service.GetAvailableActionsAsync("Account", UiActionContext.Form);

        // Assert
        result.Should().Contain(a => a.ActionType == "save");
        result.Should().Contain(a => a.ActionType == "cancel");
    }

    [Fact]
    public async Task GetAvailableActionsAsync_ReturnsRowActions_ForRowActionContext()
    {
        // Arrange
        SetupAccountPermissions(canRead: true, canUpdate: true, canDelete: true);

        // Act
        var result = await _service.GetAvailableActionsAsync("Account", UiActionContext.RowAction);

        // Assert
        result.Should().Contain(a => a.ActionType == "view");
        result.Should().Contain(a => a.ActionType == "edit");
        result.Should().Contain(a => a.ActionType == "delete");
    }

    [Fact]
    public async Task GetFieldDescriptorAsync_ReturnsFieldDescriptor_ForValidField()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe().Fields);

        // Act
        var result = await _service.GetFieldDescriptorAsync("Account", "Name", FormMode.Edit);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Name");
        result.Label.Should().Be("Account Name");
        result.Type.Should().Be("string");
    }

    [Fact]
    public async Task GetFieldDescriptorAsync_ReturnsNull_ForInvalidField()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetFieldsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe().Fields);

        // Act
        var result = await _service.GetFieldDescriptorAsync("Account", "NonExistentField", FormMode.Edit);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecordTypeSelectorAsync_ReturnsNull_WhenOnlyOneRecordType()
    {
        // Arrange
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>
            {
                new() { RecordTypeId = "012000000000000AAA", Name = "Master", Available = true, Master = true }
            });

        // Act
        var result = await _service.GetRecordTypeSelectorAsync("Account");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRecordTypeSelectorAsync_ReturnsSelector_WhenMultipleRecordTypes()
    {
        // Arrange
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>
            {
                new() { RecordTypeId = "012000000000001AAA", Name = "Business", Available = true, DefaultRecordTypeMapping = true },
                new() { RecordTypeId = "012000000000002AAA", Name = "Person", Available = true }
            });

        // Act
        var result = await _service.GetRecordTypeSelectorAsync("Account");

        // Assert
        result.Should().NotBeNull();
        result!.Options.Should().HaveCount(2);
        result.ShowSelector.Should().BeTrue();
    }

    [Fact]
    public async Task GetPicklistOptionsAsync_ReturnsPicklistOptions()
    {
        // Arrange
        _schemaServiceMock
            .Setup(s => s.GetPicklistValuesAsync("Account", "Industry", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PicklistValuesResult
            {
                Values = new List<PicklistEntry>
                {
                    new() { Value = "Technology", Label = "Technology", Active = true },
                    new() { Value = "Finance", Label = "Finance", Active = true },
                    new() { Value = "Healthcare", Label = "Healthcare", Active = true }
                }
            });

        // Act
        var result = await _service.GetPicklistOptionsAsync("Account", "Industry");

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain(o => o.Value == "Technology");
    }

    [Fact]
    public async Task GetRelatedListsAsync_ReturnsRelatedLists_ForParentObject()
    {
        // Arrange
        SetupContactPermissions();
        _schemaServiceMock
            .Setup(s => s.GetChildRelationshipsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChildRelationship>
            {
                new() { ChildSObject = "Contact", Field = "AccountId", RelationshipName = "Contacts" }
            });
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContactDescribe());
        _schemaServiceMock
            .Setup(s => s.GetNameFieldAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");
        _schemaServiceMock
            .Setup(s => s.GetAccessibleFieldsAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateContactDescribe().Fields);

        // Act
        var result = await _service.GetRelatedListsAsync("Account");

        // Assert
        result.Should().NotBeEmpty();
        result.First().ChildObject.Should().Be("Contact");
        result.First().RelationshipName.Should().Be("Contacts");
    }

    [Fact]
    public async Task FormDescriptor_IncludesRequiredFields()
    {
        // Arrange
        SetupAccountPermissions();
        var describe = CreateAccountDescribe();
        describe.Fields.First(f => f.Name == "Name").Nillable = false; // Make Name required

        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);
        _schemaServiceMock
            .Setup(s => s.GetRecordTypesAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RecordTypeInfo>());

        // Act
        var result = await _service.GetFormAsync("Account", FormMode.Create);

        // Assert
        var nameField = result.Fields.FirstOrDefault(f => f.Name == "Name");
        nameField.Should().NotBeNull();
        nameField!.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task ListDescriptor_IncludesNameFieldAsLink()
    {
        // Arrange
        SetupAccountPermissions();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccountDescribe());
        _schemaServiceMock
            .Setup(s => s.GetNameFieldAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync("Name");

        // Act
        var result = await _service.GetListAsync("Account");

        // Assert
        var nameColumn = result.Columns.FirstOrDefault(c => c.FieldName == "Name");
        nameColumn.Should().NotBeNull();
        nameColumn!.IsLink.Should().BeTrue();
    }

    private void SetupAccountPermissions(
        bool canCreate = true,
        bool canRead = true,
        bool canUpdate = true,
        bool canDelete = true)
    {
        var snapshot = new ObjectPermissionSnapshot
        {
            ObjectName = "Account",
            ObjectLabel = "Account",
            CanCreate = canCreate,
            CanRead = canRead,
            CanUpdate = canUpdate,
            CanDelete = canDelete,
            FieldPermissions = new Dictionary<string, FieldPermission>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = new() { FieldName = "Name", Label = "Account Name", CanRead = true, CanCreate = true, CanUpdate = true },
                ["Industry"] = new() { FieldName = "Industry", Label = "Industry", CanRead = true, CanCreate = true, CanUpdate = true },
                ["Phone"] = new() { FieldName = "Phone", Label = "Phone", CanRead = true, CanCreate = true, CanUpdate = true }
            }
        };

        _permissionServiceMock
            .Setup(p => p.GetPermissionsAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        _permissionServiceMock
            .Setup(p => p.CanPerformActionAsync("Account", It.IsAny<PermissionAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string obj, PermissionAction action, CancellationToken ct) => action switch
            {
                PermissionAction.Create => canCreate,
                PermissionAction.Read => canRead,
                PermissionAction.Update => canUpdate,
                PermissionAction.Delete => canDelete,
                _ => false
            });
    }

    private void SetupContactPermissions()
    {
        var snapshot = new ObjectPermissionSnapshot
        {
            ObjectName = "Contact",
            ObjectLabel = "Contact",
            CanCreate = true,
            CanRead = true,
            CanUpdate = true,
            CanDelete = true,
            FieldPermissions = new Dictionary<string, FieldPermission>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = new() { FieldName = "Name", Label = "Full Name", CanRead = true, CanCreate = true, CanUpdate = true },
                ["Email"] = new() { FieldName = "Email", Label = "Email", CanRead = true, CanCreate = true, CanUpdate = true }
            }
        };

        _permissionServiceMock
            .Setup(p => p.GetPermissionsAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        _permissionServiceMock
            .Setup(p => p.CanPerformActionAsync("Contact", It.IsAny<PermissionAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static SObjectDescribe CreateAccountDescribe()
    {
        return new SObjectDescribe
        {
            Name = "Account",
            Label = "Account",
            LabelPlural = "Accounts",
            Createable = true,
            Retrieveable = true,
            Updateable = true,
            Deletable = true,
            Queryable = true,
            Searchable = true,
            Fields = new List<SObjectField>
            {
                new() { Name = "Id", Label = "Account ID", Type = "id", Accessible = true, Createable = false, Updateable = false, Sortable = true, Filterable = true },
                new() { Name = "Name", Label = "Account Name", Type = "string", Accessible = true, Createable = true, Updateable = true, Nillable = false, Sortable = true, Filterable = true, Length = 255 },
                new() { Name = "Industry", Label = "Industry", Type = "picklist", Accessible = true, Createable = true, Updateable = true, Sortable = true, Filterable = true },
                new() { Name = "Phone", Label = "Phone", Type = "phone", Accessible = true, Createable = true, Updateable = true, Sortable = false, Filterable = true },
                new() { Name = "Website", Label = "Website", Type = "url", Accessible = true, Createable = true, Updateable = true }
            }
        };
    }

    private static SObjectDescribe CreateContactDescribe()
    {
        return new SObjectDescribe
        {
            Name = "Contact",
            Label = "Contact",
            LabelPlural = "Contacts",
            Createable = true,
            Retrieveable = true,
            Updateable = true,
            Deletable = true,
            Queryable = true,
            Searchable = true,
            Fields = new List<SObjectField>
            {
                new() { Name = "Id", Label = "Contact ID", Type = "id", Accessible = true, Createable = false, Updateable = false },
                new() { Name = "Name", Label = "Full Name", Type = "string", Accessible = true, Createable = false, Updateable = false },
                new() { Name = "FirstName", Label = "First Name", Type = "string", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "LastName", Label = "Last Name", Type = "string", Accessible = true, Createable = true, Updateable = true, Nillable = false },
                new() { Name = "Email", Label = "Email", Type = "email", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "Phone", Label = "Phone", Type = "phone", Accessible = true, Createable = true, Updateable = true }
            }
        };
    }
}
