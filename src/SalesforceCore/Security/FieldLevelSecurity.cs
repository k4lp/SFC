using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.Security;

/// <summary>
/// Service for enforcing Field-Level Security (FLS).
/// </summary>
public interface IFieldLevelSecurityService
{
    /// <summary>
    /// Gets fields that the current user can read.
    /// </summary>
    Task<IReadOnlyList<SObjectField>> GetReadableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields that the current user can create.
    /// </summary>
    Task<IReadOnlyList<SObjectField>> GetCreateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets fields that the current user can update.
    /// </summary>
    Task<IReadOnlyList<SObjectField>> GetUpdateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user can read a specific field.
    /// </summary>
    Task<bool> CanReadFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user can create/set a specific field.
    /// </summary>
    Task<bool> CanCreateFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the current user can update a specific field.
    /// </summary>
    Task<bool> CanUpdateFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters a record dictionary to only include readable fields.
    /// </summary>
    Task<IDictionary<string, object?>> FilterReadableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters a record dictionary to only include createable fields.
    /// </summary>
    Task<IDictionary<string, object?>> FilterCreateableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters a record dictionary to only include updateable fields.
    /// </summary>
    Task<IDictionary<string, object?>> FilterUpdateableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all fields in a record can be written to.
    /// </summary>
    Task<FlsValidationResult> ValidateForCreateAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all fields in a record can be updated.
    /// </summary>
    Task<FlsValidationResult> ValidateForUpdateAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of FLS permissions for an object.
    /// </summary>
    Task<FlsPermissionSummary> GetPermissionSummaryAsync(
        string objectName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of FLS validation.
/// </summary>
public class FlsValidationResult
{
    /// <summary>
    /// Whether all fields passed FLS validation.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Fields that failed FLS validation.
    /// </summary>
    public List<FlsViolation> Violations { get; init; } = new();

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static FlsValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static FlsValidationResult Failure(params FlsViolation[] violations)
        => new() { IsValid = false, Violations = violations.ToList() };
}

/// <summary>
/// Represents a single FLS violation.
/// </summary>
public record FlsViolation(
    string FieldName,
    string FieldLabel,
    FlsOperation Operation,
    string Message);

/// <summary>
/// Type of FLS operation.
/// </summary>
public enum FlsOperation
{
    Read,
    Create,
    Update
}

/// <summary>
/// Summary of FLS permissions for an object.
/// </summary>
public class FlsPermissionSummary
{
    /// <summary>
    /// Object API name.
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of fields.
    /// </summary>
    public int TotalFields { get; set; }

    /// <summary>
    /// Number of readable fields.
    /// </summary>
    public int ReadableFields { get; set; }

    /// <summary>
    /// Number of createable fields.
    /// </summary>
    public int CreateableFields { get; set; }

    /// <summary>
    /// Number of updateable fields.
    /// </summary>
    public int UpdateableFields { get; set; }

    /// <summary>
    /// Fields with no access.
    /// </summary>
    public List<string> InaccessibleFields { get; set; } = new();

    /// <summary>
    /// Fields that are read-only.
    /// </summary>
    public List<string> ReadOnlyFields { get; set; } = new();
}

/// <summary>
/// Implementation of FLS service.
/// </summary>
public class FieldLevelSecurityService : IFieldLevelSecurityService
{
    private readonly ISchemaService _schemaService;

    public FieldLevelSecurityService(ISchemaService schemaService)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SObjectField>> GetReadableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        return await _schemaService.GetAccessibleFieldsAsync(objectName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SObjectField>> GetCreateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        return await _schemaService.GetCreateableFieldsAsync(objectName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SObjectField>> GetUpdateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        return await _schemaService.GetUpdateableFieldsAsync(objectName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> CanReadFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        return fieldMap.TryGetValue(fieldName, out var field) && field.Accessible;
    }

    /// <inheritdoc/>
    public async Task<bool> CanCreateFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        return fieldMap.TryGetValue(fieldName, out var field) && field.Createable;
    }

    /// <inheritdoc/>
    public async Task<bool> CanUpdateFieldAsync(
        string objectName,
        string fieldName,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        return fieldMap.TryGetValue(fieldName, out var field) && field.Updateable;
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object?>> FilterReadableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in record)
        {
            if (fieldMap.TryGetValue(key, out var field) && field.Accessible)
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object?>> FilterCreateableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in record)
        {
            if (fieldMap.TryGetValue(key, out var field) && field.Createable)
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }

    /// <inheritdoc/>
    public async Task<IDictionary<string, object?>> FilterUpdateableFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in record)
        {
            if (fieldMap.TryGetValue(key, out var field) && field.Updateable)
            {
                filtered[key] = value;
            }
        }

        return filtered;
    }

    /// <inheritdoc/>
    public async Task<FlsValidationResult> ValidateForCreateAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var violations = new List<FlsViolation>();

        foreach (var fieldName in record.Keys)
        {
            if (!fieldMap.TryGetValue(fieldName, out var field))
            {
                violations.Add(new FlsViolation(
                    fieldName,
                    fieldName,
                    FlsOperation.Create,
                    $"Field '{fieldName}' does not exist on object '{objectName}'."));
                continue;
            }

            if (!field.Createable)
            {
                violations.Add(new FlsViolation(
                    field.Name,
                    field.Label,
                    FlsOperation.Create,
                    $"You do not have permission to create field '{field.Label}'."));
            }
        }

        return violations.Count == 0
            ? FlsValidationResult.Success()
            : FlsValidationResult.Failure(violations.ToArray());
    }

