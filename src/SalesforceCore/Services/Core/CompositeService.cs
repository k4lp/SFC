using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Implementation of composite (batch) operations for Salesforce.
/// Supports up to 25 sub-requests per composite call.
/// </summary>
public class CompositeService : ICompositeService
{
    private readonly ISalesforceClient _client;
    private readonly SalesforceOptions _options;
    private readonly ILogger<CompositeService> _logger;

    /// <summary>
    /// Creates a new CompositeService.
    /// </summary>
    public CompositeService(
        ISalesforceClient client,
        IOptions<SalesforceOptions> options,
        ILogger<CompositeService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    #region Composite Graph API

    /// <inheritdoc/>
    public async Task<CompositeGraphResponse> ExecuteGraphAsync(
        CompositeGraphRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Graphs.Count == 0)
        {
            return new CompositeGraphResponse();
        }

        var totalNodes = request.Graphs.Sum(g => g.CompositeRequest.Count);
        if (totalNodes > SalesforceConstants.Composite.MaxGraphNodes)
        {
            throw new SalesforceException(
                $"Composite graph request exceeds maximum of {SalesforceConstants.Composite.MaxGraphNodes} nodes. " +
                $"Current count: {totalNodes}");
        }

        if (request.Graphs.Count > SalesforceConstants.Composite.MaxGraphsPerRequest)
        {
            throw new SalesforceException(
                $"Composite graph request exceeds maximum of {SalesforceConstants.Composite.MaxGraphsPerRequest} graphs. " +
                $"Current count: {request.Graphs.Count}");
        }

        _logger.LogDebug(
            "Executing composite graph request with {GraphCount} graphs and {NodeCount} total nodes",
            request.Graphs.Count, totalNodes);

        var response = await _client.PostAsync<CompositeGraphResponse>(
            SalesforceConstants.Paths.CompositeGraph,
            request,
            cancellationToken);

        var successCount = response.Graphs.Count(g => g.IsSuccess);
        var failCount = response.Graphs.Count - successCount;

        _logger.LogDebug(
            "Composite graph request completed: {Success} graphs succeeded, {Failed} failed",
            successCount, failCount);

        return response;
    }

    /// <inheritdoc/>
    public async Task<CompositeGraphResponse> ExecuteGraphAsync(
        CompositeGraphBuilder builder,
        CancellationToken cancellationToken = default)
    {
        if (builder == null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return await ExecuteGraphAsync(builder.Build(), cancellationToken);
    }

    /// <inheritdoc/>
    public CompositeGraphBuilder CreateGraphBuilder() => new CompositeGraphBuilder();

    #endregion

    #region Standard Composite API

    /// <inheritdoc/>
    public async Task<CompositeResponse> ExecuteAsync(CompositeRequest request, CancellationToken cancellationToken = default)
    {
        if (request.CompositeSubRequests.Count == 0)
        {
            return new CompositeResponse();
        }

        if (request.CompositeSubRequests.Count > SalesforceConstants.Composite.MaxSubRequestsPerBatch)
        {
            throw new SalesforceException(
                $"Composite request exceeds maximum of {SalesforceConstants.Composite.MaxSubRequestsPerBatch} sub-requests. " +
                $"Current count: {request.CompositeSubRequests.Count}");
        }

        _logger.LogDebug("Executing composite request with {Count} sub-requests", request.CompositeSubRequests.Count);

        var response = await _client.PostAsync<CompositeResponse>(SalesforceConstants.Paths.Composite, request, cancellationToken);

        var successCount = response.CompositeSubResponses.Count(r => r.IsSuccess);
        var failCount = response.CompositeSubResponses.Count - successCount;

        _logger.LogDebug("Composite request completed: {Success} succeeded, {Failed} failed", successCount, failCount);

        return response;
    }

    /// <inheritdoc/>
    public ICompositeBatchBuilder CreateBatch()
    {
        return new CompositeBatchBuilder(this, _options);
    }

    /// <inheritdoc/>
    public async Task<List<CompositeOperationResult>> CreateRecordsAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            return new List<CompositeOperationResult>();
        }

        var results = new List<CompositeOperationResult>();

