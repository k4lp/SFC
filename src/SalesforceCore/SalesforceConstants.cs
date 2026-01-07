namespace SalesforceCore;

/// <summary>
/// Centralized constants for the SalesforceCore library.
/// Eliminates hardcoded values throughout the codebase.
/// </summary>
public static class SalesforceConstants
{
    #region Default Values

    /// <summary>
    /// Default Salesforce OAuth domain for production.
    /// </summary>
    public const string DefaultDomain = "https://login.salesforce.com";

    /// <summary>
    /// Salesforce OAuth domain for sandbox environments.
    /// </summary>
    public const string SandboxDomain = "https://test.salesforce.com";

    /// <summary>
    /// Default Salesforce REST API version.
    /// </summary>
    public const string DefaultApiVersion = "v60.0";

    #endregion

    #region OAuth Grant Types

    /// <summary>
    /// OAuth grant types used in Salesforce authentication.
    /// </summary>
    public static class GrantTypes
    {
        /// <summary>
        /// Authorization code grant type for PKCE flow.
        /// </summary>
        public const string AuthorizationCode = "authorization_code";

        /// <summary>
        /// Refresh token grant type.
        /// </summary>
        public const string RefreshToken = "refresh_token";

        /// <summary>
        /// JWT Bearer grant type for server-to-server authentication.
        /// </summary>
        public const string JwtBearer = "urn:ietf:params:oauth:grant-type:jwt-bearer";
    }

    #endregion

    #region Claim Types

    /// <summary>
    /// Custom claim types used for Salesforce identity.
    /// </summary>
    public static class Claims
    {
        /// <summary>
        /// Claim namespace prefix for Salesforce claims.
        /// </summary>
        public const string NamespacePrefix = "urn:salesforce:";

        /// <summary>
        /// Claim type for Salesforce instance URL.
        /// </summary>
        public const string InstanceUrl = NamespacePrefix + "instance_url";

        /// <summary>
        /// Claim type for Salesforce organization ID.
        /// </summary>
        public const string OrganizationId = NamespacePrefix + "organization_id";

        /// <summary>
        /// Claim type for Salesforce user ID.
        /// </summary>
        public const string UserId = NamespacePrefix + "user_id";
    }

    #endregion

    #region API Paths

    /// <summary>
    /// Salesforce API path components.
    /// </summary>
    public static class Paths
    {
        /// <summary>
        /// Base path for Salesforce services.
        /// </summary>
        public const string ServicesBase = "/services";

        /// <summary>
        /// Path prefix for REST API data endpoints.
        /// </summary>
        public const string DataPath = "/services/data/";

        /// <summary>
        /// OAuth token endpoint path.
        /// </summary>
        public const string OAuthToken = "/services/oauth2/token";

        /// <summary>
        /// OAuth revoke endpoint path.
        /// </summary>
        public const string OAuthRevoke = "/services/oauth2/revoke";

        /// <summary>
        /// OAuth authorize endpoint path.
        /// </summary>
        public const string OAuthAuthorize = "/services/oauth2/authorize";

        /// <summary>
        /// SObjects endpoint path (relative to versioned API path).
        /// </summary>
        public const string SObjects = "/sobjects";

        /// <summary>
        /// Query endpoint path (relative to versioned API path).
        /// </summary>
        public const string Query = "/query";

        /// <summary>
        /// Composite endpoint path (relative to versioned API path).
        /// </summary>
        public const string Composite = "/composite";

        /// <summary>
        /// Bulk API ingest jobs endpoint (relative to versioned API path).
        /// </summary>
        public const string BulkIngest = "/jobs/ingest";

        /// <summary>
        /// Bulk API query jobs endpoint (relative to versioned API path).
        /// </summary>
        public const string BulkQuery = "/jobs/query";

        /// <summary>
        /// Search endpoint for SOSL queries (relative to versioned API path).
        /// </summary>
        public const string Search = "/search";

        /// <summary>
        /// Tooling API base path (relative to versioned API path).
        /// </summary>
        public const string Tooling = "/tooling";

        /// <summary>
        /// Tooling API query endpoint.
        /// </summary>
        public const string ToolingQuery = "/tooling/query";

