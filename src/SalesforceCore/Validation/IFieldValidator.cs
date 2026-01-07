using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Validation;

/// <summary>
/// Interface for validating field values against Salesforce schema constraints.
/// </summary>
public interface IFieldValidator
{
    /// <summary>
    /// Validates a single field value against the field's metadata.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <param name="value">Value to validate.</param>
    /// <returns>Validation result with any errors.</returns>
    ValidationResult ValidateField(SObjectField field, object? value);

    /// <summary>
    /// Validates a record dictionary against an object's schema.
    /// </summary>
    /// <param name="objectName">Salesforce object name.</param>
    /// <param name="record">Dictionary of field name to value.</param>
    /// <param name="isCreate">Whether this is for a create operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<ValidationResult> ValidateRecordAsync(
        string objectName,
        IDictionary<string, object?> record,
        bool isCreate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a typed entity against schema constraints.
    /// </summary>
    /// <typeparam name="T">Entity type.</typeparam>
    /// <param name="entity">Entity to validate.</param>
    /// <param name="isCreate">Whether this is for a create operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<ValidationResult> ValidateEntityAsync<T>(
        T entity,
        bool isCreate = true,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Validates a picklist value against allowed values.
    /// </summary>
    /// <param name="field">Field metadata.</param>
    /// <param name="value">Picklist value to validate.</param>
    /// <returns>True if value is valid.</returns>
    bool ValidatePicklistValue(SObjectField field, string? value);

    /// <summary>
    /// Validates a dependent picklist value against controlling field value.
    /// </summary>
    /// <param name="field">Dependent field metadata.</param>
    /// <param name="value">Dependent picklist value.</param>
    /// <param name="controllingValue">Value of controlling field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if value is valid for the controlling value.</returns>
    Task<bool> ValidateDependentPicklistAsync(
        SObjectField field,
        string? value,
        string? controllingValue,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a validation operation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static ValidationResult Failure(params ValidationError[] errors)
        => new() { IsValid = false, Errors = errors.ToList() };

    /// <summary>
    /// Creates a failed validation result with a single error.
    /// </summary>
    public static ValidationResult Failure(string fieldName, string errorCode, string message)
        => Failure(new ValidationError(fieldName, errorCode, message));

    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors (empty if valid).
    /// </summary>
    public List<ValidationError> Errors { get; init; } = new();

    /// <summary>
    /// Combines this result with another.
    /// </summary>
    public ValidationResult Merge(ValidationResult other)
    {
        if (other.IsValid && IsValid)
            return Success();

        return new ValidationResult
        {
            IsValid = false,
            Errors = Errors.Concat(other.Errors).ToList()
        };
    }

    /// <summary>
    /// Gets errors for a specific field.
    /// </summary>
    public IEnumerable<ValidationError> GetFieldErrors(string fieldName)
        => Errors.Where(e => e.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the first error message, or null if valid.
    /// </summary>
    public string? FirstErrorMessage => Errors.FirstOrDefault()?.Message;
}

/// <summary>
/// A single validation error.
/// </summary>
public record ValidationError(
    string FieldName,
    string ErrorCode,
    string Message,
    object? AttemptedValue = null);

/// <summary>
/// Standard validation error codes.
/// </summary>
public static class ValidationErrorCodes
{
    public const string Required = "REQUIRED_FIELD_MISSING";
    public const string MaxLength = "STRING_TOO_LONG";
    public const string MinLength = "STRING_TOO_SHORT";
    public const string InvalidType = "INVALID_TYPE";
    public const string InvalidPicklist = "INVALID_PICKLIST_VALUE";
    public const string InvalidDependentPicklist = "INVALID_DEPENDENT_PICKLIST";
    public const string InvalidLookup = "INVALID_OR_NULL_FOR_RESTRICTED_PICKLIST";
    public const string InvalidDate = "INVALID_DATE_FORMAT";
    public const string InvalidEmail = "INVALID_EMAIL_ADDRESS";
    public const string InvalidUrl = "INVALID_URL";
    public const string InvalidPhone = "INVALID_PHONE_FORMAT";
    public const string PrecisionExceeded = "NUMBER_PRECISION_EXCEEDED";
    public const string ScaleExceeded = "NUMBER_SCALE_EXCEEDED";
    public const string ValueOutOfRange = "VALUE_OUT_OF_RANGE";
    public const string ReadOnlyField = "FIELD_NOT_UPDATEABLE";
    public const string NotCreateable = "FIELD_NOT_CREATEABLE";
    public const string FieldNotFound = "INVALID_FIELD";
    public const string DuplicateExternalId = "DUPLICATE_EXTERNAL_ID";
    public const string InvalidIdFormat = "INVALID_ID_FIELD";
}
