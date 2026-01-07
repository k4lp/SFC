using System.Text.Json;
using System.Text.Json.Serialization;

namespace SalesforceCore.Models.Configuration;

/// <summary>
/// Root configuration object containing all module definitions.
/// Loaded from salesforce_config.json.
/// </summary>
public class SalesforceConfig
{
    /// <summary>
    /// Global settings that apply to all modules.
    /// </summary>
    [JsonPropertyName("globalSettings")]
    public GlobalSettings GlobalSettings { get; set; } = new();

    /// <summary>
    /// List of configured modules (SObjects).
    /// </summary>
    [JsonPropertyName("modules")]
    public List<ModuleConfig> Modules { get; set; } = new();
}

/// <summary>
/// Global settings that apply to all modules.
/// </summary>
public class GlobalSettings
{
    /// <summary>
    /// Default number of results in lookup searches.
    /// </summary>
    [JsonPropertyName("defaultLookupResultLimit")]
    public int DefaultLookupResultLimit { get; set; } = 10;

    /// <summary>
    /// Enable caching of recent items for lookups.
    /// </summary>
    [JsonPropertyName("enableRecentItemsCache")]
    public bool EnableRecentItemsCache { get; set; } = true;

    /// <summary>
    /// Default form layout style.
    /// </summary>
    [JsonPropertyName("defaultFormLayout")]
    public string DefaultFormLayout { get; set; } = "Responsive";

    /// <summary>
    /// Default date format for display.
    /// </summary>
    [JsonPropertyName("dateFormat")]
    public string DateFormat { get; set; } = "yyyy-MM-dd";

    /// <summary>
    /// Default datetime format for display.
    /// </summary>
    [JsonPropertyName("dateTimeFormat")]
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm";

    /// <summary>
    /// Currency symbol for currency fields.
    /// </summary>
    [JsonPropertyName("currencySymbol")]
    public string CurrencySymbol { get; set; } = "$";

    /// <summary>
    /// Decimal separator for numeric fields.
    /// </summary>
    [JsonPropertyName("decimalSeparator")]
    public string DecimalSeparator { get; set; } = ".";

    /// <summary>
    /// Thousands separator for numeric fields.
    /// </summary>
    [JsonPropertyName("thousandsSeparator")]
    public string ThousandsSeparator { get; set; } = ",";
}

/// <summary>
/// Configuration for a single Salesforce module (SObject).
/// </summary>
public class ModuleConfig
{
    /// <summary>
    /// API name of the SObject.
    /// </summary>
    [JsonPropertyName("sObject")]
    public string SObject { get; set; } = string.Empty;

    /// <summary>
    /// Display label for this module.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Plural label for this module.
    /// </summary>
    [JsonPropertyName("pluralLabel")]
    public string? PluralLabel { get; set; }

    /// <summary>
    /// Category for grouping in navigation (e.g., "CRM", "Custom").
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "Standard";

    /// <summary>
    /// FontAwesome icon class (e.g., "fas fa-building").
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "fas fa-cube";

    /// <summary>
    /// Whether this module is visible in navigation.
    /// </summary>
    [JsonPropertyName("isVisible")]
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Sort order in navigation.
    /// </summary>
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; } = 100;

    /// <summary>
    /// Visibility policy for access control.
    /// </summary>
    [JsonPropertyName("visibilityPolicy")]
    public string? VisibilityPolicy { get; set; }

    /// <summary>
    /// Fields to display in list views.
    /// </summary>
    [JsonPropertyName("listFields")]
    public List<string> ListFields { get; set; } = new();

    /// <summary>
    /// Fields to display in detail views.
    /// </summary>
    [JsonPropertyName("detailFields")]
    public List<string> DetailFields { get; set; } = new();

    /// <summary>
    /// Fields to use for searching.
    /// </summary>
    [JsonPropertyName("searchFields")]
    public List<string> SearchFields { get; set; } = new();

    /// <summary>
    /// Default sort field for list views.
    /// </summary>
    [JsonPropertyName("defaultSortField")]
    public string? DefaultSortField { get; set; }

    /// <summary>
    /// Default sort direction (true = descending).
    /// </summary>
    [JsonPropertyName("defaultSortDescending")]
    public bool DefaultSortDescending { get; set; } = false;

    /// <summary>
    /// Form section configurations for create/edit views.
    /// </summary>
    [JsonPropertyName("formSections")]
    public List<FormSection> FormSections { get; set; } = new();

    /// <summary>
    /// Individual field overrides.
    /// </summary>
    [JsonPropertyName("fieldOverrides")]
    public Dictionary<string, FieldOverride> FieldOverrides { get; set; } = new();

    /// <summary>
    /// Relationship/lookup field configurations.
    /// </summary>
    [JsonPropertyName("relationshipConfigs")]
    public List<RelationshipConfig> RelationshipConfigs { get; set; } = new();

    /// <summary>
    /// Related list configurations for detail views.
    /// </summary>
    [JsonPropertyName("relatedLists")]
    public List<RelatedListConfig> RelatedLists { get; set; } = new();

    /// <summary>
    /// Custom actions available for this module.
    /// </summary>
    [JsonPropertyName("customActions")]
    public List<CustomAction> CustomActions { get; set; } = new();
}

