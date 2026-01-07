using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Models.Authorization;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Layout;
using SalesforceCore.Services.Metadata;
using Xunit;

namespace SalesforceCore.Tests;

public class PermissionServiceTests
{
    private readonly Mock<ISchemaService> _schemaServiceMock;
    private readonly Mock<ICacheProvider> _cacheProviderMock;
    private readonly Mock<IUserContextProvider> _userProviderMock;
    private readonly Mock<IDynamicUiConfigProvider> _configProviderMock;
    private readonly Mock<ILogger<PermissionService>> _loggerMock;
    private readonly DynamicUiOptions _options;
    private readonly PermissionService _service;

    public PermissionServiceTests()
    {
        _schemaServiceMock = new Mock<ISchemaService>();
        _cacheProviderMock = new Mock<ICacheProvider>();
        _userProviderMock = new Mock<IUserContextProvider>();
        _configProviderMock = new Mock<IDynamicUiConfigProvider>();
        _loggerMock = new Mock<ILogger<PermissionService>>();
        _options = new DynamicUiOptions { BypassCache = true };
        _configProviderMock.Setup(cp => cp.Current).Returns(_options);

        _service = new PermissionService(
            _schemaServiceMock.Object,
            _cacheProviderMock.Object,
            _userProviderMock.Object,
            Options.Create(_options),
            _configProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GetPermissionsAsync_ReturnsPermissionSnapshot_ForValidObject()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetPermissionsAsync("Account");

        // Assert
        result.Should().NotBeNull();
        result.ObjectName.Should().Be("Account");
        result.ObjectLabel.Should().Be("Account");
        result.CanCreate.Should().BeTrue();
        result.CanRead.Should().BeTrue();
        result.CanUpdate.Should().BeTrue();
        result.CanDelete.Should().BeTrue();
        result.FieldPermissions.Should().ContainKey("Name");
    }

    [Fact]
    public async Task GetPermissionsAsync_ThrowsException_ForInvalidObject()
    {
        // Arrange
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("InvalidObject", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SObjectDescribe?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GetPermissionsAsync("InvalidObject"));

        exception.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task CanPerformActionAsync_ReturnsTrue_WhenObjectIsCreateable()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.CanPerformActionAsync("Account", PermissionAction.Create);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanPerformActionAsync_ReturnsFalse_WhenObjectIsNotDeleteable()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        describe.Deletable = false;
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.CanPerformActionAsync("Account", PermissionAction.Delete);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAccessFieldAsync_ReturnsTrue_WhenFieldIsReadable()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.CanAccessFieldAsync("Account", "Name", PermissionAction.Read);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessFieldAsync_ReturnsFalse_WhenFieldDoesNotExist()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.CanAccessFieldAsync("Account", "NonExistentField", PermissionAction.Read);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetReadableFieldsAsync_ReturnsOnlyAccessibleFields()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        // Make one field inaccessible
        describe.Fields.First(f => f.Name == "Industry").Accessible = false;
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetReadableFieldsAsync("Account");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain("Name");
        result.Should().NotContain("Industry");
    }

    [Fact]
    public async Task GetCreateableFieldsAsync_ReturnsOnlyCreateableFields()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetCreateableFieldsAsync("Account");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain("Name");
    }

