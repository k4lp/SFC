using System.Text.Json.Nodes;

namespace SalesforceCore.Services.Apex;

/// <summary>
/// Service for calling custom Apex REST endpoints.
/// These are endpoints defined with @RestResource annotations in Apex classes.
/// </summary>
public interface IApexService
{
    /// <summary>
    /// Performs a GET request to a custom Apex REST endpoint.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path (relative to /services/apexrest/).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    /// <example>
    /// <code>
    /// // For an Apex endpoint: @RestResource(urlMapping='/myapi/accounts/*')
    /// var result = await apexService.GetAsync&lt;MyResponse&gt;("/myapi/accounts/001xxx");
    /// </code>
    /// </example>
    Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns raw JSON.
    /// </summary>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonObject> GetAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request with query parameters.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="queryParams">Query parameters to append to the URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> GetAsync<T>(string path, Dictionary<string, string> queryParams, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a POST request to a custom Apex REST endpoint.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body (will be serialized to JSON).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    /// <example>
    /// <code>
    /// var request = new { name = "Test", type = "Customer" };
    /// var result = await apexService.PostAsync&lt;CreateResponse&gt;("/myapi/create", request);
    /// </code>
    /// </example>
    Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a POST request and returns raw JSON.
    /// </summary>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonObject> PostAsync(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a POST request without a body.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> PostAsync<T>(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PUT request to a custom Apex REST endpoint.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> PutAsync<T>(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PUT request and returns raw JSON.
    /// </summary>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonObject> PutAsync(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PATCH request to a custom Apex REST endpoint.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> PatchAsync<T>(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a PATCH request and returns raw JSON.
    /// </summary>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="body">Request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JsonObject response.</returns>
    Task<JsonObject> PatchAsync(string path, object body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a DELETE request to a custom Apex REST endpoint.
    /// </summary>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a DELETE request and returns the response.
    /// </summary>
    /// <typeparam name="T">Response type to deserialize.</typeparam>
    /// <param name="path">The custom endpoint path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T> DeleteAsync<T>(string path, CancellationToken cancellationToken = default);
}
