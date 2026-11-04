using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Query;

/// <summary>
/// Converts LINQ expressions to SOQL WHERE clauses.
/// Supports basic operations: equals, not equals, greater than, less than, contains, starts with, ends with.
/// </summary>
public class SoqlExpressionVisitor : ExpressionVisitor
{
    private readonly StringBuilder _whereClause = new();
    private readonly Type _entityType;

    /// <summary>
    /// Gets the generated WHERE clause.
    /// </summary>
    public string WhereClause => _whereClause.ToString().Trim();

    /// <summary>
    /// Creates a new SOQL expression visitor.
    /// </summary>
    /// <param name="entityType">The entity type being queried.</param>
    public SoqlExpressionVisitor(Type entityType)
    {
        _entityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
    }

    /// <summary>
    /// Translates a lambda expression to a SOQL WHERE clause.
    /// </summary>
    public static string Translate<T>(Expression<Func<T, bool>> predicate)
    {
        var visitor = new SoqlExpressionVisitor(typeof(T));
        if (visitor.TryAppendBooleanPredicate(predicate.Body))
        {
            return visitor.WhereClause;
        }
        visitor.Visit(predicate.Body);
        return visitor.WhereClause;
    }

    /// <summary>
    /// Translates an expression to a SOQL WHERE clause.
    /// </summary>
    public static string Translate(Expression expression, Type entityType)
    {
        var visitor = new SoqlExpressionVisitor(entityType);
        if (visitor.TryAppendBooleanPredicate(expression))
        {
            return visitor.WhereClause;
        }
        visitor.Visit(expression);
        return visitor.WhereClause;
    }

    protected override Expression VisitBinary(BinaryExpression node)
    {
        if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
        {
            _whereClause.Append('(');
            AppendBooleanOperand(node.Left);
            _whereClause.Append(node.NodeType == ExpressionType.AndAlso ? " AND " : " OR ");
            AppendBooleanOperand(node.Right);
            _whereClause.Append(')');
            return node;
        }

        if (!IsComparison(node.NodeType))
        {
            // Arithmetic, coalesce, string concat, etc. over captured values: evaluate client-side.
            if (!ContainsParameter(node))
            {
                AppendValue(GetExpressionValue(node));
                return node;
            }

            throw new NotSupportedException(
                $"Operator '{node.NodeType}' over query fields is not supported in SOQL (expression: {node}). " +
                "Compute the value outside the query, or compare the field against a captured value.");
        }

        // Normalise "constant op field" to "field op' constant" (SOQL needs the field on the left).
        if (!ContainsParameter(node.Left) && ContainsParameter(node.Right))
        {
            node = Expression.MakeBinary(FlipComparison(node.NodeType), node.Right, node.Left, node.IsLiftedToNull, node.Method);
        }

        if (ContainsParameter(node.Left) && ContainsParameter(node.Right))
        {
            throw new NotSupportedException(
                $"Comparing two fields ({node}) is not supported: SOQL compares a field only against a literal. " +
                "Use a formula field in Salesforce, or filter in memory.");
        }

        _whereClause.Append('(');

        // Handle null comparisons specially
        if (IsNullComparison(node, out var nullPath, out var isEquals) && nullPath != null)
        {
            _whereClause.Append(nullPath);
            _whereClause.Append(isEquals ? " = NULL" : " != NULL");
            _whereClause.Append(')');
            return node;
        }

        Visit(node.Left);
        var enumType = GetConvertedEnumType(node.Left);

        _whereClause.Append(node.NodeType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " != ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            _ => throw new NotSupportedException($"Binary operator '{node.NodeType}' is not supported in SOQL")
        });

        if (enumType != null && !ContainsParameter(node.Right))
        {
            // x.EnumField == Stage.Closed compiles to Convert(x.EnumField, int) == 1; send the enum's picklist value.
            var raw = GetExpressionValue(node.Right);
            AppendValue(raw == null ? null : Enum.ToObject(enumType, raw));
        }
        else
        {
            Visit(node.Right);
        }

        _whereClause.Append(')');

