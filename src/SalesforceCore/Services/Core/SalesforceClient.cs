using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;

namespace SalesforceCore.Services.Core;

/// <summary>
/// HTTP client implementation for Salesforce REST API.
/// Handles authentication, serialization, and error handling.
/// </summary>
public class SalesforceClient : ISalesforceClient
{
    private readonly HttpClient _httpClient;
    private readonly ITokenProvider _tokenProvider;
    private readonly SalesforceOptions _options;
    private readonly ILogger<SalesforceClient> _logger;
    private volatile string? _instanceUrl; // Volatile for thread-safe reads

    /// <summary>
    /// Creates a new SalesforceClient.
    /// </summary>
    public SalesforceClient(
        HttpClient httpClient,
        ITokenProvider tokenProvider,
        IOptions<SalesforceOptions> options,
        ILogger<SalesforceClient> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;

        _httpClient.Timeout = _options.HttpTimeout;
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc/>
    public string InstanceUrl => _instanceUrl ?? throw new InvalidOperationException("Instance URL not set. Ensure authentication is complete.");

    /// <inheritdoc/>
    public string ApiVersion => _options.ApiVersion;

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken);
        return JsonSerializer.Deserialize<T>(response)!;
    }

    /// <inheritdoc/>
    public async Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Get, endpoint, null, cancellationToken);
        return JsonNode.Parse(response)!;
    }

    /// <inheritdoc/>
    public async Task<T> PostAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Post, endpoint, payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(response)!;
    }

    /// <inheritdoc/>
    public async Task<JsonNode> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Post, endpoint, payload, cancellationToken);
        return JsonNode.Parse(response)!;
    }

    /// <inheritdoc/>
    public async Task<T> PutAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Put, endpoint, payload, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            if (typeof(T) == typeof(JsonNode) || typeof(T) == typeof(JsonObject))
            {
                return (T)(object)new JsonObject();
            }

            if (typeof(T) == typeof(JsonArray))
            {
                return (T)(object)new JsonArray();
            }

            return default!;
        }
        return JsonSerializer.Deserialize<T>(response)!;
    }

    /// <inheritdoc/>
    public async Task<JsonNode> PutAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Put, endpoint, payload, cancellationToken);
        // Some Salesforce endpoints may return 204 No Content on PUT-like operations.
        if (string.IsNullOrWhiteSpace(response))
        {
            return new JsonObject();
        }
        return JsonNode.Parse(response)!;
    }

    /// <inheritdoc/>
    public async Task<T> PatchAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Patch, endpoint, payload, cancellationToken);

        // Salesforce PATCH commonly returns 204 No Content (empty body) on success.
        // For JSON node types, return an empty object to avoid null dereferences in callers.
        if (string.IsNullOrWhiteSpace(response))
        {
            if (typeof(T) == typeof(JsonNode) || typeof(T) == typeof(JsonObject))
            {
                return (T)(object)new JsonObject();
            }

            if (typeof(T) == typeof(JsonArray))
            {
                return (T)(object)new JsonArray();
            }

            return default!;
        }

        return JsonSerializer.Deserialize<T>(response)!;
    }

    /// <inheritdoc/>
    public async Task<JsonNode> PatchAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Patch, endpoint, payload, cancellationToken);
        // Salesforce PATCH returns 204 No Content (empty response) on success.
        // Return an empty object to keep downstream callers safe (e.g., .AsObject()).
        if (string.IsNullOrWhiteSpace(response))
        {
            return new JsonObject();
        }
        return JsonNode.Parse(response)!;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync(HttpMethod.Delete, endpoint, null, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<T> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(HttpMethod.Delete, endpoint, null, cancellationToken);
        // Salesforce DELETE commonly returns 204 No Content (empty body) on success.
        if (string.IsNullOrWhiteSpace(response))
        {
            if (typeof(T) == typeof(JsonNode) || typeof(T) == typeof(JsonObject))
            {
                return (T)(object)new JsonObject();
            }

            if (typeof(T) == typeof(JsonArray))
            {
                return (T)(object)new JsonArray();
            }

            return default!;
        }
        return JsonSerializer.Deserialize<T>(response)!;
    }

    /// <inheritdoc/>
    public async Task<byte[]> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken);
        var url = BuildApiUrl(endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request, token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The returned stream wraps both the content stream and the HTTP response.
    /// When the stream is disposed, it will properly dispose the underlying HTTP response
    /// to prevent memory leaks and connection pool exhaustion.
    /// </remarks>
    public async Task<Stream> GetStreamAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken);
        var url = BuildApiUrl(endpoint);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request, token);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            // Return a wrapper stream that disposes the response when the stream is closed
            return new ResponseStreamWrapper(contentStream, response, request);
        }
        catch
        {
            // If anything goes wrong, ensure we clean up
            response?.Dispose();
            request.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public string BuildApiUrl(string relativePath)
    {
        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw new InvalidOperationException("Instance URL not set");
        }

        // Ensure the path starts with /
        if (!relativePath.StartsWith("/"))
        {
            relativePath = "/" + relativePath;
        }

        // If path doesn't include services/, add the API version path
        if (!relativePath.StartsWith(SalesforceConstants.Paths.ServicesBase, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{relativePath}";
        }

        return $"{_instanceUrl.TrimEnd('/')}{relativePath}";
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return await _tokenProvider.GetAccessTokenAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task PutRawAsync(string endpoint, string content, string contentType, CancellationToken cancellationToken = default)
    {
        var token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken);
        var url = BuildApiUrl(endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        AddAuthHeader(request, token);
        request.Content = new StringContent(content, Encoding.UTF8, contentType);

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Salesforce API PUT (raw) {Url}, ContentType: {ContentType}", url, contentType);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> GetRawAsync(string endpoint, string acceptHeader, CancellationToken cancellationToken = default)
    {
        var token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken);
        var url = BuildApiUrl(endpoint);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeader(request, token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptHeader));

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Salesforce API GET (raw) {Url}, Accept: {Accept}", url, acceptHeader);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<string> SendRequestAsync(
        HttpMethod method,
        string endpoint,
        object? payload,
        CancellationToken cancellationToken,
        int retryCount = 0,
        string? accessTokenOverride = null)
    {
        string token;
        if (!string.IsNullOrWhiteSpace(accessTokenOverride))
        {
            token = accessTokenOverride;
            await EnsureInstanceUrlAsync(cancellationToken);
        }
        else
        {
            token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken);
        }

        var url = BuildApiUrl(endpoint);

        using var request = new HttpRequestMessage(method, url);
        AddAuthHeader(request, token);

        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (_options.EnableDebugLogging)
        {
            _logger.LogDebug("Salesforce API {Method} {Url}", method, url);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed: {Method} {Url}", method, url);
            throw new SalesforceException($"HTTP request failed: {ex.Message}", innerException: ex);
        }

        // Handle 401 - Token may have expired
        // We handle this explicitly here because it requires a specific "Refresh Token" action,
        // which standard resilience policies don't know how to perform.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Dispose the 401 response before retrying
            response.Dispose();

            if (retryCount < 1)
            {
                _logger.LogWarning("Received 401, attempting token refresh");
                var newToken = await _tokenProvider.RefreshTokenAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(newToken))
                {
                    // Recursive retry with fresh token, incrementing retry count to prevent infinite loops
                    return await SendRequestAsync(
                        method,
                        endpoint,
                        payload,
                        cancellationToken,
                        retryCount + 1,
                        accessTokenOverride: newToken);
                }
            }
            throw SalesforceAuthException.TokenExpiredException();
        }

        // Rate limiting (429) and transient errors (5xx) are now handled by the
        // Polly policy registered in ServiceCollectionExtensions.
        // We simply check for success here.

        try
        {
            await EnsureSuccessAsync(response, cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (_options.EnableDebugLogging)
            {
                _logger.LogDebug("Salesforce API response: {StatusCode} {Length} bytes",
                    response.StatusCode, content.Length);
            }

            return content;
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task EnsureInstanceUrlAsync(CancellationToken cancellationToken)
    {
        var instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken);
        if (!string.IsNullOrEmpty(instanceUrl))
        {
            _instanceUrl = instanceUrl;
            return;
        }

        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw SalesforceAuthException.MissingTokenException();
        }
    }

    private async Task<string> EnsureAuthenticatedAndGetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw SalesforceAuthException.MissingTokenException();
        }

        var instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken);
        if (!string.IsNullOrEmpty(instanceUrl))
        {
            _instanceUrl = instanceUrl;
            return token;
        }

        // Best-effort recovery: some providers populate instance_url during refresh/auth flows.
        // Avoid failing when the provider is lazily authenticated (e.g., server-to-server providers).
        var refreshedToken = await _tokenProvider.RefreshTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(refreshedToken))
        {
            token = refreshedToken;
            instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken);
            if (!string.IsNullOrEmpty(instanceUrl))
            {
                _instanceUrl = instanceUrl;
                return token;
            }
        }

        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw SalesforceAuthException.MissingTokenException();
        }

        return token;
    }

    private static void AddAuthHeader(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        // Try to parse Salesforce error response
        try
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                // Salesforce returns errors as an array
                if (content.TrimStart().StartsWith("["))
                {
                    var errors = JsonSerializer.Deserialize<List<SalesforceError>>(content);
                    if (errors != null && errors.Count > 0)
                    {
                        throw SalesforceException.FromErrors(errors, statusCode);
                    }
                }
                else
                {
                    var errorObj = JsonNode.Parse(content);
                    var message = errorObj?["message"]?.ToString() ??
                                  errorObj?["error_description"]?.ToString() ??
                                  errorObj?["error"]?.ToString() ??
                                  content;
                    var errorCode = errorObj?["errorCode"]?.ToString() ??
                                    errorObj?["error"]?.ToString();

                    throw new SalesforceException(message, errorCode, statusCode, rawResponse: content);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, use raw content
        }

        // Default error handling
        var defaultMessage = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "Resource not found",
            HttpStatusCode.Forbidden => "Access denied",
            HttpStatusCode.BadRequest => "Invalid request",
            HttpStatusCode.InternalServerError => "Salesforce server error",
            _ => $"HTTP {statusCode}: {response.ReasonPhrase}"
        };

        throw new SalesforceException(
            string.IsNullOrWhiteSpace(content) ? defaultMessage : content,
            httpStatusCode: statusCode,
            rawResponse: content);
    }
}

/// <summary>
/// Stream wrapper that ensures proper disposal of HttpResponseMessage when the stream is closed.
/// Prevents memory leaks and connection pool exhaustion when using GetStreamAsync.
/// </summary>
internal sealed class ResponseStreamWrapper : Stream
{
    private readonly Stream _innerStream;
    private readonly HttpResponseMessage _response;
    private readonly HttpRequestMessage _request;
    private bool _disposed;

    public ResponseStreamWrapper(Stream innerStream, HttpResponseMessage response, HttpRequestMessage request)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _innerStream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _innerStream.ReadAsync(buffer, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => _innerStream.WriteAsync(buffer, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                _response.Dispose();
                _request.Dispose();
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _innerStream.DisposeAsync();
            _response.Dispose();
            _request.Dispose();
            _disposed = true;
        }
        await base.DisposeAsync();
    }
}
