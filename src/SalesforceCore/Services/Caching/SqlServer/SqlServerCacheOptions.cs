namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// Configuration options for the encrypted SQL Server cache.
/// These options are used when calling AddSalesforceEncryptedSqlServerCache().
/// </summary>
/// <remarks>
/// <para>
/// Security notes:
/// - EncryptionKey should be loaded from a secure vault (Azure Key Vault, AWS Secrets Manager, etc.)
/// - Never store the encryption key in source code or configuration files
/// - The encryption key must be exactly 32 bytes (256 bits) when base64-decoded
/// </para>
/// <para>
/// For government deployments:
/// - Always provide an explicit EncryptionKey from a FIPS 140-2 compliant key management system
/// - Enable SQL Server Transparent Data Encryption (TDE) for defense in depth
/// - Configure appropriate retry and timeout values for your SLA requirements
/// </para>
/// </remarks>
public class SqlServerCacheOptions
{
    /// <summary>
    /// The connection string for the SQL Server database.
    /// Required.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The base64-encoded 256-bit (32-byte) encryption key.
    /// For production deployments, always specify an explicit key from a secure vault.
    /// </summary>
    /// <remarks>
    /// Generate a secure key using:
    /// <code>
    /// var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    /// </code>
    /// </remarks>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Allows deriving the encryption key from non-secret inputs (development-only escape hatch).
    /// Default: false.
    /// </summary>
    public bool AllowInsecureKeyDerivation { get; set; } = false;

    /// <summary>
    /// Table name for cache entries.
    /// Default: "SalesforceEncryptedCacheEntries"
    /// </summary>
    public string TableName { get; set; } = "SalesforceEncryptedCacheEntries";

    /// <summary>
    /// Schema name for cache table.
    /// Default: "dbo"
    /// </summary>
    public string SchemaName { get; set; } = "dbo";

    /// <summary>
    /// Cache key prefix to avoid collisions.
    /// Default: "SF_"
    /// </summary>
    public string KeyPrefix { get; set; } = "SF_";

    /// <summary>
    /// Interval for cleaning up expired cache entries.
    /// Default: 30 minutes
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Enables write-behind buffering for read-path metadata updates (access count, last accessed,
    /// sliding expiration refresh). Default: true.
    /// </summary>
    public bool WriteBehindEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of queued access events. Default: 50,000.
    /// </summary>
    public int WriteBehindCapacity { get; set; } = 50_000;

    /// <summary>
    /// Maximum number of distinct keys updated in a single batch. Default: 500.
    /// </summary>
    public int WriteBehindMaxBatchSize { get; set; } = 500;

    /// <summary>
    /// Flush interval for buffered updates. Default: 5 seconds.
    /// </summary>
    public TimeSpan WriteBehindFlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Minimum extension required before persisting a sliding expiration refresh. Default: 60 seconds.
    /// </summary>
    public int SlidingExpirationRefreshThresholdSeconds { get; set; } = 60;

    /// <summary>
    /// Grace period for cleanup to avoid deleting entries before buffered refresh flush. Default: 10 seconds.
    /// </summary>
    public TimeSpan CleanupGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum retry attempts for transient database failures.
    /// Default: 3
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Maximum delay between retries for transient failures.
    /// Default: 5 seconds
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Command timeout for database operations.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to automatically create the cache table if it doesn't exist.
    /// Default: true
    /// Set to false in production if you prefer to manage schema migrations manually.
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;

    /// <summary>
    /// Whether to enable detailed logging for debugging.
    /// Default: false
    /// Warning: Enabling this may log sensitive cache keys.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}