        /// <summary>
        /// Tooling API execute anonymous endpoint.
        /// </summary>
        public const string ToolingExecuteAnonymous = "/tooling/executeAnonymous";

        /// <summary>
        /// Apex REST base path (not versioned).
        /// </summary>
        public const string ApexRest = "/services/apexrest";

        /// <summary>
        /// Composite Graph endpoint (relative to versioned API path).
        /// </summary>
        public const string CompositeGraph = "/composite/graph";

        /// <summary>
        /// Analytics/Reports base endpoint (relative to versioned API path).
        /// </summary>
        public const string Analytics = "/analytics";

        /// <summary>
        /// Reports endpoint (relative to versioned API path).
        /// </summary>
        public const string AnalyticsReports = "/analytics/reports";

        /// <summary>
        /// Limits endpoint (relative to versioned API path).
        /// </summary>
        public const string Limits = "/limits";

        /// <summary>
        /// Recent items endpoint (relative to versioned API path).
        /// </summary>
        public const string Recent = "/recent";
    }

    #endregion

    #region Default Timeouts and Intervals

    /// <summary>
    /// Default timeout and interval values.
    /// </summary>
    public static class Defaults
    {
        /// <summary>
        /// Default HTTP request timeout in seconds.
        /// </summary>
        public const int HttpTimeoutSeconds = 30;

        /// <summary>
        /// Default maximum response content buffer size in bytes (10MB).
        /// </summary>
        public const long MaxResponseContentBufferSizeBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Default maximum retry attempts.
        /// </summary>
        public const int MaxRetryAttempts = 3;

        /// <summary>
        /// Default retry delay base in seconds (for exponential backoff).
        /// </summary>
        public const double RetryDelayBaseSeconds = 1.0;

        /// <summary>
        /// Default exponential backoff multiplier.
        /// </summary>
        public const int ExponentialBackoffBase = 2;

        /// <summary>
        /// Default bulk job poll interval in seconds.
        /// </summary>
        public const int BulkPollIntervalSeconds = 5;

        /// <summary>
        /// Default bulk job timeout in minutes.
        /// </summary>
        public const int BulkTimeoutMinutes = 30;

        /// <summary>
        /// Default schema cache duration in hours.
        /// </summary>
        public const int SchemaCacheDurationHours = 1;

        /// <summary>
        /// Default lookup cache duration in minutes.
        /// </summary>
        public const int LookupCacheDurationMinutes = 15;

        /// <summary>
        /// Default session timeout in hours.
        /// </summary>
        public const int SessionTimeoutHours = 8;

        /// <summary>
        /// Default page size for list views.
        /// </summary>
        public const int DefaultPageSize = 25;

        /// <summary>
        /// Maximum page size allowed.
        /// </summary>
        public const int MaxPageSize = 100;

        /// <summary>
        /// Default lookup search result limit.
        /// </summary>
        public const int LookupSearchLimit = 15;

        /// <summary>
        /// Maximum file upload size in bytes (25MB).
        /// </summary>
        public const long MaxFileUploadSize = 25 * 1024 * 1024;

        /// <summary>
        /// Circuit breaker sampling duration in seconds.
        /// </summary>
        public const int CircuitBreakerSamplingSeconds = 30;

        /// <summary>
        /// Circuit breaker break duration in seconds.
        /// </summary>
        public const int CircuitBreakerBreakSeconds = 30;

        /// <summary>
        /// Total request timeout in seconds.
        /// </summary>
        public const int TotalRequestTimeoutSeconds = 60;

        /// <summary>
        /// Token refresh lock timeout in seconds.
        /// </summary>
        public const int TokenRefreshLockTimeoutSeconds = 30;

        /// <summary>
        /// Distributed lock timeout in seconds.
        /// </summary>
        public const int DistributedLockTimeoutSeconds = 30;

        /// <summary>
        /// Token expiry buffer in minutes (refresh before actual expiry).
        /// </summary>
        public const int TokenExpiryBufferMinutes = 5;

        /// <summary>
        /// Background token refresh check interval in minutes.
        /// </summary>
        public const int TokenRefreshCheckIntervalMinutes = 1;

