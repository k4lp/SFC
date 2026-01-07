using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Service for intelligent lookup field search operations.
/// Supports polymorphic lookups, dependent lookups, and smart ranking.
/// </summary>
public interface ILookupService
{
    /// <summary>
    /// Searches for lookup records with intelligent ranking.
    /// </summary>
    /// <param name="options">Search options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with ranking.</returns>
    Task<LookupSearchResult> SearchAsync(
        LookupSearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches with relationship configuration.
    /// </summary>
    /// <param name="targetObject">Target object type.</param>
    /// <param name="query">Search query.</param>
    /// <param name="config">Relationship configuration.</param>
    /// <param name="contextValues">Current form values for dependent lookups.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    Task<LookupSearchResult> SearchWithConfigAsync(
        string targetObject,
        string query,
        RelationshipConfig? config = null,
        Dictionary<string, string>? contextValues = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches across multiple target objects for polymorphic lookups.
    /// </summary>
    /// <param name="targetObjects">Target object types.</param>
    /// <param name="query">Search query.</param>
    /// <param name="limit">Maximum results per object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Combined search results.</returns>
    Task<LookupSearchResult> SearchPolymorphicAsync(
        IEnumerable<string> targetObjects,
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recent items for a target object.
    /// </summary>
    /// <param name="targetObject">Target object type.</param>
    /// <param name="limit">Maximum items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent items.</returns>
    Task<List<LookupResultItem>> GetRecentItemsAsync(
        string targetObject,
        int limit = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates relevance score for a lookup result.
    /// </summary>
    /// <param name="displayName">Record display name.</param>
    /// <param name="query">Search query.</param>
    /// <param name="weights">Scoring weights.</param>
    /// <returns>Relevance score.</returns>
    double CalculateScore(string displayName, string query, LookupWeights? weights = null);
}
