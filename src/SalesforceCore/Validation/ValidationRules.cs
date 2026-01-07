using SalesforceCore.Models.Metadata;

namespace SalesforceCore.Validation;

/// <summary>
/// Custom validation rule interface for defining business rules.
/// </summary>
public interface IValidationRule
{
    /// <summary>
    /// Unique identifier for the rule.
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Human-readable description of the rule.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Object API name this rule applies to (null for global rules).
    /// </summary>
    string? ObjectName { get; }

    /// <summary>
    /// Field API name this rule applies to (null for record-level rules).
    /// </summary>
    string? FieldName { get; }

    /// <summary>
    /// Priority order (lower = runs first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Whether to stop validation if this rule fails.
    /// </summary>
    bool StopOnFailure { get; }

    /// <summary>
    /// Validates a record against this rule.
    /// </summary>
    /// <param name="context">Validation context with record data and metadata.</param>
    /// <returns>Validation result.</returns>
    Task<ValidationResult> ValidateAsync(ValidationContext context);
}

/// <summary>
/// Context provided to validation rules.
/// </summary>
public class ValidationContext
{
    /// <summary>
    /// Object API name being validated.
    /// </summary>
    public required string ObjectName { get; init; }

    /// <summary>
    /// Record data being validated.
    /// </summary>
    public required IDictionary<string, object?> Record { get; init; }

    /// <summary>
    /// Original record data (for updates).
    /// </summary>
    public IDictionary<string, object?>? OriginalRecord { get; init; }

    /// <summary>
    /// Field metadata map.
    /// </summary>
    public required IReadOnlyDictionary<string, SObjectField> FieldMap { get; init; }

    /// <summary>
    /// Whether this is a create operation.
    /// </summary>
    public bool IsCreate { get; init; }

