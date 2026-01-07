using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Utilities;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Represents a single sub-request in a composite batch.
/// </summary>
public class CompositeSubRequest
{
    /// <summary>
    /// HTTP method for this sub-request.
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    /// <summary>
    /// Relative URL for this sub-request.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Reference ID for this sub-request. Used for dependencies.
    /// </summary>
    [JsonPropertyName("referenceId")]
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Request body for POST/PATCH requests.
    /// </summary>
    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Body { get; set; }

    /// <summary>
    /// HTTP headers for this sub-request.
    /// </summary>
    [JsonPropertyName("httpHeaders")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? HttpHeaders { get; set; }
}

/// <summary>
/// Represents the composite request body.
/// </summary>
public class CompositeRequest
{
    /// <summary>
    /// Whether to stop processing on first error.
    /// </summary>
    [JsonPropertyName("allOrNone")]
    public bool AllOrNone { get; set; }

    /// <summary>
    /// Whether all sub-requests should be in a single transaction.
    /// </summary>
    [JsonPropertyName("collateSubrequests")]
    public bool CollateSubrequests { get; set; }

    /// <summary>
    /// List of sub-requests to execute.
    /// </summary>
    [JsonPropertyName("compositeRequest")]
    public List<CompositeSubRequest> CompositeSubRequests { get; set; } = new();
}

/// <summary>
/// Represents the result of a single composite sub-request.
/// </summary>
public class CompositeSubResponse
{
    /// <summary>
    /// HTTP status code for this sub-request.
    /// </summary>
    [JsonPropertyName("httpStatusCode")]
    public int HttpStatusCode { get; set; }

    /// <summary>
    /// Response body.
    /// </summary>
    [JsonPropertyName("body")]
    public JsonNode? Body { get; set; }

    /// <summary>
    /// Reference ID matching the sub-request.
    /// </summary>
    [JsonPropertyName("referenceId")]
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// HTTP headers from response.
    /// </summary>
    [JsonPropertyName("httpHeaders")]
    public Dictionary<string, string>? HttpHeaders { get; set; }

    /// <summary>
    /// Whether this sub-request was successful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => HttpStatusCode >= 200 && HttpStatusCode < 300;
}

/// <summary>
/// Represents the composite response.
/// </summary>
public class CompositeResponse
{
    /// <summary>
    /// List of sub-responses.
    /// </summary>
    [JsonPropertyName("compositeResponse")]
    public List<CompositeSubResponse> CompositeSubResponses { get; set; } = new();

    /// <summary>
    /// Whether all sub-requests were successful.
    /// </summary>
    [JsonIgnore]
    public bool AllSuccess => CompositeSubResponses.All(r => r.IsSuccess);

    /// <summary>
    /// Gets the response for a specific reference ID.
    /// </summary>
    public CompositeSubResponse? GetResponse(string referenceId)
    {
        return CompositeSubResponses.FirstOrDefault(r => r.ReferenceId == referenceId);
    }
}

/// <summary>
/// Result of a composite operation.
/// </summary>
public class CompositeOperationResult
{
    /// <summary>
    /// Whether this operation was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Reference ID for this result.
    /// </summary>
    public string ReferenceId { get; set; } = string.Empty;

    /// <summary>
    /// Record ID if created or updated.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Error code if failed.
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Full response body.
    /// </summary>
    public JsonNode? ResponseBody { get; set; }
}

/// <summary>
/// Builder for creating composite sub-requests.
/// </summary>
public class CompositeSubRequestBuilder
{
    private readonly CompositeSubRequest _request = new();

    /// <summary>
    /// Creates a GET sub-request.
    /// </summary>
    public static CompositeSubRequestBuilder Get(string url, string referenceId)
    {
        var builder = new CompositeSubRequestBuilder();
        builder._request.Method = "GET";
        builder._request.Url = url;
        builder._request.ReferenceId = referenceId;
        return builder;
    }

    /// <summary>
    /// Creates a POST sub-request.
    /// </summary>
    public static CompositeSubRequestBuilder Post(string url, string referenceId, object body)
    {
        var builder = new CompositeSubRequestBuilder();
        builder._request.Method = "POST";
        builder._request.Url = url;
        builder._request.ReferenceId = referenceId;
        builder._request.Body = body;
        return builder;
    }

    /// <summary>
    /// Creates a PATCH sub-request.
    /// </summary>
    public static CompositeSubRequestBuilder Patch(string url, string referenceId, object body)
    {
        var builder = new CompositeSubRequestBuilder();
        builder._request.Method = "PATCH";
        builder._request.Url = url;
        builder._request.ReferenceId = referenceId;
        builder._request.Body = body;
        return builder;
    }

