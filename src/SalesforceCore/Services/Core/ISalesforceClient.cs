using System.Text.Json.Nodes;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Core HTTP client interface for Salesforce REST API operations.
/// Handles authentication, request/response, and error handling.
/// </summary>
public interface ISalesforceClient
{
    /// <summary>
    /// Gets the current Salesforce instance URL.
    /// </summary>
    string InstanceUrl { get; }

    /// <summary>
    /// Gets the configured API version.
    /// </summary>
    string ApiVersion { get; }

    /// <summary>
    /// Performs a GET request to the specified endpoint.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="endpoint">API endpoint (relative to instance URL).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns raw JSON.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a POST request with JSON payload.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="payload">Request payload (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> PostAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a POST request and returns raw JSON.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="payload">Request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonNode> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PUT request with JSON payload.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    Task<T> PutAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PUT request and returns raw JSON.
    /// </summary>
    Task<JsonNode> PutAsync(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PATCH request with JSON payload and returns deserialized response.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="payload">Request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> PatchAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PATCH request and returns raw JSON.
    /// </summary>
    Task<JsonNode> PatchAsync(string endpoint, object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a DELETE request.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a DELETE request and returns deserialized response.
    /// </summary>
    Task<T> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns raw bytes.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Byte array response.</returns>
    Task<byte[]> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns a stream.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response stream.</returns>
    Task<Stream> GetStreamAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the full API endpoint URL.
    /// </summary>
    /// <param name="relativePath">Relative path.</param>
    /// <returns>Full URL.</returns>
    string BuildApiUrl(string relativePath);

    /// <summary>
    /// Gets the current access token from the token provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Access token or null if not authenticated.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PUT request with raw string content (e.g., CSV for Bulk API).
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="content">Raw string content.</param>
    /// <param name="contentType">Content type (e.g., "text/csv").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PutRawAsync(string endpoint, string content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns raw string content with a specific Accept header.
    /// </summary>
    /// <param name="endpoint">API endpoint.</param>
    /// <param name="acceptHeader">Accept header value (e.g., "text/csv").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw string response.</returns>
    Task<string> GetRawAsync(string endpoint, string acceptHeader, CancellationToken cancellationToken = default);
}

/// <summary>
/// Token provider interface for managing Salesforce OAuth tokens.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Gets the current access token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Access token or null if not authenticated.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current instance URL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Instance URL or null if not authenticated.</returns>
    Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the current token.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the access token if possible.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New access token or null.</returns>
    Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a valid token exists.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if authenticated.</returns>
    Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default);
}