        return node;
    }

    private static Type? GetConvertedEnumType(Expression expression)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
        {
            var operandType = Nullable.GetUnderlyingType(u.Operand.Type) ?? u.Operand.Type;
            if (operandType.IsEnum) return operandType;
        }

        return null;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
            if (node.Operand is MethodCallExpression negatedCall && TryAppendInPredicate(negatedCall, negate: true))
            {
                return node;
            }

            _whereClause.Append("NOT ");
            if (!TryAppendBooleanMember(node.Operand, wrapInParentheses: true))
            {
                Visit(node.Operand);
            }
            return node;
        }

        if (node.NodeType == ExpressionType.Convert)
        {
            // Handle type conversions (like enum to int)
            return Visit(node.Operand);
        }

        throw new NotSupportedException($"Unary operator '{node.NodeType}' is not supported in SOQL");
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        // Check if this is a captured variable (closure)
        if (node.Expression is ConstantExpression constExpr)
        {
            var value = GetMemberValue(node);
            AppendValue(value);
            return node;
        }

        // Check if this is accessing a nested property on a captured variable
        if (IsClosureAccess(node, out var closureValue))
        {
            AppendValue(closureValue);
            return node;
        }

        // Check if this is a DateTime/DateTimeOffset member access (e.g., .Year, .Month)
        if (TryHandleDateTimeMember(node))
        {
            return node;
        }

        if (TryGetMemberPath(node, out var path))
        {
            _whereClause.Append(SecurityUtils.SanitizeFieldName(path));
            return node;
        }

        // Try to evaluate as constant
        var memberValue = GetMemberValue(node);
        AppendValue(memberValue);
        return node;
    }

    /// <summary>
    /// Handles DateTime/DateTimeOffset member access like .Year, .Month, .Day, .Hour.
    /// Translates them to SOQL date functions (CALENDAR_YEAR, CALENDAR_MONTH, etc.).
    /// </summary>
    private bool TryHandleDateTimeMember(MemberExpression node)
    {
        // Check if the member is on DateTime or DateTimeOffset
        var declaringType = node.Member.DeclaringType;
        if (declaringType != typeof(DateTime) && 
            declaringType != typeof(DateTimeOffset) &&
            declaringType != typeof(DateTime?) &&
            declaringType != typeof(DateTimeOffset?))
        {
            return false;
        }

        // Get the SOQL function name for this member
        var soqlFunction = node.Member.Name switch
        {
            "Year" => "CALENDAR_YEAR",
            "Month" => "CALENDAR_MONTH",
            "Day" => "DAY_IN_MONTH",
            "Hour" => "HOUR_IN_DAY",
            "DayOfWeek" => "DAY_IN_WEEK",
            "DayOfYear" => "DAY_IN_YEAR",
            _ => null
        };

        if (soqlFunction == null)
        {
            return false;
        }

        // The parent expression should be the field path (e.g., x.CreatedDate)
        if (node.Expression == null || !TryGetMemberPath(node.Expression, out var fieldPath))
        {
            return false;
        }

        _whereClause.Append($"{soqlFunction}({SecurityUtils.SanitizeFieldName(fieldPath)})");
        return true;
    }

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AppendValue(node.Value);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType == typeof(SoqlFunctions))
        {
            return HandleSoqlFunction(node);
        }

        // Semi-joins and IN lists (Queryable/Enumerable/List/MemoryExtensions Contains)
        if (TryAppendInPredicate(node, negate: false))
        {
            return node;
        }

        // string.IsNullOrEmpty(x.Field) / IsNullOrWhiteSpace: Salesforce stores empty text as null.
        if (node.Method.DeclaringType == typeof(string) && node.Object == null &&
            node.Method.Name is "IsNullOrEmpty" or "IsNullOrWhiteSpace" &&
            TryGetMemberPath(node.Arguments[0], out var nullOrEmptyPath))
        {
            _whereClause.Append('(').Append(SecurityUtils.SanitizeFieldName(nullOrEmptyPath)).Append(" = NULL)");
            return node;
        }

        // Any call that does not touch the query parameter (DateTime.UtcNow.AddDays(-7), Guid.ToString(), helper methods)
        // is evaluated client-side and sent as a literal.
        if (!ContainsParameter(node))
        {
            if (node.Type == typeof(bool))
            {
                // A stand-alone boolean such as list.Contains("C") would become a bare TRUE/FALSE, which SOQL rejects.
                throw new NotSupportedException(
                    $"The condition '{node}' does not reference any query field; evaluate it before building the query.");
            }

            AppendValue(GetExpressionValue(node));
            return node;
        }

        // String methods
        if (node.Method.DeclaringType == typeof(string))
        {
            return HandleStringMethod(node);
        }

        // Enumerable methods (Contains)
        if (node.Method.DeclaringType == typeof(Enumerable) ||
            (node.Method.DeclaringType?.IsGenericType == true &&
             node.Method.DeclaringType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            return HandleEnumerableMethod(node);
        }

        // Collection Contains
        if (node.Method.Name == "Contains" && node.Object != null)
        {
            return HandleCollectionContains(node);
        }

        throw new NotSupportedException(
            $"Method '{node.Method.DeclaringType?.Name}.{node.Method.Name}' is not supported in SOQL (expression: {node}). " +
            "See docs/19-LINQ-to-SOQL.md for the supported operators, or use SoqlFunctions / WhereCondition for SOQL-specific constructs.");
    }

    protected override Expression VisitNew(NewExpression node) => EvaluateClientSide(node);
    protected override Expression VisitNewArray(NewArrayExpression node) => EvaluateClientSide(node);
    protected override Expression VisitConditional(ConditionalExpression node) => EvaluateClientSide(node);
    protected override Expression VisitInvocation(InvocationExpression node) => EvaluateClientSide(node);
    protected override Expression VisitListInit(ListInitExpression node) => EvaluateClientSide(node);
    protected override Expression VisitMemberInit(MemberInitExpression node) => EvaluateClientSide(node);

    private Expression EvaluateClientSide(Expression node)
    {
        if (ContainsParameter(node))
        {
            throw new NotSupportedException(
                $"Expression '{node}' ({node.NodeType}) uses query fields in a way SOQL cannot express. " +
                "Compare fields directly against captured values.");
        }

        AppendValue(GetExpressionValue(node));
        return node;
    }

    // ---------------------------------------------------------------------------------------
    // SoqlFunctions translation
    // ---------------------------------------------------------------------------------------

    private Expression HandleSoqlFunction(MethodCallExpression node)
    {
        var args = node.Arguments;
        switch (node.Method.Name)
        {
            case nameof(SoqlFunctions.Literal):
            case nameof(SoqlFunctions.LiteralDate):
                _whereClause.Append(FormatDateLiteral(args));
                return node;

            case nameof(SoqlFunctions.DatePart):
            {
                var fn = (DateFunction)GetExpressionValue(args[0])!;
                _whereClause.Append(fn.ToString()).Append('(').Append(RenderFieldOperand(args[1])).Append(')');
                return node;
            }

            case nameof(SoqlFunctions.CalendarYear): AppendFunction("CALENDAR_YEAR", args[0]); return node;
            case nameof(SoqlFunctions.CalendarMonth): AppendFunction("CALENDAR_MONTH", args[0]); return node;
            case nameof(SoqlFunctions.CalendarQuarter): AppendFunction("CALENDAR_QUARTER", args[0]); return node;
            case nameof(SoqlFunctions.FiscalYear): AppendFunction("FISCAL_YEAR", args[0]); return node;
            case nameof(SoqlFunctions.FiscalQuarter): AppendFunction("FISCAL_QUARTER", args[0]); return node;
            case nameof(SoqlFunctions.FiscalMonth): AppendFunction("FISCAL_MONTH", args[0]); return node;
            case nameof(SoqlFunctions.WeekInYear): AppendFunction("WEEK_IN_YEAR", args[0]); return node;
            case nameof(SoqlFunctions.WeekInMonth): AppendFunction("WEEK_IN_MONTH", args[0]); return node;
            case nameof(SoqlFunctions.DayOnly): AppendFunction("DAY_ONLY", args[0]); return node;
            case nameof(SoqlFunctions.ConvertTimezone):
            case nameof(SoqlFunctions.ToLabel):
                _whereClause.Append(RenderFieldOperand(node));
                return node;

            case nameof(SoqlFunctions.Includes):
            case nameof(SoqlFunctions.Excludes):
            {
                var values = (GetExpressionValue(args[1]) as IEnumerable<string>)?.ToList() ?? new List<string>();
                if (values.Count == 0)
                {
                    throw new ArgumentException($"SoqlFunctions.{node.Method.Name} needs at least one value.");
                }

                _whereClause.Append('(').Append(RenderFieldOperand(args[0]))
                    .Append(node.Method.Name == nameof(SoqlFunctions.Includes) ? " INCLUDES (" : " EXCLUDES (")
                    .Append(string.Join(", ", values.Select(v => $"'{SecurityUtils.SanitizeForSoql(v)}'")))
                    .Append("))");
                return node;
            }

            case nameof(SoqlFunctions.In):
            case nameof(SoqlFunctions.NotIn):
                AppendInSubquery(args[0], args[1], negate: node.Method.Name == nameof(SoqlFunctions.NotIn));
                return node;

            case nameof(SoqlFunctions.Like):
            {
                var pattern = GetExpressionValue(args[1])?.ToString() ?? string.Empty;
                _whereClause.Append('(').Append(RenderFieldOperand(args[0]))
                    .Append(" LIKE '").Append(SecurityUtils.SanitizeForSoql(pattern)).Append("')");
                return node;
            }

            case nameof(SoqlFunctions.Distance):
                _whereClause.Append(RenderDistance(node));
                return node;
        }

        throw new NotSupportedException($"SoqlFunctions.{node.Method.Name} is not supported here.");
    }

    private void AppendFunction(string function, Expression field)
    {
        _whereClause.Append(function).Append('(').Append(RenderFieldOperand(field)).Append(')');
    }

    private string FormatDateLiteral(IReadOnlyList<Expression> args)
    {
        var literal = (DateLiteral)GetExpressionValue(args[0])!;
        var needsN = DateLiteralHelper.RequiresParameter(literal);
        if (args.Count > 1)
        {
            if (!needsN)
            {
                throw new ArgumentException($"Date literal {literal} does not take an N value.");
            }

            var n = Convert.ToInt32(GetExpressionValue(args[1]), CultureInfo.InvariantCulture);
            if (n < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(args), n, "Date literal N must be zero or positive.");
            }

            return $"{literal}:{n.ToString(CultureInfo.InvariantCulture)}";
        }

        if (needsN)
        {
            throw new ArgumentException($"Date literal {literal} requires an N value: SoqlFunctions.Literal({literal}, n).");
        }

        return literal.ToString();
    }

    internal string RenderDistance(MethodCallExpression node)
    {
        var args = node.Arguments;
        var lat = Convert.ToDouble(GetExpressionValue(args[1]), CultureInfo.InvariantCulture);
        var lon = Convert.ToDouble(GetExpressionValue(args[2]), CultureInfo.InvariantCulture);
        if (double.IsNaN(lat) || double.IsNaN(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(node), "DISTANCE latitude must be in [-90, 90] and longitude in [-180, 180].");
        }

        var unit = (DistanceUnit)GetExpressionValue(args[3])!;
        return $"DISTANCE({RenderFieldOperand(args[0])}, GEOLOCATION({lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}), '{(unit == DistanceUnit.Kilometers ? "km" : "mi")}')";
    }

    /// <summary>
    /// Renders a field reference, optionally wrapped in toLabel()/convertTimezone().
    /// </summary>
    private string RenderFieldOperand(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
        {
            expression = u.Operand;
        }

        if (expression is MethodCallExpression call && call.Method.DeclaringType == typeof(SoqlFunctions))
        {
            if (call.Method.Name == nameof(SoqlFunctions.ToLabel))
                return $"toLabel({RenderFieldOperand(call.Arguments[0])})";
            if (call.Method.Name == nameof(SoqlFunctions.ConvertTimezone))
                return $"convertTimezone({RenderFieldOperand(call.Arguments[0])})";
        }

        if (TryGetMemberPath(expression, out var path))
        {
            return SecurityUtils.SanitizeFieldName(path);
        }

        throw new NotSupportedException($"Expected a field reference (x => x.Field) but found '{expression}'.");
    }

    // ---------------------------------------------------------------------------------------
    // IN lists and semi-joins
    // ---------------------------------------------------------------------------------------

    private bool TryAppendInPredicate(MethodCallExpression node, bool negate)
    {
        if (node.Method.DeclaringType == typeof(SoqlFunctions) &&
            node.Method.Name is nameof(SoqlFunctions.In) or nameof(SoqlFunctions.NotIn))
        {
            var isNotIn = node.Method.Name == nameof(SoqlFunctions.NotIn);
            AppendInSubquery(node.Arguments[0], node.Arguments[1], negate ^ isNotIn);
            return true;
        }

        if (node.Method.Name != "Contains")
        {
            return false;
        }

        Expression? collectionExpr = null;
        Expression? itemExpr = null;
        var declaring = node.Method.DeclaringType;

        if (declaring == typeof(Queryable) || declaring == typeof(Enumerable) || declaring == typeof(MemoryExtensions))
        {
            if (node.Object == null && (node.Arguments.Count == 2 ||
                (node.Arguments.Count == 3 && !ContainsParameter(node.Arguments[2]) && GetExpressionValue(node.Arguments[2]) == null)))
            {
                collectionExpr = UnwrapSpanConversion(node.Arguments[0]);
                itemExpr = node.Arguments[1];
            }
        }
        else if (node.Object != null && node.Arguments.Count == 1 && declaring != typeof(string) &&
                 typeof(System.Collections.IEnumerable).IsAssignableFrom(node.Object.Type))
        {
            collectionExpr = node.Object;
            itemExpr = node.Arguments[0];
        }

        if (collectionExpr == null || itemExpr == null || ContainsParameter(collectionExpr) || !ContainsParameter(itemExpr))
        {
            return false;
        }

        var collection = GetExpressionValue(collectionExpr);
        if (collection is IQueryable queryable && queryable.Provider is SalesforceQueryProvider)
        {
            AppendInSubquery(itemExpr, collectionExpr, negate);
            return true;
        }

        var values = ExtractValues(collection as System.Collections.IEnumerable);
        if (values.Count == 0)
        {
            // x IN () is always false; NOT IN () is always true.
            _whereClause.Append(negate ? "(Id != NULL)" : "(Id = NULL)");
            return true;
        }

        _whereClause.Append(RenderFieldOperand(itemExpr));
        _whereClause.Append(negate ? " NOT IN (" : " IN (");
        _whereClause.Append(string.Join(", ", values.Select(FormatValue)));
        _whereClause.Append(')');
        return true;
    }

    private void AppendInSubquery(Expression fieldExpr, Expression subqueryExpr, bool negate)
    {
        if (GetExpressionValue(subqueryExpr) is not IQueryable subquery || subquery.Provider is not SalesforceQueryProvider provider)
        {
            throw new NotSupportedException("A semi-join subquery must be a Salesforce queryable (data.Query<T>().Select(x => x.Field)).");
        }

        _whereClause.Append(RenderFieldOperand(fieldExpr));
        _whereClause.Append(negate ? " NOT IN (" : " IN (");
        _whereClause.Append(provider.BuildSemiJoinSoql(subquery.Expression));
        _whereClause.Append(')');
    }

    /// <summary>
    /// C# 14 binds <c>array.Contains(x)</c> to <c>MemoryExtensions.Contains(ReadOnlySpan)</c> via an implicit
    /// conversion; peel that conversion off to get back to the array.
    /// </summary>
    private static Expression UnwrapSpanConversion(Expression expression)
    {
        while (true)
        {
            switch (expression)
            {
                case MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } implicitCall:
                    expression = implicitCall.Arguments[0];
                    continue;
                case UnaryExpression { NodeType: ExpressionType.Convert, Method.Name: "op_Implicit" } convert:
                    expression = convert.Operand;
                    continue;
                case UnaryExpression { NodeType: ExpressionType.Convert } plain when plain.Type.IsByRefLike:
                    expression = plain.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private Expression HandleStringMethod(MethodCallExpression node)
    {
        var methodName = node.Method.Name;

        // Get the object being called on
        if (node.Object == null)
            throw new NotSupportedException($"Static string method '{methodName}' is not supported");

        switch (methodName)
        {
            case "Contains":
                Visit(node.Object);
                _whereClause.Append(" LIKE ");
                AppendLikePattern(node.Arguments[0], "%", "%");
                return node;

            case "StartsWith":
                Visit(node.Object);
                _whereClause.Append(" LIKE ");
                AppendLikePattern(node.Arguments[0], "", "%");
                return node;

            case "EndsWith":
                Visit(node.Object);
                _whereClause.Append(" LIKE ");
                AppendLikePattern(node.Arguments[0], "%", "");
                return node;

            case "ToUpper":
            case "ToLower":
            case "Trim":
                throw new NotSupportedException($"String method '{methodName}' is not supported in SOQL");

            case "Equals":
                Visit(node.Object);
                _whereClause.Append(" = ");
                var equalsValue = GetExpressionValue(node.Arguments[0]);
                AppendValue(equalsValue);
                return node;

            default:
                throw new NotSupportedException(
                    $"String method '{methodName}' is not supported in SOQL (expression: {node}). " +
                    "See docs/19-LINQ-to-SOQL.md; SoqlFunctions.Like covers custom LIKE patterns.");
        }
    }

    private Expression HandleEnumerableMethod(MethodCallExpression node)
    {
        if (node.Method.Name == "Contains")
        {
            // Enumerable.Contains(collection, item) or collection.Contains(item)
            Expression collectionExpr;
            Expression itemExpr;

            if (node.Object != null)
            {
                // Instance method: collection.Contains(item)
                collectionExpr = node.Object;
                itemExpr = node.Arguments[0];
            }
            else
            {
                // Static method: Enumerable.Contains(collection, item)
                collectionExpr = node.Arguments[0];
                itemExpr = node.Arguments[1];
            }

            var values = ExtractValues(collectionExpr);
            if (values.Count == 0)
            {
                AppendAlwaysFalsePredicate();
                return node;
            }

            AppendFieldReference(itemExpr);
            _whereClause.Append(" IN (");
            _whereClause.Append(string.Join(", ", values.Select(FormatValue)));
            _whereClause.Append(')');
            return node;
        }

        throw new NotSupportedException($"Enumerable method '{node.Method.Name}' is not supported in SOQL");
    }

    private Expression HandleCollectionContains(MethodCallExpression node)
    {
        // collection.Contains(field) -> field IN (...)
        var collection = node.Object != null
            ? GetExpressionValue(node.Object) as System.Collections.IEnumerable
            : null;
        var itemExpr = node.Arguments[0];

        var values = ExtractValues(collection);
        if (values.Count == 0)
        {
            AppendAlwaysFalsePredicate();
            return node;
        }

        AppendFieldReference(itemExpr);
        _whereClause.Append(" IN (");
        _whereClause.Append(string.Join(", ", values.Select(FormatValue)));
        _whereClause.Append(')');
        return node;
    }

    private bool IsNullComparison(BinaryExpression node, out string? fieldPath, out bool isEquals)
    {
        fieldPath = null;
        isEquals = node.NodeType == ExpressionType.Equal;

        if (node.NodeType != ExpressionType.Equal && node.NodeType != ExpressionType.NotEqual)
            return false;

        // Field (possibly wrapped in a nullable Convert) compared to a null constant, on either side.
        if (IsNullConstant(node.Right) && TryGetMemberPath(node.Left, out var left))
        {
            fieldPath = SecurityUtils.SanitizeFieldName(left);
            return true;
        }

        if (IsNullConstant(node.Left) && TryGetMemberPath(node.Right, out var right))
        {
            fieldPath = SecurityUtils.SanitizeFieldName(right);
            return true;
        }

        return false;
    }

    private static bool IsNullConstant(Expression expr)
    {
        while (expr is UnaryExpression { NodeType: ExpressionType.Convert } u) expr = u.Operand;
        return expr is ConstantExpression { Value: null };
    }

    private static bool IsComparison(ExpressionType type) => type is
        ExpressionType.Equal or ExpressionType.NotEqual or
        ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
        ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static ExpressionType FlipComparison(ExpressionType type) => type switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => type
    };

    private bool IsClosureAccess(MemberExpression node, out object? value)
    {
        value = null;

        // Walk up the expression tree to find if this is a closure access
        var memberStack = new Stack<MemberInfo>();
        Expression? current = node;

        while (current is MemberExpression memberExpr)
        {
            memberStack.Push(memberExpr.Member);
            current = memberExpr.Expression;
        }

        if (current is ConstantExpression constExpr)
        {
            // This is a closure access
            value = constExpr.Value;
            while (memberStack.Count > 0)
            {
                var member = memberStack.Pop();
                value = member switch
                {
                    FieldInfo fi => fi.GetValue(value),
                    PropertyInfo pi => pi.GetValue(value),
                    _ => null
                };
            }
            return true;
        }

        return false;
    }

    private string GetSalesforceFieldName(MemberInfo member, Type? declaringType = null)
    {
        // Check for SalesforceField attribute
        var fieldAttr = member.GetCustomAttribute<SalesforceFieldAttribute>();
        if (fieldAttr != null)
        {
            return fieldAttr.FieldName;
        }

        // Use the mapper if available
        var targetType = declaringType ?? _entityType;
        return SalesforceMapper.GetFieldName(targetType, member.Name);
    }

    private static string GetSalesforceFieldName(Type entityType, MemberInfo member)
    {
        var fieldAttr = member.GetCustomAttribute<SalesforceFieldAttribute>();
        return fieldAttr?.FieldName ?? member.Name;
    }

    private object? GetMemberValue(MemberExpression node)
    {
        EnsureNoParameter(node, "member value");

        // For constant expressions or closure access
        var objectMember = Expression.Convert(node, typeof(object));
        var getterLambda = Expression.Lambda<Func<object>>(objectMember);
        var getter = getterLambda.Compile();
        return getter();
    }

    private object? GetExpressionValue(Expression expr)
    {
        EnsureNoParameter(expr, "expression value");

        var objectExpr = Expression.Convert(expr, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(objectExpr);
        var compiled = lambda.Compile();
        return compiled();
    }

    private void AppendValue(object? value)
    {
        _whereClause.Append(FormatValue(value));
    }

    private bool TryAppendBooleanPredicate(Expression expression)
    {
        if (TryAppendBooleanMember(expression, wrapInParentheses: false))
        {
            return true;
        }

        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Not)
        {
            if (TryAppendBooleanMember(unary.Operand, wrapInParentheses: true))
            {
                _whereClause.Insert(0, "NOT ");
                return true;
            }
        }

        return false;
    }

    private void AppendBooleanOperand(Expression expression)
    {
        if (!TryAppendBooleanMember(expression, wrapInParentheses: true))
        {
            Visit(expression);
        }
    }

    private bool TryAppendBooleanMember(Expression expression, bool wrapInParentheses)
    {
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            expression = unary.Operand;
        }

        if (expression is MemberExpression member &&
            (member.Type == typeof(bool) || member.Type == typeof(bool?)) &&
            TryGetMemberPath(member, out var path))
        {
            if (wrapInParentheses)
            {
                _whereClause.Append('(');
            }

            _whereClause.Append(SecurityUtils.SanitizeFieldName(path));
            _whereClause.Append(" = TRUE");

            if (wrapInParentheses)
            {
                _whereClause.Append(')');
            }

            return true;
        }

        return false;
    }

    private void AppendLikePattern(Expression expression, string prefix, string suffix)
    {
        var value = GetExpressionValue(expression);
        var sanitized = SecurityUtils.SanitizeSoqlLike(value?.ToString() ?? "");
        _whereClause.Append($"'{prefix}{sanitized}{suffix}'");
    }

    private bool TryGetMemberPath(Expression expression, out string path)
    {
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
        {
            expression = unary.Operand;
        }

        if (expression is MemberExpression member)
        {
            return TryGetMemberPath(member, out path);
        }

        path = string.Empty;
        return false;
    }

    private bool TryGetMemberPath(MemberExpression node, out string path)
    {
        path = string.Empty;

        var parts = new List<string>();
        Expression? current = node;

        while (current is MemberExpression member)
        {
            // x.NullableField.Value is the same SOQL field as x.NullableField.
            if (member.Member.Name == "Value" && member.Expression != null && Nullable.GetUnderlyingType(member.Expression.Type) != null)
            {
                current = member.Expression;
                continue;
            }

            parts.Insert(0, GetSalesforceFieldName(member.Member, member.Member.DeclaringType));
            current = member.Expression;

            if (current is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            {
                current = unary.Operand;
            }
        }

        if (current is ParameterExpression)
        {
            path = string.Join(".", parts);
            return true;
        }

        return false;
    }

    private void AppendFieldReference(Expression expression)
    {
        if (!TryGetMemberPath(expression, out var path))
        {
            throw new NotSupportedException("Contains must compare against a field reference.");
        }

        _whereClause.Append(SecurityUtils.SanitizeFieldName(path));
    }

    private static List<object?> ExtractValues(Expression expression)
    {
        var enumerable = GetExpressionValueStatic(expression) as System.Collections.IEnumerable;
        return ExtractValues(enumerable);
    }

    private static List<object?> ExtractValues(System.Collections.IEnumerable? enumerable)
    {
        var values = new List<object?>();
        if (enumerable == null)
        {
            return values;
        }

        foreach (var item in enumerable)
        {
            values.Add(item);
        }

        return values;
    }

    private void AppendAlwaysFalsePredicate()
    {
        _whereClause.Append("(Id = NULL)");
    }

    private static void EnsureNoParameter(Expression expression, string context)
    {
        if (ContainsParameter(expression))
        {
            throw new NotSupportedException($"Cannot evaluate {context} from a query parameter expression.");
        }
    }

    private static bool ContainsParameter(Expression expression)
    {
        var detector = new ParameterDetector();
        detector.Visit(expression);
        return detector.Found;
    }

    private sealed class ParameterDetector : ExpressionVisitor
    {
        // Parameters bound by lambdas nested inside the expression (e.g. c in sub.Select(c => c.AccountId))
        // are not query parameters: the expression is still evaluable client-side.
        private readonly HashSet<ParameterExpression> _bound = new();

        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node == null || Found)
            {
                return node;
            }

            return base.Visit(node);
        }

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            var added = node.Parameters.Where(_bound.Add).ToList();
            Visit(node.Body);
            foreach (var p in added) _bound.Remove(p);
            return node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (!_bound.Contains(node)) Found = true;
            return node;
        }
    }

    private static object? GetExpressionValueStatic(Expression expr)
    {
        var detector = new ParameterDetector();
        detector.Visit(expr);
        if (detector.Found)
        {
            throw new NotSupportedException("Cannot evaluate expression value from a query parameter expression.");
        }

        var objectExpr = Expression.Convert(expr, typeof(object));
        var lambda = Expression.Lambda<Func<object>>(objectExpr);
        var compiled = lambda.Compile();
        return compiled();
    }

    private static string FormatValue(object? value) => value switch
    {
        double dbl => FormatFloatingPoint(dbl),
        float fl => FormatFloatingPoint(fl),
        _ => Mapping.SalesforceWireFormat.SoqlLiteral(value)
    };

    /// <summary>
    /// SOQL numeric literals have no exponent form. Positional digits with full round-trip precision (2.5.0 and
    /// earlier went through decimal and kept 15 significant digits).
    /// </summary>
    private static string FormatFloatingPoint(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NotSupportedException($"The number {value.ToString(CultureInfo.InvariantCulture)} cannot be written as a SOQL literal.");
        }

        return Mapping.SalesforceWireFormat.FormatNumber(value);
    }

    private static string FormatFloatingPoint(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new NotSupportedException($"The number {value.ToString(CultureInfo.InvariantCulture)} cannot be written as a SOQL literal.");
        }

        return Mapping.SalesforceWireFormat.FormatNumber(value);
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
}

/// <summary>
/// Extension methods for building SOQL from LINQ expressions.
/// </summary>
public static class SoqlExpressionExtensions
{
    /// <summary>
    /// Converts a LINQ predicate to a SOQL WHERE clause.
    /// </summary>
    public static string ToSoqlWhere<T>(this Expression<Func<T, bool>> predicate)
    {
        return SoqlExpressionVisitor.Translate(predicate);
    }
}