    /// <summary>
    /// Creates a DELETE sub-request.
    /// </summary>
    public static CompositeSubRequestBuilder Delete(string url, string referenceId)
    {
        var builder = new CompositeSubRequestBuilder();
        builder._request.Method = "DELETE";
        builder._request.Url = url;
        builder._request.ReferenceId = referenceId;
        return builder;
    }

    /// <summary>
    /// Adds a header to the sub-request.
    /// </summary>
    public CompositeSubRequestBuilder WithHeader(string name, string value)
    {
        _request.HttpHeaders ??= new Dictionary<string, string>();
        _request.HttpHeaders[name] = value;
        return this;
    }

    /// <summary>
    /// Builds the sub-request.
    /// </summary>
    public CompositeSubRequest Build() => _request;
}

#region Composite Graph Models

/// <summary>
/// Represents a Composite Graph request containing multiple graphs.
/// Each graph is a set of related sub-requests that can reference each other.
/// </summary>
public class CompositeGraphRequest
{
    /// <summary>
    /// List of graphs to execute.
    /// </summary>
    [JsonPropertyName("graphs")]
    public List<GraphDefinition> Graphs { get; set; } = new();
}

/// <summary>
/// Represents a single graph within a Composite Graph request.
/// </summary>
public class GraphDefinition
{
    /// <summary>
    /// Unique identifier for this graph.
    /// </summary>
    [JsonPropertyName("graphId")]
    public string GraphId { get; set; } = string.Empty;

    /// <summary>
    /// The composite request containing the sub-requests for this graph.
    /// </summary>
    [JsonPropertyName("compositeRequest")]
    public List<CompositeSubRequest> CompositeRequest { get; set; } = new();
}

/// <summary>
/// Response from a Composite Graph request.
/// </summary>
public class CompositeGraphResponse
{
    /// <summary>
    /// Results for each graph in the request.
    /// </summary>
    [JsonPropertyName("graphs")]
    public List<GraphResult> Graphs { get; set; } = new();

    /// <summary>
    /// Gets the result for a specific graph by ID.
    /// </summary>
    public GraphResult? GetGraphResult(string graphId)
    {
        return Graphs.FirstOrDefault(g => g.GraphId == graphId);
    }

    /// <summary>
    /// Whether all graphs completed successfully.
    /// </summary>
    [JsonIgnore]
    public bool AllSuccess => Graphs.All(g => g.IsSuccess);
}

/// <summary>
/// Result of a single graph within the Composite Graph response.
/// </summary>
public class GraphResult
{
    /// <summary>
    /// The graph ID matching the request.
    /// </summary>
    [JsonPropertyName("graphId")]
    public string GraphId { get; set; } = string.Empty;

    /// <summary>
    /// The composite response for this graph.
    /// </summary>
    [JsonPropertyName("graphResponse")]
    public GraphResponseContent? GraphResponse { get; set; }

    /// <summary>
    /// Whether this graph succeeded.
    /// </summary>
    [JsonPropertyName("isSuccessful")]
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Alias for IsSuccessful.
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => IsSuccessful;
}

/// <summary>
/// The response content for a single graph.
/// </summary>
public class GraphResponseContent
{
    /// <summary>
    /// List of composite sub-responses for the graph.
    /// </summary>
    [JsonPropertyName("compositeResponse")]
    public List<CompositeSubResponse> CompositeResponse { get; set; } = new();

    /// <summary>
    /// Gets the response for a specific reference ID.
    /// </summary>
    public CompositeSubResponse? GetResponse(string referenceId)
    {
        return CompositeResponse.FirstOrDefault(r => r.ReferenceId == referenceId);
    }
}

/// <summary>
/// Builder for constructing Composite Graph requests.
/// </summary>
public class CompositeGraphBuilder
{
    private readonly CompositeGraphRequest _request = new();
    private GraphDefinition? _currentGraph;
    private int _graphCounter;
    private int _referenceCounter;

    /// <summary>
    /// Starts a new graph in the request.
    /// </summary>
    /// <param name="graphId">Optional graph ID (auto-generated if not provided).</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder StartGraph(string? graphId = null)
    {
        _currentGraph = new GraphDefinition
        {
            GraphId = graphId ?? $"graph_{_graphCounter++}"
        };
        _request.Graphs.Add(_currentGraph);
        _referenceCounter = 0; // Reset reference counter for new graph
        return this;
    }

