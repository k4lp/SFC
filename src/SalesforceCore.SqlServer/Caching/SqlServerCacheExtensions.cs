using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Infrastructure.Locking;
using SalesforceCore.Infrastructure.Locking.SqlServer;
using SalesforceCore.Infrastructure.Processing;

namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>
/// Extension methods for configuring the encrypted SQL Server cache.
/// </summary>
public static class SqlServerCacheExtensions
{
    /// <summary>
    /// Adds encrypted SQL Server-based distributed cache for SalesforceCore.
    /// This cache implementation provides AES-256-GCM encryption for all cached data.
    /// The cache table is automatically created on first use - NO SETUP REQUIRED.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration containing connection string and Salesforce settings.</param>
    /// <param name="connectionStringName">
    /// Name of the connection string in configuration.
    /// Default: "DefaultConnection"
    /// </param>
    /// <returns>Service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method configures SQL Server-based caching with mandatory encryption for environments
    /// where Redis is not available or where encryption at rest is required.
    /// </para>
    /// <para>
    /// Usage - Just two lines, same as Redis:
    /// <code>
    /// // In Program.cs (ASP.NET Core)
    /// builder.Services.AddSalesforceEncryptedSqlServerCache(builder.Configuration);
    /// builder.Services.AddSalesforceCore(builder.Configuration);
    /// // That's it! Table is auto-created on startup.
    ///
    /// // In Program.cs (Console App)
    /// services.AddSalesforceEncryptedSqlServerCache(configuration);
    /// services.AddSalesforceCore(configuration);
    /// // That's it! Table is auto-created on startup.
    /// </code>
    /// </para>
    /// <para>
    /// Configuration (appsettings.json):
    /// <code>
    /// {
    ///   "ConnectionStrings": {
    ///     "DefaultConnection": "Server=...;Database=...;..."
    ///   },
    ///   "Salesforce": {
    ///     "CacheProvider": "SqlServer",
    ///     "SqlCacheEncryptionKey": "base64-encoded-32-byte-key",
    ///     "CacheCleanupInterval": "00:30:00"
    ///   }
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the connection string is not found in configuration.
    /// </exception>
    public static IServiceCollection AddSalesforceEncryptedSqlServerCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection")
    {
        var connectionString = configuration.GetConnectionString(connectionStringName);

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{connectionStringName}' not found in configuration. " +
                "Encrypted SQL Server cache requires a valid connection string. " +
                "Add a ConnectionStrings section to your configuration.");
        }

        // Bind Salesforce options for cache-specific settings
        services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));

        // Update options with connection string name
        services.PostConfigure<SalesforceOptions>(options =>
        {
            options.SqlCacheConnectionStringName = connectionStringName;
            options.CacheProvider = CacheProviderType.SqlServer;
        });

        return AddSalesforceEncryptedSqlServerCacheCore(services, connectionString);
    }

    /// <summary>
    /// Adds encrypted SQL Server-based distributed cache with explicit connection string.
    /// The cache table is automatically created on first use - NO SETUP REQUIRED.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="configureOptions">Optional configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Usage:
    /// <code>
    /// services.AddSalesforceEncryptedSqlServerCache(
    ///     "Server=...;Database=...;...",
    ///     options =>
    ///     {
    ///         options.EncryptionKey = "base64-encoded-32-byte-key";
    ///         options.CleanupInterval = TimeSpan.FromMinutes(15);
    ///     });
    /// // Table is auto-created on startup.
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when connectionString is null or empty.
    /// </exception>
    public static IServiceCollection AddSalesforceEncryptedSqlServerCache(
        this IServiceCollection services,
        string connectionString,
        Action<SqlServerCacheOptions>? configureOptions = null)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentNullException(nameof(connectionString),
                "Encrypted SQL Server cache requires a valid connection string.");
        }

        var options = new SqlServerCacheOptions { ConnectionString = connectionString };
        configureOptions?.Invoke(options);

        // Apply options to SalesforceOptions
        services.Configure<SalesforceOptions>(sfOptions =>
        {
            sfOptions.CacheProvider = CacheProviderType.SqlServer;
            sfOptions.CacheKeyPrefix = options.KeyPrefix;
            sfOptions.CacheCleanupInterval = options.CleanupInterval;
            sfOptions.SqlCacheWriteBehind.Enabled = options.WriteBehindEnabled;
            sfOptions.SqlCacheWriteBehind.Capacity = options.WriteBehindCapacity;
            sfOptions.SqlCacheWriteBehind.MaxBatchSize = options.WriteBehindMaxBatchSize;
            sfOptions.SqlCacheWriteBehind.FlushInterval = options.WriteBehindFlushInterval;
            sfOptions.SqlCacheWriteBehind.SlidingExpirationRefreshThresholdSeconds = options.SlidingExpirationRefreshThresholdSeconds;
            sfOptions.SqlCacheWriteBehind.CleanupGracePeriod = options.CleanupGracePeriod;
            sfOptions.AllowInsecureSqlCacheKeyDerivation = options.AllowInsecureKeyDerivation;
            if (!string.IsNullOrEmpty(options.EncryptionKey))
            {
                sfOptions.SqlCacheEncryptionKey = options.EncryptionKey;
            }
        });

        return AddSalesforceEncryptedSqlServerCacheCore(services, connectionString, options);
    }

    /// <summary>
    /// Core implementation for adding the encrypted SQL Server cache.
    /// </summary>
    private static IServiceCollection AddSalesforceEncryptedSqlServerCacheCore(
        IServiceCollection services,
        string connectionString,
        SqlServerCacheOptions? options = null)
    {
        options ??= new SqlServerCacheOptions { ConnectionString = connectionString };

        // Ensure critical configuration is validated at startup.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>());
        services.AddOptions<SalesforceOptions>().ValidateOnStart();

        // Provide SQL Server-based distributed locking for cross-server coordination.
        // Consumers can override this by registering their own IDistributedLockProvider.
        services.TryAddSingleton<IDistributedLockProvider>(sp =>
            new SqlAppLockProvider(connectionString, sp.GetRequiredService<ILogger<SqlAppLockProvider>>()));

        // Configure batch processor defaults from SalesforceOptions (bound from configuration).
        // This is used by the SQL cache write-behind access tracking.
        services.AddOptions<ChannelBatchProcessorOptions>()
            .Configure<IOptions<SalesforceOptions>>((batchOptions, sfOptions) =>
            {
                var wb = sfOptions.Value.SqlCacheWriteBehind;
                batchOptions.Capacity = wb.Capacity;
                batchOptions.BatchSize = wb.MaxBatchSize;
                batchOptions.FlushInterval = wb.FlushInterval;
            });

        // Register SQL cache access write-behind processor (singleton + hosted service).
        services.TryAddSingleton<IChannelBatchHandler<CacheAccessEvent>, SqlCacheAccessBatchHandler>();
        services.TryAddSingleton<ChannelBatchProcessor<CacheAccessEvent>>();
        services.TryAddSingleton<IBatchProcessor<CacheAccessEvent>>(sp => sp.GetRequiredService<ChannelBatchProcessor<CacheAccessEvent>>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService>(sp =>
            sp.GetRequiredService<ChannelBatchProcessor<CacheAccessEvent>>()));

        // Register the cache DbContext
        services.AddDbContext<EncryptedSqlServerCacheDbContext>(dbOptions =>
        {
            dbOptions.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: options.MaxRetryCount,
                    maxRetryDelay: options.MaxRetryDelay,
                    errorNumbersToAdd: null);

                sqlOptions.CommandTimeout((int)options.CommandTimeout.TotalSeconds);
            });

            // Disable tracking for better performance on cache operations
            dbOptions.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Register the encrypted cache as IDistributedCache
        // This allows seamless integration with existing DistributedCacheProvider
        services.AddSingleton<IDistributedCache, EncryptedSqlServerCache>();

        // Register auto-initialization service - creates table on startup automatically (optional).
        if (options.AutoCreateTable)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EncryptedSqlServerCacheInitializer>());
        }

        // Register cleanup background service
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EncryptedSqlServerCacheCleanupService>());

        return services;
    }

    /// <summary>
    /// Gets statistics about the encrypted SQL Server cache.
    /// Useful for monitoring and capacity planning.
    /// </summary>
    /// <param name="serviceProvider">Service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cache statistics.</returns>
    public static async Task<CacheStatistics> GetSalesforceEncryptedCacheStatisticsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var cleanupService = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheCleanupService>();
        return await cleanupService.GetStatisticsAsync(cancellationToken);
    }
}

