using System.Text.Json.Nodes;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Data;

namespace SalesforceCore.AspNetCore.ViewModels;

/// <summary>
/// Form mode enum.
/// </summary>
public enum FormMode
{
    /// <summary>Create new record.</summary>
    Create,
    /// <summary>Edit existing record.</summary>
    Edit,
    /// <summary>View-only mode.</summary>
    View
}

/// <summary>
/// Base view model for Salesforce views.
/// </summary>
public abstract class SalesforceViewModelBase
{
    /// <summary>
    /// API name of the SObject.
    /// </summary>
    public string SObject { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the object.
    /// </summary>
    public string ObjectLabel { get; set; } = string.Empty;

    /// <summary>
    /// Module configuration if available.
    /// </summary>
    public ModuleConfig? ModuleConfig { get; set; }

    /// <summary>
    /// Success message from TempData.
    /// </summary>
    public string? SuccessMessage { get; set; }

    /// <summary>
    /// Error message from TempData.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// MVC options used for UI rendering.
    /// </summary>
    public SalesforceMvcOptions MvcOptions { get; set; } = new();
}

/// <summary>
/// View model for list/index views.
/// </summary>
public class ListViewModel : SalesforceViewModelBase
{
    /// <summary>
    /// Plural label for the object.
    /// </summary>
    public string ObjectLabelPlural { get; set; } = string.Empty;

    /// <summary>
    /// Records to display.
    /// </summary>
    public List<JsonObject> Records { get; set; } = new();

    /// <summary>
    /// Field names to display as columns.
    /// </summary>
    public List<string> Fields { get; set; } = new();

    /// <summary>
    /// Field metadata for display formatting.
    /// </summary>
    public Dictionary<string, SObjectField> FieldDefinitions { get; set; } = new();

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>
    /// Page size.
    /// </summary>
    public int PageSize { get; set; } = 25;

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage { get; set; }

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage { get; set; }

    /// <summary>
    /// Current search query.
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Current sort field.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Whether sorting is descending.
    /// </summary>
    public bool OrderDescending { get; set; }

    /// <summary>
    /// Whether user can create records.
    /// </summary>
    public bool CanCreate { get; set; }
}

/// <summary>
/// View model for detail views.
/// </summary>
public class DetailsViewModel : SalesforceViewModelBase
{
    /// <summary>
    /// Record ID.
    /// </summary>
    public string RecordId { get; set; } = string.Empty;

    /// <summary>
    /// Record data.
    /// </summary>
    public JsonObject Record { get; set; } = new();

    /// <summary>
    /// All fields with metadata.
    /// </summary>
    public List<SObjectField> Fields { get; set; } = new();

    /// <summary>
    /// Field metadata by name.
    /// </summary>
    public Dictionary<string, SObjectField> FieldDefinitions { get; set; } = new();

    /// <summary>
    /// Hydrated lookup display names.
    /// </summary>
    public Dictionary<string, string> HydratedLookups { get; set; } = new();

    /// <summary>
    /// Attached files.
    /// </summary>
    public List<AttachedFile> AttachedFiles { get; set; } = new();

    /// <summary>
    /// Whether user can edit.
    /// </summary>
    public bool CanEdit { get; set; }

    /// <summary>
    /// Whether user can delete.
    /// </summary>
    public bool CanDelete { get; set; }

    /// <summary>
    /// Gets display value for a field.
    /// </summary>
    public string GetDisplayValue(string fieldName)
    {
        var value = Record[fieldName];
        if (value == null) return string.Empty;

        // Check for hydrated lookup
        if (HydratedLookups.TryGetValue(fieldName, out var lookupName))
        {
            return lookupName;
        }

        return value.ToString() ?? string.Empty;
    }
}

/// <summary>
/// View model for create/edit forms.
/// </summary>
public class FormViewModel : SalesforceViewModelBase
{
    /// <summary>
    /// Form mode (Create/Edit).
    /// </summary>
    public FormMode Mode { get; set; } = FormMode.Create;

    /// <summary>
    /// Record ID (for edit mode).
    /// </summary>
    public string? RecordId { get; set; }

    /// <summary>
    /// Current record data.
    /// </summary>
    public JsonObject Record { get; set; } = new();

