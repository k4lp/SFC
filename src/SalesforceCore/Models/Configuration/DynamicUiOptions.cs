using SalesforceCore.Models.Layout;

namespace SalesforceCore.Models.Configuration;

/// <summary>
/// Configuration options for the Dynamic UI system.
/// All dynamic UI features can be configured via this options class,
/// either in appsettings.json or programmatically.
/// </summary>
public class DynamicUiOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "DynamicUi";

    /// <summary>
    /// Path to the dynamic UI configuration JSON file.
    /// If null, configuration must be provided programmatically.
    /// Default: "dynamic_ui.json"
    /// </summary>
    public string? ConfigFilePath { get; set; } = "dynamic_ui.json";

    /// <summary>
    /// Whether to watch the config file for changes and reload automatically.
    /// Default: true
    /// </summary>
    public bool WatchConfigFile { get; set; } = true;

    /// <summary>
    /// Cache duration for permission snapshots.
    /// Default: 5 minutes
    /// </summary>
    public TimeSpan PermissionCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cache duration for layout descriptors.
    /// Default: 10 minutes
    /// </summary>
    public TimeSpan LayoutCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether to bypass caching (useful for development).
    /// Default: false
    /// </summary>
    public bool BypassCache { get; set; } = false;

    /// <summary>
    /// Timeout for permission-related API calls (getting object/field permissions).
    /// If a permission fetch exceeds this timeout, the system uses PermissionFallbackMode.
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan PermissionTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Fallback behavior when permission fetch times out.
    /// Default: DenyAll (fail secure)
    /// </summary>
    public PermissionFallbackMode PermissionFallbackMode { get; set; } = PermissionFallbackMode.DenyAll;

    /// <summary>
    /// Whether to hide navigation items the user cannot access.
    /// If false, items are shown but disabled.
    /// Default: true
    /// </summary>
    public bool HideInaccessibleNavItems { get; set; } = true;

    /// <summary>
    /// Whether to hide form fields the user cannot see.
    /// If false, fields are shown but marked as hidden.
    /// Default: true
    /// </summary>
    public bool HideInaccessibleFields { get; set; } = true;

    /// <summary>
    /// Whether to hide create/edit buttons if user lacks permissions.
    /// Default: true
    /// </summary>
    public bool HideUnauthorizedActions { get; set; } = true;

    /// <summary>
    /// Default number of columns for forms.
    /// Default: 1
    /// </summary>
    public int DefaultFormColumns { get; set; } = 1;

    /// <summary>
    /// Default page size for list views.
    /// Default: 25
    /// </summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>
    /// Maximum page size for list views.
    /// Default: 100
    /// </summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>
    /// Navigation configuration.
    /// </summary>
    public NavigationConfig Navigation { get; set; } = new();

    /// <summary>
    /// Object-specific configurations.
    /// </summary>
    public Dictionary<string, ObjectUiConfig> Objects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Theming configuration.
    /// </summary>
    public ThemingConfig Theming { get; set; } = new();

    /// <summary>
    /// Feature flags for enabling/disabling UI features.
    /// </summary>
    public Dictionary<string, bool> FeatureFlags { get; set; } = new();

    /// <summary>
    /// Gets the configuration for a specific object, or creates a default.
    /// </summary>
    public ObjectUiConfig GetObjectConfig(string objectName)
    {
        if (Objects.TryGetValue(objectName, out var config))
            return config;

        return new ObjectUiConfig { ObjectName = objectName };
    }

    /// <summary>
    /// Checks if a feature flag is enabled.
    /// </summary>
    public bool IsFeatureEnabled(string featureName)
    {
        return FeatureFlags.TryGetValue(featureName, out var enabled) && enabled;
    }
}

/// <summary>
/// Fallback behavior when permission API calls time out.
/// </summary>
public enum PermissionFallbackMode
{
    /// <summary>
    /// Deny all access on timeout (fail secure). This is the default and most secure option.
    /// </summary>
    DenyAll,

    /// <summary>
    /// Allow read-only access on timeout. Use with caution in non-critical applications.
    /// </summary>
    AllowReadOnly,

    /// <summary>
    /// Use cached permissions if available, otherwise deny all access.
    /// Provides graceful degradation for temporary network issues.
    /// </summary>
    UseCachedOrDeny
}

/// <summary>
/// Navigation configuration.
/// </summary>
public class NavigationConfig
{
    /// <summary>
    /// Application name shown in navigation.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Logo URL.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Navigation items configuration.
    /// </summary>
    public List<NavigationItemConfig> Items { get; set; } = new();

    /// <summary>
    /// Utility items (user menu, settings, etc.).
    /// </summary>
    public List<NavigationItemConfig> UtilityItems { get; set; } = new();

    /// <summary>
    /// Default navigation items to include if no config is provided.
    /// These are generated from Salesforce objects.
    /// </summary>
    public List<string> DefaultObjects { get; set; } = new() { "Account", "Contact", "Lead", "Opportunity" };

