using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Http;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Security;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Authenticated transport for the Salesforce REST API.
/// </summary>
/// <remarks>
/// <para>All verbs funnel into one path (<see cref="SendAsync"/>): resolve token + validated instance URL, build the
/// URL under the configured <see cref="SalesforceApiVersion"/>, send through the HttpClient pipeline
/// (user handlers → logging → API-usage tracking → resilience), refresh the token once on 401, and map failures to the
/// typed <see cref="SalesforceException"/> hierarchy via <see cref="SalesforceErrorMapper"/>.</para>
/// <para>The client never filters or blocks requests for authorization reasons: Salesforce enforces access and its
/// errors are surfaced as-is.</para>
/// </remarks>
public class SalesforceClient : ISalesforceClient
{
    private readonly HttpClient _httpClient;
    private readonly ITokenProvider _tokenProvider;
    private readonly SalesforceOptions _options;
    private readonly ILogger<SalesforceClient> _logger;
    private readonly SalesforceApiVersion _apiVersion;
    private volatile string? _instanceUrl;

    /// <summary>Serializer options used for request bodies.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a new SalesforceClient.
    /// </summary>
    public SalesforceClient(
        HttpClient httpClient,
        ITokenProvider tokenProvider,
        IOptions<SalesforceOptions> options,
        ILogger<SalesforceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiVersion = SalesforceApiVersion.TryParse(_options.ApiVersion, out var v)
            ? v
            : throw new ArgumentException($"Invalid SalesforceOptions.ApiVersion '{_options.ApiVersion}'. Expected e.g. 'v62.0'.");
    }

    /// <inheritdoc/>
    public string InstanceUrl => _instanceUrl ?? throw new InvalidOperationException("Instance URL not set. Ensure authentication is complete.");

    /// <inheritdoc/>
    public string ApiVersion => _apiVersion.ToString();

    // ------------------------------------------------------------------ core path

    /// <inheritdoc/>
    public async Task<SalesforceResponse> SendAsync(SalesforceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var response = await SendWithRefreshAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var content = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (request.ThrowOnError && !response.IsSuccessStatusCode)
        {
            throw MapError(response, content);
        }
        return new SalesforceResponse((int)response.StatusCode, CollectHeaders(response), content, response.Content?.Headers.ContentType?.MediaType);
    }

