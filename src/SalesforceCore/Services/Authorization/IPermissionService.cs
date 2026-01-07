using SalesforceCore.Models.Authorization;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Service for evaluating object and field-level permissions.
/// Aggregates CRUD ability and FLS from Salesforce metadata.
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// Gets the complete permission snapshot for an object.
    /// Includes CRUD flags and field-level permissions.
    /// Results are cached for the configured duration.
    /// </summary>
    /// <param name="objectName">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Permission snapshot for the object.</returns>
    Task<ObjectPermissionSnapshot> GetPermissionsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets permission snapshots for multiple objects.
    /// </summary>
    /// <param name="context">Permission request context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Permission result with snapshots for all requested objects.</returns>
    Task<PermissionResult> GetPermissionsAsync(
        PermissionRequestContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user can perform an action on an object.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="action">Action to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the action is allowed.</returns>
    Task<bool> CanPerformActionAsync(
        string objectName,
        PermissionAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user can perform an action on a field.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="action">Action to check (Read, Create, Update).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the action is allowed.</returns>
    Task<bool> CanAccessFieldAsync(
        string objectName,
        string fieldName,
        PermissionAction action,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks multiple permissions at once.
    /// </summary>
    /// <param name="checks">List of permission checks to perform.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results of all permission checks.</returns>
    Task<IReadOnlyList<PermissionCheckResult>> CheckPermissionsAsync(
        IEnumerable<(string ObjectName, PermissionAction Action, string? FieldName)> checks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of fields the user can read for an object.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of readable field names.</returns>
    Task<IReadOnlyList<string>> GetReadableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of fields the user can create.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of createable field names.</returns>
    Task<IReadOnlyList<string>> GetCreateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of fields the user can update.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of updateable field names.</returns>
    Task<IReadOnlyList<string>> GetUpdateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available actions for an object based on permissions.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of allowed actions.</returns>
    Task<IReadOnlyList<PermissionAction>> GetAllowedActionsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached permissions for an object or all objects.
    /// </summary>
    /// <param name="objectName">Object to invalidate, or null for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateCacheAsync(
        string? objectName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Preloads permissions for commonly used objects.
    /// Call this at application startup to warm the cache.
    /// </summary>
    /// <param name="objectNames">Objects to preload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PreloadPermissionsAsync(
        IEnumerable<string> objectNames,
        CancellationToken cancellationToken = default);
}