        /// <summary>
        /// Default permission cache duration in minutes.
        /// </summary>
        public const int PermissionCacheDurationMinutes = 5;

        /// <summary>
        /// Default layout cache duration in minutes.
        /// </summary>
        public const int LayoutCacheDurationMinutes = 10;

        /// <summary>
        /// Default JWT token expiration in hours.
        /// </summary>
        public const int JwtTokenExpirationHours = 1;

        /// <summary>
        /// Maximum history query limit.
        /// </summary>
        public const int MaxHistoryQueryLimit = 200;

        /// <summary>
        /// Maximum report search limit.
        /// </summary>
        public const int MaxReportSearchLimit = 100;
    }

    #endregion

    #region HTTP Headers

    /// <summary>
    /// HTTP header names used in Salesforce API calls.
    /// </summary>
    public static class Headers
    {
        /// <summary>
        /// Content-Type header for JSON.
        /// </summary>
        public const string ContentTypeJson = "application/json";

        /// <summary>
        /// Content-Type header for CSV (Bulk API).
        /// </summary>
        public const string ContentTypeCsv = "text/csv";

        /// <summary>
        /// User-Agent header value.
        /// </summary>
        public const string UserAgent = "SalesforceCore/1.0";

        /// <summary>
        /// Sforce-Auto-Assign header for assignment rule control.
        /// </summary>
        public const string SforceAutoAssign = "Sforce-Auto-Assign";

        /// <summary>
        /// Sforce-Duplicate-Rule-Header for duplicate rule control.
        /// </summary>
        public const string SforceDuplicateRule = "Sforce-Duplicate-Rule-Header";
    }

    #endregion

    #region Configuration Keys

    /// <summary>
    /// Configuration section names and keys.
    /// </summary>
    public static class ConfigKeys
    {
        /// <summary>
        /// Main Salesforce configuration section name.
        /// </summary>
        public const string SalesforceSection = "Salesforce";

        /// <summary>
        /// JWT configuration section name.
        /// </summary>
        public const string JwtSection = "SalesforceJwt";

        /// <summary>
        /// MVC configuration section name.
        /// </summary>
        public const string MvcSection = "SalesforceMvc";
    }

    #endregion

    #region Bulk API Constants

    /// <summary>
    /// Bulk API specific constants.
    /// </summary>
    public static class Bulk
    {
        /// <summary>
        /// Maximum records per Bulk API batch.
        /// </summary>
        public const int MaxRecordsPerBatch = 10000;

        /// <summary>
        /// Upload complete state for closing a job.
        /// </summary>
        public const string StateUploadComplete = "UploadComplete";

        /// <summary>
        /// Aborted state for canceling a job.
        /// </summary>
        public const string StateAborted = "Aborted";
    }

    #endregion

    #region Composite API Constants

    /// <summary>
    /// Composite API specific constants.
    /// </summary>
    public static class Composite
    {
        /// <summary>
        /// Maximum sub-requests per composite call.
        /// </summary>
        public const int MaxSubRequestsPerBatch = 25;

        /// <summary>
        /// Maximum nodes per composite graph request.
        /// </summary>
        public const int MaxGraphNodes = 500;

        /// <summary>
        /// Maximum graphs per composite graph request.
        /// </summary>
        public const int MaxGraphsPerRequest = 25;
    }

    #endregion

    #region Validation Patterns

    /// <summary>
    /// Validation patterns for Salesforce data types.
    /// </summary>
    public static class ValidationPatterns
    {
        /// <summary>
        /// Pattern for validating Salesforce 15 or 18 character IDs.
        /// </summary>
        public const string SalesforceIdPattern = @"^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$";

        /// <summary>
        /// Pattern for validating sObject API names.
        /// </summary>
        public const string SObjectNamePattern = @"^[a-zA-Z][a-zA-Z0-9_]*(__c)?$";

        /// <summary>
        /// Pattern for validating field API names.
        /// </summary>
        public const string FieldNamePattern = @"^[a-zA-Z][a-zA-Z0-9_]*(__c|__r)?$";
    }

    #endregion
}
