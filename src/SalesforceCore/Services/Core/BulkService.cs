using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Implementation of Salesforce Bulk API 2.0 operations.
/// </summary>
public class BulkService : IBulkService
{
    private readonly ISalesforceClient _client;
    private readonly SalesforceOptions _options;
    private readonly ILogger<BulkService> _logger;

    /// <summary>
    /// Creates a new BulkService.
    /// </summary>
    public BulkService(
        ISalesforceClient client,
        IOptions<SalesforceOptions> options,
        ILogger<BulkService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    #region Job Management

    /// <inheritdoc/>
    public async Task<BulkJobInfo> CreateJobAsync(CreateBulkJobRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ObjectName))
            throw new ArgumentException("Object name is required", nameof(request));

        if (!SecurityUtils.IsValidObjectName(request.ObjectName))
            throw new ArgumentException($"Invalid object name: {request.ObjectName}", nameof(request));

        _logger.LogDebug("Creating bulk {Operation} job for {Object}", request.Operation, request.ObjectName);

        var response = await _client.PostAsync<BulkJobInfo>(SalesforceConstants.Paths.BulkIngest, request, cancellationToken);

        _logger.LogInformation("Created bulk job {JobId} for {Operation} on {Object}",
            response.Id, request.Operation, request.ObjectName);