        // Process in batches
        foreach (var batch in recordList.Chunk(SalesforceConstants.Composite.MaxSubRequestsPerBatch))
        {
            var request = new CompositeRequest
            {
                AllOrNone = allOrNone,
                CollateSubrequests = true
            };

            var index = 0;
            foreach (var record in batch)
            {
                var referenceId = $"create_{index}";
                request.CompositeSubRequests.Add(new CompositeSubRequest
                {
                    Method = "POST",
                    Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}",
                    ReferenceId = referenceId,
                    Body = record
                });
                index++;
            }

            var response = await ExecuteAsync(request, cancellationToken);
            results.AddRange(ParseBatchResults(response));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<List<CompositeOperationResult>> UpdateRecordsAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            return new List<CompositeOperationResult>();
        }

        var results = new List<CompositeOperationResult>();

        foreach (var batch in recordList.Chunk(SalesforceConstants.Composite.MaxSubRequestsPerBatch))
        {
            var request = new CompositeRequest
            {
                AllOrNone = allOrNone,
                CollateSubrequests = true
            };

            var index = 0;
            foreach (var record in batch)
            {
                if (!record.TryGetValue("Id", out var idValue) || idValue == null)
                {
                    throw new SalesforceValidationException(
                        "Record must contain Id field for update",
                        new Dictionary<string, string[]> { { "Id", new[] { "Id is required for update" } } });
                }

                var id = idValue.ToString()!;
                var updateData = record.Where(kvp => kvp.Key != "Id")
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var referenceId = $"update_{index}";
                request.CompositeSubRequests.Add(new CompositeSubRequest
                {
                    Method = "PATCH",
                    Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}/{id}",
                    ReferenceId = referenceId,
                    Body = updateData
                });
                index++;
            }

            var response = await ExecuteAsync(request, cancellationToken);
            results.AddRange(ParseBatchResults(response));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<List<CompositeOperationResult>> DeleteRecordsAsync(
        string objectName,
        IEnumerable<string> ids,
        bool allOrNone = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return new List<CompositeOperationResult>();
        }

        var results = new List<CompositeOperationResult>();

        foreach (var batch in idList.Chunk(SalesforceConstants.Composite.MaxSubRequestsPerBatch))
        {
            var request = new CompositeRequest
            {
                AllOrNone = allOrNone,
                CollateSubrequests = true
            };

            var index = 0;
            foreach (var id in batch)
            {
                var referenceId = $"delete_{index}";
                request.CompositeSubRequests.Add(new CompositeSubRequest
                {
                    Method = "DELETE",
                    Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}/{id}",
                    ReferenceId = referenceId
                });
                index++;
            }

            var response = await ExecuteAsync(request, cancellationToken);
            results.AddRange(ParseBatchResults(response));
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<List<CompositeOperationResult>> UpsertRecordsAsync(
        string objectName,
        string externalIdField,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentException("Object name is required", nameof(objectName));

        if (!SecurityUtils.IsValidObjectName(objectName))
            throw new ArgumentException($"Invalid object name: {objectName}", nameof(objectName));

        if (!SecurityUtils.IsValidFieldName(externalIdField))
            throw new ArgumentException($"Invalid external ID field: {externalIdField}", nameof(externalIdField));

        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            return new List<CompositeOperationResult>();
        }

        var results = new List<CompositeOperationResult>();

        foreach (var batch in recordList.Chunk(SalesforceConstants.Composite.MaxSubRequestsPerBatch))
        {
            var request = new CompositeRequest
            {
                AllOrNone = allOrNone,
                CollateSubrequests = true
            };

            var index = 0;
            foreach (var record in batch)
            {
                if (!record.TryGetValue(externalIdField, out var externalIdValue) || externalIdValue == null)
                {
                    throw new SalesforceValidationException(
                        $"Record must contain {externalIdField} field for upsert",
                        new Dictionary<string, string[]> { { externalIdField, new[] { $"{externalIdField} is required for upsert" } } });
                }

                var externalId = externalIdValue.ToString()!;
                var upsertData = record.Where(kvp => kvp.Key != externalIdField)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var referenceId = $"upsert_{index}";
                request.CompositeSubRequests.Add(new CompositeSubRequest
                {
                    Method = "PATCH",
                    Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}/{externalIdField}/{UrlUtils.Escape(externalId)}",
                    ReferenceId = referenceId,
                    Body = upsertData
                });
                index++;
            }

            var response = await ExecuteAsync(request, cancellationToken);
            results.AddRange(ParseBatchResults(response));
        }

        return results;
    }

    #endregion

    #region Private Helpers

    private static List<CompositeOperationResult> ParseBatchResults(CompositeResponse response)
    {
        var results = new List<CompositeOperationResult>();

        foreach (var subResponse in response.CompositeSubResponses)
        {
            var result = new CompositeOperationResult
            {
                ReferenceId = subResponse.ReferenceId,
                Success = subResponse.IsSuccess,
                ResponseBody = subResponse.Body
            };

            if (subResponse.IsSuccess)
            {
                // Extract ID from successful create response
                if (subResponse.Body is JsonObject body)
                {
                    result.Id = body["id"]?.ToString();
                }
            }
            else
            {
                // Extract error information
                if (subResponse.Body is JsonArray errors && errors.Count > 0)
                {
                    var error = errors[0];
                    result.ErrorMessage = error?["message"]?.ToString();
                    result.ErrorCode = error?["errorCode"]?.ToString();
                }
                else if (subResponse.Body is JsonObject errorBody)
                {
                    result.ErrorMessage = errorBody["message"]?.ToString();
                    result.ErrorCode = errorBody["errorCode"]?.ToString();
                }
            }

            results.Add(result);
        }

        return results;
    }

    #endregion

    #region Batch Builder

    /// <summary>
    /// Builder for creating composite batch operations.
    /// </summary>
    private class CompositeBatchBuilder : ICompositeBatchBuilder
    {
        private readonly CompositeService _service;
        private readonly SalesforceOptions _options;
        private readonly CompositeRequest _request = new();
        private int _referenceCounter;

        public CompositeBatchBuilder(CompositeService service, SalesforceOptions options)
        {
            _service = service;
            _options = options;
        }

        public int Count => _request.CompositeSubRequests.Count;

        public ICompositeBatchBuilder Create(string objectName, Dictionary<string, object?> data, string? referenceId = null)
        {
            referenceId ??= $"ref_{_referenceCounter++}";
            _request.CompositeSubRequests.Add(new CompositeSubRequest
            {
                Method = "POST",
                Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}",
                ReferenceId = referenceId,
                Body = data
            });
            return this;
        }

        public ICompositeBatchBuilder Update(string objectName, string id, Dictionary<string, object?> data, string? referenceId = null)
        {
            referenceId ??= $"ref_{_referenceCounter++}";
            _request.CompositeSubRequests.Add(new CompositeSubRequest
            {
                Method = "PATCH",
                Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}/{id}",
                ReferenceId = referenceId,
                Body = data
            });
            return this;
        }

        public ICompositeBatchBuilder Delete(string objectName, string id, string? referenceId = null)
        {
            referenceId ??= $"ref_{_referenceCounter++}";
            _request.CompositeSubRequests.Add(new CompositeSubRequest
            {
                Method = "DELETE",
                Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.SObjects}/{objectName}/{id}",
                ReferenceId = referenceId
            });
            return this;
        }

        public ICompositeBatchBuilder Query(string soql, string? referenceId = null)
        {
            referenceId ??= $"ref_{_referenceCounter++}";
            _request.CompositeSubRequests.Add(new CompositeSubRequest
            {
                Method = "GET",
                Url = $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{SalesforceConstants.Paths.Query}?q={UrlUtils.Escape(soql)}",
                ReferenceId = referenceId
            });
            return this;
        }

        public ICompositeBatchBuilder Add(CompositeSubRequest subRequest)
        {
            _request.CompositeSubRequests.Add(subRequest);
            return this;
        }

        public ICompositeBatchBuilder WithAllOrNone(bool allOrNone = true)
        {
            _request.AllOrNone = allOrNone;
            return this;
        }

        public ICompositeBatchBuilder WithCollation(bool collate = true)
        {
            _request.CollateSubrequests = collate;
            return this;
        }

        public async Task<List<CompositeOperationResult>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (_request.CompositeSubRequests.Count == 0)
            {
                return new List<CompositeOperationResult>();
            }

            var response = await _service.ExecuteAsync(_request, cancellationToken);
            return ParseBatchResults(response);
        }
    }

    #endregion
}
