using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Authorization;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Layout;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Utilities;

// Use aliases to resolve ambiguity between Models.Configuration and Models.Layout
using LayoutFormSection = SalesforceCore.Models.Layout.FormSection;

namespace SalesforceCore.Services.Layout;

/// <summary>
/// Implementation of layout descriptor service.
/// Builds dynamic UI descriptors from metadata, permissions, and configuration.
/// </summary>
public class LayoutDescriptorService : ILayoutDescriptorService
{
    private readonly ISchemaService _schemaService;
    private readonly IPermissionService _permissionService;
    private readonly IVisibilityService _visibilityService;
    private readonly IUserContextProvider _userProvider;
    private readonly ICacheProvider _cacheProvider;
    private readonly IDynamicUiConfigProvider? _configProvider;
    private readonly ILogger<LayoutDescriptorService> _logger;
    private readonly DynamicUiOptions _options;
    private static readonly ConcurrentDictionary<string, byte> _knownLayoutCacheKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> _knownUserSuffixes = new(StringComparer.OrdinalIgnoreCase);

    private const string CacheKeyPrefix = "layout_";

    public LayoutDescriptorService(
        ISchemaService schemaService,
        IPermissionService permissionService,
        IVisibilityService visibilityService,
        IUserContextProvider userProvider,
        ICacheProvider cacheProvider,
        IOptions<DynamicUiOptions> options,
        IDynamicUiConfigProvider? configProvider,
        ILogger<LayoutDescriptorService> logger)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _visibilityService = visibilityService ?? throw new ArgumentNullException(nameof(visibilityService));
        _userProvider = userProvider ?? throw new ArgumentNullException(nameof(userProvider));
        _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        _configProvider = configProvider;
        _options = options?.Value ?? new DynamicUiOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<NavigationDescriptor> GetNavigationAsync(
        string? currentPath = null,
        CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var cacheKey = $"{CacheKeyPrefix}nav{GetUserCacheKeySuffix()}";
        RegisterLayoutCacheKey(cacheKey);

        if (options.BypassCache)
        {
            var descriptor = await BuildNavigationAsync(currentPath, cancellationToken);
            if (!string.IsNullOrEmpty(currentPath))
            {
                SetActiveState(descriptor, currentPath);
            }
            return descriptor;
        }

        var navDescriptor = await _cacheProvider.GetOrCreateAsync(
            cacheKey,
            async ct => await BuildNavigationAsync(null, ct),
            options.LayoutCacheDuration,
            cancellationToken);

        if (navDescriptor != null && !string.IsNullOrEmpty(currentPath))
        {
            SetActiveState(navDescriptor, currentPath);
        }

        return navDescriptor ?? new NavigationDescriptor();
    }

    /// <inheritdoc/>
    public async Task<FormDescriptor> GetFormAsync(
        string objectName,
        FormMode mode,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentNullException(nameof(objectName));
        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var options = GetOptions();
        var recordTypePart = string.IsNullOrWhiteSpace(recordTypeId) ? "default" : recordTypeId;
        var cacheKey = $"{CacheKeyPrefix}form_{objectName.ToLowerInvariant()}_{mode}_{recordTypePart}{GetUserCacheKeySuffix()}";
        RegisterLayoutCacheKey(cacheKey);

        if (options.BypassCache)
        {
            return await BuildFormDescriptorAsync(objectName, mode, recordTypeId, cancellationToken);
        }

        var descriptor = await _cacheProvider.GetOrCreateAsync(
            cacheKey,
            async ct => await BuildFormDescriptorAsync(objectName, mode, recordTypeId, ct),
            options.LayoutCacheDuration,
            cancellationToken);

        return descriptor ?? new FormDescriptor { ObjectName = objectName, Mode = mode };
    }

    /// <inheritdoc/>
    public async Task<ListDescriptor> GetListAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentNullException(nameof(objectName));
        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var options = GetOptions();
        var cacheKey = $"{CacheKeyPrefix}list_{objectName.ToLowerInvariant()}{GetUserCacheKeySuffix()}";
        RegisterLayoutCacheKey(cacheKey);

        if (options.BypassCache)
        {
            return await BuildListDescriptorAsync(objectName, cancellationToken);
        }

        var descriptor = await _cacheProvider.GetOrCreateAsync(
            cacheKey,
            async ct => await BuildListDescriptorAsync(objectName, ct),
            options.LayoutCacheDuration,
            cancellationToken);

