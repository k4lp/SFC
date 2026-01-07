using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
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

        _whereClause.Append('(');

        // Handle null comparisons specially
        if (IsNullComparison(node, out var memberExpr, out var isEquals) && memberExpr != null)
        {
            VisitMember(memberExpr);
            _whereClause.Append(isEquals ? " = NULL" : " != NULL");
            _whereClause.Append(')');
            return node;
        }

        Visit(node.Left);

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

        Visit(node.Right);

        _whereClause.Append(')');

        return node;
    }

    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType == ExpressionType.Not)
        {
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

    protected override Expression VisitConstant(ConstantExpression node)
    {
        AppendValue(node.Value);
        return node;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
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

        throw new NotSupportedException($"Method '{node.Method.Name}' is not supported in SOQL");
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
                throw new NotSupportedException($"String method '{methodName}' is not supported in SOQL");
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

    private bool IsNullComparison(BinaryExpression node, out MemberExpression? memberExpr, out bool isEquals)
    {
        memberExpr = null;
        isEquals = node.NodeType == ExpressionType.Equal;

        if (node.NodeType != ExpressionType.Equal && node.NodeType != ExpressionType.NotEqual)
            return false;

        // Check left is member and right is null constant
        if (node.Left is MemberExpression leftMember && IsNullConstant(node.Right))
        {
            memberExpr = leftMember;
            return true;
        }

        // Check right is member and left is null constant
        if (node.Right is MemberExpression rightMember && IsNullConstant(node.Left))
        {
            memberExpr = rightMember;
            return true;
        }

        return false;
    }

    private static bool IsNullConstant(Expression expr)
    {
        return expr is ConstantExpression { Value: null };
    }

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
        public bool Found { get; private set; }

        public override Expression? Visit(Expression? node)
        {
            if (node == null || Found)
            {
                return node;
            }

            if (node.NodeType == ExpressionType.Parameter)
            {
                Found = true;
                return node;
            }

            return base.Visit(node);
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
            _ => $"'{SecurityUtils.SanitizeForSoql(value.ToString() ?? "")}'"
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
