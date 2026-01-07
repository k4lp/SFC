using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Represents the result of a SOSL search query.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// List of search result groups, one per object type searched.
    /// </summary>
    [JsonPropertyName("searchRecords")]
    public List<SearchRecord> SearchRecords { get; set; } = new();
}

/// <summary>
/// Represents a single record returned from a SOSL search.
/// </summary>
public class SearchRecord
{
    /// <summary>
    /// The Salesforce record ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Attributes containing object type and URL.
    /// </summary>
    [JsonPropertyName("attributes")]
    public RecordAttributes? Attributes { get; set; }

    /// <summary>
    /// Additional fields returned in the search.
    /// Use the indexer or GetValue to access dynamic fields.
    /// </summary>
    /// <remarks>
    /// Changed from Dictionary&lt;string, JsonNode&gt; to Dictionary&lt;string, JsonElement&gt;
    /// for .NET 10 compatibility. JsonExtensionData requires IDictionary&lt;string, JsonElement&gt;
    /// or IDictionary&lt;string, object&gt; in .NET 10+.
    /// </remarks>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }

    /// <summary>
    /// Gets a field value by name.
    /// </summary>
    /// <typeparam name="T">Type to convert to.</typeparam>
    /// <param name="fieldName">Field name.</param>
    /// <returns>Field value or default.</returns>
    public T? GetValue<T>(string fieldName)
    {
        if (AdditionalFields != null && AdditionalFields.TryGetValue(fieldName, out var element))
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        return default;
    }
}

/// <summary>
/// Represents the attributes metadata for a Salesforce record.
/// </summary>
public class RecordAttributes
{
    /// <summary>
    /// The SObject type name.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The record URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Defines the scope for SOSL searches.
/// </summary>
public enum SearchScope
{
    /// <summary>
    /// Search all fields.
    /// </summary>
    AllFields,

    /// <summary>
    /// Search only name fields.
    /// </summary>
    NameFields,

    /// <summary>
    /// Search email fields.
    /// </summary>
    EmailFields,

    /// <summary>
    /// Search phone fields.
    /// </summary>
    PhoneFields,

    /// <summary>
    /// Search sidebar fields (custom searchable fields).
    /// </summary>
    SidebarFields
}

/// <summary>
/// Configuration for a SOSL search returning clause.
/// </summary>
public class SearchReturningClause
{
    /// <summary>
    /// The SObject type to search.
    /// </summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>
    /// Fields to return in results.
    /// </summary>
    public List<string> Fields { get; set; } = new() { "Id" };

    /// <summary>
    /// Optional WHERE clause to filter results.
    /// </summary>
    public string? WhereClause { get; set; }

    /// <summary>
    /// Optional ORDER BY clause.
    /// </summary>
    public string? OrderBy { get; set; }

    /// <summary>
    /// Maximum records to return for this object type.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Builds the RETURNING clause portion for this object.
    /// </summary>
    internal string Build()
    {
        var fieldList = string.Join(", ", Fields);
        var result = $"{ObjectType}({fieldList}";

        if (!string.IsNullOrWhiteSpace(WhereClause))
        {
            result += $" WHERE {WhereClause}";
        }

        if (!string.IsNullOrWhiteSpace(OrderBy))
        {
            result += $" ORDER BY {OrderBy}";
        }

        if (Limit.HasValue)
        {
            result += $" LIMIT {Limit.Value}";
        }

        result += ")";
        return result;
    }
}

/// <summary>
/// Builder for constructing SOSL search queries.
/// </summary>
public class SoslBuilder
{
    private string _searchTerm = string.Empty;
    private SearchScope _scope = SearchScope.AllFields;
    private readonly List<SearchReturningClause> _returningClauses = new();
    private int? _limit;
    private bool _withSnippet;
    private bool _withSpellCorrection = true;

    /// <summary>
    /// Sets the search term (supports wildcards * and ?).
    /// </summary>
    /// <param name="term">Search term.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder Find(string term)
    {
        _searchTerm = term;
        return this;
    }

