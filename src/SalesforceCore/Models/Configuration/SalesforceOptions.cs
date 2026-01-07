namespace SalesforceCore.Models.Configuration;

/// <summary>
/// Main configuration options for SalesforceCore library.
/// Configure via appsettings.json or programmatically.
/// All defaults are defined in <see cref="SalesforceConstants"/> for centralization.
/// </summary>
public class SalesforceOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = SalesforceConstants.ConfigKeys.SalesforceSection;

    /// <summary>
    /// Salesforce OAuth domain (e.g., "https://login.salesforce.com" or custom domain).
    /// Default: "https://login.salesforce.com"
    /// </summary>
    public string Domain { get; set; } = SalesforceConstants.DefaultDomain;

    /// <summary>
    /// Connected App Client ID (Consumer Key).
    /// Required for OAuth authentication.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Connected App Client Secret (Consumer Secret).
    /// Optional for public apps using PKCE.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// OAuth callback path (relative to app root).
    /// Default: "/salesforce/callback"
    /// </summary>
    public string CallbackPath { get; set; } = "/salesforce/callback";

    /// <summary>
    /// OAuth scopes to request.
    /// Default: openid, profile, email, api, refresh_token, offline_access
    /// </summary>
    public string[] Scopes { get; set; } = { "openid", "profile", "email", "api", "refresh_token", "offline_access" };

    /// <summary>
    /// Salesforce REST API version.
    /// Default: "v60.0"
    /// </summary>
    public string ApiVersion { get; set; } = SalesforceConstants.DefaultApiVersion;

    /// <summary>
    /// HTTP request timeout.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.HttpTimeoutSeconds);

    /// <summary>
    /// Maximum response content buffer size in bytes.
    /// Default: 10MB
    /// </summary>
    public long MaxResponseContentBufferSize { get; set; } = SalesforceConstants.Defaults.MaxResponseContentBufferSizeBytes;

    /// <summary>
    /// Maximum retry attempts for failed requests.
    /// Default: 3
    /// </summary>
    public int MaxRetries { get; set; } = SalesforceConstants.Defaults.MaxRetryAttempts;

    /// <summary>
    /// Base delay for retry exponential backoff.
    /// Default: 1 second (delays will be 1s, 2s, 4s, 8s...)
    /// </summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.RetryDelayBaseSeconds);

    /// <summary>
    /// Schema metadata cache duration.
    /// Default: 1 hour
    /// </summary>
    public TimeSpan SchemaCacheDuration { get; set; } = TimeSpan.FromHours(SalesforceConstants.Defaults.SchemaCacheDurationHours);

    /// <summary>
    /// Lookup result cache duration.
    /// Default: 15 minutes
    /// </summary>
    public TimeSpan LookupCacheDuration { get; set; } = TimeSpan.FromMinutes(SalesforceConstants.Defaults.LookupCacheDurationMinutes);

    /// <summary>
    /// Specifies which cache provider to use.
    /// Default: Memory
    /// </summary>
    /// <remarks>
    /// Options:
    /// - Memory: In-process memory cache (IMemoryCache). Default for single-instance apps.
    /// - Distributed: Requires external IDistributedCache (Redis, NCache). User must register.
    /// - SqlServer: Uses SQL Server via EF Core with mandatory AES-256 encryption.
    /// </remarks>
    public Services.Caching.CacheProviderType CacheProvider { get; set; } = Services.Caching.CacheProviderType.Memory;

    /// <summary>
    /// Enable distributed cache (Redis) instead of memory cache.
    /// Default: false
    /// </summary>
    /// <remarks>
    /// DEPRECATED: Use CacheProvider = CacheProviderType.Distributed instead.
    /// This property is maintained for backward compatibility.
    /// </remarks>
    [Obsolete("Use CacheProvider = CacheProviderType.Distributed instead. This property will be removed in v2.0.")]
    public bool UseDistributedCache
    {
        get => CacheProvider == Services.Caching.CacheProviderType.Distributed;
        set => CacheProvider = value ? Services.Caching.CacheProviderType.Distributed : Services.Caching.CacheProviderType.Memory;
    }

    /// <summary>
    /// Prefix for all cache keys. Useful for shared Redis instances
    /// to avoid key collisions between environments (dev, staging, prod).
    /// Example: "SF_PROD_" or "MyApp_SF_"
    /// Default: "SF_"
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "SF_";

    /// <summary>
    /// Connection string name for SQL Server cache.
    /// Only used when CacheProvider = SqlServer.
    /// If not specified, uses "DefaultConnection".
    /// </summary>
    public string SqlCacheConnectionStringName { get; set; } = "DefaultConnection";

    /// <summary>
    /// Encryption key for SQL Server cache.
    /// Only used when CacheProvider = SqlServer.
    /// Must be a base64-encoded 256-bit (32-byte) key.
    /// For production deployments, this must be explicitly configured from a secure vault.
    /// </summary>
    public string? SqlCacheEncryptionKey { get; set; }

    /// <summary>
    /// Allows deriving the SQL cache encryption key from non-secret inputs (NOT recommended).
    /// This exists only as a development escape hatch and should never be enabled in production.
    /// </summary>
    public bool AllowInsecureSqlCacheKeyDerivation { get; set; } = false;

    /// <summary>
    /// Interval for cleaning up expired cache entries.
    /// Only used when CacheProvider = SqlServer.
    /// Default: 30 minutes
    /// </summary>
    public TimeSpan CacheCleanupInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Write-behind buffering settings for the encrypted SQL Server cache.
    /// Only used when CacheProvider = SqlServer.
    /// </summary>
    public SqlCacheWriteBehindOptions SqlCacheWriteBehind { get; set; } = new();

    /// <summary>
    /// Default page size for list views.
    /// Default: 25
    /// </summary>
    public int DefaultPageSize { get; set; } = SalesforceConstants.Defaults.DefaultPageSize;

    /// <summary>
    /// Maximum page size allowed.
    /// Default: 100
    /// </summary>
    public int MaxPageSize { get; set; } = SalesforceConstants.Defaults.MaxPageSize;

    /// <summary>
    /// Maximum results for lookup searches.
    /// Default: 15
    /// </summary>
    public int LookupSearchLimit { get; set; } = SalesforceConstants.Defaults.LookupSearchLimit;

    /// <summary>
    /// Maximum file upload size in bytes.
    /// Default: 25MB
    /// </summary>
    public long MaxFileUploadSize { get; set; } = SalesforceConstants.Defaults.MaxFileUploadSize;

    /// <summary>
    /// Enable verbose logging for debugging.
    /// Default: false
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// Path to module configuration file.
    /// Default: "salesforce_config.json"
    /// </summary>
    public string ConfigFilePath { get; set; } = "salesforce_config.json";

    /// <summary>
    /// Session cookie name.
    /// Default: "__Host-SalesforceSession"
    /// </summary>
    public string SessionCookieName { get; set; } = "__Host-SalesforceSession";

    /// <summary>
    /// Session timeout duration.
    /// Default: 8 hours
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromHours(SalesforceConstants.Defaults.SessionTimeoutHours);

    /// <summary>
    /// Forces secure cookie policy (HTTPS only).
    /// Set to true when deploying behind reverse proxies (Azure, AWS, etc.)
    /// where the app sees HTTP but the client sees HTTPS.
    /// Default: true (recommended for production)
    /// </summary>
    public bool ForceSecureCookie { get; set; } = true;

    /// <summary>
    /// Enable sliding session expiration.
    /// Default: true
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// When true, forces users to re-authenticate with Salesforce on login.
    /// This adds "prompt=login" to the authorization URL, preventing automatic login
    /// with existing Salesforce sessions. Useful for applications requiring explicit
    /// user re-authentication after logout.
    /// Default: false
    /// </summary>
    public bool PromptLogin { get; set; } = false;

    /// <summary>
    /// Enforce Salesforce Field Level Security.
    /// Default: true
    /// </summary>
    public bool EnforceFieldLevelSecurity { get; set; } = true;

    /// <summary>
    /// Validate and sanitize SOQL inputs.
    /// Default: true
    /// </summary>
    public bool ValidateSoqlInputs { get; set; } = true;

    /// <summary>
    /// When true, only properties with [SalesforceField] attribute are included in SOQL queries.
    /// Properties without the attribute are automatically ignored unless they are:
    /// - The Id property (by name or [SalesforceId] attribute)
    /// - Decorated with [SalesforceChildRelationship] or [SalesforceLookup]
    /// Set to true to prevent "No such column" errors from unmapped properties.
    /// Default: false (for backward compatibility, but recommended to set true for new projects).
    /// </summary>
    public bool RequireSalesforceFieldAttribute { get; set; } = false;

    #region Bulk API Options

    /// <summary>
    /// Default polling interval for bulk jobs.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan BulkPollInterval { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.BulkPollIntervalSeconds);

    /// <summary>
    /// Default timeout for bulk job operations.
    /// Default: 30 minutes
    /// </summary>
    public TimeSpan BulkJobTimeout { get; set; } = TimeSpan.FromMinutes(SalesforceConstants.Defaults.BulkTimeoutMinutes);

    #endregion

    #region Resilience Options

    /// <summary>
    /// Circuit breaker sampling duration.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CircuitBreakerSamplingDuration { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.CircuitBreakerSamplingSeconds);

    /// <summary>
    /// Circuit breaker break duration.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.CircuitBreakerBreakSeconds);

    /// <summary>
    /// Total request timeout (including retries).
    /// Default: 60 seconds
    /// </summary>
    public TimeSpan TotalRequestTimeout { get; set; } = TimeSpan.FromSeconds(SalesforceConstants.Defaults.TotalRequestTimeoutSeconds);

    /// <summary>
    /// Timeout for each individual HTTP attempt before retry.
    /// This is separate from TotalRequestTimeout which covers all retries combined.
    /// If a single request exceeds this timeout, Polly will cancel it and retry.
    /// Default: 30 seconds (matches HttpTimeout)
    /// </summary>
    public TimeSpan PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    #endregion
}

