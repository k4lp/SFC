namespace SalesforceCore.Models.Layout;

/// <summary>
/// Represents a navigation menu item.
/// Supports hierarchical menus with permission-based visibility.
/// </summary>
public class NavigationItem
{
    /// <summary>
    /// Unique identifier for this navigation item.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display label for the menu item.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Icon identifier (CSS class or icon name).
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Route or URL for this item.
    /// Can be relative ("/sf/Account") or absolute.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    /// Salesforce object this item relates to (for permission checks).
    /// </summary>
    public string? SObject { get; set; }

    /// <summary>
    /// Required permission action (Read, Create, etc.).
    /// </summary>
    public PermissionRequirement? RequiredPermission { get; set; }

    /// <summary>
    /// Child navigation items.
    /// </summary>
    public List<NavigationItem> Children { get; set; } = new();

    /// <summary>
    /// Display order (lower = first).
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this item is currently active/selected.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether this item is visible (based on permissions).
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Whether this item is enabled (can be disabled even if visible).
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Tooltip text.
    /// </summary>
    public string? Tooltip { get; set; }

    /// <summary>
    /// Badge text (e.g., for counts).
    /// </summary>
    public string? Badge { get; set; }

    /// <summary>
    /// CSS classes to apply.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Target for links (_blank, _self, etc.).
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Feature flags required for this item.
    /// </summary>
    public List<string> RequiredFeatures { get; set; } = new();

    /// <summary>
    /// Custom data attributes.
    /// </summary>
    public Dictionary<string, string> DataAttributes { get; set; } = new();
}

/// <summary>
/// Permission requirement for a navigation item or action.
/// </summary>
public class PermissionRequirement
{
    /// <summary>
    /// Object name for the permission check.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Required action (Create, Read, Update, Delete).
    /// </summary>
    public string Action { get; set; } = "Read";

    /// <summary>
    /// Whether all specified permissions are required (AND) or any (OR).
    /// </summary>
    public bool RequireAll { get; set; } = true;

    /// <summary>
    /// Additional field-level permissions required.
    /// </summary>
    public List<FieldPermissionRequirement> FieldPermissions { get; set; } = new();

    /// <summary>
    /// Creates a read permission requirement.
    /// </summary>
    public static PermissionRequirement Read(string objectName) =>
        new() { ObjectName = objectName, Action = "Read" };

    /// <summary>
    /// Creates a create permission requirement.
    /// </summary>
    public static PermissionRequirement Create(string objectName) =>
        new() { ObjectName = objectName, Action = "Create" };

    /// <summary>
    /// Creates an update permission requirement.
    /// </summary>
    public static PermissionRequirement Update(string objectName) =>
        new() { ObjectName = objectName, Action = "Update" };

    /// <summary>
    /// Creates a delete permission requirement.
    /// </summary>
    public static PermissionRequirement Delete(string objectName) =>
        new() { ObjectName = objectName, Action = "Delete" };
}

/// <summary>
/// Field-level permission requirement.
/// </summary>
public class FieldPermissionRequirement
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Required action (Read, Create, Update).
    /// </summary>
    public string Action { get; set; } = "Read";
}

/// <summary>
/// Descriptor for a form layout.
/// </summary>
public class FormDescriptor
{
    /// <summary>
    /// Object this form is for.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Object display label.
    /// </summary>
    public string ObjectLabel { get; set; } = string.Empty;

    /// <summary>
    /// Form mode (Create, Edit, View).
    /// </summary>
    public FormMode Mode { get; set; }

    /// <summary>
    /// Form title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Form sections.
    /// </summary>
    public List<FormSection> Sections { get; set; } = new();

    /// <summary>
    /// All fields in the form (flattened from sections).
    /// </summary>
    public List<FieldDescriptor> Fields { get; set; } = new();

    /// <summary>
    /// Available actions for this form.
    /// </summary>
    public List<FormAction> Actions { get; set; } = new();