    [Fact]
    public async Task GetUpdateableFieldsAsync_ReturnsOnlyUpdateableFields()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetUpdateableFieldsAsync("Account");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain("Name");
    }

    [Fact]
    public async Task GetAllowedActionsAsync_ReturnsAllActions_ForFullyAccessibleObject()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetAllowedActionsAsync("Account");

        // Assert
        result.Should().Contain(PermissionAction.Create);
        result.Should().Contain(PermissionAction.Read);
        result.Should().Contain(PermissionAction.Update);
        result.Should().Contain(PermissionAction.Delete);
    }

    [Fact]
    public async Task GetAllowedActionsAsync_ReturnsLimitedActions_ForRestrictedObject()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        describe.Createable = false;
        describe.Deletable = false;
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        // Act
        var result = await _service.GetAllowedActionsAsync("Account");

        // Assert
        result.Should().NotContain(PermissionAction.Create);
        result.Should().Contain(PermissionAction.Read);
        result.Should().Contain(PermissionAction.Update);
        result.Should().NotContain(PermissionAction.Delete);
    }

    [Fact]
    public async Task CheckPermissionsAsync_ReturnsBatchResults_ForMultipleChecks()
    {
        // Arrange
        var describe = CreateAccountDescribe();
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(describe);

        var checks = new[]
        {
            ("Account", PermissionAction.Create, (string?)null),
            ("Account", PermissionAction.Read, (string?)"Name"),
            ("Account", PermissionAction.Update, (string?)"Industry")
        };

        // Act
        var results = await _service.CheckPermissionsAsync(checks);

        // Assert
        results.Should().HaveCount(3);
        results[0].IsAllowed.Should().BeTrue();
        results[1].IsAllowed.Should().BeTrue();
        results[2].IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task GetPermissionsAsync_BatchRequest_ReturnsMultipleSnapshots()
    {
        // Arrange
        var accountDescribe = CreateAccountDescribe();
        var contactDescribe = CreateContactDescribe();

        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountDescribe);
        _schemaServiceMock
            .Setup(s => s.GetDescribeAsync("Contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contactDescribe);

        var context = PermissionRequestContext.ForObjects("Account", "Contact");

        // Act
        var result = await _service.GetPermissionsAsync(context);

        // Assert
        result.Snapshots.Should().HaveCount(2);
        result.Snapshots.Should().ContainKey("Account");
        result.Snapshots.Should().ContainKey("Contact");
        result.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetPermissionsAsync_CachesResults_WhenCacheEnabled()
    {
        // Arrange
        var cacheOptions = new DynamicUiOptions { BypassCache = false };
        var describe = CreateAccountDescribe();
        var cachedSnapshot = ObjectPermissionSnapshot.FromDescribe(describe);
        var cacheProviderMock = new Mock<ICacheProvider>();

        cacheProviderMock
            .Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<ObjectPermissionSnapshot?>>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedSnapshot);

        var userProviderMock = new Mock<IUserContextProvider>();
        var configProviderMock = new Mock<IDynamicUiConfigProvider>();
        configProviderMock.Setup(c => c.Current).Returns(cacheOptions);

        var service = new PermissionService(
            _schemaServiceMock.Object,
            cacheProviderMock.Object,
            userProviderMock.Object,
            Options.Create(cacheOptions),
            configProviderMock.Object,
            _loggerMock.Object);

        // Act
        var result = await service.GetPermissionsAsync("Account");

        // Assert
        result.Should().NotBeNull();
        cacheProviderMock.Verify(c => c.GetOrCreateAsync(
            It.Is<string>(key => key.Contains("account") && key.Contains("_anon")),
            It.IsAny<Func<CancellationToken, Task<ObjectPermissionSnapshot?>>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
                new() { Name = "Id", Label = "Account ID", Type = "id", Accessible = true, Createable = false, Updateable = false },
                new() { Name = "Name", Label = "Account Name", Type = "string", Accessible = true, Createable = true, Updateable = true, Nillable = false },
                new() { Name = "Industry", Label = "Industry", Type = "picklist", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "Phone", Label = "Phone", Type = "phone", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "Website", Label = "Website", Type = "url", Accessible = true, Createable = true, Updateable = true }
            },
            RecordTypeInfos = new List<RecordTypeInfo>
            {
                new() { RecordTypeId = "012000000000000AAA", Name = "Master", Available = true, Master = true }
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
                new() { Name = "FirstName", Label = "First Name", Type = "string", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "LastName", Label = "Last Name", Type = "string", Accessible = true, Createable = true, Updateable = true, Nillable = false },
                new() { Name = "Email", Label = "Email", Type = "email", Accessible = true, Createable = true, Updateable = true },
                new() { Name = "Phone", Label = "Phone", Type = "phone", Accessible = true, Createable = true, Updateable = true }
            }
        };
    }
}