    /// <inheritdoc/>
    public async Task<FlsValidationResult> ValidateForUpdateAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var violations = new List<FlsViolation>();

        foreach (var fieldName in record.Keys)
        {
            // Skip Id field - it's used for identification, not update
            if (fieldName.Equals("Id", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fieldMap.TryGetValue(fieldName, out var field))
            {
                violations.Add(new FlsViolation(
                    fieldName,
                    fieldName,
                    FlsOperation.Update,
                    $"Field '{fieldName}' does not exist on object '{objectName}'."));
                continue;
            }

            if (!field.Updateable)
            {
                violations.Add(new FlsViolation(
                    field.Name,
                    field.Label,
                    FlsOperation.Update,
                    $"You do not have permission to update field '{field.Label}'."));
            }
        }

        return violations.Count == 0
            ? FlsValidationResult.Success()
            : FlsValidationResult.Failure(violations.ToArray());
    }

    /// <inheritdoc/>
    public async Task<FlsPermissionSummary> GetPermissionSummaryAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var fields = await _schemaService.GetFieldsAsync(objectName, cancellationToken);

        var summary = new FlsPermissionSummary
        {
            ObjectName = objectName,
            TotalFields = fields.Count,
            ReadableFields = fields.Count(f => f.Accessible),
            CreateableFields = fields.Count(f => f.Createable),
            UpdateableFields = fields.Count(f => f.Updateable)
        };

        foreach (var field in fields)
        {
            if (!field.Accessible)
            {
                summary.InaccessibleFields.Add(field.Name);
            }
            else if (!field.Createable && !field.Updateable)
            {
                summary.ReadOnlyFields.Add(field.Name);
            }
        }

        return summary;
    }
}

/// <summary>
/// Attribute to enforce FLS checking on service methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class EnforceFlsAttribute : Attribute
{
    /// <summary>
    /// Whether to enforce read access.
    /// </summary>
    public bool EnforceRead { get; set; } = true;

    /// <summary>
    /// Whether to enforce create access.
    /// </summary>
    public bool EnforceCreate { get; set; } = true;

    /// <summary>
    /// Whether to enforce update access.
    /// </summary>
    public bool EnforceUpdate { get; set; } = true;

    /// <summary>
    /// Whether to strip inaccessible fields or throw an exception.
    /// </summary>
    public bool StripInaccessible { get; set; } = false;
}

/// <summary>
/// Exception thrown when FLS validation fails.
/// </summary>
public class FlsException : Exception
{
    /// <summary>
    /// The FLS violations that caused the exception.
    /// </summary>
    public IReadOnlyList<FlsViolation> Violations { get; }

    public FlsException(IEnumerable<FlsViolation> violations)
        : base(BuildMessage(violations))
    {
        Violations = violations.ToList();
    }

    private static string BuildMessage(IEnumerable<FlsViolation> violations)
    {
        var violationList = violations.ToList();
        if (violationList.Count == 1)
            return violationList[0].Message;

        return $"Field-level security violations: {string.Join(", ", violationList.Select(v => v.FieldName))}";
    }
}

/// <summary>
/// Extension methods for FLS operations.
/// </summary>
public static class FlsExtensions
{
    /// <summary>
    /// Removes fields the user cannot access from a record.
    /// </summary>
    public static IDictionary<string, object?> StripInaccessibleFields(
        this IDictionary<string, object?> record,
        IReadOnlyDictionary<string, SObjectField> fieldMap,
        FlsOperation operation)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in record)
        {
            if (!fieldMap.TryGetValue(key, out var field))
                continue;

            var hasAccess = operation switch
            {
                FlsOperation.Read => field.Accessible,
                FlsOperation.Create => field.Createable,
                FlsOperation.Update => field.Updateable,
                _ => false
            };

            if (hasAccess)
            {
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Throws an exception if the validation result has violations.
    /// </summary>
    public static void ThrowIfInvalid(this FlsValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new FlsException(result.Violations);
        }
    }
}