    /// <summary>
    /// Record type selector if multiple record types are available.
    /// </summary>
    public RecordTypeSelector? RecordTypeSelector { get; set; }

    /// <summary>
    /// The record type currently in context for this form.
    /// </summary>
    public string? RecordTypeId { get; set; }

    /// <summary>
    /// Number of columns for the form layout.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Custom CSS class for the form.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Whether to show validation summary.
    /// </summary>
    public bool ShowValidationSummary { get; set; } = true;

    /// <summary>
    /// Whether this descriptor should be rendered.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Whether the form is read-only.
    /// </summary>
    public bool IsReadOnly => Mode == FormMode.View;

    /// <summary>
    /// Validation rules for the form.
    /// </summary>
    public List<FormValidationRule> ValidationRules { get; set; } = new();
}

/// <summary>
/// Form operating mode.
/// </summary>
public enum FormMode
{
    /// <summary>Creating a new record.</summary>
    Create,
    /// <summary>Editing an existing record.</summary>
    Edit,
    /// <summary>Viewing a record (read-only).</summary>
    View
}

/// <summary>
/// A section within a form.
/// </summary>
public class FormSection
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
    public List<FieldDescriptor> Fields { get; set; } = new();

    /// <summary>
    /// Number of columns in this section.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether the section is collapsible.
    /// </summary>
    public bool IsCollapsible { get; set; }

    /// <summary>
    /// Whether the section is initially collapsed.
    /// </summary>
    public bool IsCollapsed { get; set; }

    /// <summary>
    /// Whether the section is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// CSS class for the section.
    /// </summary>
    public string? CssClass { get; set; }
}

/// <summary>
/// Descriptor for a field in a form or view.
/// </summary>
public class FieldDescriptor
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Field type (string, picklist, reference, etc.).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// HTML input type to render (text, email, number, etc.).
    /// </summary>
    public string InputType { get; set; } = "text";

    /// <summary>
    /// Control type to render (input, select, lookup, textarea, etc.).
    /// </summary>
    public string ControlType { get; set; } = "input";

    /// <summary>
    /// Whether the field is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether the field is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Whether the field is hidden.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Whether the field is visible (based on permissions).
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Display order within section.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Column span (for multi-column layouts).
    /// </summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>
    /// Maximum length for input.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Minimum value for numeric fields.
    /// </summary>
    public decimal? MinValue { get; set; }

    /// <summary>
    /// Maximum value for numeric fields.
    /// </summary>
    public decimal? MaxValue { get; set; }

    /// <summary>
    /// Step value for numeric fields.
    /// </summary>
    public decimal? Step { get; set; }

    /// <summary>
    /// Placeholder text.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Help text.
    /// </summary>
    public string? HelpText { get; set; }

    /// <summary>
    /// Default value.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Picklist options (for picklist fields).
    /// </summary>
    public List<PicklistOption>? PicklistOptions { get; set; }

    /// <summary>
    /// Controlling field name (for dependent picklists).
    /// </summary>
    public string? ControllingField { get; set; }

    /// <summary>
    /// Lookup configuration (for lookup fields).
    /// </summary>
    public LookupConfig? LookupConfig { get; set; }

    /// <summary>
    /// Validation pattern (regex).
    /// </summary>
    public string? ValidationPattern { get; set; }

    /// <summary>
    /// Validation error message.
    /// </summary>
    public string? ValidationMessage { get; set; }

    /// <summary>
    /// CSS class for the field container.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// CSS class for the input element.
    /// </summary>
    public string? InputCssClass { get; set; }

    /// <summary>
    /// Custom data attributes.
    /// </summary>
    public Dictionary<string, string> DataAttributes { get; set; } = new();
}

/// <summary>
/// Picklist option for dropdown fields.
/// </summary>
public class PicklistOption
{
    /// <summary>
    /// Option value.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Display label.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default option.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Whether this option is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Controlling values this option is valid for (dependent picklists).
    /// </summary>
    public List<string>? ValidForValues { get; set; }
}

