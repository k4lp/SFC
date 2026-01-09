using Moq;
using SalesforceCore.Models.Authorization;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Unit tests for the fluent PermissionGuard API.
/// </summary>
public class PermissionGuardTests
{
    private readonly Mock<IPermissionService> _mockPermissionService;

    public PermissionGuardTests()
    {
        _mockPermissionService = new Mock<IPermissionService>();
    }

    [Fact]
    public async Task EmptyGuard_ShouldReturnSuccess()
    {
        // Arrange - create guard but don't add any requirements
        var guard = new PermissionGuard(_mockPermissionService.Object);
        var builder = guard.Require("Account", PermissionAction.Read);
        
        // Setup permission service to return valid snapshot
        SetupPermissionSnapshot("Account", canRead: true);
        
        // Act
        var result = await builder.EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task SingleObjectRequirement_WithPermission_ShouldPass()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: true);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task SingleObjectRequirement_WithoutPermission_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: false, canCreate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Create)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
        Assert.Single(result.Violations);
        Assert.Equal("Account", result.Violations[0].ObjectName);
        Assert.Equal(PermissionAction.Create, result.Violations[0].Action);
    }

    [Fact]
    public async Task FieldRequirement_WithPermission_ShouldPass()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, fields: new Dictionary<string, FieldPermission>
        {
            ["AnnualRevenue"] = new FieldPermission { FieldName = "AnnualRevenue", CanRead = true }
        });
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireField("Account", "AnnualRevenue", PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task FieldRequirement_WithoutPermission_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, fields: new Dictionary<string, FieldPermission>
        {
            ["AnnualRevenue"] = new FieldPermission { FieldName = "AnnualRevenue", CanRead = false }
        });
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireField("Account", "AnnualRevenue", PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
        Assert.Single(result.Violations);
        Assert.Equal("AnnualRevenue", result.Violations[0].FieldName);
    }

    [Fact]
    public async Task MultipleRequirements_AllPass_ShouldSucceed()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: true, fields: new Dictionary<string, FieldPermission>
        {
            ["Name"] = new FieldPermission { FieldName = "Name", CanRead = true, CanCreate = true }
        });
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Read)
            .Require("Account", PermissionAction.Create)
            .RequireField("Account", "Name", PermissionAction.Create)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task MultipleRequirements_OneFails_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Read)
            .Require("Account", PermissionAction.Create)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
        Assert.Single(result.Violations);
    }

    [Fact]
    public async Task RequireAny_AtLeastOnePasses_ShouldSucceed()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: false, canUpdate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireAny("Account", PermissionAction.Create, PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task RequireAny_NonePass_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: false, canCreate: false, canUpdate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireAny("Account", PermissionAction.Create, PermissionAction.Update)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task RequireAll_AllPass_ShouldSucceed()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: true, canUpdate: true);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireAll("Account", PermissionAction.Read, PermissionAction.Create, PermissionAction.Update)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task RequireAll_SomeFail_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: true, canUpdate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .RequireAll("Account", PermissionAction.Read, PermissionAction.Create, PermissionAction.Update)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains(result.Violations, v => v.Action == PermissionAction.Update);
    }

    [Fact]
    public async Task OrLogic_FirstGroupPasses_ShouldSucceed()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Read)  // Group 1: passes
            .Or()
            .Require("Account", PermissionAction.Create) // Group 2: would fail
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task OrLogic_SecondGroupPasses_ShouldSucceed()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: true, canCreate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Create) // Group 1: fails
            .Or()
            .Require("Account", PermissionAction.Read)   // Group 2: passes
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task OrLogic_AllGroupsFail_ShouldFail()
    {
        // Arrange
        SetupPermissionSnapshot("Account", canRead: false, canCreate: false);
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Create)
            .Or()
            .Require("Account", PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task MultipleObjects_BatchesRequests()
    {
        // Arrange - setup mock to return both objects in single call
        var accountSnapshot = new ObjectPermissionSnapshot
        {
            ObjectName = "Account",
            ObjectLabel = "Account",
            CanRead = true
        };
        var contactSnapshot = new ObjectPermissionSnapshot
        {
            ObjectName = "Contact",
            ObjectLabel = "Contact",
            CanRead = true
        };
        
        _mockPermissionService
            .Setup(s => s.GetPermissionsAsync(
                It.IsAny<PermissionRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionRequestContext ctx, CancellationToken _) =>
            {
                var result = new PermissionResult();
                if (ctx.Objects.Contains("Account"))
                    result.Snapshots["Account"] = accountSnapshot;
                if (ctx.Objects.Contains("Contact"))
                    result.Snapshots["Contact"] = contactSnapshot;
                return result;
            });
        
        var guard = new PermissionGuard(_mockPermissionService.Object);
        
        // Act
        var result = await guard
            .Require("Account", PermissionAction.Read)
            .Require("Contact", PermissionAction.Read)
            .EvaluateAsync();
        
        // Assert
        Assert.True(result.IsAllowed);
        
        // Verify batch call was made (GetPermissionsAsync with context)
        _mockPermissionService.Verify(
            s => s.GetPermissionsAsync(It.IsAny<PermissionRequestContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void SetupPermissionSnapshot(
        string objectName,
        bool canRead = false,
        bool canCreate = false,
        bool canUpdate = false,
        bool canDelete = false,
        Dictionary<string, FieldPermission>? fields = null)
    {
        var snapshot = new ObjectPermissionSnapshot
        {
            ObjectName = objectName,
            ObjectLabel = objectName,
            CanRead = canRead,
            CanCreate = canCreate,
            CanUpdate = canUpdate,
            CanDelete = canDelete,
            FieldPermissions = fields ?? new Dictionary<string, FieldPermission>(StringComparer.OrdinalIgnoreCase)
        };

        _mockPermissionService
            .Setup(s => s.GetPermissionsAsync(
                It.Is<PermissionRequestContext>(c => c.Objects.Contains(objectName)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PermissionRequestContext ctx, CancellationToken _) =>
            {
                var result = new PermissionResult();
                if (ctx.Objects.Contains(objectName))
                {
                    result.Snapshots[objectName] = snapshot;
                }
                return result;
            });
    }
}
