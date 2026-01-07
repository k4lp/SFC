using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Service for high-volume data operations using Salesforce Bulk API 2.0.
/// Use this for processing thousands to millions of records.
/// </summary>
public interface IBulkService
{
    #region Job Management

    /// <summary>
    /// Creates a new bulk ingest job.
    /// </summary>
    /// <param name="request">Job creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created job information.</returns>
    Task<BulkJobInfo> CreateJobAsync(CreateBulkJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a bulk job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job information.</returns>
    Task<BulkJobInfo> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads CSV data to a bulk job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="csvData">The CSV data to upload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadJobDataAsync(string jobId, string csvData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads CSV data from a stream to a bulk job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="dataStream">The stream containing CSV data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UploadJobDataAsync(string jobId, Stream dataStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the job to begin processing.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated job information.</returns>
    Task<BulkJobInfo> CloseJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aborts a bulk job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated job information.</returns>
    Task<BulkJobInfo> AbortJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a bulk job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all bulk jobs.
    /// </summary>
    /// <param name="isPkChunkingEnabled">Filter by PK chunking.</param>
    /// <param name="jobType">Filter by job type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of bulk jobs.</returns>
    Task<List<BulkJobInfo>> GetAllJobsAsync(bool? isPkChunkingEnabled = null, string? jobType = null, CancellationToken cancellationToken = default);

    #endregion

    #region Results

    /// <summary>
    /// Gets successful results from a completed job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV data of successful records.</returns>
    Task<string> GetSuccessfulResultsAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets failed results from a completed job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV data of failed records with error messages.</returns>
    Task<string> GetFailedResultsAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets unprocessed records from an aborted job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV data of unprocessed records.</returns>
    Task<string> GetUnprocessedRecordsAsync(string jobId, CancellationToken cancellationToken = default);

    #endregion

    #region High-Level Operations

    /// <summary>
    /// Inserts records in bulk and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="records">The records to insert.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> InsertAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts records in bulk from raw CSV and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="csvData">RFC 4180 compliant CSV with a header row.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> InsertAsync(
        string objectName,
        string csvData,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates records in bulk and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="records">The records to update (must include Id).</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> UpdateAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts records in bulk and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="externalIdField">The external ID field name.</param>
    /// <param name="records">The records to upsert.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> UpsertAsync(
        string objectName,
        string externalIdField,
        IEnumerable<Dictionary<string, object?>> records,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts records in bulk from raw CSV and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="externalIdField">The external ID field name.</param>
    /// <param name="csvData">RFC 4180 compliant CSV with a header row.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> UpsertAsync(
        string objectName,
        string externalIdField,
        string csvData,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes records in bulk and waits for completion.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="ids">The record IDs to delete.</param>
    /// <param name="hardDelete">Whether to bypass recycle bin.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait for completion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job results.</returns>
    Task<BulkJobResults> DeleteAsync(
        string objectName,
        IEnumerable<string> ids,
        bool hardDelete = false,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a job to complete.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The final job information.</returns>
    Task<BulkJobInfo> WaitForCompletionAsync(
        string jobId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Bulk Query

    /// <summary>
    /// Creates a bulk query job.
    /// </summary>
    /// <param name="request">Query request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created job information.</returns>
    Task<BulkJobInfo> CreateQueryJobAsync(CreateBulkQueryRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets query results from a completed query job.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="locator">Locator for paginated results.</param>
    /// <param name="maxRecords">Maximum records to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV data of query results.</returns>
    Task<string> GetQueryResultsAsync(string jobId, string? locator = null, int? maxRecords = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a bulk query and returns all results.
    /// </summary>
    /// <param name="soql">The SOQL query.</param>
    /// <param name="includeDeleted">Include deleted records (queryAll).</param>
    /// <param name="pollInterval">Interval to check job status.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>CSV data of all query results.</returns>
    Task<string> QueryAsync(
        string soql,
        bool includeDeleted = false,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    #endregion
}
