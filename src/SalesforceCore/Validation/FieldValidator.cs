using System.Text.RegularExpressions;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Utilities;

namespace SalesforceCore.Validation;

/// <summary>
/// Implementation of field validation against Salesforce schema constraints.
/// </summary>
public class FieldValidator : IFieldValidator
{
    private readonly ISchemaService _schemaService;

    // Pre-compiled regex patterns for common validations
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UrlRegex = new(
        @"^(https?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PhoneRegex = new(
        @"^[\d\s\+\-\(\)\.ext]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SalesforceIdRegex = new(
        @"^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$",
        RegexOptions.Compiled);

    public FieldValidator(ISchemaService schemaService)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    }

    /// <inheritdoc/>
    public ValidationResult ValidateField(SObjectField field, object? value)
    {
        var errors = new List<ValidationError>();

        // Check required fields
        if (field.IsRequired && IsNullOrEmpty(value))
        {
            errors.Add(new ValidationError(
                field.Name,
                ValidationErrorCodes.Required,
                $"Field '{field.Label}' is required.",
                value));
            return ValidationResult.Failure(errors.ToArray());
        }

        // Null/empty values are valid for non-required fields
        if (IsNullOrEmpty(value))
            return ValidationResult.Success();

        // Type-specific validation
        var typeError = ValidateByType(field, value!);  // value is not null here
        if (typeError != null)
            errors.Add(typeError);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateRecordAsync(
        string objectName,
        IDictionary<string, object?> record,
        bool isCreate = true,
        CancellationToken cancellationToken = default)
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);
        var result = ValidationResult.Success();

        foreach (var (fieldName, value) in record)
        {
            // Check if field exists
            if (!fieldMap.TryGetValue(fieldName, out var field))
            {
                result = result.Merge(ValidationResult.Failure(
                    fieldName,
                    ValidationErrorCodes.FieldNotFound,
                    $"Field '{fieldName}' does not exist on object '{objectName}'."));
                continue;
            }

            // Check field permissions
            if (isCreate && !field.Createable)
            {
                result = result.Merge(ValidationResult.Failure(
                    fieldName,
                    ValidationErrorCodes.NotCreateable,
                    $"Field '{fieldName}' cannot be set during create."));
                continue;
            }

            if (!isCreate && !field.Updateable)
            {
                result = result.Merge(ValidationResult.Failure(
                    fieldName,
                    ValidationErrorCodes.ReadOnlyField,
                    $"Field '{fieldName}' is read-only and cannot be updated."));
                continue;
            }

            // Validate field value
            result = result.Merge(ValidateField(field, value));

            // Validate Dependent Picklists
            if (field.DependentPicklist && !string.IsNullOrEmpty(field.ControllerName))
            {
                if (record.TryGetValue(field.ControllerName, out var controllingValObj) && controllingValObj != null)
                {
                    var controllingVal = controllingValObj.ToString();

                    // Retrieve controlling field to get index
                    if (fieldMap.TryGetValue(field.ControllerName, out var controllingField))
                    {
                        var controllerIndex = controllingField.PicklistValues?
                            .FindIndex(p => p.Value.Equals(controllingVal, StringComparison.OrdinalIgnoreCase));

                        if (controllerIndex.HasValue && controllerIndex.Value >= 0)
                        {
                            var isValid = false;

                            // Check validFor bitmask
                             var dependentOption = field.PicklistValues?
                                .FirstOrDefault(p => p.Value.Equals(value?.ToString(), StringComparison.OrdinalIgnoreCase));

                             if (dependentOption != null && !string.IsNullOrEmpty(dependentOption.ValidFor))
                             {
                                 var validIndices = BitmaskUtils.DecodeValidForBitmap(dependentOption.ValidFor);
                                 if (validIndices.Contains(controllerIndex.Value))
                                 {
                                     isValid = true;
                                 }
                             }
                             // If dependent option has no ValidFor, it might be valid for all or none?
                             // Salesforce usually implies ValidFor must exist for dependent values.
                             // If the value exists but isn't valid for this controller, fail.

                             if (!isValid && dependentOption != null)
                             {
                                 result = result.Merge(ValidationResult.Failure(
                                     fieldName,
                                     ValidationErrorCodes.InvalidDependentPicklist,
                                     $"Value '{value}' is not valid for the controlling value '{controllingVal}'."));
                             }
                        }
                    }
                }
            }
        }

        // Check for missing required fields on create
        if (isCreate)
        {
            var missingRequired = fieldMap.Values
                .Where(f => f.IsRequired && f.Createable && !record.ContainsKey(f.Name))
                .ToList();

            foreach (var field in missingRequired)
            {
                result = result.Merge(ValidationResult.Failure(
                    field.Name,
                    ValidationErrorCodes.Required,
                    $"Required field '{field.Label}' is missing."));
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateEntityAsync<T>(
        T entity,
        bool isCreate = true,
        CancellationToken cancellationToken = default) where T : class
    {
        var objectName = SalesforceMapper.GetObjectName<T>();
        var record = SalesforceMapper.ToSalesforceDictionary(entity, forCreate: isCreate, forUpdate: !isCreate);
        return await ValidateRecordAsync(objectName, record, isCreate, cancellationToken);
    }

    /// <inheritdoc/>
    public bool ValidatePicklistValue(SObjectField field, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return field.Nillable;

        if (!field.IsPicklist)
            return true;

        // For multi-select picklists, validate each value
        if (field.Type.Equals("multipicklist", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return values.All(v => IsValidPicklistValue(field, v.Trim()));
        }

        return IsValidPicklistValue(field, value);
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateDependentPicklistAsync(
        SObjectField field,
        string? value,
        string? controllingValue,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(value))
            return field.Nillable;

        // If not dependent or no controller, just validate existence
        if (!field.DependentPicklist || string.IsNullOrEmpty(field.ControllerName))
            return ValidatePicklistValue(field, value);

        // If controlling value is missing but required, we can't validate properly,
        // but typically a dependent field requires a controlling value.
        // If the controlling field is null, the dependent field should probably be null too.
        if (string.IsNullOrEmpty(controllingValue))
        {
            // If dependent field has a value but controlling is empty, that's invalid
            return false;
        }

        // Fallback: simple check if value is active.
        // Note: Without the object name or controlling field definition, we cannot robustly
        // determine if the value is valid for the specific controlling value.
        // For robust validation, use ValidateRecordAsync which has access to the full object schema.
        return field.PicklistValues
            .Where(p => p.Active)
            .Any(p => p.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsValidPicklistValue(SObjectField field, string value)
    {
        // For restricted picklists, must be an exact match
        if (field.RestrictedPicklist)
        {
            return field.PicklistValues
                .Where(p => p.Active)
                .Any(p => p.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        }

        // Non-restricted picklists allow any value
        return true;
    }

    private ValidationError? ValidateByType(SObjectField field, object value)
    {
        var stringValue = value.ToString() ?? "";

        return field.Type.ToLowerInvariant() switch
        {
            "string" or "textarea" or "encryptedstring" => ValidateString(field, stringValue),
            "email" => ValidateEmail(field, stringValue),
            "url" => ValidateUrl(field, stringValue),
            "phone" => ValidatePhone(field, stringValue),
            "picklist" or "multipicklist" => ValidatePicklist(field, stringValue),
            "reference" or "id" => ValidateId(field, stringValue),
            "boolean" => ValidateBoolean(field, value),
            "int" => ValidateInt(field, value),
            "double" or "currency" or "percent" => ValidateDouble(field, value),
            "date" => ValidateDate(field, value),
            "datetime" => ValidateDateTime(field, value),
            "time" => ValidateTime(field, value),
            _ => null
        };
    }

    private ValidationError? ValidateString(SObjectField field, string value)
    {
        if (field.Length > 0 && value.Length > field.Length)
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.MaxLength,
                $"Field '{field.Label}' exceeds maximum length of {field.Length}. Current length: {value.Length}.",
                value);
        }
        return null;
    }

    private ValidationError? ValidateEmail(SObjectField field, string value)
    {
        if (!EmailRegex.IsMatch(value))
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidEmail,
                $"Field '{field.Label}' is not a valid email address.",
                value);
        }
        return ValidateString(field, value);
    }

    private ValidationError? ValidateUrl(SObjectField field, string value)
    {
        if (!UrlRegex.IsMatch(value) && !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidUrl,
                $"Field '{field.Label}' is not a valid URL.",
                value);
        }
        return ValidateString(field, value);
    }

    private ValidationError? ValidatePhone(SObjectField field, string value)
    {
        if (!PhoneRegex.IsMatch(value))
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidPhone,
                $"Field '{field.Label}' contains invalid phone number characters.",
                value);
        }
        return ValidateString(field, value);
    }

    private ValidationError? ValidatePicklist(SObjectField field, string value)
    {
        if (!ValidatePicklistValue(field, value))
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidPicklist,
                $"Field '{field.Label}' has invalid picklist value '{value}'.",
                value);
        }
        return null;
    }

    private ValidationError? ValidateId(SObjectField field, string value)
    {
        if (!SalesforceIdRegex.IsMatch(value))
        {
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidIdFormat,
                $"Field '{field.Label}' is not a valid Salesforce ID format.",
                value);
        }
        return null;
    }

    private ValidationError? ValidateBoolean(SObjectField field, object value)
    {
        if (value is bool)
            return null;

        if (value is string s)
        {
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("false", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return new ValidationError(
            field.Name,
            ValidationErrorCodes.InvalidType,
            $"Field '{field.Label}' must be a boolean value.",
            value);
    }

    private ValidationError? ValidateInt(SObjectField field, object value)
    {
        if (value is int or long or short or byte)
            return null;

        if (value is string s && long.TryParse(s, out _))
            return null;

        return new ValidationError(
            field.Name,
            ValidationErrorCodes.InvalidType,
            $"Field '{field.Label}' must be an integer value.",
            value);
    }

    private ValidationError? ValidateDouble(SObjectField field, object value)
    {
        double numValue;
        if (value is double d)
            numValue = d;
        else if (value is decimal dec)
            numValue = (double)dec;
        else if (value is float f)
            numValue = f;
        else if (value is int i)
            numValue = i;
        else if (value is long l)
            numValue = l;
        else if (value is string s && double.TryParse(s, out var parsed))
            numValue = parsed;
        else
            return new ValidationError(
                field.Name,
                ValidationErrorCodes.InvalidType,
                $"Field '{field.Label}' must be a numeric value.",
                value);

        // Check precision
        if (field.Precision > 0)
        {
            var numStr = numValue.ToString("G");
            var parts = numStr.Split('.');
            var intDigits = parts[0].TrimStart('-').Length;
            var decDigits = parts.Length > 1 ? parts[1].Length : 0;

            var maxIntDigits = field.Precision - field.Scale;
            if (intDigits > maxIntDigits)
            {
                return new ValidationError(
                    field.Name,
                    ValidationErrorCodes.PrecisionExceeded,
                    $"Field '{field.Label}' exceeds precision. Max {maxIntDigits} integer digits allowed.",
                    value);
            }

            if (decDigits > field.Scale)
            {
                return new ValidationError(
                    field.Name,
                    ValidationErrorCodes.ScaleExceeded,
                    $"Field '{field.Label}' exceeds scale. Max {field.Scale} decimal places allowed.",
                    value);
            }
        }

        return null;
    }

    private ValidationError? ValidateDate(SObjectField field, object value)
    {
        if (value is DateOnly or DateTime or DateTimeOffset)
            return null;

        if (value is string s)
        {
            if (DateOnly.TryParse(s, out _) || DateTime.TryParse(s, out _))
                return null;
        }

        return new ValidationError(
            field.Name,
            ValidationErrorCodes.InvalidDate,
            $"Field '{field.Label}' is not a valid date.",
            value);
    }

    private ValidationError? ValidateDateTime(SObjectField field, object value)
    {
        if (value is DateTime or DateTimeOffset)
            return null;

        if (value is string s && DateTime.TryParse(s, out _))
            return null;

        return new ValidationError(
            field.Name,
            ValidationErrorCodes.InvalidDate,
            $"Field '{field.Label}' is not a valid datetime.",
            value);
    }

    private ValidationError? ValidateTime(SObjectField field, object value)
    {
        if (value is TimeOnly or TimeSpan)
            return null;

        if (value is string s && TimeOnly.TryParse(s, out _))
            return null;

        return new ValidationError(
            field.Name,
            ValidationErrorCodes.InvalidType,
            $"Field '{field.Label}' is not a valid time.",
            value);
    }

    private static bool IsNullOrEmpty(object? value)
    {
        return value switch
        {
            null => true,
            string s => string.IsNullOrEmpty(s),
            _ => false
        };
    }
}