/// <summary>
/// Configuration for a form section in create/edit views.
/// </summary>
public class FormSection
{
    /// <summary>
    /// Section title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Fields to include in this section.
    /// </summary>
    [JsonPropertyName("fields")]
    public List<string> Fields { get; set; } = new();

    /// <summary>
    /// Number of columns for layout.
    /// </summary>
    [JsonPropertyName("columns")]
    public int Columns { get; set; } = 2;

    /// <summary>
    /// Whether this section is collapsible.
    /// </summary>
    [JsonPropertyName("collapsible")]
    public bool Collapsible { get; set; } = false;

    /// <summary>
    /// Whether this section is initially collapsed.
    /// </summary>
    [JsonPropertyName("collapsed")]
    public bool Collapsed { get; set; } = false;

    /// <summary>
    /// Condition for showing this section (field name = value).
    /// </summary>
    [JsonPropertyName("showWhen")]
    public Dictionary<string, string>? ShowWhen { get; set; }
}

/// <summary>
/// Override configuration for individual fields.
/// </summary>
public class FieldOverride
{
    /// <summary>
    /// Custom label for the field.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Placeholder text for input.
    /// </summary>
    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    /// <summary>
    /// Help text shown below the field.
    /// </summary>
    [JsonPropertyName("helpText")]
    public string? HelpText { get; set; }

    /// <summary>
    /// Override required status.
    /// </summary>
    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    /// <summary>
    /// Override read-only status.
    /// </summary>
    [JsonPropertyName("readOnly")]
    public bool? ReadOnly { get; set; }

    /// <summary>
    /// Hide this field in forms.
    /// </summary>
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; } = false;

    /// <summary>
    /// Maximum length override.
    /// </summary>
    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    /// <summary>
    /// Minimum value for numeric fields.
    /// </summary>
    [JsonPropertyName("minValue")]
    public double? MinValue { get; set; }

    /// <summary>
    /// Maximum value for numeric fields.
    /// </summary>
    [JsonPropertyName("maxValue")]
    public double? MaxValue { get; set; }

    /// <summary>
    /// Regex pattern for validation.
    /// </summary>
    [JsonPropertyName("validationPattern")]
    public string? ValidationPattern { get; set; }

    /// <summary>
    /// Custom validation error message.
    /// </summary>
    [JsonPropertyName("validationMessage")]
    public string? ValidationMessage { get; set; }

    /// <summary>
    /// Default value for new records.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    /// <summary>
    /// CSS class for custom styling.
    /// </summary>
    [JsonPropertyName("cssClass")]
    public string? CssClass { get; set; }

    /// <summary>
    /// Condition for showing this field (field name = value).
    /// </summary>
    [JsonPropertyName("showWhen")]
    public Dictionary<string, string>? ShowWhen { get; set; }
}

