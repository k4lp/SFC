using System.Text.Json.Nodes;

namespace SalesforceCore.Http;

/// <summary>
/// A transport-level request to the Salesforce API. This is the universal extension point of
/// <see cref="Services.Core.ISalesforceClient.SendAsync"/>: any endpoint, verb, body and header can be sent
/// without the library needing a dedicated method.
/// </summary>
/// <remarks>
/// <para><see cref="Path"/> is relative to the instance: a path that does not start with <c>/services/</c> is
/// placed under the data API of the configured version (<c>/services/data/vNN.0</c>). Absolute URLs are not accepted;
/// the instance host always comes from the (validated) token provider so tokens are never sent elsewhere.</para>
/// <para>Bodies are stored as data (not as an <see cref="HttpContent"/>) so the request can be replayed after a token refresh.</para>
/// </remarks>
public sealed class SalesforceRequest
{
    /// <summary>Creates a request.</summary>
    public SalesforceRequest(HttpMethod method, string path)
    {
        Method = method ?? throw new ArgumentNullException(nameof(method));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
        if (Uri.TryCreate(path, UriKind.Absolute, out var abs) && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            throw new ArgumentException("SalesforceRequest.Path must be relative to the instance URL.", nameof(path));
        Path = path;
    }

    /// <summary>HTTP method.</summary>
    public HttpMethod Method { get; }

    /// <summary>Instance-relative path (optionally with query string).</summary>
    public string Path { get; }

    /// <summary>JSON body (serialized with the client's serializer options). Mutually exclusive with the other bodies.</summary>
    public object? JsonBody { get; init; }

    /// <summary>Raw text body (e.g. CSV for Bulk API 2.0).</summary>
    public string? TextBody { get; init; }

    /// <summary>Binary body.</summary>
    public ReadOnlyMemory<byte>? BinaryBody { get; init; }

    /// <summary>Content type for <see cref="TextBody"/>/<see cref="BinaryBody"/> (default <c>application/json</c>).</summary>
    public string? ContentType { get; init; }

    /// <summary>Accept header (default <c>application/json</c>).</summary>
    public string? Accept { get; init; }

    /// <summary>Additional request headers (e.g. <c>Sforce-Auto-Assign</c>, <c>If-Match</c>, <c>Sforce-Call-Options</c>).</summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true (default) a non-success status throws a typed <see cref="Models.Errors.SalesforceException"/>.
    /// Set to false to inspect error responses yourself.</summary>
    public bool ThrowOnError { get; init; } = true;

    /// <summary>GET request.</summary>
    public static SalesforceRequest Get(string path) => new(HttpMethod.Get, path);
    /// <summary>DELETE request.</summary>
    public static SalesforceRequest Delete(string path) => new(HttpMethod.Delete, path);
    /// <summary>POST with a JSON body.</summary>
    public static SalesforceRequest Post(string path, object? body) => new(HttpMethod.Post, path) { JsonBody = body };
    /// <summary>PATCH with a JSON body.</summary>
    public static SalesforceRequest Patch(string path, object? body) => new(HttpMethod.Patch, path) { JsonBody = body };
    /// <summary>PUT with a JSON body.</summary>
    public static SalesforceRequest Put(string path, object? body) => new(HttpMethod.Put, path) { JsonBody = body };

    /// <summary>Adds a header and returns this request (fluent).</summary>
    public SalesforceRequest WithHeader(string name, string value) { Headers[name] = value; return this; }
}

/// <summary>
/// A buffered Salesforce API response (status, headers and body), analogous to Azure SDK's <c>Response</c>.
/// </summary>
public sealed class SalesforceResponse
{
    /// <summary>Creates a response.</summary>
    public SalesforceResponse(int status, IReadOnlyDictionary<string, IReadOnlyList<string>> headers, string content, string? contentType = null)
    {
        Status = status;
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Content = content ?? string.Empty;
        ContentType = contentType;
    }

    /// <summary>HTTP status code.</summary>
    public int Status { get; }

    /// <summary>True for 2xx.</summary>
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Response headers (case-insensitive).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    /// <summary>Response body as text ("" for 204).</summary>
    public string Content { get; }

    /// <summary>Response media type, if any.</summary>
    public string? ContentType { get; }

    /// <summary>Salesforce request id (<c>Sforce-Request-Id</c> / <c>X-Request-Id</c>) for support cases.</summary>
    public string? RequestId => GetHeader("Sforce-Request-Id") ?? GetHeader("X-Request-Id");

    /// <summary>First value of a header, or null.</summary>
    public string? GetHeader(string name) => Headers.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

    /// <summary>Parses the body as JSON (empty body → empty object).</summary>
    public JsonNode AsJson() => string.IsNullOrWhiteSpace(Content) ? new JsonObject() : JsonNode.Parse(Content)!;

    /// <summary>Deserializes the body (empty body → default).</summary>
    public T? As<T>(System.Text.Json.JsonSerializerOptions? options = null)
        => string.IsNullOrWhiteSpace(Content) ? default : System.Text.Json.JsonSerializer.Deserialize<T>(Content, options);
}