        return response;
    }

    /// <inheritdoc/>
    public async Task<BulkJobInfo> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return await _client.GetAsync<BulkJobInfo>($"{SalesforceConstants.Paths.BulkIngest}/{jobId}", cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UploadJobDataAsync(string jobId, string csvData, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Uploading data to bulk job {JobId}", jobId);

        var endpoint = $"{SalesforceConstants.Paths.BulkIngest}/{jobId}/batches";
        await _client.PutRawAsync(endpoint, csvData, SalesforceConstants.Headers.ContentTypeCsv, cancellationToken);

        _logger.LogDebug("Data uploaded successfully to job {JobId}", jobId);
    }

    /// <inheritdoc/>
    public async Task UploadJobDataAsync(string jobId, Stream dataStream, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(dataStream);
        var csvData = await reader.ReadToEndAsync(cancellationToken);
        await UploadJobDataAsync(jobId, csvData, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobInfo> CloseJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Closing bulk job {JobId}", jobId);

        var response = await _client.PostAsync<BulkJobInfo>(
            $"{SalesforceConstants.Paths.BulkIngest}/{jobId}",
            new { state = SalesforceConstants.Bulk.StateUploadComplete },
            cancellationToken);

        _logger.LogInformation("Bulk job {JobId} closed, state: {State}", jobId, response.State);

        return response;
    }

    /// <inheritdoc/>
    public async Task<BulkJobInfo> AbortJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Aborting bulk job {JobId}", jobId);

        var response = await _client.PostAsync<BulkJobInfo>(
            $"{SalesforceConstants.Paths.BulkIngest}/{jobId}",
            new { state = SalesforceConstants.Bulk.StateAborted },
            cancellationToken);

        return response;
    }

    /// <inheritdoc/>
    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting bulk job {JobId}", jobId);
        await _client.DeleteAsync($"{SalesforceConstants.Paths.BulkIngest}/{jobId}", cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<BulkJobInfo>> GetAllJobsAsync(bool? isPkChunkingEnabled = null, string? jobType = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (isPkChunkingEnabled.HasValue)
            queryParams.Add($"isPkChunkingEnabled={isPkChunkingEnabled.Value.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(jobType))
            queryParams.Add($"jobType={jobType}");

        var endpoint = queryParams.Count > 0
            ? $"{SalesforceConstants.Paths.BulkIngest}?{string.Join("&", queryParams)}"
            : SalesforceConstants.Paths.BulkIngest;

        var response = await _client.GetAsync(endpoint, cancellationToken);
        var recordsNode = response["records"];
        var records = recordsNode != null ? JsonSerializer.Deserialize<List<BulkJobInfo>>(recordsNode) : null;
        return records ?? new List<BulkJobInfo>();
    }

    #endregion

    #region Results

    /// <inheritdoc/>
    public async Task<string> GetSuccessfulResultsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.BulkIngest}/{jobId}/successfulResults";
        return await GetCsvResultsAsync(endpoint, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> GetFailedResultsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.BulkIngest}/{jobId}/failedResults";
        return await GetCsvResultsAsync(endpoint, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> GetUnprocessedRecordsAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.BulkIngest}/{jobId}/unprocessedrecords";
        return await GetCsvResultsAsync(endpoint, cancellationToken);
    }

    #endregion

    #region High-Level Operations

    /// <inheritdoc/>
    public async Task<BulkJobResults> InsertAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteBulkOperationAsync(
            objectName,
            BulkOperation.insert,
            null,
            records,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobResults> InsertAsync(
        string objectName,
        string csvData,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteBulkOperationWithCsvAsync(
            objectName,
            BulkOperation.insert,
            null,
            csvData,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobResults> UpdateAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteBulkOperationAsync(
            objectName,
            BulkOperation.update,
            null,
            records,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobResults> UpsertAsync(
        string objectName,
        string externalIdField,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteBulkOperationAsync(
            objectName,
            BulkOperation.upsert,
            externalIdField,
            records,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobResults> UpsertAsync(
        string objectName,
        string externalIdField,
        string csvData,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteBulkOperationWithCsvAsync(
            objectName,
            BulkOperation.upsert,
            externalIdField,
            csvData,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobResults> DeleteAsync(
        string objectName,
        IEnumerable<string> ids,
        bool hardDelete = false,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var records = ids.Select(id => new Dictionary<string, object?> { { "Id", id } });

        return await ExecuteBulkOperationAsync(
            objectName,
            hardDelete ? BulkOperation.hardDelete : BulkOperation.delete,
            null,
            records,
            pollInterval,
            timeout,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BulkJobInfo> WaitForCompletionAsync(
        string jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        pollInterval ??= _options.BulkPollInterval;
        timeout ??= _options.BulkJobTimeout;

        var startTime = DateTime.UtcNow;
        BulkJobInfo job;

        do
        {
            job = await GetJobAsync(jobId, cancellationToken);

            if (job.IsComplete)
            {
                _logger.LogInformation(
                    "Bulk job {JobId} completed. State: {State}, Processed: {Processed}, Failed: {Failed}",
                    jobId, job.State, job.NumberRecordsProcessed, job.NumberRecordsFailed);
                return job;
            }

            if (DateTime.UtcNow - startTime > timeout)
            {
                _logger.LogWarning("Bulk job {JobId} timed out after {Timeout}", jobId, timeout);
                throw new SalesforceException($"Bulk job {jobId} timed out after {timeout}");
            }

            _logger.LogDebug("Bulk job {JobId} state: {State}, waiting...", jobId, job.State);
            await Task.Delay(pollInterval.Value, cancellationToken);

        } while (!cancellationToken.IsCancellationRequested);

        return job;
    }

    #endregion

    #region Bulk Query

    /// <inheritdoc/>
    public async Task<BulkJobInfo> CreateQueryJobAsync(CreateBulkQueryRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Creating bulk query job");

        var response = await _client.PostAsync<BulkJobInfo>(SalesforceConstants.Paths.BulkQuery, request, cancellationToken);

        _logger.LogInformation("Created bulk query job {JobId}", response.Id);

        return response;
    }

    /// <inheritdoc/>
    public async Task<string> GetQueryResultsAsync(string jobId, string? locator = null, int? maxRecords = null, CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrEmpty(locator))
            queryParams.Add($"locator={locator}");
        if (maxRecords.HasValue)
            queryParams.Add($"maxRecords={maxRecords.Value}");

        var endpoint = queryParams.Count > 0
            ? $"{SalesforceConstants.Paths.BulkQuery}/{jobId}/results?{string.Join("&", queryParams)}"
            : $"{SalesforceConstants.Paths.BulkQuery}/{jobId}/results";

        return await GetCsvResultsAsync(endpoint, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> QueryAsync(
        string soql,
        bool includeDeleted = false,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // Validate SOQL input
        if (string.IsNullOrWhiteSpace(soql))
            throw new ArgumentException("SOQL query is required", nameof(soql));

        // Basic SOQL validation - bulk queries must be SELECT statements
        var trimmedSoql = soql.TrimStart();
        if (!trimmedSoql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new SalesforceQueryException("Bulk query must be a SELECT statement", soql, "MALFORMED_QUERY");

        var job = await CreateQueryJobAsync(new CreateBulkQueryRequest
        {
            Query = soql,
            Operation = includeDeleted ? BulkOperation.queryAll : BulkOperation.query
        }, cancellationToken);

        // Wait for job completion - use configurable defaults
        pollInterval ??= _options.BulkPollInterval;
        timeout ??= _options.BulkJobTimeout;

        var startTime = DateTime.UtcNow;

        while (true)
        {
            var jobInfo = await GetQueryJobInfoAsync(job.Id, cancellationToken);

            if (jobInfo.IsComplete)
            {
                if (jobInfo.State == BulkJobState.Failed)
                {
                    throw new SalesforceException($"Bulk query failed: {jobInfo.ErrorMessage}");
                }

                // Get all results
                return await GetQueryResultsAsync(job.Id, cancellationToken: cancellationToken);
            }

            if (DateTime.UtcNow - startTime > timeout)
            {
                await AbortQueryJobAsync(job.Id, cancellationToken);
                throw new SalesforceException($"Bulk query timed out after {timeout}");
            }

            await Task.Delay(pollInterval.Value, cancellationToken);
        }
    }

    #endregion

    #region Private Methods

    private async Task<BulkJobResults> ExecuteBulkOperationAsync(
        string objectName,
        BulkOperation operation,
        string? externalIdField,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0)
        {
            return new BulkJobResults { Job = new BulkJobInfo { State = BulkJobState.JobComplete } };
        }

        var csvData = ConvertToCsv(recordList);
        return await ExecuteBulkOperationWithCsvAsync(
            objectName,
            operation,
            externalIdField,
            csvData,
            pollInterval,
            timeout,
            cancellationToken);
    }

    private async Task<BulkJobResults> ExecuteBulkOperationWithCsvAsync(
        string objectName,
        BulkOperation operation,
        string? externalIdField,
        string csvData,
        TimeSpan? pollInterval,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(csvData))
        {
            _logger.LogWarning("Bulk CSV payload was empty for {Operation} on {Object}", operation, objectName);
            return new BulkJobResults { Job = new BulkJobInfo { State = BulkJobState.JobComplete } };
        }

        // Create job
        var job = await CreateJobAsync(new CreateBulkJobRequest
        {
            ObjectName = objectName,
            Operation = operation,
            ExternalIdFieldName = externalIdField,
            ContentType = BulkContentType.CSV
        }, cancellationToken);

        try
        {
            // Upload data
            await UploadJobDataAsync(job.Id, csvData, cancellationToken);

            // Close job to start processing
            await CloseJobAsync(job.Id, cancellationToken);

            // Wait for completion
            job = await WaitForCompletionAsync(job.Id, pollInterval, timeout, cancellationToken);

            // Get results
            var results = new BulkJobResults { Job = job };

            if (job.NumberRecordsProcessed > 0)
            {
                var successCsv = await GetSuccessfulResultsAsync(job.Id, cancellationToken);
                results.SuccessfulRecords = ParseResultsCsv(successCsv, true);
            }

            if (job.NumberRecordsFailed > 0)
            {
                var failedCsv = await GetFailedResultsAsync(job.Id, cancellationToken);
                results.FailedRecords = ParseResultsCsv(failedCsv, false);
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk operation failed, aborting job {JobId}", job.Id);
            try
            {
                await AbortJobAsync(job.Id, cancellationToken);
            }
            catch (Exception abortEx)
            {
                _logger.LogDebug(abortEx, "Failed to abort bulk job {JobId} during error cleanup", job.Id);
            }
            throw;
        }
    }

    /// <summary>
    /// Converts records to RFC 4180 compliant CSV using CsvHelper.
    /// </summary>
    private static string ConvertToCsv(List<Dictionary<string, object?>> records)
    {
        if (records.Count == 0)
            return string.Empty;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            ShouldQuote = args => args.Field?.Contains(',') == true ||
                                  args.Field?.Contains('"') == true ||
                                  args.Field?.Contains('\n') == true ||
                                  args.Field?.Contains('\r') == true
        };

        using var writer = new StringWriter();
        using var csv = new CsvWriter(writer, config);

        // Get all unique field names
        var fields = records.SelectMany(r => r.Keys).Distinct().ToList();

        // Write header row
        foreach (var field in fields)
        {
            csv.WriteField(field);
        }
        csv.NextRecord();

        // Write data rows
        foreach (var record in records)
        {
            foreach (var field in fields)
            {
                record.TryGetValue(field, out var value);
                csv.WriteField(value?.ToString() ?? "");
            }
            csv.NextRecord();
        }

        return writer.ToString();
    }

    /// <summary>
    /// Parses Salesforce Bulk API result CSV using CsvHelper for RFC 4180 compliance.
    /// Handles complex edge cases like:
    /// - Rich Text fields containing newlines
    /// - Escaped quotes within quoted fields
    /// - Various line endings (CR, LF, CRLF)
    /// </summary>
    private static List<BulkRecordResult> ParseResultsCsv(string csv, bool success)
    {
        var results = new List<BulkRecordResult>();
        if (string.IsNullOrWhiteSpace(csv))
            return results;

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null, // Ignore missing fields
            BadDataFound = null, // Ignore bad data gracefully
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, config);

        // Read header record
        csvReader.Read();
        csvReader.ReadHeader();
        var headers = csvReader.HeaderRecord ?? Array.Empty<string>();

        var rowNum = 1;
        while (csvReader.Read())
        {
            var result = new BulkRecordResult
            {
                Success = success,
                RowNumber = rowNum++,
                OriginalData = new Dictionary<string, string?>()
            };

            // Extract Salesforce system fields
            result.Id = GetFieldValue(csvReader, "sf__Id");
            var createdValue = GetFieldValue(csvReader, "sf__Created");
            result.Created = !string.IsNullOrEmpty(createdValue) &&
                             createdValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            result.Error = GetFieldValue(csvReader, "sf__Error");

            // Store original data (non-system fields)
            foreach (var header in headers)
            {
                if (!header.StartsWith("sf__", StringComparison.OrdinalIgnoreCase))
                {
                    result.OriginalData[header] = GetFieldValue(csvReader, header);
                }
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Safely gets a field value from the CSV reader.
    /// Returns null if the field doesn't exist in the current row.
    /// </summary>
    /// <remarks>
    /// Only catches specific expected exceptions (field not found scenarios).
    /// Other exceptions are allowed to propagate for proper error handling.
    /// </remarks>
    private static string? GetFieldValue(CsvReader reader, string fieldName)
    {
        try
        {
            // Check if the field exists in the header record first
            if (reader.HeaderRecord != null && !reader.HeaderRecord.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
            {
                return null; // Field doesn't exist in this CSV - expected case
            }
            return reader.GetField(fieldName);
        }
        catch (CsvHelper.MissingFieldException)
        {
            // Field doesn't exist in the current row - expected case
            return null;
        }
        catch (CsvHelper.ReaderException)
        {
            // Field couldn't be read properly - expected for malformed data
            return null;
        }
        // Other exceptions (OutOfMemory, etc.) should propagate for proper error handling
    }

    private async Task<string> GetCsvResultsAsync(string endpoint, CancellationToken cancellationToken)
    {
        return await _client.GetRawAsync(endpoint, SalesforceConstants.Headers.ContentTypeCsv, cancellationToken);
    }

    private async Task<BulkJobInfo> GetQueryJobInfoAsync(string jobId, CancellationToken cancellationToken)
    {
        return await _client.GetAsync<BulkJobInfo>($"{SalesforceConstants.Paths.BulkQuery}/{jobId}", cancellationToken);
    }

    private async Task AbortQueryJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await _client.PostAsync($"{SalesforceConstants.Paths.BulkQuery}/{jobId}", new { state = SalesforceConstants.Bulk.StateAborted }, cancellationToken);
    }

    #endregion
}
