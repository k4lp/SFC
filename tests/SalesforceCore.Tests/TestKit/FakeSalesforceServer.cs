using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SalesforceCore.Tests.TestKit;

/// <summary>A request captured by <see cref="FakeSalesforceServer"/>.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Bearer, string? Body, string? ContentType, string Accept, IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>Path without query string.</summary>
    public string Path => Uri.AbsolutePath;
    /// <summary>Decoded query string value.</summary>
    public string? Query(string name) => System.Web.HttpUtility.ParseQueryString(Uri.Query)[name];
    /// <summary>Body parsed as JSON.</summary>
    public JsonNode Json => JsonNode.Parse(Body ?? "{}")!;
}

/// <summary>
/// In-memory fake of the Salesforce REST API. Routes are matched by method + path regex (first match wins,
/// most recently added first) so tests read like a server definition. Unmatched requests get a Salesforce-style 404.
/// Thread-safe; every request is journaled in <see cref="Requests"/>.
/// </summary>
public sealed class FakeSalesforceServer : HttpMessageHandler
{
    private sealed record Route(HttpMethod? Method, Regex Path, Func<RecordedRequest, HttpResponseMessage> Respond, int? Times)
    {
        public int Hits;
    }

    private readonly List<Route> _routes = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    /// <summary>Default instance URL the <see cref="TestTokenProvider"/> reports.</summary>
    public const string InstanceUrl = "https://acme.my.salesforce.com";

    /// <summary>Exception to throw for every request (simulates network failure).</summary>
    public Exception? FailWith { get; set; }

    /// <summary>All requests in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _requests.ToList();

    /// <summary>Registers a route. <paramref name="pathPattern"/> is a regex anchored to the full path (without query).</summary>
    public FakeSalesforceServer On(HttpMethod? method, string pathPattern, Func<RecordedRequest, HttpResponseMessage> respond, int? times = null)
    {
        lock (_routes) _routes.Insert(0, new Route(method, new Regex("^" + pathPattern + "$", RegexOptions.IgnoreCase), respond, times));
        return this;
    }

    /// <summary>Registers a JSON route.</summary>
    public FakeSalesforceServer OnJson(HttpMethod? method, string pathPattern, object json, HttpStatusCode status = HttpStatusCode.OK, int? times = null, IDictionary<string, string>? headers = null)
        => On(method, pathPattern, _ => Json(status, json, headers), times);

    /// <summary>GET JSON route.</summary>
    public FakeSalesforceServer Get(string pathPattern, object json, HttpStatusCode status = HttpStatusCode.OK) => OnJson(HttpMethod.Get, pathPattern, json, status);

    /// <summary>Route returning a Salesforce error array.</summary>
    public FakeSalesforceServer Error(HttpMethod? method, string pathPattern, HttpStatusCode status, string errorCode, string message, params string[] fields)
        => OnJson(method, pathPattern, SalesforceJson.Errors((errorCode, message, fields)), status);

    /// <summary>Builds a JSON response.</summary>
    public static HttpResponseMessage Json(HttpStatusCode status, object json, IDictionary<string, string>? headers = null)
    {
        var text = json is string s ? s : json is JsonNode n ? n.ToJsonString() : JsonSerializer.Serialize(json);
        var r = new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        if (headers != null) foreach (var (k, v) in headers) r.Headers.TryAddWithoutValidation(k, v);
        return r;
    }

    /// <summary>Builds a text response.</summary>
    public static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType = "text/plain")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    /// <summary>Builds an empty response (e.g. 204).</summary>
    public static HttpResponseMessage Empty(HttpStatusCode status = HttpStatusCode.NoContent) => new(status) { Content = new ByteArrayContent(Array.Empty<byte>()) };

    /// <summary>Requests whose path matches a regex.</summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(string pathPattern, HttpMethod? method = null)
    {
        var rx = new Regex("^" + pathPattern + "$", RegexOptions.IgnoreCase);
        return Requests.Where(r => rx.IsMatch(r.Path) && (method == null || r.Method == method)).ToList();
    }

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var rec = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Parameter,
            request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Content?.Headers.ContentType?.MediaType,
            string.Join(",", request.Headers.Accept),
            headers);
        _requests.Enqueue(rec);
        if (FailWith != null) throw FailWith;

        Route? match;
        lock (_routes)
        {
            match = _routes.FirstOrDefault(r =>
                (r.Method == null || r.Method == request.Method) && r.Path.IsMatch(rec.Path) && (r.Times == null || r.Hits < r.Times));
            if (match != null) match.Hits++;
        }
        if (match == null)
        {
            return Json(HttpStatusCode.NotFound, SalesforceJson.Errors(("NOT_FOUND", $"The requested resource does not exist: {request.Method} {rec.Path}", Array.Empty<string>())));
        }
        return match.Respond(rec);
    }
}
