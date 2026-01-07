using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Apex;

/// <summary>
/// Implementation of the custom Apex REST service.
/// Provides a wrapper for calling custom @RestResource endpoints.
/// Refactored to use ISalesforceClient for consistent auth and resilience.
/// </summary>
public class ApexService : IApexService
{
    private readonly ISalesforceClient _client;
    private readonly ILogger<ApexService> _logger;

    /// <summary>
    /// Creates a new ApexService.
    /// </summary>
    public ApexService(
        ISalesforceClient client,
        ILogger<ApexService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        return await _client.GetAsync<T>(url, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JsonObject> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        var result = await _client.GetAsync(url, cancellationToken);
        return result.AsObject();
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(string path, Dictionary<string, string> queryParams, CancellationToken cancellationToken = default)
    {
        var queryString = string.Join("&", queryParams.Select(kv =>
            $"{UrlUtils.Escape(kv.Key)}={UrlUtils.Escape(kv.Value)}"));

        var fullPath = path.Contains('?')
            ? $"{path}&{queryString}"
            : $"{path}?{queryString}";

        return await GetAsync<T>(fullPath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        return await _client.PostAsync<T>(url, body, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JsonObject> PostAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        var result = await _client.PostAsync(url, body, cancellationToken);
        return result.AsObject();
    }

    /// <inheritdoc/>
    public async Task<T> PostAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        return await _client.PostAsync<T>(url, null!, cancellationToken); 
    }

    /// <inheritdoc/>
    public async Task<T> PutAsync<T>(string path, object body, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        return await _client.PutAsync<T>(url, body, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JsonObject> PutAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        var result = await _client.PutAsync(url, body, cancellationToken);
        return result.AsObject();
    }

    /// <inheritdoc/>
    public async Task<T> PatchAsync<T>(string path, object body, CancellationToken cancellationToken = default)
    {
         var url = BuildApexPath(path);
         return await _client.PatchAsync<T>(url, body, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JsonObject> PatchAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        var result = await _client.PatchAsync(url, body, cancellationToken);
        return result.AsObject();
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        await _client.DeleteAsync(url, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> DeleteAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var url = BuildApexPath(path);
        return await _client.DeleteAsync<T>(url, cancellationToken);
    }

    private string BuildApexPath(string path)
    {
        if (path.StartsWith(SalesforceConstants.Paths.ApexRest, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (!path.StartsWith("/"))
        {
            path = "/" + path;
        }

        return $"{SalesforceConstants.Paths.ApexRest}{path}";
    }
}
