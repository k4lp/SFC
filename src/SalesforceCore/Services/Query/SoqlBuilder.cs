using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalesforceCore.Attributes;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Query;

/// <summary>
/// Fluent builder for constructing SOQL queries with proper sanitization.
/// </summary>
public class SoqlBuilder
{
    private readonly string _objectName;
    private readonly ILogger<SoqlBuilder> _logger;
    private readonly List<string> _selectFields = new();
    private readonly List<string> _whereClauses = new();
    private readonly List<string> _orderByFields = new();
    private readonly List<string> _groupByFields = new();
    private string? _havingClause;
    private int? _limit;
    private int? _offset;
    private bool _forUpdate;
    private bool _forView;
    private bool _forReference;

    /// <summary>
    /// Creates a new SOQL builder for the specified object.
    /// </summary>
    /// <param name="objectName">The Salesforce object to query.</param>
    /// <param name="logger">Optional logger for diagnostics (unsafe clause usage, invalid inputs).</param>
    public SoqlBuilder(string objectName, ILogger<SoqlBuilder>? logger = null)
        : this(objectName, isRelationshipName: false, logger)
    {
    }

    internal SoqlBuilder(string objectName, bool isRelationshipName, ILogger<SoqlBuilder>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        _logger = logger ?? NullLogger<SoqlBuilder>.Instance;
        _objectName = isRelationshipName
            ? SecurityUtils.SanitizeFieldName(objectName)
            : SecurityUtils.SanitizeObjectName(objectName);
    }

    /// <summary>
    /// Creates a new SOQL builder for the specified object.
    /// </summary>
    /// <param name="objectName">The Salesforce object to query.</param>
    /// <param name="logger">Optional logger for diagnostics (unsafe clause usage, invalid inputs).</param>
    public static SoqlBuilder From(string objectName, ILogger<SoqlBuilder>? logger = null) => new(objectName, logger);

    #region SELECT

    /// <summary>
    /// Adds a field to the SELECT clause.
    /// </summary>
    public SoqlBuilder Select(string field)
    {
        if (!string.IsNullOrWhiteSpace(field))
        {
            _selectFields.Add(SecurityUtils.SanitizeFieldName(field));
        }
        return this;
    }

    /// <summary>
    /// Adds multiple fields to the SELECT clause.
    /// </summary>
    public SoqlBuilder Select(params string[] fields)
    {
        foreach (var field in fields)
        {
            Select(field);
        }
        return this;
    }

    /// <summary>
    /// Adds multiple fields to the SELECT clause.
    /// </summary>
    public SoqlBuilder Select(IEnumerable<string> fields)
    {
        foreach (var field in fields)
        {
            Select(field);
        }
        return this;
    }

    /// <summary>
    /// Adds a COUNT() aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectCount(string? alias = null)
    {
        _selectFields.Add(alias != null ? $"COUNT() {SecurityUtils.SanitizeFieldName(alias)}" : "COUNT()");
        return this;
    }