/// <summary>
/// Background service that automatically creates the cache table on application startup.
/// This ensures zero-setup experience - just like Redis, no manual table creation needed.
/// </summary>
internal sealed class EncryptedSqlServerCacheInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EncryptedSqlServerCacheInitializer> _logger;

    public EncryptedSqlServerCacheInitializer(
        IServiceScopeFactory scopeFactory,
        ILogger<EncryptedSqlServerCacheInitializer> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("CACHE_AUDIT: Initializing encrypted SQL Server cache table...");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

            // EnsureCreated will create the table if it doesn't exist
            // It's idempotent - safe to call multiple times
            var created = await context.Database.EnsureCreatedAsync(cancellationToken);

            if (created)
            {
                _logger.LogInformation(
                    "CACHE_AUDIT: Encrypted SQL Server cache table created successfully. " +
                    "Table: SalesforceEncryptedCacheEntries");
            }
            else
            {
                _logger.LogInformation(
                    "CACHE_AUDIT: Encrypted SQL Server cache table already exists. " +
                    "Table: SalesforceEncryptedCacheEntries");
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't crash the app
            // The cache operations will fail gracefully if the table doesn't exist
            _logger.LogError(ex,
                "CACHE_AUDIT: Failed to initialize encrypted SQL Server cache table. " +
                "Error: {Error}. " +
                "Cache operations will fail until the table is created. " +
                "You can manually run the migration script: migrations/001_AddSalesforceEncryptedCacheTable.sql",
                ex.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
