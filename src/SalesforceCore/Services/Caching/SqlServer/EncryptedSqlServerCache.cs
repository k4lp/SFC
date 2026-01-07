using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Infrastructure.Processing;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// SQL Server-based distributed cache with mandatory AES-256-GCM encryption.
/// Implements IDistributedCache for seamless integration with existing infrastructure.
/// Designed for government-grade deployments requiring encryption at rest and full audit logging.
/// </summary>
/// <remarks>
/// <para>
/// Security features (all mandatory, cannot be disabled):
/// - AES-256-GCM encryption for all cached values
/// - Unique nonce (IV) generated for each write operation
/// - Authentication tag ensures data integrity
/// - Key derivation using PBKDF2 with SHA-512 when using connection string
/// </para>
/// <para>
/// Audit logging (full, cannot be disabled):
/// - All cache operations are logged with structured data
/// - Includes: operation type, key, size, timing, success/failure
/// - Access counts and timestamps maintained for compliance
/// </para>
/// <para>
/// Performance characteristics:
/// - Read latency: ~5-20ms (vs ~1ms for Redis)
/// - Write latency: ~10-30ms (vs ~1ms for Redis)
/// - Suitable for moderate traffic applications
/// - Survives application pool recycles
/// </para>
/// </remarks>
public class EncryptedSqlServerCache : IDistributedCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EncryptedSqlServerCache> _logger;
    private readonly byte[] _encryptionKey;
    private readonly string _keyPrefix;
    private readonly TimeProvider _timeProvider;
    private readonly IBatchProcessor<CacheAccessEvent>? _accessBatchProcessor;
    private readonly SqlCacheWriteBehindOptions _writeBehindOptions;

    private long _lastWriteBehindOverflowLogTimestamp;
    private static readonly TimeSpan WriteBehindOverflowLogInterval = TimeSpan.FromSeconds(30);

    // AES-256-GCM constants
    private const int NonceSize = 12;  // 96 bits for GCM
    private const int TagSize = 16;    // 128 bits for GCM auth tag
    private const int KeySize = 32;    // 256 bits for AES-256

    // PBKDF2 constants for key derivation
    private const int Pbkdf2Iterations = 100000;
    private static readonly byte[] Pbkdf2Salt = Encoding.UTF8.GetBytes("SalesforceCore.EncryptedSqlServerCache.v1");

    /// <summary>
    /// Creates a new EncryptedSqlServerCache instance.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for DbContext creation.</param>
    /// <param name="options">Salesforce options containing cache configuration.</param>
    /// <param name="logger">Logger for audit and diagnostic logging.</param>
    /// <param name="accessBatchProcessor">Batch processor for background access tracking.</param>
    /// <param name="timeProvider">Optional time provider for testing.</param>
    public EncryptedSqlServerCache(
        IServiceScopeFactory scopeFactory,
        IOptions<SalesforceOptions> options,
        ILogger<EncryptedSqlServerCache> logger,
        IBatchProcessor<CacheAccessEvent>? accessBatchProcessor = null,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _accessBatchProcessor = accessBatchProcessor;

        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _keyPrefix = opts.CacheKeyPrefix;
        _writeBehindOptions = opts.SqlCacheWriteBehind;

        // Derive or use explicit encryption key
        _encryptionKey = DeriveEncryptionKey(opts);

        _logger.LogInformation(
            "EncryptedSqlServerCache initialized. KeyPrefix: {KeyPrefix}, KeySource: {KeySource}",
            _keyPrefix,
            string.IsNullOrEmpty(opts.SqlCacheEncryptionKey)
                ? (opts.AllowInsecureSqlCacheKeyDerivation ? "Derived(Insecure)" : "Missing")
                : "Explicit");
    }

    /// <summary>
    /// Derives the encryption key from configuration.
    /// </summary>
    private static byte[] DeriveEncryptionKey(SalesforceOptions options)
    {
        if (!string.IsNullOrEmpty(options.SqlCacheEncryptionKey))
        {
            // Use explicit key from configuration (recommended for production)
            var key = Convert.FromBase64String(options.SqlCacheEncryptionKey);
            if (key.Length != KeySize)
            {
                throw new InvalidOperationException(
                    $"SqlCacheEncryptionKey must be exactly {KeySize} bytes (256 bits) when base64-decoded. " +
                    $"Received {key.Length} bytes. Generate a key using: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))");
            }
            return key;
        }

        if (!options.AllowInsecureSqlCacheKeyDerivation)
        {
            throw new InvalidOperationException(
                "SqlCacheEncryptionKey is required when using the encrypted SQL Server cache. " +
                "Generate one with: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) " +
                "and store it in a secure vault. " +
                "For development only, you may set AllowInsecureSqlCacheKeyDerivation=true to derive a deterministic key.");
        }

        // Derive key from non-secret inputs (development-only fallback; insecure for production).
        var derivationInput = $"{options.SqlCacheConnectionStringName}:{options.CacheKeyPrefix}";
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(derivationInput),
            Pbkdf2Salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA512,
            KeySize);
    }

    /// <inheritdoc/>
    public byte[]? Get(string key)
    {
        return GetAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var fullKey = GetFullKey(key);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

            var now = _timeProvider.GetUtcNow();

            var entry = await context.CacheEntries
                .FirstOrDefaultAsync(e => e.Id == fullKey && e.ExpiresAtTime > now, token);

            if (entry == null)
            {
                LogCacheOperation("GET", fullKey, false, 0, startTime, "Miss");
                return null;
            }

            // Decrypt the value
            var decryptedValue = Decrypt(entry.EncryptedValue);

            // Write-behind access tracking + (optional) sliding expiration refresh.
            await TrackAccessAsync(context, entry, now, token);

            LogCacheOperation("GET", fullKey, true, decryptedValue.Length, startTime, "Hit");

            return decryptedValue;
        }
        catch (Exception ex)
        {
            LogCacheOperation("GET", fullKey, false, 0, startTime, $"Error: {ex.Message}");
            _logger.LogError(ex, "Failed to get value from encrypted SQL cache for key {Key}", fullKey);
            return null;
        }
    }

    /// <inheritdoc/>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        SetAsync(key, value, options).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var fullKey = GetFullKey(key);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

            var now = _timeProvider.GetUtcNow();
            var (expiresAt, slidingSeconds, absoluteExpiration) = CalculateExpiration(options, now);

            // Encrypt the value
            var encryptedValue = Encrypt(value);
            await UpsertAsync(
                context,
                fullKey,
                encryptedValue,
                expiresAt,
                slidingSeconds,
                absoluteExpiration,
                now,
                value.Length,
                token);

            LogCacheOperation("SET", fullKey, true, value.Length, startTime,
                $"Expires: {expiresAt:O}, Sliding: {slidingSeconds?.ToString() ?? "None"}");
        }
        catch (Exception ex)
        {
            LogCacheOperation("SET", fullKey, false, value.Length, startTime, $"Error: {ex.Message}");
            _logger.LogError(ex, "Failed to set value in encrypted SQL cache for key {Key}", fullKey);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Refresh(string key)
    {
        RefreshAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
	    public async Task RefreshAsync(string key, CancellationToken token = default)
	    {
	        ArgumentNullException.ThrowIfNull(key);

        var fullKey = GetFullKey(key);
        var startTime = _timeProvider.GetTimestamp();

	        try
	        {
	            using var scope = _scopeFactory.CreateScope();
	            var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

	            var now = _timeProvider.GetUtcNow();

	            var entry = await context.CacheEntries
	                .Where(e => e.Id == fullKey && e.ExpiresAtTime > now)
	                .Select(e => new
	                {
	                    e.Id,
	                    e.ExpiresAtTime,
	                    e.SlidingExpirationInSeconds,
	                    e.AbsoluteExpiration
	                })
	                .FirstOrDefaultAsync(token);

	            if (entry == null)
	            {
	                LogCacheOperation("REFRESH", fullKey, false, 0, startTime, "Not found");
	                return;
	            }

	            if (entry.SlidingExpirationInSeconds.HasValue)
	            {
	                var newExpiry = now.AddSeconds(entry.SlidingExpirationInSeconds.Value);

	                // Don't extend past absolute expiration
	                if (entry.AbsoluteExpiration.HasValue && newExpiry > entry.AbsoluteExpiration.Value)
	                {
	                    newExpiry = entry.AbsoluteExpiration.Value;
	                }

	                await context.CacheEntries
	                    .Where(e => e.Id == entry.Id && e.ExpiresAtTime > now)
	                    .ExecuteUpdateAsync(
	                        s => s
	                            .SetProperty(e => e.ExpiresAtTime, newExpiry)
	                            .SetProperty(e => e.LastAccessedAt, now)
	                            .SetProperty(e => e.AccessCount, e => e.AccessCount + 1),
	                        token);

	                LogCacheOperation("REFRESH", fullKey, true, 0, startTime, $"New expiry: {newExpiry:O}");
	            }
	            else
	            {
	                LogCacheOperation("REFRESH", fullKey, true, 0, startTime, "No sliding expiration");
            }
        }
        catch (Exception ex)
        {
            LogCacheOperation("REFRESH", fullKey, false, 0, startTime, $"Error: {ex.Message}");
            _logger.LogError(ex, "Failed to refresh encrypted SQL cache entry for key {Key}", fullKey);
        }
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        RemoveAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var fullKey = GetFullKey(key);
        var startTime = _timeProvider.GetTimestamp();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

            var deletedCount = await context.CacheEntries
                .Where(e => e.Id == fullKey)
                .ExecuteDeleteAsync(token);

            LogCacheOperation("REMOVE", fullKey, true, 0, startTime,
                deletedCount > 0 ? "Deleted" : "Not found");
        }
        catch (Exception ex)
        {
            LogCacheOperation("REMOVE", fullKey, false, 0, startTime, $"Error: {ex.Message}");
            _logger.LogError(ex, "Failed to remove encrypted SQL cache entry for key {Key}", fullKey);
        }
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <returns>Encrypted data: [12-byte nonce][ciphertext][16-byte tag]</returns>
    private byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Combine: nonce + ciphertext + tag
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return result;
    }

    /// <summary>
    /// Decrypts data encrypted with AES-256-GCM.
    /// </summary>
    /// <param name="encryptedData">Encrypted data: [12-byte nonce][ciphertext][16-byte tag]</param>
    /// <returns>Decrypted plaintext.</returns>
    private byte[] Decrypt(byte[] encryptedData)
    {
        if (encryptedData.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted data is too short to be valid.");
        }

        var nonce = new byte[NonceSize];
        var ciphertextLength = encryptedData.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var tag = new byte[TagSize];

        Buffer.BlockCopy(encryptedData, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(encryptedData, NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(encryptedData, NonceSize + ciphertextLength, tag, 0, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    /// <summary>
    /// Calculates expiration times from options.
    /// </summary>
    private static (DateTimeOffset ExpiresAt, long? SlidingSeconds, DateTimeOffset? AbsoluteExpiration)
        CalculateExpiration(DistributedCacheEntryOptions options, DateTimeOffset now)
    {
        DateTimeOffset? absoluteExpiration = null;
        long? slidingSeconds = null;

        if (options.AbsoluteExpiration.HasValue)
        {
            absoluteExpiration = options.AbsoluteExpiration.Value;
        }
        else if (options.AbsoluteExpirationRelativeToNow.HasValue)
        {
            absoluteExpiration = now.Add(options.AbsoluteExpirationRelativeToNow.Value);
        }

        if (options.SlidingExpiration.HasValue)
        {
            slidingSeconds = (long)options.SlidingExpiration.Value.TotalSeconds;
        }

        // Calculate initial expiration
        DateTimeOffset expiresAt;
        if (slidingSeconds.HasValue)
        {
            expiresAt = now.AddSeconds(slidingSeconds.Value);
            if (absoluteExpiration.HasValue && expiresAt > absoluteExpiration)
            {
                expiresAt = absoluteExpiration.Value;
            }
        }
        else if (absoluteExpiration.HasValue)
        {
            expiresAt = absoluteExpiration.Value;
        }
        else
        {
            // Default: 1 hour if nothing specified
            expiresAt = now.AddHours(1);
            absoluteExpiration = expiresAt;
        }

        return (expiresAt, slidingSeconds, absoluteExpiration);
    }

    /// <summary>
    /// Updates access tracking for audit purposes.
    /// </summary>
    private async Task UpdateAccessTrackingAsync(
        EncryptedSqlServerCacheDbContext context,
        EncryptedCacheEntry entry,
        DateTimeOffset now,
        CancellationToken token)
    {
        try
        {
            await context.CacheEntries
                .Where(e => e.Id == entry.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.LastAccessedAt, now)
                    .SetProperty(e => e.AccessCount, e => e.AccessCount + 1),
                    token);
        }
        catch (Exception ex)
        {
            // Log but don't fail the read operation
            _logger.LogWarning(ex, "Failed to update access tracking for key {Key}", entry.Id);
        }
    }

    /// <summary>
    /// Refreshes sliding expiration for a cache entry.
    /// </summary>
    private async Task RefreshSlidingExpirationAsync(
        EncryptedSqlServerCacheDbContext context,
        EncryptedCacheEntry entry,
        DateTimeOffset now,
        CancellationToken token)
    {
        if (!entry.SlidingExpirationInSeconds.HasValue)
            return;

        try
        {
            var newExpiry = now.AddSeconds(entry.SlidingExpirationInSeconds.Value);

            // Don't extend past absolute expiration
            if (entry.AbsoluteExpiration.HasValue && newExpiry > entry.AbsoluteExpiration.Value)
            {
                newExpiry = entry.AbsoluteExpiration.Value;
            }

            // Only update if significantly different (avoid constant updates)
            var thresholdSeconds = GetEffectiveSlidingExpirationRefreshThresholdSeconds(entry.SlidingExpirationInSeconds.Value);
            if ((newExpiry - entry.ExpiresAtTime).TotalSeconds > thresholdSeconds)
            {
                await context.CacheEntries
                    .Where(e => e.Id == entry.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.ExpiresAtTime, newExpiry),
                        token);
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail the read operation
            _logger.LogWarning(ex, "Failed to refresh sliding expiration for key {Key}", entry.Id);
        }
    }

    /// <summary>
    /// Gets the full cache key with prefix.
    /// </summary>
    private string GetFullKey(string key)
    {
        return string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}{key}";
    }

    private async Task TrackAccessAsync(
        EncryptedSqlServerCacheDbContext context,
        EncryptedCacheEntry entry,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_writeBehindOptions.Enabled || _accessBatchProcessor == null)
        {
            // Safe (awaited) fallback for environments where write-behind is disabled.
            await UpdateAccessTrackingAsync(context, entry, now, cancellationToken);
            if (entry.SlidingExpirationInSeconds.HasValue)
            {
                await RefreshSlidingExpirationAsync(context, entry, now, cancellationToken);
            }
            return;
        }

        DateTimeOffset? newExpiresAtTime = null;
        var requiresImmediateExpiryRefresh = false;
        if (entry.SlidingExpirationInSeconds.HasValue)
        {
            var slidingSeconds = entry.SlidingExpirationInSeconds.Value;
            var proposed = now.AddSeconds(slidingSeconds);
            if (entry.AbsoluteExpiration.HasValue && proposed > entry.AbsoluteExpiration.Value)
            {
                proposed = entry.AbsoluteExpiration.Value;
            }

            var thresholdSeconds = GetEffectiveSlidingExpirationRefreshThresholdSeconds(slidingSeconds);
            if ((proposed - entry.ExpiresAtTime).TotalSeconds > thresholdSeconds)
            {
                newExpiresAtTime = proposed;
            }

            // Correctness guard: when a sliding entry is close to expiry, a delayed write-behind flush can
            // cause the entry to expire between requests (e.g., server-side auth tickets). In that case we
            // refresh expiry synchronously (best-effort) while still buffering audit metadata.
            if (proposed > entry.ExpiresAtTime && (entry.ExpiresAtTime - now) <= GetWriteBehindSynchronousRefreshWindow())
            {
                requiresImmediateExpiryRefresh = true;
                newExpiresAtTime = proposed;
            }
        }

        var accessEvent = new CacheAccessEvent(
            entry.Id,
            now,
            AccessCountDelta: 1,
            newExpiresAtTime);

        var enqueued = _accessBatchProcessor.TryEnqueue(accessEvent);

        if (requiresImmediateExpiryRefresh && newExpiresAtTime.HasValue)
        {
            try
            {
                await context.CacheEntries
                    .Where(e => e.Id == entry.Id && e.ExpiresAtTime < newExpiresAtTime.Value)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(e => e.ExpiresAtTime, newExpiresAtTime.Value),
                        cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE_AUDIT: Failed to apply synchronous sliding expiration refresh for key {Key}", entry.Id);
            }
        }

        if (enqueued)
        {
            return;
        }

        // Overflow: keep request path safe and avoid log floods.
        if (ShouldLogWriteBehindOverflow())
        {
            _logger.LogWarning(
                "CACHE_AUDIT: SQL cache write-behind buffer is full. " +
                "Access updates are being throttled; sliding expiration may fall back to minimal sync refresh.");
        }

        // Preserve correctness for sliding expiration: if the entry needs an expiry extension and
        // we couldn't enqueue, perform a minimal synchronous expiry update (best-effort).
        if (newExpiresAtTime.HasValue)
        {
            try
            {
                await context.CacheEntries
                    .Where(e => e.Id == entry.Id && e.ExpiresAtTime < newExpiresAtTime.Value)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(e => e.ExpiresAtTime, newExpiresAtTime.Value),
                        cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CACHE_AUDIT: Failed to apply fallback sliding expiration refresh for key {Key}", entry.Id);
            }
        }
    }

    private int GetEffectiveSlidingExpirationRefreshThresholdSeconds(long slidingSeconds)
    {
        // Clamp configured threshold so it never exceeds the sliding window; otherwise sliding entries
        // can silently stop refreshing (and expire) for small sliding durations.
        var configured = Math.Max(0, _writeBehindOptions.SlidingExpirationRefreshThresholdSeconds);
        if (configured == 0)
        {
            return 0;
        }

        if (slidingSeconds <= 1)
        {
            return 0;
        }

        var halfWindow = (int)Math.Max(1, Math.Min(int.MaxValue, slidingSeconds / 2));
        return Math.Min(configured, halfWindow);
    }

    private TimeSpan GetWriteBehindSynchronousRefreshWindow()
    {
        // Conservative: require at least one flush interval of headroom to avoid expiry before the batch write.
        // Using 2x flush interval avoids dependence on timer alignment.
        var twiceFlush = TimeSpan.FromTicks(_writeBehindOptions.FlushInterval.Ticks * 2);
        return twiceFlush <= TimeSpan.Zero ? _writeBehindOptions.FlushInterval : twiceFlush;
    }

    private bool ShouldLogWriteBehindOverflow()
    {
        var nowTimestamp = _timeProvider.GetTimestamp();
        var last = Interlocked.Read(ref _lastWriteBehindOverflowLogTimestamp);

        if (last != 0 && _timeProvider.GetElapsedTime(last) < WriteBehindOverflowLogInterval)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _lastWriteBehindOverflowLogTimestamp, nowTimestamp, last) == last;
    }

    private static async Task UpsertAsync(
        EncryptedSqlServerCacheDbContext context,
        string id,
        byte[] encryptedValue,
        DateTimeOffset expiresAt,
        long? slidingExpirationInSeconds,
        DateTimeOffset? absoluteExpiration,
        DateTimeOffset now,
        long originalSize,
        CancellationToken cancellationToken)
    {
        var entityType = context.Model.FindEntityType(typeof(EncryptedCacheEntry));
        var tableName = entityType?.GetTableName() ?? "SalesforceEncryptedCacheEntries";
        var schema = entityType?.GetSchema();
        var qualifiedTable = string.IsNullOrWhiteSpace(schema)
            ? $"[{tableName}]"
            : $"[{schema}].[{tableName}]";

        var sql = $@"
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

UPDATE {qualifiedTable} WITH (UPDLOCK, SERIALIZABLE)
SET
    EncryptedValue = @EncryptedValue,
    ExpiresAtTime = @ExpiresAtTime,
    SlidingExpirationInSeconds = @SlidingExpirationInSeconds,
    AbsoluteExpiration = @AbsoluteExpiration,
    LastAccessedAt = @LastAccessedAt,
    OriginalSize = @OriginalSize
WHERE Id = @Id;

IF @@ROWCOUNT = 0
BEGIN
    INSERT INTO {qualifiedTable}
        (Id, EncryptedValue, ExpiresAtTime, SlidingExpirationInSeconds, AbsoluteExpiration, CreatedAt, LastAccessedAt, AccessCount, OriginalSize)
    VALUES
        (@Id, @EncryptedValue, @ExpiresAtTime, @SlidingExpirationInSeconds, @AbsoluteExpiration, @CreatedAt, @LastAccessedAt, 0, @OriginalSize);
END

COMMIT;
";

        var parameters = new object[]
        {
            new Microsoft.Data.SqlClient.SqlParameter("@Id", System.Data.SqlDbType.NVarChar, 449) { Value = id },
            new Microsoft.Data.SqlClient.SqlParameter("@EncryptedValue", System.Data.SqlDbType.VarBinary, -1) { Value = encryptedValue },
            new Microsoft.Data.SqlClient.SqlParameter("@ExpiresAtTime", System.Data.SqlDbType.DateTimeOffset) { Value = expiresAt },
            new Microsoft.Data.SqlClient.SqlParameter("@SlidingExpirationInSeconds", System.Data.SqlDbType.BigInt) { Value = (object?)slidingExpirationInSeconds ?? DBNull.Value },
            new Microsoft.Data.SqlClient.SqlParameter("@AbsoluteExpiration", System.Data.SqlDbType.DateTimeOffset) { Value = (object?)absoluteExpiration ?? DBNull.Value },
            new Microsoft.Data.SqlClient.SqlParameter("@CreatedAt", System.Data.SqlDbType.DateTimeOffset) { Value = now },
            new Microsoft.Data.SqlClient.SqlParameter("@LastAccessedAt", System.Data.SqlDbType.DateTimeOffset) { Value = now },
            new Microsoft.Data.SqlClient.SqlParameter("@OriginalSize", System.Data.SqlDbType.BigInt) { Value = originalSize },
        };

        await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    /// <summary>
    /// Logs a cache operation for audit purposes.
    /// </summary>
    private void LogCacheOperation(
        string operation,
        string key,
        bool success,
        long sizeBytes,
        long startTimestamp,
        string details)
    {
        var elapsed = _timeProvider.GetElapsedTime(startTimestamp);

        _logger.LogInformation(
            "CACHE_AUDIT: Operation={Operation}, Key={Key}, Success={Success}, " +
            "SizeBytes={SizeBytes}, ElapsedMs={ElapsedMs:F2}, Details={Details}",
            operation,
            key,
            success,
            sizeBytes,
            elapsed.TotalMilliseconds,
            details);
    }
}