    /// <summary>
    /// Adds a COUNT(field) aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectCount(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"COUNT({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"COUNT({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a SUM aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectSum(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"SUM({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"SUM({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds an AVG aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectAvg(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"AVG({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"AVG({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a MIN aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectMin(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"MIN({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"MIN({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a MAX aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectMax(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"MAX({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"MAX({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a COUNT_DISTINCT aggregate to the SELECT clause.
    /// </summary>
    public SoqlBuilder SelectCountDistinct(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"COUNT_DISTINCT({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"COUNT_DISTINCT({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a FORMAT() function to apply localized formatting to a field.
    /// Use for number, date, time, and currency fields.
    /// </summary>
    /// <param name="field">Field to format.</param>
    /// <param name="alias">Optional alias for the formatted field (required when selecting same field multiple times).</param>
    public SoqlBuilder SelectFormat(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"FORMAT({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"FORMAT({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a toLabel() function to return translated picklist values.
    /// </summary>
    /// <param name="field">Picklist field to translate.</param>
    /// <param name="alias">Optional alias.</param>
    public SoqlBuilder SelectToLabel(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"toLabel({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"toLabel({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a convertCurrency() function to convert currency fields to the user's currency.
    /// </summary>
    /// <param name="field">Currency field to convert.</param>
    /// <param name="alias">Optional alias.</param>
    public SoqlBuilder SelectConvertCurrency(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"convertCurrency({sanitizedField}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"convertCurrency({sanitizedField})");
        return this;
    }

    /// <summary>
    /// Adds a FORMAT(convertCurrency()) to get localized formatted currency.
    /// Combines currency conversion with locale formatting.
    /// </summary>
    /// <param name="field">Currency field to convert and format.</param>
    /// <param name="alias">Optional alias.</param>
    public SoqlBuilder SelectFormatConvertCurrency(string field, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _selectFields.Add(alias != null
            ? $"FORMAT(convertCurrency({sanitizedField})) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"FORMAT(convertCurrency({sanitizedField}))");
        return this;
    }

    /// <summary>
    /// Adds a date function with optional convertTimezone for datetime fields.
    /// </summary>
    /// <param name="dateFunction">The date function to apply.</param>
    /// <param name="field">DateTime field to extract from.</param>
    /// <param name="convertTimezone">Whether to convert to user's timezone before extraction.</param>
    /// <param name="alias">Optional alias.</param>
    public SoqlBuilder SelectDateFunction(DateFunction dateFunction, string field, bool convertTimezone = false, string? alias = null)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var fieldExpr = convertTimezone ? $"convertTimezone({sanitizedField})" : sanitizedField;
        var funcName = dateFunction.ToString();

        _selectFields.Add(alias != null
            ? $"{funcName}({fieldExpr}) {SecurityUtils.SanitizeFieldName(alias)}"
            : $"{funcName}({fieldExpr})");
        return this;
    }

    /// <summary>
    /// Adds CALENDAR_YEAR() function for date grouping.
    /// </summary>
    public SoqlBuilder SelectCalendarYear(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.CALENDAR_YEAR, field, convertTimezone, alias);

    /// <summary>
    /// Adds CALENDAR_QUARTER() function for date grouping.
    /// </summary>
    public SoqlBuilder SelectCalendarQuarter(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.CALENDAR_QUARTER, field, convertTimezone, alias);

    /// <summary>
    /// Adds CALENDAR_MONTH() function for date grouping.
    /// </summary>
    public SoqlBuilder SelectCalendarMonth(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.CALENDAR_MONTH, field, convertTimezone, alias);

    /// <summary>
    /// Adds FISCAL_YEAR() function for fiscal year grouping.
    /// </summary>
    public SoqlBuilder SelectFiscalYear(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.FISCAL_YEAR, field, convertTimezone, alias);

    /// <summary>
    /// Adds FISCAL_QUARTER() function for fiscal quarter grouping.
    /// </summary>
    public SoqlBuilder SelectFiscalQuarter(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.FISCAL_QUARTER, field, convertTimezone, alias);

    /// <summary>
    /// Adds FISCAL_MONTH() function for fiscal month grouping.
    /// </summary>
    public SoqlBuilder SelectFiscalMonth(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.FISCAL_MONTH, field, convertTimezone, alias);

    /// <summary>
    /// Adds DAY_IN_MONTH() function for day extraction.
    /// </summary>
    public SoqlBuilder SelectDayInMonth(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.DAY_IN_MONTH, field, convertTimezone, alias);

    /// <summary>
    /// Adds DAY_IN_WEEK() function for weekday extraction (1 = Sunday).
    /// </summary>
    public SoqlBuilder SelectDayInWeek(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.DAY_IN_WEEK, field, convertTimezone, alias);

    /// <summary>
    /// Adds DAY_IN_YEAR() function for day-of-year extraction.
    /// </summary>
    public SoqlBuilder SelectDayInYear(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.DAY_IN_YEAR, field, convertTimezone, alias);

    /// <summary>
    /// Adds DAY_ONLY() function to extract date from datetime.
    /// </summary>
    public SoqlBuilder SelectDayOnly(string field, string? alias = null)
        => SelectDateFunction(DateFunction.DAY_ONLY, field, false, alias);

    /// <summary>
    /// Adds HOUR_IN_DAY() function for hour extraction (0-23).
    /// </summary>
    public SoqlBuilder SelectHourInDay(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.HOUR_IN_DAY, field, convertTimezone, alias);

    /// <summary>
    /// Adds WEEK_IN_MONTH() function for week-of-month extraction.
    /// </summary>
    public SoqlBuilder SelectWeekInMonth(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.WEEK_IN_MONTH, field, convertTimezone, alias);

    /// <summary>
    /// Adds WEEK_IN_YEAR() function for week-of-year extraction.
    /// </summary>
    public SoqlBuilder SelectWeekInYear(string field, bool convertTimezone = false, string? alias = null)
        => SelectDateFunction(DateFunction.WEEK_IN_YEAR, field, convertTimezone, alias);

    /// <summary>
    /// Adds a subquery for related records.
    /// </summary>
    public SoqlBuilder SelectSubQuery(string relationshipName, Action<SoqlBuilder> configure)
    {
        var subBuilder = new SoqlBuilder(relationshipName, isRelationshipName: true, _logger);
        configure(subBuilder);
        _selectFields.Add($"({subBuilder.Build()})");
        return this;
    }

    #endregion

    #region WHERE

    /// <summary>
    /// Adds a WHERE condition with equals comparison.
    /// </summary>
    public SoqlBuilder Where(string field, object? value)
    {
        return WhereEquals(field, value);
    }

    /// <summary>
    /// Adds a WHERE condition with equals comparison.
    /// </summary>
    public SoqlBuilder WhereEquals(string field, object? value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} = {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with not equals comparison.
    /// </summary>
    public SoqlBuilder WhereNotEquals(string field, object? value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} != {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition for null check.
    /// </summary>
    public SoqlBuilder WhereNull(string field)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _whereClauses.Add($"{sanitizedField} = NULL");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition for not null check.
    /// </summary>
    public SoqlBuilder WhereNotNull(string field)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _whereClauses.Add($"{sanitizedField} != NULL");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with greater than comparison.
    /// </summary>
    public SoqlBuilder WhereGreaterThan(string field, object value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} > {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with greater than or equal comparison.
    /// </summary>
    public SoqlBuilder WhereGreaterThanOrEqual(string field, object value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} >= {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with less than comparison.
    /// </summary>
    public SoqlBuilder WhereLessThan(string field, object value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} < {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with less than or equal comparison.
    /// </summary>
    public SoqlBuilder WhereLessThanOrEqual(string field, object value)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = FormatValue(value);
        _whereClauses.Add($"{sanitizedField} <= {sanitizedValue}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with LIKE pattern matching.
    /// </summary>
    public SoqlBuilder WhereLike(string field, string pattern)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedPattern = SecurityUtils.SanitizeSoql(pattern).Replace("\\", "\\\\");
        _whereClauses.Add($"{sanitizedField} LIKE '{sanitizedPattern}'");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition for values in a list.
    /// </summary>
    public SoqlBuilder WhereIn(string field, IEnumerable<object?> values)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var valueList = values?.ToList() ?? new List<object?>();
        if (valueList.Count == 0)
        {
            _whereClauses.Add("Id = NULL");
            return this;
        }

        var formattedValues = string.Join(", ", valueList.Select(FormatValue));
        _whereClauses.Add($"{sanitizedField} IN ({formattedValues})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition for values not in a list.
    /// </summary>
    public SoqlBuilder WhereNotIn(string field, IEnumerable<object?> values)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var valueList = values?.ToList() ?? new List<object?>();
        if (valueList.Count == 0)
        {
            _whereClauses.Add("Id != NULL");
            return this;
        }

        var formattedValues = string.Join(", ", valueList.Select(FormatValue));
        _whereClauses.Add($"{sanitizedField} NOT IN ({formattedValues})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using an IN (subquery) clause.
    /// </summary>
    /// <remarks>
    /// The subquery should typically select a single field, for example:
    /// <code>
    /// var subquery = SoqlBuilder.From("PermissionSetAssignment")
    ///     .Select("PermissionSetId")
    ///     .WhereEquals("AssigneeId", userId);
    ///
    /// var query = SoqlBuilder.From("SetupEntityAccess")
    ///     .Select("Id")
    ///     .WhereInSubquery("ParentId", subquery)
    ///     .Build();
    /// </code>
    /// </remarks>
    /// <param name="field">The field to compare.</param>
    /// <param name="subquery">A <see cref="SoqlBuilder"/> that renders the subquery.</param>
    public SoqlBuilder WhereInSubquery(string field, SoqlBuilder subquery)
    {
        ArgumentNullException.ThrowIfNull(subquery);

        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var renderedSubquery = subquery.Build();
        if (string.IsNullOrWhiteSpace(renderedSubquery))
        {
            throw new ArgumentException("Subquery cannot be empty.", nameof(subquery));
        }

        _whereClauses.Add($"{sanitizedField} IN ({renderedSubquery})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a NOT IN (subquery) clause.
    /// </summary>
    /// <param name="field">The field to compare.</param>
    /// <param name="subquery">A <see cref="SoqlBuilder"/> that renders the subquery.</param>
    public SoqlBuilder WhereNotInSubquery(string field, SoqlBuilder subquery)
    {
        ArgumentNullException.ThrowIfNull(subquery);

        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var renderedSubquery = subquery.Build();
        if (string.IsNullOrWhiteSpace(renderedSubquery))
        {
            throw new ArgumentException("Subquery cannot be empty.", nameof(subquery));
        }

        _whereClauses.Add($"{sanitizedField} NOT IN ({renderedSubquery})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with INCLUDES (multi-select picklist).
    /// </summary>
    public SoqlBuilder WhereIncludes(string field, IEnumerable<string> values)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var valueList = values?.ToList() ?? new List<string>();
        if (valueList.Count == 0)
        {
            _logger.LogWarning("INCLUDES received no values for field {Field}. Rendering an always-false predicate.", sanitizedField);
            _whereClauses.Add("Id = NULL");
            return this;
        }

        var formattedValues = string.Join(";", valueList.Select(SecurityUtils.SanitizeForSoql));
        _whereClauses.Add($"{sanitizedField} INCLUDES ('{formattedValues}')");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition with EXCLUDES (multi-select picklist).
    /// </summary>
    public SoqlBuilder WhereExcludes(string field, IEnumerable<string> values)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var valueList = values?.ToList() ?? new List<string>();
        if (valueList.Count == 0)
        {
            _logger.LogWarning("EXCLUDES received no values for field {Field}. Rendering an always-true predicate.", sanitizedField);
            _whereClauses.Add("Id != NULL");
            return this;
        }

        var formattedValues = string.Join(";", valueList.Select(SecurityUtils.SanitizeForSoql));
        _whereClauses.Add($"{sanitizedField} EXCLUDES ('{formattedValues}')");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition built from a SoqlCondition object.
    /// Use this for complex conditions that cannot be expressed using the simple Where methods.
    /// </summary>
    /// <param name="condition">A pre-validated condition object.</param>
    /// <returns>This builder for method chaining.</returns>
    public SoqlBuilder WhereCondition(SoqlCondition condition)
    {
        if (condition == null)
            throw new ArgumentNullException(nameof(condition));

        var rendered = condition.Render();
        if (!string.IsNullOrWhiteSpace(rendered))
        {
            _whereClauses.Add(rendered);
        }
        return this;
    }

    /// <summary>
    /// Adds a pre-built WHERE clause that was generated by a trusted internal source
    /// (like the LINQ expression visitor). This is NOT for external use.
    /// </summary>
    /// <remarks>
    /// This method exists ONLY for use by SoqlExpressionVisitor and the LINQ provider,
    /// which generate safe SOQL from type-safe expressions. It is internal to prevent misuse.
    /// </remarks>
    /// <param name="trustedClause">A WHERE clause generated by SoqlExpressionVisitor.</param>
    internal SoqlBuilder WhereExpressionVisitorClause(string trustedClause)
    {
        // This is safe because SoqlExpressionVisitor generates sanitized SOQL
        // from LINQ expressions, not from raw user input.
        if (!string.IsNullOrWhiteSpace(trustedClause))
        {
            _whereClauses.Add(trustedClause);
        }
        return this;
    }

    /// <summary>
    /// Adds a compound WHERE condition using AND.
    /// </summary>
    /// <param name="conditions">Multiple conditions to AND together.</param>
    /// <returns>This builder for method chaining.</returns>
    public SoqlBuilder WhereAnd(params SoqlCondition[] conditions)
    {
        if (conditions == null || conditions.Length == 0)
            return this;

        var validConditions = conditions.Where(c => c != null).ToList();
        if (validConditions.Count == 0)
            return this;

        if (validConditions.Count == 1)
        {
            return WhereCondition(validConditions[0]);
        }

        var rendered = string.Join(" AND ", validConditions.Select(c => $"({c.Render()})"));
        _whereClauses.Add(rendered);
        return this;
    }

    /// <summary>
    /// Adds a compound WHERE condition using OR.
    /// </summary>
    /// <param name="conditions">Multiple conditions to OR together.</param>
    /// <returns>This builder for method chaining.</returns>
    public SoqlBuilder WhereOr(params SoqlCondition[] conditions)
    {
        if (conditions == null || conditions.Length == 0)
            return this;

        var validConditions = conditions.Where(c => c != null).ToList();
        if (validConditions.Count == 0)
            return this;

        if (validConditions.Count == 1)
        {
            return WhereCondition(validConditions[0]);
        }

        var rendered = string.Join(" OR ", validConditions.Select(c => $"({c.Render()})"));
        _whereClauses.Add($"({rendered})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a date literal.
    /// </summary>
    public SoqlBuilder WhereDateLiteral(string field, DateLiteral literal)
    {
        if (DateLiteralHelper.RequiresParameter(literal))
        {
            _logger.LogWarning("Date literal {Literal} requires a parameter. Use WhereDateLiteralN instead.", literal);
            throw new ArgumentException($"Date literal {literal} requires a parameter. Use WhereDateLiteralN.", nameof(literal));
        }

        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _whereClauses.Add($"{sanitizedField} = {literal}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a parameterized date literal (e.g., LAST_N_DAYS:30).
    /// </summary>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="dateLiteralExpression">The date literal expression (use DateLiteralHelper).</param>
    public SoqlBuilder WhereDateLiteralN(string field, string dateLiteralExpression)
    {
        if (!DateLiteralNCondition.IsValidDateLiteralNExpression(dateLiteralExpression))
        {
            throw new ArgumentException($"Invalid date literal expression: {dateLiteralExpression}", nameof(dateLiteralExpression));
        }

        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _whereClauses.Add($"{sanitizedField} = {dateLiteralExpression}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a date literal with comparison operator.
    /// </summary>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="op">Comparison operator (=, !=, &gt;, &lt;, &gt;=, &lt;=).</param>
    /// <param name="literal">The date literal.</param>
    public SoqlBuilder WhereDateLiteralCompare(string field, string op, DateLiteral literal)
    {
        if (DateLiteralHelper.RequiresParameter(literal))
        {
            _logger.LogWarning("Date literal {Literal} requires a parameter. Use WhereDateLiteralN instead.", literal);
            throw new ArgumentException($"Date literal {literal} requires a parameter. Use WhereDateLiteralN.", nameof(literal));
        }

        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var normalizedOp = NormalizeComparisonOperator(op);
        _whereClauses.Add($"{sanitizedField} {normalizedOp} {literal}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using toLabel() for translated picklist comparison.
    /// Example: WHERE toLabel(Industry) = 'Agriculture'
    /// </summary>
    /// <param name="field">The picklist field.</param>
    /// <param name="translatedValue">The translated value to compare against.</param>
    public SoqlBuilder WhereToLabelEquals(string field, string translatedValue)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var sanitizedValue = SecurityUtils.SanitizeForSoql(translatedValue);
        _whereClauses.Add($"toLabel({sanitizedField}) = '{sanitizedValue}'");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a date function comparison.
    /// Example: WHERE CALENDAR_YEAR(CreatedDate) = 2024
    /// </summary>
    /// <param name="dateFunction">The date function to apply.</param>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="value">The value to compare against.</param>
    /// <param name="convertTimezone">Whether to convert to user's timezone.</param>
    public SoqlBuilder WhereDateFunction(DateFunction dateFunction, string field, int value, bool convertTimezone = false)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var fieldExpr = convertTimezone ? $"convertTimezone({sanitizedField})" : sanitizedField;
        _whereClauses.Add($"{dateFunction}({fieldExpr}) = {value}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition using a date function with comparison operator.
    /// </summary>
    /// <param name="dateFunction">The date function to apply.</param>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="op">Comparison operator (=, !=, &gt;, &lt;, &gt;=, &lt;=).</param>
    /// <param name="value">The value to compare against.</param>
    /// <param name="convertTimezone">Whether to convert to user's timezone.</param>
    public SoqlBuilder WhereDateFunctionCompare(DateFunction dateFunction, string field, string op, int value, bool convertTimezone = false)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var fieldExpr = convertTimezone ? $"convertTimezone({sanitizedField})" : sanitizedField;
        var normalizedOp = NormalizeComparisonOperator(op);
        _whereClauses.Add($"{dateFunction}({fieldExpr}) {normalizedOp} {value}");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition between two dates.
    /// </summary>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="startDate">The start date (inclusive).</param>
    /// <param name="endDate">The end date (inclusive).</param>
    public SoqlBuilder WhereDateBetween(string field, DateTime startDate, DateTime endDate)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);

        // Auto-swap dates if inverted to prevent always-empty results
        var (start, end) = startDate > endDate
            ? (FormatDateTimeUtc(endDate), FormatDateTimeUtc(startDate))
            : (FormatDateTimeUtc(startDate), FormatDateTimeUtc(endDate));

        // Wrap in parentheses for correct precedence in compound conditions
        _whereClauses.Add($"({sanitizedField} >= {start} AND {sanitizedField} <= {end})");
        return this;
    }

    /// <summary>
    /// Adds a WHERE condition between two date-only values.
    /// </summary>
    /// <param name="field">The date field.</param>
    /// <param name="startDate">The start date (inclusive).</param>
    /// <param name="endDate">The end date (inclusive).</param>
    public SoqlBuilder WhereDateBetween(string field, DateOnly startDate, DateOnly endDate)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);

        // Auto-swap dates if inverted to prevent always-empty results
        var (actualStart, actualEnd) = startDate > endDate ? (endDate, startDate) : (startDate, endDate);
        var start = actualStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = actualEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Wrap in parentheses for correct precedence in compound conditions
        _whereClauses.Add($"({sanitizedField} >= {start} AND {sanitizedField} <= {end})");
        return this;
    }

    #endregion

    #region ORDER BY

    /// <summary>
    /// Adds an ORDER BY clause (ascending).
    /// </summary>
    public SoqlBuilder OrderBy(string field)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _orderByFields.Add($"{sanitizedField} ASC");
        return this;
    }

    /// <summary>
    /// Adds an ORDER BY clause (descending).
    /// </summary>
    public SoqlBuilder OrderByDescending(string field)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _orderByFields.Add($"{sanitizedField} DESC");
        return this;
    }

    /// <summary>
    /// Adds an ORDER BY clause with nulls first.
    /// </summary>
    public SoqlBuilder OrderByNullsFirst(string field, bool descending = false)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var direction = descending ? "DESC" : "ASC";
        _orderByFields.Add($"{sanitizedField} {direction} NULLS FIRST");
        return this;
    }

    /// <summary>
    /// Adds an ORDER BY clause with nulls last.
    /// </summary>
    public SoqlBuilder OrderByNullsLast(string field, bool descending = false)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var direction = descending ? "DESC" : "ASC";
        _orderByFields.Add($"{sanitizedField} {direction} NULLS LAST");
        return this;
    }

    #endregion

    #region GROUP BY / HAVING

    /// <summary>
    /// Adds a GROUP BY clause.
    /// </summary>
    public SoqlBuilder GroupBy(string field)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        _groupByFields.Add(sanitizedField);
        return this;
    }

    /// <summary>
    /// Adds multiple GROUP BY fields.
    /// </summary>
    public SoqlBuilder GroupBy(params string[] fields)
    {
        foreach (var field in fields)
        {
            GroupBy(field);
        }
        return this;
    }

    /// <summary>
    /// Adds a typed HAVING clause built from aggregate conditions.
    /// </summary>
    public SoqlBuilder Having(SoqlAggregateCondition condition)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        _havingClause = condition.Render();
        return this;
    }

    /// <summary>
    /// Adds a HAVING clause.
    /// </summary>
    /// <remarks>
    /// This overload accepts raw SOQL and does not sanitize input.
    /// Prefer <see cref="HavingAggregate"/> or typed helpers when possible.
    /// </remarks>
    [Obsolete("Having(string) accepts raw SOQL and does not sanitize input. Prefer HavingAggregate or typed helpers.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SoqlBuilder Having(string condition, bool allowUnsafe = false)
    {
        if (!allowUnsafe)
        {
            _logger.LogWarning("Unsafe HAVING clause was rejected. Use typed aggregate helpers instead.");
            throw new NotSupportedException("Raw HAVING clauses are unsafe. Use Having(SoqlAggregateCondition) or HavingAggregate instead.");
        }

        _havingClause = condition;
        return this;
    }

    /// <summary>
    /// Adds a GROUP BY clause with a date function.
    /// </summary>
    /// <param name="dateFunction">The date function to apply.</param>
    /// <param name="field">The date/datetime field.</param>
    /// <param name="convertTimezone">Whether to convert to user's timezone.</param>
    public SoqlBuilder GroupByDateFunction(DateFunction dateFunction, string field, bool convertTimezone = false)
    {
        var sanitizedField = SecurityUtils.SanitizeFieldName(field);
        var fieldExpr = convertTimezone ? $"convertTimezone({sanitizedField})" : sanitizedField;
        _groupByFields.Add($"{dateFunction}({fieldExpr})");
        return this;
    }

    /// <summary>
    /// Adds GROUP BY ROLLUP for summary rows.
    /// </summary>
    /// <param name="fields">Fields to include in ROLLUP.</param>
    public SoqlBuilder GroupByRollup(params string[] fields)
    {
        var sanitizedFields = fields.Select(f => SecurityUtils.SanitizeFieldName(f));
        _groupByFields.Add($"ROLLUP({string.Join(", ", sanitizedFields)})");
        return this;
    }

    /// <summary>
    /// Adds GROUP BY CUBE for cross-tabulation.
    /// </summary>
    /// <param name="fields">Fields to include in CUBE.</param>
    public SoqlBuilder GroupByCube(params string[] fields)
    {
        var sanitizedFields = fields.Select(f => SecurityUtils.SanitizeFieldName(f));
        _groupByFields.Add($"CUBE({string.Join(", ", sanitizedFields)})");
        return this;
    }

    /// <summary>
    /// Adds a HAVING clause with aggregate comparison.
    /// </summary>
    /// <param name="aggregateFunc">The aggregate function (e.g., COUNT, SUM, AVG).</param>
    /// <param name="field">The field to aggregate (null for COUNT()).</param>
    /// <param name="op">Comparison operator.</param>
    /// <param name="value">The value to compare against.</param>
    public SoqlBuilder HavingAggregate(string aggregateFunc, string? field, string op, object value)
    {
        try
        {
            var normalizedFunc = NormalizeAggregateFunction(aggregateFunc);
            var normalizedOp = NormalizeComparisonOperator(op);
            var funcExpr = field != null
                ? $"{normalizedFunc}({SecurityUtils.SanitizeFieldName(field)})"
                : $"{normalizedFunc}()";
            _havingClause = $"{funcExpr} {normalizedOp} {FormatValue(value)}";
            return this;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid HAVING aggregate specification rejected.");
            throw;
        }
    }

    /// <summary>
    /// Adds HAVING COUNT() condition.
    /// </summary>
    public SoqlBuilder HavingCount(string op, int value)
        => HavingAggregate("COUNT", null, op, value);

    /// <summary>
    /// Adds HAVING SUM(field) condition.
    /// </summary>
    public SoqlBuilder HavingSum(string field, string op, object value)
        => HavingAggregate("SUM", field, op, value);

    /// <summary>
    /// Adds HAVING AVG(field) condition.
    /// </summary>
    public SoqlBuilder HavingAvg(string field, string op, object value)
        => HavingAggregate("AVG", field, op, value);

    #endregion

    #region LIMIT / OFFSET

    /// <summary>
    /// Sets the LIMIT clause.
    /// </summary>
    public SoqlBuilder Limit(int count)
    {
        if (count < 0)
            throw new ArgumentException("Limit must be non-negative", nameof(count));

        _limit = count;
        return this;
    }

    /// <summary>
    /// Sets the OFFSET clause.
    /// </summary>
    public SoqlBuilder Offset(int count)
    {
        if (count < 0)
            throw new ArgumentException("Offset must be non-negative", nameof(count));

        _offset = count;
        return this;
    }

    /// <summary>
    /// Sets both LIMIT and OFFSET for pagination.
    /// </summary>
    public SoqlBuilder Page(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
            throw new ArgumentException("Page number must be at least 1", nameof(pageNumber));
        if (pageSize < 1)
            throw new ArgumentException("Page size must be at least 1", nameof(pageSize));

        _limit = pageSize;
        _offset = (pageNumber - 1) * pageSize;
        return this;
    }

    #endregion

    #region Locking

    /// <summary>
    /// Adds FOR UPDATE clause (record locking).
    /// </summary>
    public SoqlBuilder ForUpdate()
    {
        _forUpdate = true;
        return this;
    }

    /// <summary>
    /// Adds FOR VIEW clause (tracking recently viewed).
    /// </summary>
    public SoqlBuilder ForView()
    {
        _forView = true;
        return this;
    }

    /// <summary>
    /// Adds FOR REFERENCE clause (tracking recently referenced).
    /// </summary>
    public SoqlBuilder ForReference()
    {
        _forReference = true;
        return this;
    }

    #endregion

    #region Build

    /// <summary>
    /// Builds the SOQL query string.
    /// </summary>
    public string Build()
    {
        var sb = new StringBuilder();

        // SELECT
        sb.Append("SELECT ");
        if (_selectFields.Count == 0)
        {
            sb.Append("Id");
        }
        else
        {
            sb.Append(string.Join(", ", _selectFields));
        }

        // FROM
        sb.Append(" FROM ");
        sb.Append(_objectName);

        // WHERE
        if (_whereClauses.Count > 0)
        {
            sb.Append(" WHERE ");
            sb.Append(string.Join(" AND ", _whereClauses));
        }

        // GROUP BY
        if (_groupByFields.Count > 0)
        {
            sb.Append(" GROUP BY ");
            sb.Append(string.Join(", ", _groupByFields));
        }

        // HAVING
        if (!string.IsNullOrEmpty(_havingClause))
        {
            sb.Append(" HAVING ");
            sb.Append(_havingClause);
        }

        // ORDER BY
        if (_orderByFields.Count > 0)
        {
            sb.Append(" ORDER BY ");
            sb.Append(string.Join(", ", _orderByFields));
        }

        // LIMIT
        if (_limit.HasValue)
        {
            sb.Append(" LIMIT ");
            sb.Append(_limit.Value);
        }

        // OFFSET
        if (_offset.HasValue)
        {
            sb.Append(" OFFSET ");
            sb.Append(_offset.Value);
        }

        // FOR UPDATE/VIEW/REFERENCE
        if (_forUpdate) sb.Append(" FOR UPDATE");
        if (_forView) sb.Append(" FOR VIEW");
        if (_forReference) sb.Append(" FOR REFERENCE");

        return sb.ToString();
    }

    /// <summary>
    /// Implicitly converts the builder to a string.
    /// </summary>
    public static implicit operator string(SoqlBuilder builder) => builder.Build();

    /// <inheritdoc/>
    public override string ToString() => Build();

    #endregion

    #region Private Methods

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{SecurityUtils.SanitizeForSoql(s)}'",
            bool b => b.ToString().ToUpperInvariant(),
            DateTime dt => FormatDateTimeUtc(dt),
            DateTimeOffset dto => FormatDateTimeOffsetUtc(dto),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            float fl => fl.ToString(CultureInfo.InvariantCulture),
            Enum e => $"'{GetEnumSalesforceValue(e)}'",
            _ => $"'{SecurityUtils.SanitizeForSoql(value.ToString() ?? "")}'",
        };
    }

    private static string FormatDateTimeUtc(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc),
            _ => dateTime
        };

        return utc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static string FormatDateTimeOffsetUtc(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gets the Salesforce API value for an enum member.
    /// Uses SalesforceValueAttribute if present, otherwise falls back to the enum member name.
    /// </summary>
    private static string GetEnumSalesforceValue(Enum e)
    {
        var type = e.GetType();
        var memberName = e.ToString();
        var memberInfo = type.GetField(memberName);

        if (memberInfo != null)
        {
            var attr = memberInfo.GetCustomAttribute<SalesforceValueAttribute>();
            if (attr != null)
            {
                return SecurityUtils.SanitizeForSoql(attr.Value);
            }
        }

        // Fallback to enum member name (sanitized for safety)
        return SecurityUtils.SanitizeForSoql(memberName);
    }

    private static string NormalizeAggregateFunction(string aggregateFunc)
    {
        if (string.IsNullOrWhiteSpace(aggregateFunc))
        {
            throw new ArgumentException("Aggregate function is required.", nameof(aggregateFunc));
        }

        var normalized = aggregateFunc.Trim().ToUpperInvariant();

        if (normalized == "COUNTDISTINCT")
        {
            normalized = "COUNT_DISTINCT";
        }

        return normalized switch
        {
            "COUNT" => "COUNT",
            "COUNT_DISTINCT" => "COUNT_DISTINCT",
            "SUM" => "SUM",
            "AVG" => "AVG",
            "MIN" => "MIN",
            "MAX" => "MAX",
            _ => throw new ArgumentException($"Unsupported aggregate function: {aggregateFunc}", nameof(aggregateFunc))
        };
    }

    private static string NormalizeComparisonOperator(string op)
    {
        if (string.IsNullOrWhiteSpace(op))
        {
            throw new ArgumentException("Comparison operator is required.", nameof(op));
        }

        var normalized = op.Trim();

        return normalized switch
        {
            "=" => "=",
            "!=" => "!=",
            "<>" => "!=",
            ">" => ">",
            ">=" => ">=",
            "<" => "<",
            "<=" => "<=",
            _ => throw new ArgumentException($"Unsupported comparison operator: {op}", nameof(op))
        };
    }

    #endregion
}

/// <summary>
/// SOQL date literals for dynamic date filtering.
/// </summary>
public enum DateLiteral
{
    YESTERDAY,
    TODAY,
    TOMORROW,
    LAST_WEEK,
    THIS_WEEK,
    NEXT_WEEK,
    LAST_MONTH,
    THIS_MONTH,
    NEXT_MONTH,
    LAST_90_DAYS,
    NEXT_90_DAYS,
    LAST_N_DAYS,
    NEXT_N_DAYS,
    THIS_QUARTER,
    LAST_QUARTER,
    NEXT_QUARTER,
    THIS_YEAR,
    LAST_YEAR,
    NEXT_YEAR,
    THIS_FISCAL_QUARTER,
    LAST_FISCAL_QUARTER,
    NEXT_FISCAL_QUARTER,
    THIS_FISCAL_YEAR,
    LAST_FISCAL_YEAR,
    NEXT_FISCAL_YEAR,
    // N-based literals (use with n parameter)
    N_DAYS_AGO,
    N_WEEKS_AGO,
    N_MONTHS_AGO,
    N_QUARTERS_AGO,
    N_YEARS_AGO,
    N_FISCAL_QUARTERS_AGO,
    N_FISCAL_YEARS_AGO
}

/// <summary>
/// SOQL date functions for extracting date/time components.
/// Use with convertTimezone() for timezone-aware operations.
/// </summary>
public enum DateFunction
{
    /// <summary>Extracts the calendar year (e.g., 2024).</summary>
    CALENDAR_YEAR,
    /// <summary>Extracts the calendar quarter (1-4).</summary>
    CALENDAR_QUARTER,
    /// <summary>Extracts the calendar month (1-12).</summary>
    CALENDAR_MONTH,
    /// <summary>Extracts the fiscal year.</summary>
    FISCAL_YEAR,
    /// <summary>Extracts the fiscal quarter (1-4).</summary>
    FISCAL_QUARTER,
    /// <summary>Extracts the fiscal month (1-12).</summary>
    FISCAL_MONTH,
    /// <summary>Extracts the day of the month (1-31).</summary>
    DAY_IN_MONTH,
    /// <summary>Extracts the day of the week (1 = Sunday, 7 = Saturday).</summary>
    DAY_IN_WEEK,
    /// <summary>Extracts the day of the year (1-366).</summary>
    DAY_IN_YEAR,
    /// <summary>Extracts the date portion from a datetime.</summary>
    DAY_ONLY,
    /// <summary>Extracts the hour (0-23).</summary>
    HOUR_IN_DAY,
    /// <summary>Extracts the week in the month (1-5).</summary>
    WEEK_IN_MONTH,
    /// <summary>Extracts the week in the year (1-53).</summary>
    WEEK_IN_YEAR
}

/// <summary>
/// Helper class for building date literal expressions with N values.
/// </summary>
public static class DateLiteralHelper
{
    /// <summary>Creates LAST_N_DAYS:n expression.</summary>
    public static string LastNDays(int n) => $"LAST_N_DAYS:{n}";

    /// <summary>Creates NEXT_N_DAYS:n expression.</summary>
    public static string NextNDays(int n) => $"NEXT_N_DAYS:{n}";

    /// <summary>Creates LAST_N_WEEKS:n expression.</summary>
    public static string LastNWeeks(int n) => $"LAST_N_WEEKS:{n}";

    /// <summary>Creates NEXT_N_WEEKS:n expression.</summary>
    public static string NextNWeeks(int n) => $"NEXT_N_WEEKS:{n}";

    /// <summary>Creates LAST_N_MONTHS:n expression.</summary>
    public static string LastNMonths(int n) => $"LAST_N_MONTHS:{n}";

    /// <summary>Creates NEXT_N_MONTHS:n expression.</summary>
    public static string NextNMonths(int n) => $"NEXT_N_MONTHS:{n}";

    /// <summary>Creates LAST_N_QUARTERS:n expression.</summary>
    public static string LastNQuarters(int n) => $"LAST_N_QUARTERS:{n}";

    /// <summary>Creates NEXT_N_QUARTERS:n expression.</summary>
    public static string NextNQuarters(int n) => $"NEXT_N_QUARTERS:{n}";

    /// <summary>Creates LAST_N_YEARS:n expression.</summary>
    public static string LastNYears(int n) => $"LAST_N_YEARS:{n}";

    /// <summary>Creates NEXT_N_YEARS:n expression.</summary>
    public static string NextNYears(int n) => $"NEXT_N_YEARS:{n}";

    /// <summary>Creates LAST_N_FISCAL_QUARTERS:n expression.</summary>
    public static string LastNFiscalQuarters(int n) => $"LAST_N_FISCAL_QUARTERS:{n}";

    /// <summary>Creates NEXT_N_FISCAL_QUARTERS:n expression.</summary>
    public static string NextNFiscalQuarters(int n) => $"NEXT_N_FISCAL_QUARTERS:{n}";

    /// <summary>Creates LAST_N_FISCAL_YEARS:n expression.</summary>
    public static string LastNFiscalYears(int n) => $"LAST_N_FISCAL_YEARS:{n}";

    /// <summary>Creates NEXT_N_FISCAL_YEARS:n expression.</summary>
    public static string NextNFiscalYears(int n) => $"NEXT_N_FISCAL_YEARS:{n}";

    /// <summary>Creates N_DAYS_AGO:n expression.</summary>
    public static string NDaysAgo(int n) => $"N_DAYS_AGO:{n}";

    /// <summary>Creates N_WEEKS_AGO:n expression.</summary>
    public static string NWeeksAgo(int n) => $"N_WEEKS_AGO:{n}";

    /// <summary>Creates N_MONTHS_AGO:n expression.</summary>
    public static string NMonthsAgo(int n) => $"N_MONTHS_AGO:{n}";

    /// <summary>Creates N_QUARTERS_AGO:n expression.</summary>
    public static string NQuartersAgo(int n) => $"N_QUARTERS_AGO:{n}";

    /// <summary>Creates N_YEARS_AGO:n expression.</summary>
    public static string NYearsAgo(int n) => $"N_YEARS_AGO:{n}";

    /// <summary>Creates N_FISCAL_QUARTERS_AGO:n expression.</summary>
    public static string NFiscalQuartersAgo(int n) => $"N_FISCAL_QUARTERS_AGO:{n}";

    /// <summary>Creates N_FISCAL_YEARS_AGO:n expression.</summary>
    public static string NFiscalYearsAgo(int n) => $"N_FISCAL_YEARS_AGO:{n}";

    internal static bool RequiresParameter(DateLiteral literal)
    {
        return literal is DateLiteral.LAST_N_DAYS
            or DateLiteral.NEXT_N_DAYS
            or DateLiteral.N_DAYS_AGO
            or DateLiteral.N_WEEKS_AGO
            or DateLiteral.N_MONTHS_AGO
            or DateLiteral.N_QUARTERS_AGO
            or DateLiteral.N_YEARS_AGO
            or DateLiteral.N_FISCAL_QUARTERS_AGO
            or DateLiteral.N_FISCAL_YEARS_AGO;
    }
}
