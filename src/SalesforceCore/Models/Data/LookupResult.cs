using SalesforceCore.Services.Query;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Result of a lookup search operation.
/// </summary>
public class LookupSearchResult
{
    /// <summary>
    /// Target object type that was searched.
    /// </summary>
    public string TargetObject { get; set; } = string.Empty;

    /// <summary>
    /// The search query used.
    /// </summary>
    public string SearchQuery { get; set; } = string.Empty;

    /// <summary>
    /// List of matching items.
    /// </summary>
    public List<LookupResultItem> Items { get; set; } = new();

    /// <summary>
    /// Whether there are more results available.
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Total number of results (if known).
    /// </summary>
    public int? TotalCount { get; set; }
}

/// <summary>
/// A single item in lookup search results.
/// </summary>
public class LookupResultItem
{
    /// <summary>
    /// Record ID.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name (resolved from Name field or equivalent).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Secondary text for additional context.
    /// </summary>
    public string? SecondaryText { get; set; }

    /// <summary>
    /// Object type (for polymorphic lookups).
    /// </summary>
    public string? ObjectType { get; set; }

    /// <summary>
    /// Object label for display.
    /// </summary>
    public string? ObjectLabel { get; set; }

    /// <summary>
    /// Icon class for the object type.
    /// </summary>
    public string? IconClass { get; set; }

    /// <summary>
    /// Additional context fields.
    /// </summary>
    public Dictionary<string, string?> ContextFields { get; set; } = new();

    /// <summary>
    /// Relevance score for ranking.
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Whether this is a recently viewed item.
    /// </summary>
    public bool IsRecent { get; set; }
}

/// <summary>
/// Represents hydrated lookup data for display.
/// </summary>
public class HydratedLookup
{
    /// <summary>
    /// The lookup field name.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// The record ID value.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The resolved display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The target object type.
    /// </summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this lookup could be resolved.
    /// </summary>
    public bool IsResolved { get; set; } = true;
}

/// <summary>
/// Options for lookup search operations.
/// </summary>
public class LookupSearchOptions
{
    /// <summary>
    /// Target object to search.
    /// </summary>
    public string TargetObject { get; set; } = string.Empty;

    /// <summary>
    /// Search query string.
    /// </summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of results.
    /// </summary>
    public int Limit { get; set; } = 15;

    /// <summary>
    /// Parent field for dependent lookups.
    /// </summary>
    public string? ParentField { get; set; }

    /// <summary>
    /// Parent value for dependent lookups.
    /// </summary>
    public string? ParentValue { get; set; }

    /// <summary>
    /// For polymorphic lookups, the allowed target types.
    /// </summary>
    public List<string>? PolymorphicTargets { get; set; }

    /// <summary>
    /// Type-safe additional filter condition.
    /// Use SoqlCondition factory methods to build conditions.
    /// </summary>
    public SoqlCondition? Filter { get; set; }

    /// <summary>
    /// Fields to search on.
    /// </summary>
    public List<string>? SearchFields { get; set; }

    /// <summary>
    /// Fields to include in context.
    /// </summary>
    public List<string>? ContextFields { get; set; }

    /// <summary>
    /// Include recently viewed items.
    /// </summary>
    public bool IncludeRecentItems { get; set; } = true;
}