    /// <summary>
    /// Fields to display in form.
    /// </summary>
    public List<SObjectField> Fields { get; set; } = new();

    /// <summary>
    /// Field metadata by name.
    /// </summary>
    public Dictionary<string, SObjectField> FieldDefinitions { get; set; } = new();

    /// <summary>
    /// Hydrated lookup display names.
    /// </summary>
    public Dictionary<string, string> HydratedLookups { get; set; } = new();

    /// <summary>
    /// Form sections for grouping.
    /// </summary>
    public List<FormSection>? FormSections => ModuleConfig?.FormSections;

    /// <summary>
    /// Field overrides.
    /// </summary>
    public Dictionary<string, FieldOverride>? FieldOverrides => ModuleConfig?.FieldOverrides;

    /// <summary>
    /// Gets current value for a field.
    /// </summary>
    public string GetFieldValue(string fieldName)
    {
        var value = Record[fieldName];
        if (value == null) return string.Empty;

        if (FieldDefinitions.TryGetValue(fieldName, out var field))
        {
            return SalesforceCore.Utilities.FieldTypeConverter.ConvertToInputValue(field, value);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets lookup display name for a field.
    /// </summary>
    public string GetLookupDisplayName(string fieldName)
    {
        return HydratedLookups.TryGetValue(fieldName, out var name) ? name : string.Empty;
    }

    /// <summary>
    /// Gets field override if exists.
    /// </summary>
    public FieldOverride? GetFieldOverride(string fieldName)
    {
        return FieldOverrides?.TryGetValue(fieldName, out var overrideConfig) == true
            ? overrideConfig
            : null;
    }

    /// <summary>
    /// Whether this is create mode.
    /// </summary>
    public bool IsCreate => Mode == FormMode.Create;

    /// <summary>
    /// Whether this is edit mode.
    /// </summary>
    public bool IsEdit => Mode == FormMode.Edit;
}

/// <summary>
/// View model for operation result pages.
/// </summary>
public class OperationResultViewModel
{
    /// <summary>
    /// Result title.
    /// </summary>
    public string Title { get; set; } = "Operation Complete";

    /// <summary>
    /// Result message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Whether operation was successful.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Icon class.
    /// </summary>
    public string IconClass { get; set; } = "fas fa-check-circle";

    /// <summary>
    /// Icon color class.
    /// </summary>
    public string ColorClass { get; set; } = "text-success";

    /// <summary>
    /// URL to redirect to.
    /// </summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// Redirect delay in seconds.
    /// </summary>
    public int RedirectDelay { get; set; } = 3;
}

/// <summary>
/// View model for navigation/sidebar.
/// </summary>
public class NavigationViewModel
{
    /// <summary>
    /// Modules grouped by category.
    /// </summary>
    public Dictionary<string, List<ModuleConfig>> ModulesByCategory { get; set; } = new();

    /// <summary>
    /// Currently active object.
    /// </summary>
    public string? ActiveObject { get; set; }
}

/// <summary>
/// View model for field input partial view.
/// </summary>
public class FieldInputModel
{
    /// <summary>
    /// Field metadata.
    /// </summary>
    public SObjectField Field { get; set; } = new();

    /// <summary>
    /// Current field value.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Display name for lookup fields.
    /// </summary>
    public string? LookupDisplayName { get; set; }

    /// <summary>
    /// Field override configuration.
    /// </summary>
    public FieldOverride? FieldOverride { get; set; }

    /// <summary>
    /// Whether this is a create form.
    /// </summary>
    public bool IsCreate { get; set; }
}

/// <summary>
/// View model for lookup field partial view.
/// </summary>
public class LookupFieldModel
{
    /// <summary>
    /// Field name.
    /// </summary>
    public string FieldName { get; set; } = "";

    /// <summary>
    /// Target object for lookup.
    /// </summary>
    public string TargetObject { get; set; } = "";

    /// <summary>
    /// Current value (record ID).
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Display name for current value.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Field label.
    /// </summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Whether field is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether this is a polymorphic lookup.
    /// </summary>
    public bool IsPolymorphic { get; set; }

    /// <summary>
    /// Possible target objects for polymorphic lookup.
    /// </summary>
    public List<string> PolymorphicTargets { get; set; } = new();
}
