using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;

using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Implementation of URL builder for Salesforce API endpoints.
/// Centralizes URL construction to eliminate hardcoded paths throughout the codebase.
/// </summary>
public class SalesforceUrlBuilder : ISalesforceUrlBuilder
{
    private readonly SalesforceOptions _options;
    private string? _instanceUrl;

    /// <summary>
    /// Creates a new SalesforceUrlBuilder.
    /// </summary>
    public SalesforceUrlBuilder(IOptions<SalesforceOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc/>
    public string? InstanceUrl => _instanceUrl;

    /// <inheritdoc/>
    public string ApiVersion => _options.ApiVersion;

    /// <inheritdoc/>
    public void SetInstanceUrl(string instanceUrl)
    {
        _instanceUrl = instanceUrl?.TrimEnd('/');
    }

    /// <inheritdoc/>
    public string BuildDataApiUrl(string relativePath)
    {
        EnsureInstanceUrl();

        // Normalize the relative path
        relativePath = NormalizePath(relativePath);

        // If the path already starts with /services/, use it as-is
        if (relativePath.StartsWith(SalesforceConstants.Paths.ServicesBase, StringComparison.OrdinalIgnoreCase))
        {
            return $"{_instanceUrl}{relativePath}";
        }

        // Otherwise, prepend the versioned data path
        return $"{_instanceUrl}{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{relativePath}";
    }

    /// <inheritdoc/>
    public string BuildServiceUrl(string servicePath)
    {
        EnsureInstanceUrl();
        return $"{_instanceUrl}{NormalizePath(servicePath)}";
    }

    /// <inheritdoc/>
    public string BuildSObjectUrl(string sObjectName, string? recordId = null)
    {
        var path = $"{SalesforceConstants.Paths.SObjects}/{sObjectName}";
        if (!string.IsNullOrEmpty(recordId))
        {
            path += $"/{recordId}";
        }
        return BuildDataApiUrl(path);
    }

    /// <inheritdoc/>
    public string BuildQueryUrl(string soql)
    {
        var encodedQuery = UrlUtils.Escape(soql);
        return BuildDataApiUrl($"{SalesforceConstants.Paths.Query}?q={encodedQuery}");
    }

    /// <inheritdoc/>
    public string BuildCompositeUrl()
    {
        return BuildDataApiUrl(SalesforceConstants.Paths.Composite);
    }

    /// <inheritdoc/>
    public string BuildBulkIngestUrl(string? jobId = null, string? suffix = null)
    {
        var path = SalesforceConstants.Paths.BulkIngest;
        if (!string.IsNullOrEmpty(jobId))
        {
            path += $"/{jobId}";
            if (!string.IsNullOrEmpty(suffix))
            {
                path += $"/{suffix}";
            }
        }
        return BuildDataApiUrl(path);
    }

    /// <inheritdoc/>
    public string BuildBulkQueryUrl(string? jobId = null, string? suffix = null)
    {
        var path = SalesforceConstants.Paths.BulkQuery;
        if (!string.IsNullOrEmpty(jobId))
        {
            path += $"/{jobId}";
            if (!string.IsNullOrEmpty(suffix))
            {
                path += $"/{suffix}";
            }
        }
        return BuildDataApiUrl(path);
    }

    /// <inheritdoc/>
    public string BuildOAuthTokenUrl(string? domain = null)
    {
        var baseDomain = (domain ?? _options.Domain).TrimEnd('/');
        return $"{baseDomain}{SalesforceConstants.Paths.OAuthToken}";
    }

    /// <inheritdoc/>
    public string BuildOAuthRevokeUrl(string? domain = null)
    {
        var baseDomain = (domain ?? _instanceUrl ?? _options.Domain).TrimEnd('/');
        return $"{baseDomain}{SalesforceConstants.Paths.OAuthRevoke}";
    }

    /// <inheritdoc/>
    public string GetVersionedPath(string relativePath)
    {
        relativePath = NormalizePath(relativePath);
        return $"{SalesforceConstants.Paths.DataPath}{_options.ApiVersion}{relativePath}";
    }

    /// <summary>
    /// Ensures the instance URL is set before building URLs.
    /// </summary>
    private void EnsureInstanceUrl()
    {
        if (string.IsNullOrEmpty(_instanceUrl))
        {
            throw new InvalidOperationException(
                "Instance URL not set. Ensure authentication is complete before making API calls.");
        }
    }

    /// <summary>
    /// Normalizes a path to ensure it starts with a forward slash.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        return path.StartsWith("/") ? path : "/" + path;
    }
}