/// <summary>
/// Configuration for lookup fields.
/// </summary>
public class LookupConfig
{
    /// <summary>
    /// Target object(s) for the lookup.
    /// </summary>
    public List<string> TargetObjects { get; set; } = new();

    /// <summary>
    /// Search endpoint URL.
    /// </summary>
    public string? SearchUrl { get; set; }

    /// <summary>
    /// Minimum characters before search triggers.
    /// </summary>
    public int MinChars { get; set; } = 2;

    /// <summary>
    /// Debounce delay in milliseconds.
    /// </summary>
    public int DebounceMs { get; set; } = 300;

    /// <summary>
    /// Whether to allow creating new records.
    /// </summary>
    public bool AllowCreate { get; set; }

    /// <summary>
    /// Whether this is a polymorphic lookup.
    /// </summary>
    public bool IsPolymorphic { get; set; }

    /// <summary>
    /// Display field for results.
    /// </summary>
    public string DisplayField { get; set; } = "Name";

    /// <summary>
    /// Additional fields to show in results.
    /// </summary>
    public List<string> SubtitleFields { get; set; } = new();
}

/// <summary>
/// Form action button.
/// </summary>
public class FormAction
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
    /// Button type (submit, button, reset).
    /// </summary>
    public string Type { get; set; } = "button";

    /// <summary>
    /// Action type (save, cancel, delete, custom).
    /// </summary>
    public string ActionType { get; set; } = "custom";

    /// <summary>
    /// Icon identifier.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Whether the action is the primary action.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Whether the action is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Whether the action is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// CSS class for the button.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Required permission for this action.
    /// </summary>
    public PermissionRequirement? RequiredPermission { get; set; }

    /// <summary>
    /// Confirmation message (for destructive actions).
    /// </summary>
    public string? ConfirmationMessage { get; set; }

    /// <summary>
    /// Custom route or URL for action.
    /// </summary>
    public string? Route { get; set; }
}

/// <summary>
/// Record type selector configuration.
/// </summary>
public class RecordTypeSelector
{
    /// <summary>
    /// Available record types.
    /// </summary>
    public List<RecordTypeOption> Options { get; set; } = new();

    /// <summary>
    /// Default record type ID.
    /// </summary>
    public string? DefaultId { get; set; }

    /// <summary>
    /// Whether record type is required.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Whether to show the selector (false if only one record type).
    /// </summary>
    public bool ShowSelector { get; set; }
}

/// <summary>
/// Record type option.
/// </summary>
public class RecordTypeOption
{
    /// <summary>
    /// Record type ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is the default option.
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Form validation rule.
/// </summary>
public class FormValidationRule
{
    /// <summary>
    /// Rule identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Field the rule applies to (null for form-level rules).
    /// </summary>
    public string? FieldName { get; set; }

    /// <summary>
    /// Rule type (required, pattern, custom, etc.).
    /// </summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>
    /// Rule value (e.g., regex pattern).
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Descriptor for a list/table view.
/// </summary>
public class ListDescriptor
{
    /// <summary>
    /// Object this list is for.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Object display label.
    /// </summary>
    public string ObjectLabel { get; set; } = string.Empty;

    /// <summary>
    /// List title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Columns to display.
    /// </summary>
    public List<ColumnDescriptor> Columns { get; set; } = new();

    /// <summary>
    /// Available actions for each row.
    /// </summary>
    public List<FormAction> RowActions { get; set; } = new();

    /// <summary>
    /// Bulk actions available.
    /// </summary>
    public List<FormAction> BulkActions { get; set; } = new();

    /// <summary>
    /// Default sort field.
    /// </summary>
    public string? DefaultSortField { get; set; }

    /// <summary>
    /// Default sort direction.
    /// </summary>
    public SortDirection DefaultSortDirection { get; set; } = SortDirection.Ascending;