    /// <summary>
    /// Adds a create operation to the current graph.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="data">The record data.</param>
    /// <param name="referenceId">Optional reference ID (auto-generated if not provided).</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder Create(string objectName, Dictionary<string, object?> data, string? referenceId = null)
    {
        EnsureCurrentGraph();
        referenceId ??= $"ref_{_referenceCounter++}";

        _currentGraph!.CompositeRequest.Add(new CompositeSubRequest
        {
            Method = "POST",
            Url = $"/services/data/{SalesforceConstants.DefaultApiVersion}/sobjects/{objectName}",
            ReferenceId = referenceId,
            Body = data
        });
        return this;
    }

    /// <summary>
    /// Adds an update operation to the current graph.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="id">The record ID to update.</param>
    /// <param name="data">The fields to update.</param>
    /// <param name="referenceId">Optional reference ID.</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder Update(string objectName, string id, Dictionary<string, object?> data, string? referenceId = null)
    {
        EnsureCurrentGraph();
        referenceId ??= $"ref_{_referenceCounter++}";

        _currentGraph!.CompositeRequest.Add(new CompositeSubRequest
        {
            Method = "PATCH",
            Url = $"/services/data/{SalesforceConstants.DefaultApiVersion}/sobjects/{objectName}/{id}",
            ReferenceId = referenceId,
            Body = data
        });
        return this;
    }

    /// <summary>
    /// Adds a delete operation to the current graph.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="id">The record ID to delete.</param>
    /// <param name="referenceId">Optional reference ID.</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder Delete(string objectName, string id, string? referenceId = null)
    {
        EnsureCurrentGraph();
        referenceId ??= $"ref_{_referenceCounter++}";

        _currentGraph!.CompositeRequest.Add(new CompositeSubRequest
        {
            Method = "DELETE",
            Url = $"/services/data/{SalesforceConstants.DefaultApiVersion}/sobjects/{objectName}/{id}",
            ReferenceId = referenceId
        });
        return this;
    }

    /// <summary>
    /// Adds a create operation that references another operation's result.
    /// Use @{referenceId.Id} syntax in the data values.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="data">The record data with reference expressions.</param>
    /// <param name="referenceId">Optional reference ID.</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder CreateWithReference(string objectName, Dictionary<string, object?> data, string? referenceId = null)
    {
        // Same as Create, but data can contain @{refId.Field} references
        return Create(objectName, data, referenceId);
    }

    /// <summary>
    /// Adds a query operation to the current graph.
    /// </summary>
    /// <param name="soql">The SOQL query.</param>
    /// <param name="referenceId">Optional reference ID.</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder Query(string soql, string? referenceId = null)
    {
        EnsureCurrentGraph();
        referenceId ??= $"ref_{_referenceCounter++}";

        _currentGraph!.CompositeRequest.Add(new CompositeSubRequest
        {
            Method = "GET",
                            Url = $"/services/data/{SalesforceConstants.DefaultApiVersion}/query?q={UrlUtils.Escape(soql)}",            ReferenceId = referenceId
        });
        return this;
    }

    /// <summary>
    /// Adds a custom sub-request to the current graph.
    /// </summary>
    /// <param name="subRequest">The sub-request to add.</param>
    /// <returns>This builder for chaining.</returns>
    public CompositeGraphBuilder Add(CompositeSubRequest subRequest)
    {
        EnsureCurrentGraph();
        _currentGraph!.CompositeRequest.Add(subRequest);
        return this;
    }

    /// <summary>
    /// Gets the total number of nodes across all graphs.
    /// </summary>
    public int TotalNodeCount => _request.Graphs.Sum(g => g.CompositeRequest.Count);

    /// <summary>
    /// Gets the number of graphs in the request.
    /// </summary>
    public int GraphCount => _request.Graphs.Count;

    /// <summary>
    /// Builds the Composite Graph request.
    /// </summary>
    /// <returns>The composite graph request.</returns>
    public CompositeGraphRequest Build()
    {
        if (_request.Graphs.Count == 0)
        {
            throw new InvalidOperationException("No graphs defined. Call StartGraph() first.");
        }

        if (TotalNodeCount > SalesforceConstants.Composite.MaxGraphNodes)
        {
            throw new InvalidOperationException(
                $"Total node count ({TotalNodeCount}) exceeds maximum of {SalesforceConstants.Composite.MaxGraphNodes}.");
        }

        if (GraphCount > SalesforceConstants.Composite.MaxGraphsPerRequest)
        {
            throw new InvalidOperationException(
                $"Graph count ({GraphCount}) exceeds maximum of {SalesforceConstants.Composite.MaxGraphsPerRequest}.");
        }

        return _request;
    }

    private void EnsureCurrentGraph()
    {
        if (_currentGraph == null)
        {
            StartGraph();
        }
    }
}

#endregion
