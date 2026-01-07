namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// Entity representing an encrypted cache entry stored in SQL Server.
/// All values are encrypted using AES-256-GCM before storage.
/// This entity is designed for government-grade security requirements.
/// </summary>
/// <remarks>
/// <para>
/// Security features:
/// - All cache values are encrypted at rest using AES-256-GCM
/// - Nonce (IV) is stored alongside the encrypted data for decryption
/// - Authentication tag ensures data integrity
/// - No plaintext data is ever stored in the database
/// </para>
/// <para>
/// Audit features:
/// - CreatedAt timestamp for audit trail
/// - LastAccessedAt for sliding expiration and audit
/// - AccessCount for usage analytics
/// </para>
/// </remarks>
public class EncryptedCacheEntry
{
    /// <summary>
    /// Cache key (primary key).
    /// Maximum length 449 characters (SQL Server clustered index key size limit).
    /// The key itself is NOT encrypted as it's needed for lookups.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Encrypted cache value as binary data.
    /// Contains: [12-byte nonce][encrypted data][16-byte auth tag]
    /// Encrypted using AES-256-GCM.
    /// </summary>
    public byte[] EncryptedValue { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Absolute expiration time (UTC).
    /// Entry is invalid after this time.
    /// </summary>
    public DateTimeOffset ExpiresAtTime { get; set; }

    /// <summary>
    /// Sliding expiration in seconds.
    /// If set, ExpiresAtTime is extended on each access.
    /// Null means no sliding expiration.
    /// </summary>
    public long? SlidingExpirationInSeconds { get; set; }

    /// <summary>
    /// Absolute expiration limit (UTC).
    /// Sliding expiration cannot extend beyond this time.
    /// Null means no absolute limit (only sliding expiration applies).
    /// </summary>
    public DateTimeOffset? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Timestamp when this entry was created (UTC).
    /// Used for audit logging and compliance reporting.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when this entry was last accessed (UTC).
    /// Updated on every read operation for audit purposes.
    /// </summary>
    public DateTimeOffset LastAccessedAt { get; set; }

    /// <summary>
    /// Number of times this entry has been accessed.
    /// Used for cache analytics and audit reporting.
    /// </summary>
    public long AccessCount { get; set; }

    /// <summary>
    /// Size of the original unencrypted value in bytes.
    /// Used for cache size monitoring and capacity planning.
    /// </summary>
    public long OriginalSize { get; set; }

    /// <summary>
    /// Optimistic concurrency token.
    /// Prevents race conditions during updates.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