    /// <summary>
    /// Whether to auto-generate navigation from accessible objects.
    /// Default: false
    /// </summary>
    public bool AutoGenerateFromObjects { get; set; } = false;

    /// <summary>
    /// Objects to exclude from auto-generation.
    /// </summary>
    public List<string> ExcludedObjects { get; set; } = new();
}

/// <summary>
/// Configuration for a navigation item.
/// </summary>
public class NavigationItemConfig
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Icon identifier.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Route or URL.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Associated Salesforce object.
    /// </summary>
    public string? SObject { get; set; }

    /// <summary>
    /// Required permission action.
    /// </summary>
    public string? RequiredPermission { get; set; }

    /// <summary>
    /// The name of a visibility policy to apply to this item.
    /// If set, the policy must evaluate to true for the item to be visible.
    /// </summary>
    public string? VisibilityPolicy { get; set; }

    /// <summary>
    /// Required feature flags.
    /// </summary>
    public List<string>? RequiredFeatures { get; set; }

    /// <summary>
    /// Child items.
    /// </summary>
    public List<NavigationItemConfig>? Children { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this item is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// UI configuration for a specific object.
/// </summary>
public class ObjectUiConfig
{
    /// <summary>
    /// Object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Custom display label (overrides Salesforce label).
    /// </summary>
    public string? DisplayLabel { get; set; }

    /// <summary>
    /// List view configuration.
    /// </summary>
    public ListViewConfig List { get; set; } = new();

    /// <summary>
    /// Detail view configuration.
    /// </summary>
    public DetailViewConfig Detail { get; set; } = new();

    /// <summary>
    /// Form configuration (for create/edit).
    /// </summary>
    public FormViewConfig Form { get; set; } = new();

    /// <summary>
    /// Fields to always include.
    /// </summary>
    public List<string>? IncludeFields { get; set; }

    /// <summary>
    /// Fields to always exclude.
    /// </summary>
    public List<string>? ExcludeFields { get; set; }

    /// <summary>
    /// Whether create is enabled (subject to permissions).
    /// </summary>
    public bool EnableCreate { get; set; } = true;

    /// <summary>
    /// Whether edit is enabled (subject to permissions).
    /// </summary>
    public bool EnableEdit { get; set; } = true;

    /// <summary>
    /// Whether delete is enabled (subject to permissions).
    /// </summary>
    public bool EnableDelete { get; set; } = true;

    /// <summary>
    /// Custom actions for this object.
    /// </summary>
    public List<ActionConfig>? CustomActions { get; set; }

    /// <summary>
    /// The name of a visibility policy to apply to this object's views.
    /// </summary>
    public string? VisibilityPolicy { get; set; }
}

/// <summary>
/// List view configuration.
/// </summary>
public class ListViewConfig
{
    /// <summary>
    /// Columns to display.
    /// </summary>
    public List<ColumnConfig>? Columns { get; set; }

    /// <summary>
    /// Default sort field.
    /// </summary>
    public string? DefaultSortField { get; set; }

    /// <summary>
    /// Default sort direction (asc/desc).
    /// </summary>
    public string DefaultSortDirection { get; set; } = "asc";

    /// <summary>
    /// Default page size.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Whether to enable search.
    /// </summary>
    public bool EnableSearch { get; set; } = true;

    /// <summary>
    /// Whether to enable filters.
    /// </summary>
    public bool EnableFilters { get; set; } = true;

    /// <summary>
    /// Whether to enable row selection.
    /// </summary>
    public bool EnableSelection { get; set; } = false;

    /// <summary>
    /// Whether to enable export.
    /// </summary>
    public bool EnableExport { get; set; } = false;

    /// <summary>
    /// Row actions.
    /// </summary>
    public List<ActionConfig>? RowActions { get; set; }

    /// <summary>
    /// Bulk actions.
    /// </summary>
    public List<ActionConfig>? BulkActions { get; set; }
}

/// <summary>
/// Column configuration.
/// </summary>
public class ColumnConfig
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Custom header text.
    /// </summary>
    public string? Header { get; set; }

    /// <summary>
    /// Column width.
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// Whether sortable.
    /// </summary>
    public bool IsSortable { get; set; } = true;

    /// <summary>
    /// Whether filterable.
    /// </summary>
    public bool IsFilterable { get; set; } = true;

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this is a link to the record.
    /// </summary>
    public bool IsLink { get; set; }

    /// <summary>
    /// Custom format string.
    /// </summary>
    public string? Format { get; set; }
}

/// <summary>
/// Detail view configuration.
/// </summary>
public class DetailViewConfig
{
    /// <summary>
    /// Sections configuration.
    /// </summary>
    public List<SectionConfig>? Sections { get; set; }

    /// <summary>
    /// Related lists to display.
    /// </summary>
    public List<DynamicRelatedListConfig>? RelatedLists { get; set; }

    /// <summary>
    /// Number of columns.
    /// </summary>
    public int Columns { get; set; } = 2;

    /// <summary>
    /// Actions available.
    /// </summary>
    public List<ActionConfig>? Actions { get; set; }
}

