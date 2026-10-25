using Microsoft.EntityFrameworkCore;

namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// DbContext for the encrypted SQL Server-based distributed cache.
/// This is a dedicated, lightweight context specifically for cache operations.
/// Designed for government-grade deployments requiring encryption at rest.
/// </summary>
/// <remarks>
/// <para>
/// This context is intentionally separate from any application DbContext to:
/// - Minimize coupling with application data models
/// - Allow independent connection string and configuration
/// - Enable cache operations without affecting application transactions
/// - Support different database instances for cache vs application data
/// </para>
/// <para>
/// Security features:
/// - Table name prefixed with "Salesforce" to avoid conflicts
/// - Index on ExpiresAtTime for efficient cleanup queries
/// - RowVersion for optimistic concurrency control
/// - All values are encrypted before reaching this context
/// </para>
/// </remarks>
public class EncryptedSqlServerCacheDbContext : DbContext
{
    /// <summary>
    /// Creates a new instance of the cache DbContext.
    /// </summary>
    /// <param name="options">DbContext options including connection string.</param>
    public EncryptedSqlServerCacheDbContext(DbContextOptions<EncryptedSqlServerCacheDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Cache entries table.
    /// </summary>
    public DbSet<EncryptedCacheEntry> CacheEntries => Set<EncryptedCacheEntry>();

    /// <summary>
    /// Configures the cache entry entity mapping.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EncryptedCacheEntry>(entity =>
        {
            // Table name - prefixed with Salesforce for clarity
            entity.ToTable("SalesforceEncryptedCacheEntries");

            // Primary key on Id (cache key)
            entity.HasKey(e => e.Id);

            // Id: max 449 chars (SQL Server clustered index key size limit)
            entity.Property(e => e.Id)
                .HasMaxLength(449)
                .IsRequired()
                .IsUnicode(true);

            // EncryptedValue: required binary data (VARBINARY(MAX))
            entity.Property(e => e.EncryptedValue)
                .IsRequired()
                .HasColumnType("VARBINARY(MAX)");

            // ExpiresAtTime: required for expiration queries
            entity.Property(e => e.ExpiresAtTime)
                .IsRequired()
                .HasColumnType("DATETIMEOFFSET(7)");

            // SlidingExpirationInSeconds: optional
            entity.Property(e => e.SlidingExpirationInSeconds)
                .HasColumnType("BIGINT");

            // AbsoluteExpiration: optional
            entity.Property(e => e.AbsoluteExpiration)
                .HasColumnType("DATETIMEOFFSET(7)");

            // CreatedAt: audit timestamp
            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasColumnType("DATETIMEOFFSET(7)");

            // LastAccessedAt: audit timestamp
            entity.Property(e => e.LastAccessedAt)
                .IsRequired()
                .HasColumnType("DATETIMEOFFSET(7)");

            // AccessCount: audit counter
            entity.Property(e => e.AccessCount)
                .IsRequired()
                .HasColumnType("BIGINT")
                .HasDefaultValue(0);

            // OriginalSize: size tracking
            entity.Property(e => e.OriginalSize)
                .IsRequired()
                .HasColumnType("BIGINT")
                .HasDefaultValue(0);

            // RowVersion: optimistic concurrency
            entity.Property(e => e.RowVersion)
                .IsRowVersion()
                .IsConcurrencyToken();

            // Index for efficient expired entry cleanup
            entity.HasIndex(e => e.ExpiresAtTime)
                .HasDatabaseName("IX_SalesforceEncryptedCacheEntries_ExpiresAtTime");

            // Index for audit queries by access time
            entity.HasIndex(e => e.LastAccessedAt)
                .HasDatabaseName("IX_SalesforceEncryptedCacheEntries_LastAccessedAt");

            // Index for audit queries by creation time
            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("IX_SalesforceEncryptedCacheEntries_CreatedAt");
        });
    }
}
