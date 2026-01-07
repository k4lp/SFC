using SalesforceCore.Services.Metadata;

namespace SalesforceCore.Validation;

/// <summary>
/// Engine for executing validation rules against records.
/// </summary>
public interface IValidationRuleEngine
{
    /// <summary>
    /// Registers a validation rule.
    /// </summary>
    void RegisterRule(IValidationRule rule);

    /// <summary>
    /// Registers multiple validation rules.
    /// </summary>
    void RegisterRules(IEnumerable<IValidationRule> rules);

    /// <summary>
    /// Removes a validation rule.
    /// </summary>
    void UnregisterRule(string ruleId);

    /// <summary>
    /// Gets all registered rules.
    /// </summary>
    IEnumerable<IValidationRule> GetRules(string? objectName = null);

    /// <summary>
    /// Validates a record against all applicable rules.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <param name="record">Record data.</param>
    /// <param name="isCreate">Whether this is a create operation.</param>
    /// <param name="originalRecord">Original record for updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined validation result.</returns>
    Task<ValidationResult> ValidateAsync(
        string objectName,
        IDictionary<string, object?> record,
        bool isCreate = true,
        IDictionary<string, object?>? originalRecord = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of the validation rule engine.
/// </summary>
public class ValidationRuleEngine : IValidationRuleEngine
{
    private readonly ISchemaService _schemaService;
    private readonly IFieldValidator _fieldValidator;
    private readonly Dictionary<string, IValidationRule> _rules = new();
    private readonly object _rulesLock = new();

    private const int MaxRecursionDepth = 10;
    private static readonly AsyncLocal<int> _recursionDepth = new();
    // Track rule execution stack for circular dependency detection
    private static readonly AsyncLocal<Stack<string>> _ruleStack = new();

    public ValidationRuleEngine(ISchemaService schemaService, IFieldValidator fieldValidator)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _fieldValidator = fieldValidator ?? throw new ArgumentNullException(nameof(fieldValidator));
    }

    /// <inheritdoc/>
    public void RegisterRule(IValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_rulesLock)
        {
            _rules[rule.RuleId] = rule;
        }
    }

    /// <inheritdoc/>
    public void RegisterRules(IEnumerable<IValidationRule> rules)
    {
        foreach (var rule in rules)
        {
            RegisterRule(rule);
        }
    }

    /// <inheritdoc/>
    public void UnregisterRule(string ruleId)
    {
        lock (_rulesLock)
        {
            _rules.Remove(ruleId);
        }
    }

    /// <inheritdoc/>
    public IEnumerable<IValidationRule> GetRules(string? objectName = null)
    {
        lock (_rulesLock)
        {
            var rules = _rules.Values.AsEnumerable();
            if (objectName != null)
            {
                rules = rules.Where(r =>
                    r.ObjectName == null ||
                    r.ObjectName.Equals(objectName, StringComparison.OrdinalIgnoreCase));
            }
            return rules.OrderBy(r => r.Priority).ToList();
        }
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(
        string objectName,
        IDictionary<string, object?> record,
        bool isCreate = true,
        IDictionary<string, object?>? originalRecord = null,
        CancellationToken cancellationToken = default)
    {
        // Initialize rule stack if needed
        _ruleStack.Value ??= new Stack<string>();
        var stack = _ruleStack.Value;

        // Recursion guard with improved diagnostics
        var currentDepth = _recursionDepth.Value;
        if (currentDepth >= MaxRecursionDepth)
        {
            var stackTrace = string.Join(" -> ", stack.Reverse());
            return ValidationResult.Failure("_global", "RECURSION_LIMIT_EXCEEDED",
                $"Validation recursion limit ({MaxRecursionDepth}) exceeded. " +
                $"Rule execution path: {stackTrace}. Check for circular dependencies in validation rules.");
        }

        _recursionDepth.Value = currentDepth + 1;

        try
        {
            // First, run schema-based validation
            var result = await _fieldValidator.ValidateRecordAsync(objectName, record, isCreate, cancellationToken);

            // Get field metadata for rule context
            var fieldMap = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);

            // Create validation context
            var context = new ValidationContext
            {
                ObjectName = objectName,
                Record = record,
                OriginalRecord = originalRecord,
                FieldMap = fieldMap,
                IsCreate = isCreate
            };

            // Get applicable rules
            var applicableRules = GetRules(objectName);

            // Execute rules in priority order
            foreach (var rule in applicableRules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip field-specific rules if the field isn't being set
                if (rule.FieldName != null && !record.ContainsKey(rule.FieldName))
                    continue;

                // Check for circular dependency
                if (stack.Contains(rule.RuleId))
                {
                    var circularPath = string.Join(" -> ", stack.Reverse()) + " -> " + rule.RuleId;
                    return ValidationResult.Failure("_global", "CIRCULAR_DEPENDENCY",
                        $"Circular dependency detected in validation rules: {circularPath}");
                }

                // Track current rule in stack
                stack.Push(rule.RuleId);

                try
                {
                    var ruleResult = await rule.ValidateAsync(context);
                    result = result.Merge(ruleResult);

                    if (rule.StopOnFailure && !ruleResult.IsValid)
                        break;
                }
                catch (Exception ex)
                {
                    // Log error but continue with other rules
                    result = result.Merge(ValidationResult.Failure(
                        rule.FieldName ?? "_rule",
                        "RULE_EXECUTION_ERROR",
                        $"Validation rule '{rule.RuleId}' failed: {ex.Message}"));
                }
                finally
                {
                    stack.Pop();
                }
            }

            return result;
        }
        finally
        {
            _recursionDepth.Value = currentDepth;
        }
    }
}