/// <summary>
/// CSS framework option for rendering form elements.
/// </summary>
public enum CssFrameworkOption
{
    /// <summary>Bootstrap 5 classes (default).</summary>
    Bootstrap5,
    /// <summary>Salesforce Lightning Design System classes.</summary>
    SLDS,
    /// <summary>No framework-specific classes - custom CSS only.</summary>
    Custom,
    /// <summary>No framework-specific classes.</summary>
    None
}

/// <summary>
/// Toast notification position options.
/// </summary>
public enum ToastPosition
{
    /// <summary>Top-right corner (default).</summary>
    TopRight,
    /// <summary>Top-left corner.</summary>
    TopLeft,
    /// <summary>Bottom-right corner.</summary>
    BottomRight,
    /// <summary>Bottom-left corner.</summary>
    BottomLeft
}

/// <summary>
/// Configuration for the Salesforce MVC integration.
/// </summary>
public class SalesforceMvcOptions
{
    /// <summary>
    /// Route prefix for Salesforce controllers.
    /// Default: "sf" (routes will be /sf/{sObject}/...)
    /// </summary>
    public string RoutePrefix { get; set; } = "sf";

    /// <summary>
    /// Default layout view path.
    /// Default: null (uses application's _Layout.cshtml)
    /// </summary>
    public string? LayoutPath { get; set; }

