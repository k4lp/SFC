namespace SalesforceCore.Services.Core;

/// <summary>
/// Service for building Salesforce API URLs in a consistent manner.
/// Centralizes all URL construction to ensure correct path formatting.
/// </summary>
public interface ISalesforceUrlBuilder
{
    /// <summary>
    /// Gets the current instance URL.
    /// </summary>
    string? InstanceUrl { get; }

    /// <summary>
    /// Gets the configured API version.
    /// </summary>
    string ApiVersion { get; }

    /// <summary>
    /// Sets the instance URL after authentication.
    /// </summary>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    void SetInstanceUrl(string instanceUrl);

    /// <summary>
    /// Builds a full URL for a versioned API endpoint.
    /// </summary>
    /// <param name="relativePath">Relative path (e.g., "/sobjects/Account").</param>
    /// <returns>Full URL including instance and API version.</returns>
    string BuildDataApiUrl(string relativePath);

    /// <summary>
    /// Builds a full URL for a non-versioned services endpoint.
    /// </summary>
    /// <param name="servicePath">Service path (e.g., "/services/oauth2/token").</param>
    /// <returns>Full URL including instance.</returns>
    string BuildServiceUrl(string servicePath);

    /// <summary>
    /// Builds a URL for sObject operations.
    /// </summary>
    /// <param name="sObjectName">Name of the sObject.</param>
    /// <param name="recordId">Optional record ID.</param>
    /// <returns>Full URL for the sObject endpoint.</returns>
    string BuildSObjectUrl(string sObjectName, string? recordId = null);

    /// <summary>
    /// Builds a URL for SOQL query operations.
    /// </summary>
    /// <param name="soql">SOQL query string.</param>
    /// <returns>Full URL for the query endpoint with encoded query.</returns>
    string BuildQueryUrl(string soql);

    /// <summary>
    /// Builds a URL for composite API operations.
    /// </summary>
    /// <returns>Full URL for the composite endpoint.</returns>
    string BuildCompositeUrl();

    /// <summary>
    /// Builds a URL for Bulk API ingest job operations.
    /// </summary>
    /// <param name="jobId">Optional job ID for specific job operations.</param>
    /// <param name="suffix">Optional suffix (e.g., "batches", "successfulResults").</param>
    /// <returns>Full URL for the bulk ingest endpoint.</returns>
    string BuildBulkIngestUrl(string? jobId = null, string? suffix = null);

    /// <summary>
    /// Builds a URL for Bulk API query job operations.
    /// </summary>
    /// <param name="jobId">Optional job ID for specific job operations.</param>
    /// <param name="suffix">Optional suffix (e.g., "results").</param>
    /// <returns>Full URL for the bulk query endpoint.</returns>
    string BuildBulkQueryUrl(string? jobId = null, string? suffix = null);

    /// <summary>
    /// Builds a URL for OAuth token operations.
    /// </summary>
    /// <param name="domain">Optional domain override. Uses configured domain if not specified.</param>
    /// <returns>Full URL for the OAuth token endpoint.</returns>
    string BuildOAuthTokenUrl(string? domain = null);

    /// <summary>
    /// Builds a URL for OAuth revoke operations.
    /// </summary>
    /// <param name="domain">Optional domain override. Uses instance URL if not specified.</param>
    /// <returns>Full URL for the OAuth revoke endpoint.</returns>
    string BuildOAuthRevokeUrl(string? domain = null);

    /// <summary>
    /// Gets the relative path for a versioned API endpoint.
    /// </summary>
    /// <param name="relativePath">Relative path without version prefix.</param>
    /// <returns>Path with API version prefix.</returns>
    string GetVersionedPath(string relativePath);
}