/// <summary>
/// Fluent builder for configuring validation rules for an object.
/// </summary>
public class ValidationRuleBuilder
{
    private readonly string _objectName;
    private readonly List<IValidationRule> _rules = new();
    private int _priorityCounter = 100;

    public ValidationRuleBuilder(string objectName)
    {
        _objectName = objectName;
    }

    /// <summary>
    /// Creates a builder for an object.
    /// </summary>
    public static ValidationRuleBuilder ForObject(string objectName)
        => new(objectName);

    /// <summary>
    /// Adds a custom validation rule.
    /// </summary>
    public ValidationRuleBuilder AddRule(IValidationRule rule)
    {
        _rules.Add(rule);
        return this;
    }

    /// <summary>
    /// Adds a lambda-based rule.
    /// </summary>
    public ValidationRuleBuilder AddRule(
        string ruleId,
        string description,
        Func<ValidationContext, ValidationResult> validator,
        string? fieldName = null)
    {
        _rules.Add(new LambdaValidationRule(
            ruleId,
            description,
            ctx => Task.FromResult(validator(ctx)),
            _objectName,
            fieldName)
        { Priority = _priorityCounter++ });
        return this;
    }

    /// <summary>
    /// Adds a required-when rule.
    /// </summary>
    public ValidationRuleBuilder RequireWhen(
        string requiredField,
        string conditionField,
        object conditionValue)
    {
        _rules.Add(CommonValidationRules.RequiredWhen(
            $"{_objectName}_{requiredField}_required_when_{conditionField}",
            requiredField,
            conditionField,
            conditionValue,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds a pattern validation rule.
    /// </summary>
    public ValidationRuleBuilder MatchPattern(
        string fieldName,
        string pattern,
        string errorMessage)
    {
        _rules.Add(CommonValidationRules.MatchesPattern(
            $"{_objectName}_{fieldName}_pattern",
            fieldName,
            pattern,
            errorMessage,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds a date in future rule.
    /// </summary>
    public ValidationRuleBuilder DateMustBeFuture(string fieldName)
    {
        _rules.Add(CommonValidationRules.DateInFuture(
            $"{_objectName}_{fieldName}_future",
            fieldName,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds a date in past rule.
    /// </summary>
    public ValidationRuleBuilder DateMustBePast(string fieldName)
    {
        _rules.Add(CommonValidationRules.DateInPast(
            $"{_objectName}_{fieldName}_past",
            fieldName,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds a value range rule.
    /// </summary>
    public ValidationRuleBuilder ValueBetween<T>(string fieldName, T min, T max) where T : IComparable<T>
    {
        _rules.Add(CommonValidationRules.ValueInRange(
            $"{_objectName}_{fieldName}_range",
            fieldName,
            min,
            max,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds a cannot-be-cleared rule.
    /// </summary>
    public ValidationRuleBuilder CannotClear(string fieldName)
    {
        _rules.Add(CommonValidationRules.CannotBeCleared(
            $"{_objectName}_{fieldName}_cannot_clear",
            fieldName,
            _objectName));
        return this;
    }

    /// <summary>
    /// Adds an email domain validation rule.
    /// </summary>
    public ValidationRuleBuilder EmailDomainMustBe(string fieldName, params string[] allowedDomains)
    {
        _rules.Add(CommonValidationRules.EmailDomain(
            $"{_objectName}_{fieldName}_email_domain",
            fieldName,
            allowedDomains,
            _objectName));
        return this;
    }

    /// <summary>
    /// Builds and returns all rules.
    /// </summary>
    public IReadOnlyList<IValidationRule> Build() => _rules.AsReadOnly();

    /// <summary>
    /// Registers all rules with the engine.
    /// </summary>
    public void RegisterWith(IValidationRuleEngine engine)
    {
        engine.RegisterRules(_rules);
    }
}
