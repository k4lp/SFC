using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Implementation of intelligent lookup search operations with caching support.
/// Caches search results to reduce API calls across multiple servers.
/// </summary>
public class LookupService : ILookupService
{
    private readonly ISalesforceClient _client;
    private readonly ISchemaService _schemaService;
    private readonly ICacheProvider _cache;
    private readonly SalesforceOptions _options;
    private readonly ILogger<LookupService> _logger;

    private const string LookupCachePrefix = "Lookup_";
    private const string RecentItemsCachePrefix = "Recent_";

    /// <summary>
    /// Creates a new LookupService.
    /// </summary>
    public LookupService(
        ISalesforceClient client,
        ISchemaService schemaService,
        ICacheProvider cache,
        IOptions<SalesforceOptions> options,
        ILogger<LookupService> logger)
    {
        _client = client;
        _schemaService = schemaService;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LookupSearchResult> SearchAsync(
        LookupSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        // Generate cache key based on search parameters
        var cacheKey = GenerateSearchCacheKey(options);

        var cached = await _cache.GetAsync<LookupSearchResult>(cacheKey, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var result = new LookupSearchResult
        {
            TargetObject = options.TargetObject,
            SearchQuery = options.Query
        };

        try
        {
            var nameField = await _schemaService.GetNameFieldAsync(options.TargetObject, cancellationToken);
            var searchFields = options.SearchFields?.ToList() ?? GetDefaultSearchFields(options.TargetObject);
            var contextFields = options.ContextFields?.ToList() ?? GetDefaultContextFields(options.TargetObject);

            // Build field list for query
            var allFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Id", nameField };
            foreach (var field in searchFields.Concat(contextFields))
            {
                allFields.Add(field);
            }

            var validFields = await _schemaService.SanitizeFieldListAsync(
                options.TargetObject,
                allFields,
                cancellationToken);

            var fieldString = string.Join(", ", validFields);

            // Build type-safe query using SoqlBuilder
            var queryBuilder = SoqlBuilder.From(options.TargetObject)
                .Select(validFields)
                .OrderBy(nameField)
                .Limit(options.Limit);

            // Multi-field search conditions
            if (searchFields.Any() && !string.IsNullOrEmpty(options.Query))
            {
                var searchConditions = searchFields
                    .Select(f => SoqlCondition.Like(f, $"%{options.Query}%"))
                    .ToArray();
                queryBuilder.WhereCondition(SoqlCondition.Or(searchConditions));
            }

            // Parent filter for dependent lookups
            if (!string.IsNullOrEmpty(options.ParentField) && !string.IsNullOrEmpty(options.ParentValue))
            {
                queryBuilder.WhereEquals(options.ParentField, options.ParentValue);
            }

            // Type-safe additional filter
            if (options.Filter != null)
            {
                queryBuilder.WhereCondition(options.Filter);
            }

            var soql = queryBuilder.Build();
            var queryResult = await _client.GetAsync<QueryResult>($"/query/?q={UrlUtils.Escape(soql)}", cancellationToken);

            foreach (var record in queryResult.Records)
            {
                var item = CreateLookupItem(record, options.TargetObject, nameField, contextFields);
                item.Score = CalculateScore(item.DisplayName, options.Query);
                result.Items.Add(item);
            }

            // Sort by score descending
            result.Items = result.Items.OrderByDescending(i => i.Score).ToList();
            result.HasMore = queryResult.Records.Count >= options.Limit;

            // Cache the result
            await _cache.SetAsync(cacheKey, result, _options.LookupCacheDuration, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lookup search failed for {TargetObject}", options.TargetObject);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<LookupSearchResult> SearchWithConfigAsync(
        string targetObject,
        string query,
        RelationshipConfig? config = null,
        Dictionary<string, string>? contextValues = null,
        CancellationToken cancellationToken = default)
    {
        var options = new LookupSearchOptions
        {
            TargetObject = targetObject,
            Query = query,
            Limit = _options.LookupSearchLimit
        };

        if (config?.LookupBehavior != null)
        {
            var behavior = config.LookupBehavior;
            options.SearchFields = behavior.SearchFields;
            options.ContextFields = behavior.DisplayFields;
            // Note: StaticFilter from config is deprecated - use code-based SoqlCondition instead
            // This prevents SOQL injection from configuration files
        }

        // Handle dependent lookup
        if (!string.IsNullOrEmpty(config?.DependsOn) && contextValues != null)
        {
            if (contextValues.TryGetValue(config.DependsOn, out var parentValue) && !string.IsNullOrEmpty(parentValue))
            {
                options.ParentField = config.FilterField ?? config.DependsOn;
                options.ParentValue = parentValue;
            }
        }

        return await SearchAsync(options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<LookupSearchResult> SearchPolymorphicAsync(
        IEnumerable<string> targetObjects,
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        var result = new LookupSearchResult
        {
            SearchQuery = query
        };

        var tasks = targetObjects.Select(async obj =>
        {
            var searchResult = await SearchAsync(new LookupSearchOptions
            {
                TargetObject = obj,
                Query = query,
                Limit = limit
            }, cancellationToken);

            return searchResult.Items;
        });

        var allResults = await Task.WhenAll(tasks);

        result.Items = allResults
            .SelectMany(items => items)
            .OrderByDescending(i => i.Score)
            .Take(limit * targetObjects.Count())
            .ToList();

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<LookupResultItem>> GetRecentItemsAsync(
        string targetObject,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(targetObject))
            throw new ArgumentException("Invalid object name", nameof(targetObject));

        if (limit < 1 || limit > 200)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 200");

        // Note: Recent items are user-specific and not cached in distributed cache
        // They rely on LastViewedDate which is user-specific
        var items = new List<LookupResultItem>();

        try
        {
            var nameField = await _schemaService.GetNameFieldAsync(targetObject, cancellationToken);

            var soql = SoqlBuilder.From(targetObject)
                .Select("Id", nameField)
                .OrderByNullsLast("LastViewedDate", descending: true)
                .Limit(limit)
                .Build();

            var queryResult = await _client.GetAsync<QueryResult>($"/query/?q={UrlUtils.Escape(soql)}", cancellationToken);

            foreach (var record in queryResult.Records)
            {
                items.Add(new LookupResultItem
                {
                    Id = record["Id"]?.ToString() ?? "",
                    DisplayName = record[nameField]?.ToString() ?? record["Id"]?.ToString() ?? "",
                    ObjectType = targetObject,
                    IsRecent = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get recent items for {TargetObject}", targetObject);
        }

        return items;
    }

    /// <inheritdoc/>
    public double CalculateScore(string displayName, string query, LookupWeights? weights = null)
    {
        weights ??= new LookupWeights();

        if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(query))
        {
            return 0;
        }

        var name = displayName.ToLowerInvariant();
        var q = query.ToLowerInvariant();

        if (name.Equals(q))
        {
            return weights.ExactMatch;
        }

        if (name.StartsWith(q))
        {
            return weights.StartsWith;
        }

        var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Any(w => w.StartsWith(q)))
        {
            return weights.WordStartsWith;
        }

        if (name.Contains(q))
        {
            return weights.Contains;
        }

        return 0;
    }

    private LookupResultItem CreateLookupItem(
        JsonObject record,
        string objectType,
        string nameField,
        IEnumerable<string> contextFields)
    {
        var item = new LookupResultItem
        {
            Id = record["Id"]?.ToString() ?? "",
            DisplayName = record[nameField]?.ToString() ?? record["Id"]?.ToString() ?? "",
            ObjectType = objectType,
            ObjectLabel = objectType,
            IconClass = SalesforceConventions.GetDefaultIcon(objectType)
        };

        foreach (var field in contextFields)
        {
            var value = record[field]?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                item.ContextFields[field] = value;
            }
        }

        if (item.ContextFields.Any())
        {
            item.SecondaryText = string.Join(" | ", item.ContextFields.Values.Take(2));
        }

        return item;
    }

    private static string GenerateSearchCacheKey(LookupSearchOptions options)
    {
        // Create a deterministic cache key from search parameters
        var keyParts = new List<string>
        {
            LookupCachePrefix,
            options.TargetObject,
            options.Query?.ToLowerInvariant() ?? "",
            options.Limit.ToString()
        };

        if (!string.IsNullOrEmpty(options.ParentField))
        {
            keyParts.Add(options.ParentField);
            keyParts.Add(options.ParentValue ?? "");
        }

        if (options.Filter != null)
        {
            // Use the rendered condition for cache key uniqueness
            keyParts.Add(options.Filter.GetHashCode().ToString());
        }

        return string.Join("_", keyParts);
    }

    private List<string> GetDefaultSearchFields(string objectType)
    {
        if (SalesforceConventions.ObjectSearchFields.TryGetValue(objectType, out var fields))
        {
            return fields.ToList();
        }

        return new List<string> { "Name" };
    }

    private List<string> GetDefaultContextFields(string objectType)
    {
        if (SalesforceConventions.ObjectContextFields.TryGetValue(objectType, out var fields))
        {
            return fields.ToList();
        }

        return new List<string>();
    }
}