    /// <summary>
    /// Gets a field value from the record.
    /// </summary>
    public T? GetValue<T>(string fieldName)
    {
        if (Record.TryGetValue(fieldName, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    /// <summary>
    /// Gets a field value with fallback to original record.
    /// </summary>
    public T? GetValueOrOriginal<T>(string fieldName)
    {
        if (Record.TryGetValue(fieldName, out var value) && value is T typedValue)
            return typedValue;

        if (OriginalRecord?.TryGetValue(fieldName, out var origValue) == true && origValue is T origTypedValue)
            return origTypedValue;

        return default;
    }

    /// <summary>
    /// Checks if a field was modified from original.
    /// </summary>
    public bool IsFieldModified(string fieldName)
    {
        if (OriginalRecord == null)
            return true;

        Record.TryGetValue(fieldName, out var newValue);
        OriginalRecord.TryGetValue(fieldName, out var oldValue);

        return !Equals(newValue, oldValue);
    }

    /// <summary>
    /// Gets field metadata.
    /// </summary>
    public SObjectField? GetFieldMetadata(string fieldName)
    {
        FieldMap.TryGetValue(fieldName, out var field);
        return field;
    }
}

/// <summary>
/// Base class for validation rules with common functionality.
/// </summary>
public abstract class ValidationRuleBase : IValidationRule
{
    public abstract string RuleId { get; }
    public abstract string Description { get; }
    public virtual string? ObjectName => null;
    public virtual string? FieldName => null;
    public virtual int Priority => 100;
    public virtual bool StopOnFailure => false;

    public abstract Task<ValidationResult> ValidateAsync(ValidationContext context);

    /// <summary>
    /// Creates a validation failure.
    /// </summary>
    protected ValidationResult Fail(string fieldName, string errorCode, string message)
        => ValidationResult.Failure(fieldName, errorCode, message);

    /// <summary>
    /// Creates a validation success.
    /// </summary>
    protected ValidationResult Pass() => ValidationResult.Success();
}

/// <summary>
/// Lambda-based validation rule for simple inline validations.
/// </summary>
public class LambdaValidationRule : IValidationRule
{
    private readonly Func<ValidationContext, Task<ValidationResult>> _validator;

    public string RuleId { get; }
    public string Description { get; }
    public string? ObjectName { get; }
    public string? FieldName { get; }
    public int Priority { get; init; } = 100;
    public bool StopOnFailure { get; init; } = false;

    public LambdaValidationRule(
        string ruleId,
        string description,
        Func<ValidationContext, Task<ValidationResult>> validator,
        string? objectName = null,
        string? fieldName = null)
    {
        RuleId = ruleId;
        Description = description;
        _validator = validator;
        ObjectName = objectName;
        FieldName = fieldName;
    }

    /// <summary>
    /// Creates a synchronous lambda rule.
    /// </summary>
    public static LambdaValidationRule Create(
        string ruleId,
        string description,
        Func<ValidationContext, ValidationResult> validator,
        string? objectName = null,
        string? fieldName = null)
    {
        return new LambdaValidationRule(
            ruleId,
            description,
            ctx => Task.FromResult(validator(ctx)),
            objectName,
            fieldName);
    }

    public Task<ValidationResult> ValidateAsync(ValidationContext context)
        => _validator(context);
}

/// <summary>
/// Collection of common reusable validation rules.
/// </summary>
public static class CommonValidationRules
{
    /// <summary>
    /// Creates a rule that requires a field when another field has a specific value.
    /// </summary>
    public static IValidationRule RequiredWhen(
        string ruleId,
        string requiredField,
        string conditionField,
        object conditionValue,
        string? objectName = null)
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{requiredField}' is required when '{conditionField}' is '{conditionValue}'",
            ctx =>
            {
                var condValue = ctx.GetValue<object>(conditionField);
                if (!Equals(condValue, conditionValue))
                    return ValidationResult.Success();

                var reqValue = ctx.GetValue<object>(requiredField);
                if (reqValue == null || (reqValue is string s && string.IsNullOrEmpty(s)))
                {
                    return ValidationResult.Failure(
                        requiredField,
                        ValidationErrorCodes.Required,
                        $"Field '{requiredField}' is required when '{conditionField}' is '{conditionValue}'.");
                }

                return ValidationResult.Success();
            },
            objectName,
            requiredField);
    }

    /// <summary>
    /// Creates a rule that validates field value matches a regex pattern.
    /// </summary>
    public static IValidationRule MatchesPattern(
        string ruleId,
        string fieldName,
        string pattern,
        string errorMessage,
        string? objectName = null)
    {
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled);
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must match pattern: {pattern}",
            ctx =>
            {
                var value = ctx.GetValue<string>(fieldName);
                if (string.IsNullOrEmpty(value))
                    return ValidationResult.Success();

                if (!regex.IsMatch(value))
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "PATTERN_MISMATCH",
                        errorMessage);
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that validates one field is greater than another.
    /// </summary>
    public static IValidationRule FieldGreaterThan(
        string ruleId,
        string fieldName,
        string comparisonField,
        string? objectName = null)
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must be greater than '{comparisonField}'",
            ctx =>
            {
                var value = ctx.GetValue<IComparable>(fieldName);
                var compValue = ctx.GetValue<IComparable>(comparisonField);

                if (value == null || compValue == null)
                    return ValidationResult.Success();

                if (value.CompareTo(compValue) <= 0)
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "COMPARISON_FAILED",
                        $"Field '{fieldName}' must be greater than '{comparisonField}'.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that validates date is in the future.
    /// </summary>
    public static IValidationRule DateInFuture(
        string ruleId,
        string fieldName,
        string? objectName = null)
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must be a future date",
            ctx =>
            {
                var value = ctx.GetValue<object>(fieldName);
                if (value == null)
                    return ValidationResult.Success();

                DateTime date;
                if (value is DateTime dt) date = dt;
                else if (value is DateTimeOffset dto) date = dto.DateTime;
                else if (value is DateOnly d) date = d.ToDateTime(TimeOnly.MinValue);
                else if (value is string s && DateTime.TryParse(s, out var parsed)) date = parsed;
                else return ValidationResult.Success();

                if (date <= DateTime.Today)
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "DATE_MUST_BE_FUTURE",
                        $"Field '{fieldName}' must be a future date.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that validates date is in the past.
    /// </summary>
    public static IValidationRule DateInPast(
        string ruleId,
        string fieldName,
        string? objectName = null)
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must be a past date",
            ctx =>
            {
                var value = ctx.GetValue<object>(fieldName);
                if (value == null)
                    return ValidationResult.Success();

                DateTime date;
                if (value is DateTime dt) date = dt;
                else if (value is DateTimeOffset dto) date = dto.DateTime;
                else if (value is DateOnly d) date = d.ToDateTime(TimeOnly.MinValue);
                else if (value is string s && DateTime.TryParse(s, out var parsed)) date = parsed;
                else return ValidationResult.Success();

                if (date >= DateTime.Today)
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "DATE_MUST_BE_PAST",
                        $"Field '{fieldName}' must be a past date.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that validates a field value is within a range.
    /// </summary>
    public static IValidationRule ValueInRange<T>(
        string ruleId,
        string fieldName,
        T min,
        T max,
        string? objectName = null) where T : IComparable<T>
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must be between {min} and {max}",
            ctx =>
            {
                var value = ctx.GetValue<T>(fieldName);
                if (value == null)
                    return ValidationResult.Success();

                if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
                {
                    return ValidationResult.Failure(
                        fieldName,
                        ValidationErrorCodes.ValueOutOfRange,
                        $"Field '{fieldName}' must be between {min} and {max}.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that prevents field from being cleared on update.
    /// </summary>
    public static IValidationRule CannotBeCleared(
        string ruleId,
        string fieldName,
        string? objectName = null)
    {
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' cannot be cleared once set",
            ctx =>
            {
                if (ctx.IsCreate)
                    return ValidationResult.Success();

                var origValue = ctx.OriginalRecord?.TryGetValue(fieldName, out var orig) == true ? orig : null;
                if (origValue == null)
                    return ValidationResult.Success();

                var newValue = ctx.GetValue<object>(fieldName);
                if (newValue == null || (newValue is string s && string.IsNullOrEmpty(s)))
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "FIELD_CANNOT_BE_CLEARED",
                        $"Field '{fieldName}' cannot be cleared once set.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }

    /// <summary>
    /// Creates a rule that validates email domain.
    /// </summary>
    public static IValidationRule EmailDomain(
        string ruleId,
        string fieldName,
        IEnumerable<string> allowedDomains,
        string? objectName = null)
    {
        var domains = allowedDomains.Select(d => d.ToLowerInvariant()).ToHashSet();
        return LambdaValidationRule.Create(
            ruleId,
            $"Field '{fieldName}' must have an allowed email domain",
            ctx =>
            {
                var value = ctx.GetValue<string>(fieldName);
                if (string.IsNullOrEmpty(value))
                    return ValidationResult.Success();

                var atIndex = value.LastIndexOf('@');
                if (atIndex < 0)
                    return ValidationResult.Success(); // Email format validation handles this

                var domain = value[(atIndex + 1)..].ToLowerInvariant();
                if (!domains.Contains(domain))
                {
                    return ValidationResult.Failure(
                        fieldName,
                        "INVALID_EMAIL_DOMAIN",
                        $"Email domain '@{domain}' is not allowed.");
                }

                return ValidationResult.Success();
            },
            objectName,
            fieldName);
    }
}
