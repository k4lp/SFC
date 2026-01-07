using SalesforceCore.Utilities;
using Microsoft.Extensions.Logging;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Implementation of SOSL search operations for Salesforce.
/// Provides true full-text search capabilities across multiple objects.
/// </summary>
public class SearchService : ISearchService
{
    private readonly ISalesforceClient _client;
    private readonly ILogger<SearchService> _logger;

    /// <summary>
    /// Creates a new SearchService.
    /// </summary>
    public SearchService(
        ISalesforceClient client,
        ILogger<SearchService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SearchResult> SearchAsync(string sosl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sosl))
        {
            throw new ArgumentException("SOSL query cannot be empty.", nameof(sosl));
        }

        _logger.LogDebug("Executing SOSL search: {Query}", sosl);

        var encodedSosl = UrlUtils.Escape(sosl);
        var endpoint = $"{SalesforceConstants.Paths.Search}/?q={encodedSosl}";

        var result = await _client.GetAsync<SearchResult>(endpoint, cancellationToken);

        _logger.LogDebug("SOSL search returned {Count} records", result.SearchRecords.Count);

        return result;
    }

    /// <inheritdoc/>
    public async Task<SearchResult> SearchAsync(SoslBuilder builder, CancellationToken cancellationToken = default)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        var sosl = builder.Build();
        return await SearchAsync(sosl, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SearchResult> FindAsync(
        string searchTerm,
        IEnumerable<string> objects,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("Search term cannot be empty.", nameof(searchTerm));
        }

        var objectList = objects?.ToList();
        if (objectList == null || objectList.Count == 0)
        {
            throw new ArgumentException("At least one object type is required.", nameof(objects));
        }

        var builder = new SoslBuilder()
            .Find(searchTerm)
            .In(SearchScope.AllFields);

        foreach (var obj in objectList)
        {
            builder.Returning(obj);
        }

        return await SearchAsync(builder, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SearchResult> FindAsync(
        string searchTerm,
        Dictionary<string, string[]> objectFields,
        SearchScope scope = SearchScope.AllFields,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("Search term cannot be empty.", nameof(searchTerm));
        }

        if (objectFields == null || objectFields.Count == 0)
        {
            throw new ArgumentException("At least one object type with fields is required.", nameof(objectFields));
        }

        var builder = new SoslBuilder()
            .Find(searchTerm)
            .In(scope);

        foreach (var (objectType, fields) in objectFields)
        {
            var clause = new SearchReturningClause
            {
                ObjectType = objectType,
                Fields = fields.ToList(),
                Limit = limit
            };
            builder.Returning(clause);
        }

        return await SearchAsync(builder, cancellationToken);
    }

    /// <inheritdoc/>
    public SoslBuilder CreateBuilder() => new SoslBuilder();
}