        return descriptor ?? new ListDescriptor { ObjectName = objectName };
    }

    /// <inheritdoc/>
    public async Task<DetailDescriptor> GetDetailAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentNullException(nameof(objectName));
        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var options = GetOptions();
        var cacheKey = $"{CacheKeyPrefix}detail_{objectName.ToLowerInvariant()}{GetUserCacheKeySuffix()}";
        RegisterLayoutCacheKey(cacheKey);

        if (options.BypassCache)
        {
            return await BuildDetailDescriptorAsync(objectName, cancellationToken);
        }

        var descriptor = await _cacheProvider.GetOrCreateAsync(
            cacheKey,
            async ct => await BuildDetailDescriptorAsync(objectName, ct),
            options.LayoutCacheDuration,
            cancellationToken);

        return descriptor ?? new DetailDescriptor { ObjectName = objectName };
    }

    /// <inheritdoc/>
    public async Task<FieldDescriptor?> GetFieldDescriptorAsync(
        string objectName,
        string fieldName,
        FormMode mode,
        CancellationToken cancellationToken = default)
    {
        var fields = await _schemaService.GetFieldsAsync(objectName, cancellationToken);
        var field = fields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

        if (field == null)
            return null;

        var permissions = await _permissionService.GetPermissionsAsync(objectName, cancellationToken);

        return BuildFieldDescriptor(field, permissions, mode, GetOptions().GetObjectConfig(objectName), fields);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FormAction>> GetAvailableActionsAsync(
        string objectName,
        UiActionContext context,
        CancellationToken cancellationToken = default)
    {
        var options = GetOptions();
        var permissions = await _permissionService.GetPermissionsAsync(objectName, cancellationToken);
        var config = options.GetObjectConfig(objectName);
        var actions = new List<FormAction>();

        switch (context)
        {
            case UiActionContext.List:
                {
                    var allowed = permissions.CanCreate && config.EnableCreate;
                    if (allowed || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "create",
                            Label = "New",
                            ActionType = "create",
                            IsPrimary = true,
                            CssClass = "btn-primary",
                            IsEnabled = allowed
                        });
                    }
                }
                break;

            case UiActionContext.Detail:
                {
                    var canEdit = permissions.CanUpdate && config.EnableEdit;
                    if (canEdit || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "edit",
                            Label = "Edit",
                            ActionType = "edit",
                            IsPrimary = true,
                            IsEnabled = canEdit
                        });
                    }

                    var canDelete = permissions.CanDelete && config.EnableDelete;
                    if (canDelete || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "delete",
                            Label = "Delete",
                            ActionType = "delete",
                            CssClass = "btn-danger",
                            ConfirmationMessage = "Are you sure you want to delete this record?",
                            IsEnabled = canDelete
                        });
                    }
                }
                break;

            case UiActionContext.Form:
                actions.Add(new FormAction
                {
                    Id = "save",
                    Label = "Save",
                    Type = "submit",
                    ActionType = "save",
                    IsPrimary = true,
                    CssClass = "btn-primary"
                });
                actions.Add(new FormAction
                {
                    Id = "cancel",
                    Label = "Cancel",
                    Type = "button",
                    ActionType = "cancel",
                    CssClass = "btn-secondary"
                });
                break;

            case UiActionContext.RowAction:
                {
                    var canView = permissions.CanRead;
                    if (canView || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "view",
                            Label = "View",
                            ActionType = "view",
                            Order = 1,
                            IsEnabled = canView
                        });
                    }

                    var canEdit = permissions.CanUpdate && config.EnableEdit;
                    if (canEdit || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "edit",
                            Label = "Edit",
                            ActionType = "edit",
                            Order = 2,
                            IsEnabled = canEdit
                        });
                    }

                    var canDelete = permissions.CanDelete && config.EnableDelete;
                    if (canDelete || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "delete",
                            Label = "Delete",
                            ActionType = "delete",
                            Order = 3,
                            ConfirmationMessage = "Are you sure you want to delete this record?",
                            IsEnabled = canDelete
                        });
                    }
                }
                break;

            case UiActionContext.BulkAction:
                {
                    var canBulkEdit = permissions.CanUpdate && config.EnableEdit;
                    if (canBulkEdit || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "bulk_edit",
                            Label = "Bulk Edit",
                            ActionType = "bulk_edit",
                            Order = 1,
                            IsEnabled = canBulkEdit
                        });
                    }

                    var canBulkDelete = permissions.CanDelete && config.EnableDelete;
                    if (canBulkDelete || !options.HideUnauthorizedActions)
                    {
                        actions.Add(new FormAction
                        {
                            Id = "bulk_delete",
                            Label = "Bulk Delete",
                            ActionType = "bulk_delete",
                            Order = 2,
                            ConfirmationMessage = "Are you sure you want to delete selected records?",
                            IsEnabled = canBulkDelete
                        });
                    }
                }
                break;
        }

        // Append context-specific configured actions
        if (context == UiActionContext.Detail && config.Detail.Actions != null)
        {
            foreach (var actionConfig in config.Detail.Actions)
            {
                var action = CreateActionFromConfig(actionConfig);
                if (await ApplyActionVisibilityAsync(action, actionConfig, permissions, options))
                {
                    actions.Add(action);
                }
            }
        }

        if (context == UiActionContext.RowAction && config.List.RowActions != null)
        {
            foreach (var actionConfig in config.List.RowActions)
            {
                var action = CreateActionFromConfig(actionConfig);
                if (await ApplyActionVisibilityAsync(action, actionConfig, permissions, options))
                {
                    actions.Add(action);
                }
            }
        }

        if (context == UiActionContext.BulkAction && config.List.BulkActions != null)
        {
            foreach (var actionConfig in config.List.BulkActions)
            {
                var action = CreateActionFromConfig(actionConfig);
                if (await ApplyActionVisibilityAsync(action, actionConfig, permissions, options))
                {
                    actions.Add(action);
                }
            }
        }

        // Add custom actions from config
        if (config.CustomActions != null && context != UiActionContext.RowAction && context != UiActionContext.BulkAction)
        {
            foreach (var actionConfig in config.CustomActions)
            {
                var customAction = new FormAction
                {
                    Id = actionConfig.Id,
                    Label = actionConfig.Label,
                    ActionType = actionConfig.Type,
                    Icon = actionConfig.Icon,
                    IsPrimary = actionConfig.IsPrimary,
                    CssClass = actionConfig.CssClass,
                    ConfirmationMessage = actionConfig.ConfirmationMessage,
                    Order = actionConfig.Order
                };

                if (await ApplyActionVisibilityAsync(customAction, actionConfig, permissions, options))
                {
                    actions.Add(customAction);
                }
            }
        }

        return actions.OrderBy(a => a.Order).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RelatedListDescriptor>> GetRelatedListsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var childRelationships = await _schemaService.GetChildRelationshipsAsync(objectName, cancellationToken);
        var options = GetOptions();
        var config = options.GetObjectConfig(objectName);
        var relatedLists = new List<RelatedListDescriptor>();

        foreach (var relationship in childRelationships.Where(r => !r.DeprecatedAndHidden && !string.IsNullOrEmpty(r.RelationshipName)))
        {
            // Check if user can read the child object
            var canRead = await _permissionService.CanPerformActionAsync(relationship.ChildSObject, PermissionAction.Read, cancellationToken);
            if (!canRead)
                continue;

            var canCreate = await _permissionService.CanPerformActionAsync(relationship.ChildSObject, PermissionAction.Create, cancellationToken);

            var relatedList = new RelatedListDescriptor
            {
                RelationshipName = relationship.RelationshipName!,
                ChildObject = relationship.ChildSObject,
                Title = relationship.ChildSObject, // Will be replaced with label
                CanCreate = canCreate,
                MaxRecords = 5,
                Order = relatedLists.Count
            };

            // Get child object describe for label and default columns
            try
            {
                var childDescribe = await _schemaService.GetDescribeAsync(relationship.ChildSObject, cancellationToken);
                if (childDescribe != null)
                {
                    relatedList.Title = childDescribe.LabelPlural;

                    // Default columns - name field plus a few common fields
                    var nameField = await _schemaService.GetNameFieldAsync(relationship.ChildSObject, cancellationToken);
                    var accessibleFields = await _schemaService.GetAccessibleFieldsAsync(relationship.ChildSObject, cancellationToken);

                    var columns = new List<ColumnDescriptor>();
                    var order = 0;

                    // Add name field first
                    var nameFieldMeta = accessibleFields.FirstOrDefault(f => f.Name.Equals(nameField, StringComparison.OrdinalIgnoreCase));
                    if (nameFieldMeta != null)
                    {
                        columns.Add(new ColumnDescriptor
                        {
                            FieldName = nameFieldMeta.Name,
                            Header = nameFieldMeta.Label,
                            Type = nameFieldMeta.Type,
                            IsLink = true,
                            IsSortable = true,
                            Order = order++
                        });
                    }

                    // Add a few more common fields
                    var additionalFields = accessibleFields
                        .Where(f => f.Name != nameField && !f.DeprecatedAndHidden && !f.IsCompound)
                        .Take(3);

                    foreach (var field in additionalFields)
                    {
                        columns.Add(new ColumnDescriptor
                        {
                            FieldName = field.Name,
                            Header = field.Label,
                            Type = field.Type,
                            IsSortable = field.Sortable,
                            Order = order++
                        });
                    }

                    relatedList.Columns = columns;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get describe for child object {ChildObject}", relationship.ChildSObject);
            }

            relatedLists.Add(relatedList);
        }

        // Apply config overrides
        if (config.Detail.RelatedLists != null)
        {
            foreach (var configList in config.Detail.RelatedLists)
            {
                // Check Visibility Policy for Related List Override
                if (!string.IsNullOrEmpty(configList.VisibilityPolicy))
                {
                     if (!await _visibilityService.EvaluatePolicyAsync(configList.VisibilityPolicy))
                     {
                         // Remove the related list if it exists
                         relatedLists.RemoveAll(r => r.RelationshipName.Equals(configList.RelationshipName, StringComparison.OrdinalIgnoreCase));
                         continue;
                     }
                }

                var existing = relatedLists.FirstOrDefault(r =>
                    r.RelationshipName.Equals(configList.RelationshipName, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        if (!string.IsNullOrEmpty(configList.Title))
                            existing.Title = configList.Title;
                        existing.Order = configList.Order;
                        existing.MaxRecords = configList.MaxRecords;
                        existing.CanCreate = configList.ShowCreateButton && existing.CanCreate;

                        if (configList.Columns != null && configList.Columns.Any())
                        {
                            existing.Columns = configList.Columns
                                .Select(c => new ColumnDescriptor
                            {
                                FieldName = c.FieldName,
                                Header = c.Header ?? c.FieldName,
                                Width = c.Width,
                                IsSortable = c.IsSortable,
                                IsFilterable = c.IsFilterable,
                                IsLink = c.IsLink,
                                Order = c.Order,
                                Format = c.Format
                            })
                            .ToList();
                    }
                }
            }
        }

        return relatedLists.OrderBy(r => r.Order).ToList();
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(
        string? objectName = null,
        CancellationToken cancellationToken = default)
    {
        if (_configProvider != null)
        {
            await _configProvider.RefreshAsync(cancellationToken);
        }

        if (string.IsNullOrEmpty(objectName))
        {
            await _permissionService.InvalidateCacheAsync(null, cancellationToken);
            await _schemaService.InvalidateCacheAsync(null);
            foreach (var key in _knownLayoutCacheKeys.Keys.ToList())
            {
                await _cacheProvider.RemoveAsync(key, cancellationToken);
            }
            _logger.LogInformation("Refreshed all layout descriptors");
        }
        else
        {
            await _permissionService.InvalidateCacheAsync(objectName, cancellationToken);
            await _schemaService.InvalidateCacheAsync(objectName);

            // Clear specific layout caches for known users
            var suffixes = _knownUserSuffixes.Keys.ToList();
            var keys = new List<string>();
            foreach (var suffix in suffixes)
            {
                keys.Add($"{CacheKeyPrefix}list_{objectName.ToLowerInvariant()}{suffix}");
                keys.Add($"{CacheKeyPrefix}detail_{objectName.ToLowerInvariant()}{suffix}");
                // remove all form keys tracked
                foreach (var trackedKey in _knownLayoutCacheKeys.Keys.Where(k => k.Contains($"form_{objectName.ToLowerInvariant()}_", StringComparison.OrdinalIgnoreCase)))
                {
                    keys.Add(trackedKey);
                }
            }

            foreach (var key in keys)
            {
                await _cacheProvider.RemoveAsync(key, cancellationToken);
            }

            _logger.LogInformation("Refreshed layout descriptors for {ObjectName}", objectName);
        }
    }

    /// <inheritdoc/>
    public async Task<RecordTypeSelector?> GetRecordTypeSelectorAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var recordTypes = await _schemaService.GetRecordTypesAsync(objectName, cancellationToken);

        if (recordTypes == null || recordTypes.Count <= 1)
            return null;

        var availableTypes = recordTypes.Where(rt => rt.Available && !rt.Master).ToList();

        if (availableTypes.Count <= 1)
            return null;

        return new RecordTypeSelector
        {
            Options = availableTypes.Select(rt => new RecordTypeOption
            {
                Id = rt.RecordTypeId,
                Name = rt.Name,
                IsDefault = rt.DefaultRecordTypeMapping
            }).ToList(),
            DefaultId = availableTypes.FirstOrDefault(rt => rt.DefaultRecordTypeMapping)?.RecordTypeId,
            IsRequired = true,
            ShowSelector = true
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PicklistOption>> GetPicklistOptionsAsync(
        string objectName,
        string fieldName,
        string? controllingValue = null,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _schemaService.GetPicklistValuesAsync(objectName, fieldName, recordTypeId, cancellationToken);

        var options = result.Values
            .Where(v => v.Active)
            .Select(v => new PicklistOption
            {
                Value = v.Value,
                Label = v.Label,
                IsDefault = v.DefaultValue,
                IsActive = v.Active
            })
            .ToList();

        // Filter by controlling value for dependent picklists
        if (!string.IsNullOrEmpty(controllingValue) && result.IsDependentPicklist && result.DependencyMap.Count > 0)
        {
            if (result.DependencyMap.TryGetValue(controllingValue, out var validValues))
            {
                options = options.Where(o => validValues.Contains(o.Value)).ToList();
            }
        }

        return options;
    }

    #region Private Helper Methods

    private async Task<NavigationDescriptor> BuildNavigationAsync(
        string? currentPath,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building navigation descriptor");
        var options = GetOptions();

        var descriptor = new NavigationDescriptor
        {
            AppName = options.Navigation.AppName,
            LogoUrl = options.Navigation.LogoUrl
        };

        // Build navigation items from config
        if (options.Navigation.Items.Any())
        {
            foreach (var itemConfig in options.Navigation.Items.OrderBy(i => i.Order))
            {
                if (!itemConfig.IsEnabled)
                    continue;

                var item = await BuildNavigationItemAsync(itemConfig, cancellationToken);
                if (item != null && item.IsVisible)
                {
                    descriptor.MainItems.Add(item);
                }
            }
        }
        else if (options.Navigation.AutoGenerateFromObjects || options.Navigation.DefaultObjects.Any())
        {
            // Auto-generate from default objects
            var objects = options.Navigation.DefaultObjects;
            foreach (var objectName in objects)
            {
                if (options.Navigation.ExcludedObjects.Contains(objectName, StringComparer.OrdinalIgnoreCase))
                    continue;

                var canRead = await _permissionService.CanPerformActionAsync(objectName, PermissionAction.Read, cancellationToken);
                if (!canRead && options.HideInaccessibleNavItems)
                    continue;

                try
                {
                    var describe = await _schemaService.GetDescribeAsync(objectName, cancellationToken);
                    if (describe != null)
                    {
                        descriptor.MainItems.Add(new NavigationItem
                        {
                            Id = objectName.ToLowerInvariant(),
                            Label = describe.LabelPlural,
                            SObject = objectName,
                            Route = $"/sf/{objectName}",
                            IsVisible = true,
                            IsEnabled = canRead,
                            Order = descriptor.MainItems.Count
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get describe for navigation: {ObjectName}", objectName);
                }
            }
        }

        // Build utility items
        foreach (var itemConfig in options.Navigation.UtilityItems.OrderBy(i => i.Order))
        {
            if (!itemConfig.IsEnabled)
                continue;

            var item = await BuildNavigationItemAsync(itemConfig, cancellationToken);
            if (item != null)
            {
                descriptor.UtilityItems.Add(item);
            }
        }

        if (!string.IsNullOrEmpty(currentPath))
        {
            SetActiveState(descriptor, currentPath);
        }

        return descriptor;
    }

    private async Task<NavigationItem?> BuildNavigationItemAsync(
        NavigationItemConfig config,
        CancellationToken cancellationToken)
    {
        var options = GetOptions();
        var item = new NavigationItem
        {
            Id = config.Id,
            Label = config.Label,
            Icon = config.Icon,
            Route = config.Route,
            SObject = config.SObject,
            Order = config.Order,
            IsEnabled = config.IsEnabled,
            IsVisible = true,
            RequiredFeatures = config.RequiredFeatures ?? new List<string>()
        };

        // Check feature flags
        if (item.RequiredFeatures.Any())
        {
            foreach (var feature in item.RequiredFeatures)
            {
                if (!options.IsFeatureEnabled(feature))
                {
                    if (options.HideInaccessibleNavItems)
                        return null;
                    item.IsEnabled = false;
                    break;
                }
            }
        }

        // Check Visibility Policy
        if (!string.IsNullOrEmpty(config.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(config.VisibilityPolicy))
            {
                if (options.HideInaccessibleNavItems)
                    return null;
                
                item.IsEnabled = false;
                item.IsVisible = false;
            }
        }

        // Check object permission if specified
        if (!string.IsNullOrEmpty(config.SObject))
        {
            var action = config.RequiredPermission?.ToLowerInvariant() switch
            {
                "create" => PermissionAction.Create,
                "update" => PermissionAction.Update,
                "delete" => PermissionAction.Delete,
                _ => PermissionAction.Read
            };

            var canAccess = await _permissionService.CanPerformActionAsync(config.SObject, action, cancellationToken);
            if (!canAccess)
            {
                if (options.HideInaccessibleNavItems)
                    return null;
                item.IsEnabled = false;
            }

            item.RequiredPermission = new PermissionRequirement
            {
                ObjectName = config.SObject,
                Action = action.ToString()
            };
        }

        // Build children recursively
        if (config.Children != null && config.Children.Any())
        {
            foreach (var childConfig in config.Children.OrderBy(c => c.Order))
            {
                var child = await BuildNavigationItemAsync(childConfig, cancellationToken);
                if (child != null)
                {
                    item.Children.Add(child);
                }
            }

            // Hide parent if all children are hidden
            if (!item.Children.Any(c => c.IsVisible))
            {
                item.IsVisible = false;
            }
        }

        return item;
    }

    private async Task<FormDescriptor> BuildFormDescriptorAsync(
        string objectName,
        FormMode mode,
        string? recordTypeId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building form descriptor for {ObjectName} in {Mode} mode", objectName, mode);
        var options = GetOptions();

        var describe = await _schemaService.GetDescribeAsync(objectName, cancellationToken);
        if (describe == null)
            throw new InvalidOperationException($"Object '{objectName}' not found");

        var permissions = await _permissionService.GetPermissionsAsync(objectName, cancellationToken);
        var config = options.GetObjectConfig(objectName);

        // Object-level visibility
        if (!string.IsNullOrEmpty(config.VisibilityPolicy))
        {
            var isVisible = await _visibilityService.EvaluatePolicyAsync(config.VisibilityPolicy);
            if (!isVisible)
            {
                return new FormDescriptor
                {
                    ObjectName = objectName,
                    ObjectLabel = config.DisplayLabel ?? describe.Label,
                    Mode = mode,
                    Title = null,
                    IsVisible = false
                };
            }
        }

        var descriptor = new FormDescriptor
        {
            ObjectName = objectName,
            ObjectLabel = config.DisplayLabel ?? describe.Label,
            Mode = mode,
            RecordTypeId = recordTypeId,
            Title = mode switch
            {
                FormMode.Create => $"New {describe.Label}",
                FormMode.Edit => $"Edit {describe.Label}",
                FormMode.View => describe.Label,
                _ => describe.Label
            },
            Columns = config.Form.Columns > 0 ? config.Form.Columns : options.DefaultFormColumns,
            ShowValidationSummary = config.Form.ShowValidationSummary
        };

        // Build fields
        var fields = new List<FieldDescriptor>();
        var applicableFields = GetApplicableFields(describe.Fields, mode, permissions, config);

        foreach (var field in applicableFields)
        {
            var fieldDescriptor = BuildFieldDescriptor(field, permissions, mode, config, describe.Fields);
            if (fieldDescriptor != null)
            {
                // If record type is specified, filter picklist values based on record type
                if (!string.IsNullOrEmpty(recordTypeId) && field.IsPicklist)
                {
                    var picklistResult = await _schemaService.GetPicklistValuesAsync(objectName, field.Name, recordTypeId, cancellationToken);
                    fieldDescriptor.PicklistOptions = picklistResult.Values
                        .Where(v => v.Active)
                        .Select(v => new PicklistOption
                        {
                            Value = v.Value,
                            Label = v.Label,
                            IsDefault = v.DefaultValue,
                            IsActive = v.Active,
                            ValidForValues = picklistResult.DependencyMap.Count > 0
                                ? picklistResult.DependencyMap
                                    .Where(kvp => kvp.Value.Contains(v.Value, StringComparer.OrdinalIgnoreCase))
                                    .Select(kvp => kvp.Key)
                                    .ToList()
                                : null
                        })
                        .ToList();
                }
                fields.Add(fieldDescriptor);
            }
        }

        // Apply field configurations
        await ApplyFieldConfigsAsync(fields, config.Form.Fields);

        // Build sections if configured
        if (config.Form.Sections != null && config.Form.Sections.Any())
        {
            foreach (var sectionConfig in config.Form.Sections.OrderBy(s => s.Order))
            {
                // Check Visibility Policy for Section
                if (!string.IsNullOrEmpty(sectionConfig.VisibilityPolicy))
                {
                    if (!await _visibilityService.EvaluatePolicyAsync(sectionConfig.VisibilityPolicy))
                    {
                        continue; // Skip this section entirely
                    }
                }

                var section = new LayoutFormSection
                {
                    Id = sectionConfig.Id,
                    Heading = sectionConfig.Heading,
                    Columns = sectionConfig.Columns,
                    Order = sectionConfig.Order,
                    IsCollapsible = sectionConfig.IsCollapsible,
                    IsCollapsed = sectionConfig.IsCollapsed,
                    IsVisible = true
                };

                if (sectionConfig.Fields != null)
                {
                    section.Fields = fields
                        .Where(f => sectionConfig.Fields.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                await ApplyFieldConfigsAsync(section.Fields, sectionConfig.FieldConfigs);

                descriptor.Sections.Add(section);
            }

            // Fields not in any section
            var sectionFields = descriptor.Sections.SelectMany(s => s.Fields.Select(f => f.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var orphanFields = fields.Where(f => !sectionFields.Contains(f.Name)).ToList();

            if (orphanFields.Any())
            {
                descriptor.Sections.Add(new LayoutFormSection
                {
                    Id = "default",
                    Fields = orphanFields,
                    Order = int.MaxValue
                });
            }
        }
        else
        {
            // Single section with all fields
            descriptor.Sections.Add(new LayoutFormSection
            {
                Id = "default",
                Fields = fields.OrderBy(f => f.Order).ToList(),
                Columns = descriptor.Columns
            });
        }

        descriptor.Fields = fields.OrderBy(f => f.Order).ToList();

        // Add actions
        descriptor.Actions = (await GetAvailableActionsAsync(objectName, UiActionContext.Form, cancellationToken)).ToList();

        // Add record type selector
        if (mode == FormMode.Create)
        {
            descriptor.RecordTypeSelector = await GetRecordTypeSelectorAsync(objectName, cancellationToken);
        }

        return descriptor;
    }

    private async Task<ListDescriptor> BuildListDescriptorAsync(
        string objectName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building list descriptor for {ObjectName}", objectName);

        var describe = await _schemaService.GetDescribeAsync(objectName, cancellationToken);
        if (describe == null)
            throw new InvalidOperationException($"Object '{objectName}' not found");

        var permissions = await _permissionService.GetPermissionsAsync(objectName, cancellationToken);
        var options = GetOptions();
        var config = options.GetObjectConfig(objectName);
        if (!string.IsNullOrEmpty(config.VisibilityPolicy))
        {
            var isVisible = await _visibilityService.EvaluatePolicyAsync(config.VisibilityPolicy);
            if (!isVisible)
            {
                return new ListDescriptor
                {
                    ObjectName = objectName,
                    ObjectLabel = config.DisplayLabel ?? describe.LabelPlural,
                    Title = config.DisplayLabel ?? describe.LabelPlural,
                    IsVisible = false
                };
            }
        }
        var nameField = await _schemaService.GetNameFieldAsync(objectName, cancellationToken);

        var descriptor = new ListDescriptor
        {
            ObjectName = objectName,
            ObjectLabel = config.DisplayLabel ?? describe.LabelPlural,
            Title = config.DisplayLabel ?? describe.LabelPlural,
            EnableSearch = config.List.EnableSearch,
            EnableFilters = config.List.EnableFilters,
            EnableSelection = config.List.EnableSelection,
            EnableExport = config.List.EnableExport,
            PageSize = config.List.PageSize ?? options.DefaultPageSize,
            DefaultSortField = config.List.DefaultSortField ?? nameField,
            DefaultSortDirection = config.List.DefaultSortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? SortDirection.Descending
                : SortDirection.Ascending
        };

        // Build columns
        if (config.List.Columns != null && config.List.Columns.Any())
        {
            foreach (var colConfig in config.List.Columns.OrderBy(c => c.Order))
            {
                var field = describe.Fields.FirstOrDefault(f => f.Name.Equals(colConfig.FieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null || !field.Accessible)
                    continue;

                descriptor.Columns.Add(new ColumnDescriptor
                {
                    FieldName = field.Name,
                    Header = colConfig.Header ?? field.Label,
                    Type = field.Type,
                    Width = colConfig.Width,
                    IsSortable = colConfig.IsSortable && field.Sortable,
                    IsFilterable = colConfig.IsFilterable && field.Filterable,
                    IsLink = colConfig.IsLink || field.Name.Equals(nameField, StringComparison.OrdinalIgnoreCase),
                    Order = colConfig.Order,
                    Format = colConfig.Format
                });
            }
        }
        else
        {
            // Default columns
            var accessibleFields = describe.Fields
                .Where(f => f.Accessible && !f.DeprecatedAndHidden && !f.IsCompound)
                .ToList();

            var order = 0;

            // Name field first
            var nameFieldMeta = accessibleFields.FirstOrDefault(f => f.Name.Equals(nameField, StringComparison.OrdinalIgnoreCase));
            if (nameFieldMeta != null)
            {
                descriptor.Columns.Add(new ColumnDescriptor
                {
                    FieldName = nameFieldMeta.Name,
                    Header = nameFieldMeta.Label,
                    Type = nameFieldMeta.Type,
                    IsSortable = nameFieldMeta.Sortable,
                    IsFilterable = nameFieldMeta.Filterable,
                    IsLink = true,
                    Order = order++
                });
            }

            // Add common fields
            var defaultFields = new[] { "CreatedDate", "LastModifiedDate", "Owner.Name", "RecordType.Name" };
            foreach (var fieldName in defaultFields)
            {
                var field = accessibleFields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                {
                    descriptor.Columns.Add(new ColumnDescriptor
                    {
                        FieldName = field.Name,
                        Header = field.Label,
                        Type = field.Type,
                        IsSortable = field.Sortable,
                        IsFilterable = field.Filterable,
                        Order = order++
                    });
                }
            }

            // Add a few more accessible fields
            var remainingFields = accessibleFields
                .Where(f => !descriptor.Columns.Any(c => c.FieldName.Equals(f.Name, StringComparison.OrdinalIgnoreCase)))
                .Where(f => !f.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))
                .Take(3);

            foreach (var field in remainingFields)
            {
                descriptor.Columns.Add(new ColumnDescriptor
                {
                    FieldName = field.Name,
                    Header = field.Label,
                    Type = field.Type,
                    IsSortable = field.Sortable,
                    IsFilterable = field.Filterable,
                    Order = order++
                });
            }
        }

        // Add row actions
        descriptor.RowActions = (await GetAvailableActionsAsync(objectName, UiActionContext.RowAction, cancellationToken)).ToList();

        // Add bulk actions
        if (config.List.EnableSelection)
        {
            descriptor.BulkActions = (await GetAvailableActionsAsync(objectName, UiActionContext.BulkAction, cancellationToken)).ToList();
        }

        return descriptor;
    }

    private async Task<DetailDescriptor> BuildDetailDescriptorAsync(
        string objectName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building detail descriptor for {ObjectName}", objectName);

        var formDescriptor = await BuildFormDescriptorAsync(objectName, FormMode.View, null, cancellationToken);

        var descriptor = new DetailDescriptor
        {
            ObjectName = formDescriptor.ObjectName,
            ObjectLabel = formDescriptor.ObjectLabel,
            Title = formDescriptor.Title,
            Sections = formDescriptor.Sections,
            CssClass = formDescriptor.CssClass,
            IsVisible = formDescriptor.IsVisible
        };

        if (!descriptor.IsVisible)
        {
            descriptor.RelatedLists = new List<RelatedListDescriptor>();
            descriptor.Actions = new List<FormAction>();
            return descriptor;
        }

        // Add related lists
        descriptor.RelatedLists = (await GetRelatedListsAsync(objectName, cancellationToken)).ToList();

        // Add actions
        descriptor.Actions = (await GetAvailableActionsAsync(objectName, UiActionContext.Detail, cancellationToken)).ToList();

        return descriptor;
    }

    private IEnumerable<SObjectField> GetApplicableFields(
        IEnumerable<SObjectField> allFields,
        FormMode mode,
        ObjectPermissionSnapshot permissions,
        ObjectUiConfig config)
    {
        var includeSet = config.IncludeFields?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excludeSet = config.ExcludeFields?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always exclude these system fields
        excludeSet.Add("Id");

        foreach (var field in allFields)
        {
            // Skip deprecated fields
            if (field.DeprecatedAndHidden)
                continue;

            // Skip compound fields (address, geolocation)
            if (field.IsCompound)
                continue;

            // Skip excluded fields
            if (excludeSet.Contains(field.Name))
                continue;

            // If include list is specified, skip fields not in it
            if (includeSet != null && !includeSet.Contains(field.Name))
                continue;

            // Check field permissions
            if (!permissions.FieldPermissions.TryGetValue(field.Name, out var fieldPerm))
                continue;

            switch (mode)
            {
                case FormMode.Create:
                    if (!fieldPerm.CanCreate && !fieldPerm.CanRead)
                        continue;
                    break;

                case FormMode.Edit:
                    if (!fieldPerm.CanRead)
                        continue;
                    break;

                case FormMode.View:
                    if (!fieldPerm.CanRead)
                        continue;
                    break;
            }

            yield return field;
        }
    }

    private FieldDescriptor? BuildFieldDescriptor(
        SObjectField field,
        ObjectPermissionSnapshot permissions,
        FormMode mode,
        ObjectUiConfig config,
        IEnumerable<SObjectField> allFields)
    {
        if (!permissions.FieldPermissions.TryGetValue(field.Name, out var fieldPerm))
            return null;

        var isReadOnly = mode switch
        {
            FormMode.Create => !fieldPerm.CanCreate,
            FormMode.Edit => !fieldPerm.CanUpdate,
            FormMode.View => true,
            _ => true
        };

        var descriptor = new FieldDescriptor
        {
            Name = field.Name,
            Label = field.Label,
            Type = field.Type,
            IsRequired = field.IsRequired && !isReadOnly,
            IsReadOnly = isReadOnly,
            IsVisible = true,
            MaxLength = field.Length > 0 ? field.Length : null,
            HelpText = field.InlineHelpText,
            DefaultValue = field.DefaultValue,
            ControllingField = field.ControllerName,
            ControlType = GetControlType(field),
            InputType = GetInputType(field)
        };

        // Numeric constraints
        if (field.Type.Equals("double", StringComparison.OrdinalIgnoreCase) ||
            field.Type.Equals("currency", StringComparison.OrdinalIgnoreCase) ||
            field.Type.Equals("percent", StringComparison.OrdinalIgnoreCase))
        {
            descriptor.Step = field.Scale > 0 ? (decimal)Math.Pow(10, -field.Scale) : 1;
        }

        // Picklist options
        if (field.IsPicklist && field.PicklistValues != null)
        {
            List<string>? controllingValues = null;
            if (field.DependentPicklist && !string.IsNullOrEmpty(field.ControllerName))
            {
                var controllerField = allFields.FirstOrDefault(f => f.Name.Equals(field.ControllerName, StringComparison.OrdinalIgnoreCase));
                if (controllerField?.PicklistValues != null)
                {
                    controllingValues = controllerField.PicklistValues.Select(p => p.Value).ToList();
                }
            }

            descriptor.PicklistOptions = field.PicklistValues
                .Where(v => v.Active)
                .Select(v => new PicklistOption
                {
                    Value = v.Value,
                    Label = v.Label,
                    IsDefault = v.DefaultValue,
                    IsActive = v.Active,
                    ValidForValues = (!string.IsNullOrEmpty(v.ValidFor) && controllingValues != null)
                        ? DecodeValidFor(v.ValidFor, controllingValues)
                        : null
                })
                .ToList();
        }

        // Lookup config
        if (field.IsLookup && field.ReferenceTo != null && field.ReferenceTo.Any())
        {
            descriptor.LookupConfig = new LookupConfig
            {
                TargetObjects = field.ReferenceTo.ToList(),
                IsPolymorphic = field.PolymorphicForeignKey || field.ReferenceTo.Count > 1,
                DisplayField = "Name",
                MinChars = 2,
                DebounceMs = 300,
                SearchUrl = "/sf/lookup/search"
            };
        }

        return descriptor;
    }

    private static FormAction CreateActionFromConfig(ActionConfig actionConfig)
    {
        return new FormAction
        {
            Id = actionConfig.Id,
            Label = actionConfig.Label,
            ActionType = actionConfig.Type,
            Icon = actionConfig.Icon,
            IsPrimary = actionConfig.IsPrimary,
            CssClass = actionConfig.CssClass,
            ConfirmationMessage = actionConfig.ConfirmationMessage,
            Order = actionConfig.Order,
            Route = actionConfig.Route
        };
    }

    private async Task<bool> ApplyActionVisibilityAsync(
        FormAction action,
        ActionConfig config,
        ObjectPermissionSnapshot permissions,
        DynamicUiOptions options)
    {
        if (!string.IsNullOrEmpty(config.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(config.VisibilityPolicy))
            {
                if (options.HideUnauthorizedActions)
                    return false;
                action.IsEnabled = false;
            }
        }

        if (!string.IsNullOrEmpty(config.RequiredPermission))
        {
            var allowed = config.RequiredPermission.ToLowerInvariant() switch
            {
                "create" => permissions.CanCreate,
                "read" => permissions.CanRead,
                "update" => permissions.CanUpdate,
                "delete" => permissions.CanDelete,
                _ => true
            };

            if (options.HideUnauthorizedActions && !allowed)
                return false;

            action.IsEnabled = allowed;
        }

        return true;
    }

    private async Task ApplyFieldConfigsAsync(List<FieldDescriptor> fields, List<FieldConfig>? configs)
    {
        if (configs == null || !configs.Any())
            return;

        var options = GetOptions();
        foreach (var config in configs)
        {
            var field = fields.FirstOrDefault(f => f.Name.Equals(config.FieldName, StringComparison.OrdinalIgnoreCase));
            if (field == null)
                continue;

            // Check Visibility Policy
            if (!string.IsNullOrEmpty(config.VisibilityPolicy))
            {
                bool isPolicyVisible = await _visibilityService.EvaluatePolicyAsync(config.VisibilityPolicy);
                if (!isPolicyVisible)
                {
                    field.IsVisible = false;
                    // If hidden by policy, we might want to skip other updates or explicitly hide it
                    if (options.HideInaccessibleFields)
                    {
                        field.IsHidden = true;
                        field.IsReadOnly = true;
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.Label))
                field.Label = config.Label;
            if (!string.IsNullOrEmpty(config.Placeholder))
                field.Placeholder = config.Placeholder;
            if (!string.IsNullOrEmpty(config.HelpText))
                field.HelpText = config.HelpText;
            if (config.Order > 0)
                field.Order = config.Order;
            if (config.ColumnSpan > 0)
                field.ColumnSpan = config.ColumnSpan;
            if (config.IsReadOnly.HasValue)
                field.IsReadOnly = config.IsReadOnly.Value;
            if (config.IsHidden.HasValue)
                field.IsHidden = config.IsHidden.Value;
            if (config.IsRequired.HasValue)
                field.IsRequired = config.IsRequired.Value;
            if (!string.IsNullOrEmpty(config.CssClass))
                field.CssClass = config.CssClass;
            if (!string.IsNullOrEmpty(config.ControlType))
                field.ControlType = config.ControlType;
            if (!string.IsNullOrEmpty(config.ValidationPattern))
                field.ValidationPattern = config.ValidationPattern;
            if (!string.IsNullOrEmpty(config.ValidationMessage))
                field.ValidationMessage = config.ValidationMessage;
        }
    }

    private static string GetControlType(SObjectField field)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "boolean" => "checkbox",
            "picklist" => "select",
            "multipicklist" => "multiselect",
            "reference" => "lookup",
            "textarea" => "textarea",
            "date" => "date",
            "datetime" => "datetime",
            "time" => "time",
            "email" => "input",
            "phone" => "input",
            "url" => "input",
            "int" or "integer" => "input",
            "double" or "currency" or "percent" => "input",
            "base64" => "file",
            _ => "input"
        };
    }

    private static string GetInputType(SObjectField field)
    {
        return field.Type.ToLowerInvariant() switch
        {
            "email" => "email",
            "phone" => "tel",
            "url" => "url",
            "int" or "integer" => "number",
            "double" or "currency" or "percent" => "number",
            "date" => "date",
            "datetime" => "datetime-local",
            "time" => "time",
            _ => "text"
        };
    }

    private static List<string>? DecodeValidFor(string validFor, IReadOnlyList<string> controllingValues)
    {
        // ValidFor is a base64-encoded bitfield indicating which controlling values this option is valid for.
        // We map bits back to controlling picklist values to make filtering straightforward.
        var validIndices = BitmaskUtils.DecodeValidForBitmap(validFor);
        var result = new List<string>();
        foreach (var index in validIndices)
        {
            if (index < controllingValues.Count)
            {
                result.Add(controllingValues[index]);
            }
        }
        return result;
    }

    private static void SetActiveState(NavigationDescriptor descriptor, string currentPath)
    {
        foreach (var item in descriptor.MainItems)
        {
            SetActiveStateRecursive(item, currentPath);
        }
    }

    private static bool SetActiveStateRecursive(NavigationItem item, string currentPath)
    {
        if (!string.IsNullOrEmpty(item.Route) &&
            currentPath.StartsWith(item.Route, StringComparison.OrdinalIgnoreCase))
        {
            item.IsActive = true;
            return true;
        }

        foreach (var child in item.Children)
        {
            if (SetActiveStateRecursive(child, currentPath))
            {
                item.IsActive = true;
                return true;
            }
        }

        return false;
    }

    private DynamicUiOptions GetOptions() => _configProvider?.Current ?? _options;

    private void RegisterLayoutCacheKey(string cacheKey)
    {
        _knownLayoutCacheKeys.TryAdd(cacheKey, 0);
    }

    private string GetUserCacheKeySuffix()
    {
        var user = _userProvider.GetUser();
        if (user?.Identity?.IsAuthenticated != true)
        {
            return "_anon";
        }
        // Use Name or Subject claim as unique identifier
        var id = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? user.FindFirst("sub")?.Value
                 ?? user.Identity.Name
                 ?? "_unknown";
        var suffix = $"_{id}";
        _knownUserSuffixes.TryAdd(suffix, 0);
        return suffix;
    }

    #endregion
}