/// <summary>
/// Configuration for lookup/relationship fields.
/// </summary>
public class RelationshipConfig
{
    /// <summary>
    /// Field name of the lookup field.
    /// </summary>
    [JsonPropertyName("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Lookup behavior configuration.
    /// </summary>
    [JsonPropertyName("lookupBehavior")]
    public LookupBehavior LookupBehavior { get; set; } = new();

    /// <summary>
    /// Parent field for dependent lookups.
    /// </summary>
    [JsonPropertyName("dependsOn")]
    public string? DependsOn { get; set; }

    /// <summary>
    /// Filter field on target object for dependent lookups.
    /// </summary>
    [JsonPropertyName("filterField")]
    public string? FilterField { get; set; }
}

/// <summary>
/// Behavior configuration for lookup searches.
/// </summary>
public class LookupBehavior
{
    /// <summary>
    /// Fields to search on the target object.
    /// </summary>
    [JsonPropertyName("searchFields")]
    public List<string> SearchFields { get; set; } = new() { "Name" };

    /// <summary>
    /// Additional fields to display in results.
    /// </summary>
    [JsonPropertyName("displayFields")]
    public List<string> DisplayFields { get; set; } = new();

    /// <summary>
    /// Show recently viewed items first.
    /// </summary>
    [JsonPropertyName("showRecentItems")]
    public bool ShowRecentItems { get; set; } = true;

    /// <summary>
    /// Maximum number of recent items.
    /// </summary>
    [JsonPropertyName("recentItemsLimit")]
    public int RecentItemsLimit { get; set; } = 5;

    /// <summary>
    /// Scoring weights for result ranking.
    /// </summary>
    [JsonPropertyName("weights")]
    public LookupWeights Weights { get; set; } = new();

    /// <summary>
    /// Static filter to apply to all searches.
    /// </summary>
    [JsonPropertyName("staticFilter")]
    public string? StaticFilter { get; set; }
}

/// <summary>
/// Scoring weights for lookup result ranking.
/// </summary>
public class LookupWeights
{
    /// <summary>
    /// Weight for exact name matches.
    /// </summary>
    [JsonPropertyName("exactMatch")]
    public double ExactMatch { get; set; } = 100.0;

    /// <summary>
    /// Weight for starts-with matches.
    /// </summary>
    [JsonPropertyName("startsWith")]
    public double StartsWith { get; set; } = 80.0;

    /// <summary>
    /// Weight for word starts-with matches.
    /// </summary>
    [JsonPropertyName("wordStartsWith")]
    public double WordStartsWith { get; set; } = 60.0;

    /// <summary>
    /// Weight for contains matches.
    /// </summary>
    [JsonPropertyName("contains")]
    public double Contains { get; set; } = 40.0;

    /// <summary>
    /// Weight for recently viewed items.
    /// </summary>
    [JsonPropertyName("recentlyViewed")]
    public double RecentlyViewed { get; set; } = 10.0;
}

/// <summary>
/// Configuration for related lists on detail views.
/// </summary>
public class RelatedListConfig
{
    /// <summary>
    /// Child object API name.
    /// </summary>
    [JsonPropertyName("childObject")]
    public string ChildObject { get; set; } = string.Empty;

    /// <summary>
    /// Lookup field on child object.
    /// </summary>
    [JsonPropertyName("lookupField")]
    public string LookupField { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the related list.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Fields to display in the related list.
    /// </summary>
    [JsonPropertyName("displayFields")]
    public List<string> DisplayFields { get; set; } = new();

    /// <summary>
    /// Maximum records to show.
    /// </summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 5;

    /// <summary>
    /// Sort field for related records.
    /// </summary>
    [JsonPropertyName("sortField")]
    public string? SortField { get; set; }

    /// <summary>
    /// Sort direction (true = descending).
    /// </summary>
    [JsonPropertyName("sortDescending")]
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Configuration for custom actions on a module.
/// </summary>
public class CustomAction
{
    /// <summary>
    /// Action identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display label.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Icon class.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Action type (url, javascript, api).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "url";

    /// <summary>
    /// Target URL or JavaScript function.
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method for API actions.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "POST";

    /// <summary>
    /// Show on list view.
    /// </summary>
    [JsonPropertyName("showOnList")]
    public bool ShowOnList { get; set; } = false;

    /// <summary>
    /// Show on detail view.
    /// </summary>
    [JsonPropertyName("showOnDetail")]
    public bool ShowOnDetail { get; set; } = true;

    /// <summary>
    /// Require confirmation before executing.
    /// </summary>
    [JsonPropertyName("confirmMessage")]
    public string? ConfirmMessage { get; set; }

    /// <summary>
    /// CSS class for styling.
    /// </summary>
    [JsonPropertyName("cssClass")]
    public string? CssClass { get; set; }
}
