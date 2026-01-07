using SalesforceCore.Models.Layout;

namespace SalesforceCore.Services.Layout;

/// <summary>
/// Service for building dynamic UI layout descriptors.
/// Combines metadata, permissions, and configuration to produce
/// ready-to-render view/form/navigation descriptors.
/// </summary>
public interface ILayoutDescriptorService
{
    /// <summary>
    /// Gets the navigation descriptor with permission-filtered items.
    /// </summary>
    /// <param name="currentPath">Current page path for active state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Navigation descriptor with visible items.</returns>
    Task<NavigationDescriptor> GetNavigationAsync(
        string? currentPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a form descriptor for the specified object and mode.
    /// Filters fields based on permissions and configuration.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="mode">Form mode (Create, Edit, View).</param>
    /// <param name="recordTypeId">Optional record type ID for picklist filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Form descriptor ready for rendering.</returns>
    Task<FormDescriptor> GetFormAsync(
        string objectName,
        FormMode mode,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list view descriptor for the specified object.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List descriptor ready for rendering.</returns>
    Task<ListDescriptor> GetListAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a detail view descriptor for the specified object.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detail descriptor ready for rendering.</returns>
    Task<DetailDescriptor> GetDetailAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a field descriptor for a specific field.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="mode">Form mode for determining editability.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Field descriptor or null if not accessible.</returns>
    Task<FieldDescriptor?> GetFieldDescriptorAsync(
        string objectName,
        string fieldName,
        FormMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available actions for an object based on context.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="context">Action context (list, detail, form).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available actions.</returns>
    Task<IReadOnlyList<FormAction>> GetAvailableActionsAsync(
        string objectName,
        UiActionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets related list descriptors for an object.
    /// </summary>
    /// <param name="objectName">Parent object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of related list descriptors.</returns>
    Task<IReadOnlyList<RelatedListDescriptor>> GetRelatedListsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes layout descriptors for an object.
    /// </summary>
    /// <param name="objectName">Object to refresh, or null for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshAsync(
        string? objectName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the record type selector for an object.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record type selector or null if only one record type.</returns>
    Task<RecordTypeSelector?> GetRecordTypeSelectorAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets picklist options for a field, filtered by controlling value.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="fieldName">Picklist field API name.</param>
    /// <param name="controllingValue">Value of controlling field (for dependent picklists).</param>
    /// <param name="recordTypeId">Optional record type ID for filtering.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of picklist options.</returns>
    Task<IReadOnlyList<PicklistOption>> GetPicklistOptionsAsync(
        string objectName,
        string fieldName,
        string? controllingValue = null,
        string? recordTypeId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for action availability checks.
/// </summary>
public enum UiActionContext
{
    /// <summary>Actions shown in list views.</summary>
    List,
    /// <summary>Actions shown in detail views.</summary>
    Detail,
    /// <summary>Actions shown in forms.</summary>
    Form,
    /// <summary>Row-level actions in lists.</summary>
    RowAction,
    /// <summary>Bulk actions in lists.</summary>
    BulkAction
}
