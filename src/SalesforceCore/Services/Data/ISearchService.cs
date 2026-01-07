using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Data;

/// <summary>
/// Service for executing SOSL (Salesforce Object Search Language) searches.
/// SOSL allows searching across multiple objects simultaneously using
/// Salesforce's search index for efficient text searching.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Executes a raw SOSL query string.
    /// </summary>
    /// <param name="sosl">The complete SOSL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    /// <example>
    /// <code>
    /// var result = await searchService.SearchAsync(
    ///     "FIND {John} IN ALL FIELDS RETURNING Account(Id, Name), Contact(Id, Name, Email)");
    /// </code>
    /// </example>
    Task<SearchResult> SearchAsync(string sosl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a search using the SOSL builder for type-safe query construction.
    /// </summary>
    /// <param name="builder">The SOSL builder with the query configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results.</returns>
    /// <example>
    /// <code>
    /// var result = await searchService.SearchAsync(
    ///     new SoslBuilder()
    ///         .Find("John*")
    ///         .In(SearchScope.AllFields)
    ///         .Returning("Account", "Id", "Name")
    ///         .Returning("Contact", "Id", "Name", "Email")
    ///         .WithLimit(100));
    /// </code>
    /// </example>
    Task<SearchResult> SearchAsync(SoslBuilder builder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a simple search across specified objects.
    /// </summary>
    /// <param name="searchTerm">The text to search for (supports wildcards * and ?).</param>
    /// <param name="objects">The SObject types to search.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with Id for each matched record.</returns>
    /// <example>
    /// <code>
    /// var result = await searchService.FindAsync("Acme*", new[] { "Account", "Contact" });
    /// </code>
    /// </example>
    Task<SearchResult> FindAsync(
        string searchTerm,
        IEnumerable<string> objects,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a search with specified fields to return.
    /// </summary>
    /// <param name="searchTerm">The text to search for.</param>
    /// <param name="objectFields">Dictionary mapping object types to fields to return.</param>
    /// <param name="scope">Search scope (default: AllFields).</param>
    /// <param name="limit">Maximum results per object type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with specified fields.</returns>
    /// <example>
    /// <code>
    /// var result = await searchService.FindAsync(
    ///     "John",
    ///     new Dictionary&lt;string, string[]&gt;
    ///     {
    ///         ["Account"] = new[] { "Id", "Name", "Industry" },
    ///         ["Contact"] = new[] { "Id", "Name", "Email", "Phone" }
    ///     },
    ///     SearchScope.NameFields,
    ///     limit: 25);
    /// </code>
    /// </example>
    Task<SearchResult> FindAsync(
        string searchTerm,
        Dictionary<string, string[]> objectFields,
        SearchScope scope = SearchScope.AllFields,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new SOSL builder for constructing search queries.
    /// </summary>
    /// <returns>A new SOSL builder instance.</returns>
    SoslBuilder CreateBuilder();
}