/// <summary>
/// Form view configuration.
/// </summary>
public class FormViewConfig
{
    /// <summary>
    /// Sections configuration.
    /// </summary>
    public List<SectionConfig>? Sections { get; set; }

    /// <summary>
    /// Number of columns.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Field configurations.
    /// </summary>
    public List<FieldConfig>? Fields { get; set; }

    /// <summary>
    /// Field order (for fields not in sections).
    /// </summary>
    public List<string>? FieldOrder { get; set; }

    /// <summary>
    /// Whether to show validation summary.
    /// </summary>
    public bool ShowValidationSummary { get; set; } = true;
}

/// <summary>
/// Section configuration.
/// </summary>
public class SectionConfig
{
    /// <summary>
    /// Section identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Section heading.
    /// </summary>
    public string? Heading { get; set; }

    /// <summary>
    /// Fields in this section.
    /// </summary>
    public List<string>? Fields { get; set; }

    /// <summary>
    /// Field configurations for this section.
    /// </summary>
    public List<FieldConfig>? FieldConfigs { get; set; }

    /// <summary>
    /// Number of columns in section.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether collapsible.
    /// </summary>
    public bool IsCollapsible { get; set; }

    /// <summary>
    /// Whether initially collapsed.
    /// </summary>
    public bool IsCollapsed { get; set; }

    /// <summary>
    /// The name of a visibility policy to apply to this section.
    /// </summary>
    public string? VisibilityPolicy { get; set; }
}

/// <summary>
/// Field configuration.
/// </summary>
public class FieldConfig
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Custom label.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Custom placeholder.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Custom help text.
    /// </summary>
    public string? HelpText { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Column span.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>
    /// Override for read-only status.
    /// </summary>
    public bool? IsReadOnly { get; set; }

    /// <summary>
    /// Override for hidden status.
    /// </summary>
    public bool? IsHidden { get; set; }

    /// <summary>
    /// Override for required status.
    /// </summary>
    public bool? IsRequired { get; set; }

    /// <summary>
    /// The name of a visibility policy to apply to this field.
    /// </summary>
    public string? VisibilityPolicy { get; set; }

    /// <summary>
    /// Custom CSS class.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Custom control type override.
    /// </summary>
    public string? ControlType { get; set; }

    /// <summary>
    /// Custom validation pattern.
    /// </summary>
    public string? ValidationPattern { get; set; }

    /// <summary>
    /// Custom validation message.
    /// </summary>
    public string? ValidationMessage { get; set; }
}

/// <summary>
/// Related list configuration for Dynamic UI views.
/// </summary>
public class DynamicRelatedListConfig
{
    /// <summary>
    /// Relationship name.
    /// </summary>
    public string RelationshipName { get; set; } = string.Empty;

    /// <summary>
    /// Custom title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Columns to display.
    /// </summary>
    public List<ColumnConfig>? Columns { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Maximum records to show.
    /// </summary>
    public int MaxRecords { get; set; } = 5;

    /// <summary>
    /// Whether to show a create button.
    /// </summary>
    public bool ShowCreateButton { get; set; } = true;

    /// <summary>
    /// The name of a visibility policy to apply to this related list.
    /// </summary>
    public string? VisibilityPolicy { get; set; }
}

/// <summary>
/// Action configuration.
/// </summary>
public class ActionConfig
{
    /// <summary>
    /// Action identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Button label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Action type (save, cancel, delete, custom).
    /// </summary>
    public string Type { get; set; } = "custom";

    /// <summary>
    /// Icon identifier.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Whether primary action.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Required permission action.
    /// </summary>
    public string? RequiredPermission { get; set; }

    /// <summary>
    /// The name of a visibility policy to apply to this item.
    /// If set, the policy must evaluate to true for the item to be visible.
    /// </summary>
    public string? VisibilityPolicy { get; set; }

    /// <summary>
    /// CSS class.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Confirmation message.
    /// </summary>
    public string? ConfirmationMessage { get; set; }

    /// <summary>
    /// Custom route/URL.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }
}

/// <summary>
/// Theming configuration.
/// </summary>
public class ThemingConfig
{
    /// <summary>
    /// Custom CSS file path(s).
    /// </summary>
    public List<string> CssFiles { get; set; } = new();

    /// <summary>
    /// Custom JavaScript file path(s).
    /// </summary>
    public List<string> JsFiles { get; set; } = new();

    /// <summary>
    /// Whether to use embedded default CSS.
    /// </summary>
    public bool UseDefaultCss { get; set; } = true;

    /// <summary>
    /// Whether to use embedded default JS.
    /// </summary>
    public bool UseDefaultJs { get; set; } = true;

    /// <summary>
    /// CSS framework to use (Bootstrap5, SLDS, Custom, None).
    /// </summary>
    public string CssFramework { get; set; } = "Bootstrap5";

    /// <summary>
    /// Custom CSS class prefix.
    /// </summary>
    public string? ClassPrefix { get; set; }

    /// <summary>
    /// Color scheme variables.
    /// </summary>
    public Dictionary<string, string> ColorScheme { get; set; } = new();
}