    /// <summary>
    /// Sets the search scope.
    /// </summary>
    /// <param name="scope">Search scope.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder In(SearchScope scope)
    {
        _scope = scope;
        return this;
    }

    /// <summary>
    /// Adds an object type to search with default fields (Id).
    /// </summary>
    /// <param name="objectType">SObject type name.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder Returning(string objectType)
    {
        _returningClauses.Add(new SearchReturningClause { ObjectType = objectType });
        return this;
    }

    /// <summary>
    /// Adds an object type to search with specified fields.
    /// </summary>
    /// <param name="objectType">SObject type name.</param>
    /// <param name="fields">Fields to return.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder Returning(string objectType, params string[] fields)
    {
        _returningClauses.Add(new SearchReturningClause
        {
            ObjectType = objectType,
            Fields = fields.ToList()
        });
        return this;
    }

    /// <summary>
    /// Adds an object type to search with full configuration.
    /// </summary>
    /// <param name="clause">Returning clause configuration.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder Returning(SearchReturningClause clause)
    {
        _returningClauses.Add(clause);
        return this;
    }

    /// <summary>
    /// Sets the overall result limit.
    /// </summary>
    /// <param name="limit">Maximum records to return.</param>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder WithLimit(int limit)
    {
        _limit = limit;
        return this;
    }

    /// <summary>
    /// Enables result snippets.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder WithSnippet()
    {
        _withSnippet = true;
        return this;
    }

    /// <summary>
    /// Disables spell correction.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public SoslBuilder WithoutSpellCorrection()
    {
        _withSpellCorrection = false;
        return this;
    }

    /// <summary>
    /// Builds the SOSL query string.
    /// </summary>
    /// <returns>Complete SOSL query.</returns>
    public string Build()
    {
        if (string.IsNullOrWhiteSpace(_searchTerm))
        {
            throw new InvalidOperationException("Search term is required. Call Find() first.");
        }

        if (_returningClauses.Count == 0)
        {
            throw new InvalidOperationException("At least one RETURNING clause is required. Call Returning() first.");
        }

        // Escape special SOSL characters
        var escapedTerm = EscapeSoslTerm(_searchTerm);

        var sosl = $"FIND {{{escapedTerm}}}";

        // Add scope
        sosl += _scope switch
        {
            SearchScope.AllFields => " IN ALL FIELDS",
            SearchScope.NameFields => " IN NAME FIELDS",
            SearchScope.EmailFields => " IN EMAIL FIELDS",
            SearchScope.PhoneFields => " IN PHONE FIELDS",
            SearchScope.SidebarFields => " IN SIDEBAR FIELDS",
            _ => " IN ALL FIELDS"
        };

        // Add RETURNING clauses
        var returningParts = _returningClauses.Select(c => c.Build());
        sosl += $" RETURNING {string.Join(", ", returningParts)}";

        // Add LIMIT
        if (_limit.HasValue)
        {
            sosl += $" LIMIT {_limit.Value}";
        }

        // Add WITH clauses
        if (_withSnippet)
        {
            sosl += " WITH SNIPPET";
        }

        if (!_withSpellCorrection)
        {
            sosl += " WITH SPELL_CORRECTION = false";
        }

        return sosl;
    }

    /// <summary>
    /// Escapes special characters in a SOSL search term.
    /// </summary>
    private static string EscapeSoslTerm(string term)
    {
        // SOSL reserved characters that need escaping: ? & | ! { } [ ] ( ) ^ ~ * : \ " ' + -
        // However, * and ? are wildcards that users might want to use
        var reserved = new[] { '&', '|', '!', '{', '}', '[', ']', '(', ')', '^', '~', ':', '\\', '"', '\'', '+', '-' };

        foreach (var c in reserved)
        {
            term = term.Replace(c.ToString(), "\\" + c);
        }

        return term;
    }
}