    /// <summary>
    /// Default page size.
    /// </summary>
    public int PageSize { get; set; } = 25;

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
    public bool EnableSelection { get; set; }

    /// <summary>
    /// Whether to enable export.
    /// </summary>
    public bool EnableExport { get; set; }

    /// <summary>
    /// CSS class for the list.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Whether the list descriptor should be rendered.
    /// </summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Column descriptor for list views.
/// </summary>
public class ColumnDescriptor
{
    /// <summary>
    /// Field API name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Column header.
    /// </summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Field type.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Column width (CSS value).
    /// </summary>
    public string? Width { get; set; }

    /// <summary>
    /// Whether the column is sortable.
    /// </summary>
    public bool IsSortable { get; set; }

    /// <summary>
    /// Whether the column is filterable.
    /// </summary>
    public bool IsFilterable { get; set; }

    /// <summary>
    /// Whether the column is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Text alignment.
    /// </summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>
    /// Format string for display.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Whether this column is a link to the record.
    /// </summary>
    public bool IsLink { get; set; }

    /// <summary>
    /// CSS class for the column.
    /// </summary>
    public string? CssClass { get; set; }
}

/// <summary>
/// Sort direction.
/// </summary>
public enum SortDirection
{
    /// <summary>Ascending order.</summary>
    Ascending,
    /// <summary>Descending order.</summary>
    Descending
}

/// <summary>
/// Text alignment.
/// </summary>
public enum TextAlignment
{
    /// <summary>Left aligned.</summary>
    Left,
    /// <summary>Center aligned.</summary>
    Center,
    /// <summary>Right aligned.</summary>
    Right
}

/// <summary>
/// Descriptor for a detail/single record view.
/// </summary>
public class DetailDescriptor
{
    /// <summary>
    /// Object this detail is for.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Object display label.
    /// </summary>
    public string ObjectLabel { get; set; } = string.Empty;

    /// <summary>
    /// View title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Sections in the detail view.
    /// </summary>
    public List<FormSection> Sections { get; set; } = new();

    /// <summary>
    /// Related lists to display.
    /// </summary>
    public List<RelatedListDescriptor> RelatedLists { get; set; } = new();

    /// <summary>
    /// Available actions.
    /// </summary>
    public List<FormAction> Actions { get; set; } = new();

    /// <summary>
    /// CSS class for the detail view.
    /// </summary>
    public string? CssClass { get; set; }

    /// <summary>
    /// Whether this descriptor should be rendered.
    /// </summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Descriptor for a related list.
/// </summary>
public class RelatedListDescriptor
{
    /// <summary>
    /// Relationship name.
    /// </summary>
    public string RelationshipName { get; set; } = string.Empty;

    /// <summary>
    /// Child object name.
    /// </summary>
    public string ChildObject { get; set; } = string.Empty;

    /// <summary>
    /// Display title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Columns to display.
    /// </summary>
    public List<ColumnDescriptor> Columns { get; set; } = new();

    /// <summary>
    /// Display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Maximum records to show.
    /// </summary>
    public int MaxRecords { get; set; } = 5;

    /// <summary>
    /// Whether the user can add new records.
    /// </summary>
    public bool CanCreate { get; set; }

    /// <summary>
    /// Whether the list is visible.
    /// </summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Complete navigation descriptor.
/// </summary>
public class NavigationDescriptor
{
    /// <summary>
    /// Main navigation items.
    /// </summary>
    public List<NavigationItem> MainItems { get; set; } = new();

    /// <summary>
    /// User/utility navigation items.
    /// </summary>
    public List<NavigationItem> UtilityItems { get; set; } = new();

    /// <summary>
    /// Application name/brand.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Logo URL.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Current user display name.
    /// </summary>
    public string? UserDisplayName { get; set; }

    /// <summary>
    /// Custom CSS class.
    /// </summary>
    public string? CssClass { get; set; }
}