    /// <summary>
    /// Enable embedded views from the library.
    /// Default: true
    /// </summary>
    public bool UseEmbeddedViews { get; set; } = true;

    /// <summary>
    /// Enable embedded static files (CSS, JS).
    /// Default: true
    /// </summary>
    public bool UseEmbeddedStaticFiles { get; set; } = true;

    /// <summary>
    /// Path prefix for static files.
    /// Default: "/_salesforce"
    /// </summary>
    public string StaticFilesPath { get; set; } = "/_salesforce";

    /// <summary>
    /// Enable HTMX integration for dynamic updates.
    /// Default: true
    /// </summary>
    public bool EnableHtmx { get; set; } = true;

    /// <summary>
    /// Show record IDs in list views (for debugging).
    /// Default: false
    /// </summary>
    public bool ShowRecordIds { get; set; } = false;

    /// <summary>
    /// Enable confirmation dialogs for delete operations.
    /// Default: true
    /// </summary>
    public bool ConfirmDeletes { get; set; } = true;

    /// <summary>
    /// Enable file upload functionality.
    /// Default: true
    /// </summary>
    public bool EnableFileUploads { get; set; } = true;

    /// <summary>
    /// Allowed file extensions for uploads.
    /// Default: common image and document types
    /// </summary>
    public string[] AllowedFileExtensions { get; set; } =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".csv", ".xml", ".json"
    };

    #region UI Configuration

    /// <summary>
    /// The CSS framework to use for form rendering.
    /// Default: Bootstrap5
    /// </summary>
    public CssFrameworkOption CssFramework { get; set; } = CssFrameworkOption.Bootstrap5;

    /// <summary>
    /// Enable dependent picklist JavaScript handling.
    /// When enabled, includes JavaScript that automatically updates dependent
    /// picklist options based on controlling field values.
    /// Default: true
    /// </summary>
    public bool EnableDependentPicklists { get; set; } = true;

    /// <summary>
    /// Enable lookup field autocomplete JavaScript.
    /// When enabled, includes JavaScript that powers the AJAX search
    /// functionality for lookup fields.
    /// Default: true
    /// </summary>
    public bool EnableLookupAutocomplete { get; set; } = true;

    /// <summary>
    /// Custom path to override the default salesforce-core.js script.
    /// Leave null to use the embedded script.
    /// Default: null
    /// </summary>
    public string? CustomScriptPath { get; set; }

    /// <summary>
    /// Custom path to override the default salesforce-core.css stylesheet.
    /// Leave null to use the embedded stylesheet.
    /// Default: null
    /// </summary>
    public string? CustomStylePath { get; set; }

    /// <summary>
    /// Toast notification position on the screen.
    /// Default: TopRight
    /// </summary>
    public ToastPosition ToastPosition { get; set; } = ToastPosition.TopRight;

    /// <summary>
    /// Seconds before toast notifications auto-dismiss.
    /// Set to 0 to disable auto-dismiss.
    /// Default: 5 seconds
    /// </summary>
    public int ToastAutoDismissSeconds { get; set; } = 5;

    /// <summary>
    /// Whether toast notifications can be manually closed.
    /// Default: true
    /// </summary>
    public bool ToastClosable { get; set; } = true;

    /// <summary>
    /// Default number of columns for sf-model-form layout.
    /// Default: 1
    /// </summary>
    public int DefaultFormColumns { get; set; } = 1;

    /// <summary>
    /// Enable mutation observer for dynamically loaded content.
    /// When enabled, automatically initializes Salesforce components
    /// added to the DOM after page load (e.g., via AJAX).
    /// Default: true
    /// </summary>
    public bool EnableDynamicSupport { get; set; } = true;

    /// <summary>
    /// Minimum characters required before lookup search triggers.
    /// Default: 2
    /// </summary>
    public int LookupMinChars { get; set; } = 2;

    /// <summary>
    /// Debounce delay in milliseconds for lookup search input.
    /// Default: 300
    /// </summary>
    public int LookupDebounceMs { get; set; } = 300;

    #endregion

    #region Validation

    /// <summary>
    /// Enable automatic Salesforce schema validation on forms.
    /// Default: true
    /// </summary>
    public bool EnableSchemaValidation { get; set; } = true;

    /// <summary>
    /// Enable custom validation rules.
    /// Default: true
    /// </summary>
    public bool EnableCustomValidation { get; set; } = true;

    /// <summary>
    /// Show validation summary at the top of forms.
    /// Default: true
    /// </summary>
    public bool ShowValidationSummary { get; set; } = true;

    #endregion
}