    /// <inheritdoc/>
    public async Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        => Deserialize<T>(await SendAsync(SalesforceRequest.Get(endpoint), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<JsonNode> GetAsync(string endpoint, CancellationToken cancellationToken = default)
        => (await SendAsync(SalesforceRequest.Get(endpoint), cancellationToken).ConfigureAwait(false)).AsJson();

    /// <inheritdoc/>
    public async Task<T> PostAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
        => Deserialize<T>(await SendAsync(SalesforceRequest.Post(endpoint, payload), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<JsonNode> PostAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
        => (await SendAsync(SalesforceRequest.Post(endpoint, payload), cancellationToken).ConfigureAwait(false)).AsJson();

    /// <inheritdoc/>
    public async Task<T> PutAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
        => Deserialize<T>(await SendAsync(SalesforceRequest.Put(endpoint, payload), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<JsonNode> PutAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
        => (await SendAsync(SalesforceRequest.Put(endpoint, payload), cancellationToken).ConfigureAwait(false)).AsJson();

    /// <inheritdoc/>
    public async Task<T> PatchAsync<T>(string endpoint, object payload, CancellationToken cancellationToken = default)
        => Deserialize<T>(await SendAsync(SalesforceRequest.Patch(endpoint, payload), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task<JsonNode> PatchAsync(string endpoint, object payload, CancellationToken cancellationToken = default)
        => (await SendAsync(SalesforceRequest.Patch(endpoint, payload), cancellationToken).ConfigureAwait(false)).AsJson();

    /// <inheritdoc/>
    public async Task DeleteAsync(string endpoint, CancellationToken cancellationToken = default)
        => await SendAsync(SalesforceRequest.Delete(endpoint), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<T> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default)
        => Deserialize<T>(await SendAsync(SalesforceRequest.Delete(endpoint), cancellationToken).ConfigureAwait(false));

    /// <inheritdoc/>
    public async Task PutRawAsync(string endpoint, string content, string contentType, CancellationToken cancellationToken = default)
        => await SendAsync(new SalesforceRequest(HttpMethod.Put, endpoint) { TextBody = content, ContentType = contentType }, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<string> GetRawAsync(string endpoint, string acceptHeader, CancellationToken cancellationToken = default)
        => (await SendAsync(new SalesforceRequest(HttpMethod.Get, endpoint) { Accept = acceptHeader }, cancellationToken).ConfigureAwait(false)).Content;

    /// <inheritdoc/>
    public async Task<byte[]> GetBytesAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRefreshAsync(SalesforceRequest.Get(endpoint), HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The returned stream owns the HTTP response; dispose it to release the connection.
    /// </remarks>
    public async Task<Stream> GetStreamAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var response = await SendWithRefreshAsync(SalesforceRequest.Get(endpoint), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
            var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ResponseStreamWrapper(contentStream, response);
        }
        catch
        {
            response.Dispose();
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
        return $"{_instanceUrl.TrimEnd('/')}{BuildPath(relativePath)}";
    }

    /// <summary>
    /// Normalises an endpoint into an instance-relative path: <c>/services/...</c> paths pass through, anything else is
    /// placed under <c>/services/data/{ApiVersion}</c>.
    /// </summary>
    public string BuildPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }
        if (!relativePath.StartsWith(SalesforceConstants.Paths.ServicesBase, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = $"{_apiVersion.DataPath}{relativePath}";
        }
        return relativePath;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        => await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

    // ------------------------------------------------------------------ internals

    private async Task<HttpResponseMessage> SendWithRefreshAsync(SalesforceRequest request, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        var token = await EnsureAuthenticatedAndGetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            var message = CreateMessage(request, token);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(message, completion, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                message.Dispose();
                _logger.LogError(ex, "Salesforce HTTP request failed: {Method} {Path}", request.Method, PathForLog(request.Path));
                throw new SalesforceConnectionException($"HTTP request failed: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is TaskCanceledException or TimeoutException or Polly.Timeout.TimeoutRejectedException && !cancellationToken.IsCancellationRequested)
            {
                message.Dispose();
                throw new SalesforceConnectionException("The Salesforce request timed out.", ex);
            }

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                // The message is owned by the response's lifetime for streaming; HttpClient does not need it afterwards.
                return response;
            }

            // 401: refresh once (resilience policies can't perform a token refresh).
            response.Dispose();
            message.Dispose();
            if (attempt >= 1)
            {
                throw SalesforceAuthException.TokenExpiredException();
            }

            _logger.LogWarning("Received 401, attempting token refresh");
            var newToken = await _tokenProvider.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(newToken))
            {
                throw SalesforceAuthException.TokenExpiredException();
            }
            token = newToken;
            await EnsureInstanceUrlAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private HttpRequestMessage CreateMessage(SalesforceRequest request, string token)
    {
        var message = new HttpRequestMessage(request.Method, BuildApiUrl(request.Path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.Clear();
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(request.Accept ?? SalesforceConstants.Headers.ContentTypeJson));

        foreach (var (name, value) in request.Headers)
        {
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Header '{name}' is managed by the client and cannot be overridden.");
            }
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (request.JsonBody != null)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(request.JsonBody, request.JsonBody.GetType(), JsonOptions);
            message.Content = new ByteArrayContent(json);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        else if (request.TextBody != null)
        {
            message.Content = new StringContent(request.TextBody, Encoding.UTF8, request.ContentType ?? "text/plain");
        }
        else if (request.BinaryBody is { } bytes)
        {
            message.Content = new ReadOnlyMemoryContent(bytes);
            message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType ?? "application/octet-stream");
        }
        return message;
    }

    private string ValidateInstanceUrl(string instanceUrl)
    {
        if (!_options.ValidateInstanceUrl)
        {
            return instanceUrl;
        }

        if (!SalesforceUrlValidator.TryValidate(instanceUrl, _options.AdditionalAllowedHosts, out var reason))
        {
            _logger.LogError("Refusing to send access token to untrusted instance URL: {Reason}", reason);
            throw new SalesforceAuthException($"Rejected Salesforce instance URL: {reason}");
        }

        return instanceUrl.Trim();
    }

    private async Task EnsureInstanceUrlAsync(CancellationToken cancellationToken)
    {
        var instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(instanceUrl))
        {
            _instanceUrl = ValidateInstanceUrl(instanceUrl);
            return;
        }

        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw SalesforceAuthException.MissingTokenException();
        }
    }

    private async Task<string> EnsureAuthenticatedAndGetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            throw SalesforceAuthException.MissingTokenException();
        }

        var instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(instanceUrl))
        {
            _instanceUrl = ValidateInstanceUrl(instanceUrl);
            return token;
        }

        // Some providers only learn instance_url during a refresh (lazy server-to-server flows).
        var refreshedToken = await _tokenProvider.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(refreshedToken))
        {
            token = refreshedToken;
            instanceUrl = await _tokenProvider.GetInstanceUrlAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(instanceUrl))
            {
                _instanceUrl = ValidateInstanceUrl(instanceUrl);
                return token;
            }
        }

        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw SalesforceAuthException.MissingTokenException();
        }

        return token;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        var content = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw MapError(response, content);
    }

    private SalesforceException MapError(HttpResponseMessage response, string content)
    {
        var requestId = response.Headers.TryGetValues("Sforce-Request-Id", out var ids) ? ids.FirstOrDefault()
            : response.Headers.TryGetValues("X-Request-Id", out var xids) ? xids.FirstOrDefault() : null;
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        return SalesforceErrorMapper.FromResponse((int)response.StatusCode, content, requestId, retryAfter, _options.MaxErrorBodyLength);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers) headers[h.Key] = h.Value.ToList();
        if (response.Content != null)
        {
            foreach (var h in response.Content.Headers) headers[h.Key] = h.Value.ToList();
        }
        return headers;
    }

    private static T Deserialize<T>(SalesforceResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Content))
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
        return JsonSerializer.Deserialize<T>(response.Content)!;
    }

    internal static string PathForLog(string path)
    {
        var q = path.IndexOf('?');
        return q < 0 ? path : path[..q] + "?…";
    }
}

/// <summary>
/// Stream wrapper that disposes the owning <see cref="HttpResponseMessage"/> with the stream.
/// </summary>
internal sealed class ResponseStreamWrapper : Stream
{
    private readonly Stream _innerStream;
    private readonly HttpResponseMessage _response;
    private bool _disposed;

    public ResponseStreamWrapper(Stream innerStream, HttpResponseMessage response)
    {
        _innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
        _response = response ?? throw new ArgumentNullException(nameof(response));
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
            }
            _disposed = true;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            _disposed = true;
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
