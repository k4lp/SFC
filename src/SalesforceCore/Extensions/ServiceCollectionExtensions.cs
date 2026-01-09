using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Security;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Configuration;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Layout;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Reports;
using SalesforceCore.Services.Tooling;
using SalesforceCore.Services.Files;
using SalesforceCore.Validation;
using SalesforceCore.Tracking;
using SalesforceCore.Schema;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SalesforceCore.Extensions;

/// <summary>
/// Extension methods for registering SalesforceCore services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SalesforceCore services with configuration action.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(
        this IServiceCollection services,
        Action<SalesforceOptions> configure)
    {
        // Configure options
        services.Configure(configure);

        // Register core services
        RegisterCoreServices(services, null);

        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration section
        services.Configure<SalesforceOptions>(
            configuration.GetSection(SalesforceOptions.SectionName));

        // Register core services
        RegisterCoreServices(services, configuration);

        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services with default configuration.
    /// Requires Salesforce:ClientId and Salesforce:Domain in configuration.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(this IServiceCollection services)
    {
        // Use empty configuration, expects appsettings.json configuration
        RegisterCoreServices(services, null);
        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services without authentication (for custom token providers).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCoreWithoutAuth(
        this IServiceCollection services,
        Action<SalesforceOptions> configure)
    {
        services.Configure(configure);

        // Fail-fast options validation (especially important for SQL cache encryption settings).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>());
        services.AddOptions<SalesforceOptions>().ValidateOnStart();

        // Get options to determine cache provider
        var options = new SalesforceOptions();
        configure(options);

        // Register cache provider based on configuration
        RegisterCacheProvider(services, options);

        // Register only core services without auth
        services.AddHttpClient<ISalesforceClient, SalesforceClient>();
        services.AddScoped<ISalesforceUrlBuilder, SalesforceUrlBuilder>();
        services.AddScoped<ISchemaService, SchemaService>();
        services.AddScoped<IDataService, DataService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IConfigurationService, ConfigurationService>();
        services.AddScoped<ILimitsService, LimitsService>();

        // Register typed data service for strongly-typed operations with LINQ support
        services.AddScoped<ITypedDataService, TypedDataService>();

        // Register Validation, Tracking, and Schema services
        services.AddScoped<IFieldValidator, FieldValidator>();
        services.AddScoped<IValidationRuleEngine, ValidationRuleEngine>();
        services.AddScoped<IChangeTracker, ChangeTracker>();
        services.AddScoped<IRecordTypeManager, RecordTypeManager>();

        return services;
    }

    /// <summary>
    /// Adds a custom token provider.
    /// </summary>
    /// <typeparam name="TTokenProvider">Token provider implementation type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceTokenProvider<TTokenProvider>(
        this IServiceCollection services)
        where TTokenProvider : class, ITokenProvider
    {
        services.AddScoped<ITokenProvider, TTokenProvider>();
        return services;
    }

    private static void RegisterCoreServices(IServiceCollection services, IConfiguration? configuration)
    {
        // Get options for configuration (use defaults if not available)
        var options = new SalesforceOptions();
        configuration?.GetSection(SalesforceOptions.SectionName).Bind(options);

        // Fail-fast options validation (especially important for SQL cache encryption settings).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>());
        services.AddOptions<SalesforceOptions>().ValidateOnStart();

        // Register cache provider based on configuration
        RegisterCacheProvider(services, options);

        // Configure SalesforceMapper with options
        // This affects SOQL query generation for typed operations
        Mapping.SalesforceMapper.Configure(options.RequireSalesforceFieldAttribute);

        // Determine if we need to register a default TokenProvider
        // We only do this if no ITokenProvider is already registered
        if (!services.Any(s => s.ServiceType == typeof(ITokenProvider)))
        {
            if (configuration != null && configuration.GetSection("SalesforceJwt").Exists())
            {
                services.Configure<JwtTokenProviderOptions>(configuration.GetSection(JwtTokenProviderOptions.SectionName));
                services.AddScoped<ITokenProvider, JwtTokenProvider>();
            }
            else if (configuration != null && configuration.GetSection("SalesforceClientCredentials").Exists())
            {
                services.Configure<ClientCredentialsOptions>(configuration.GetSection(ClientCredentialsOptions.SectionName));
                services.AddScoped<ITokenProvider, ClientCredentialsTokenProvider>();
            }
            // If SalesforceMvc is present, it registers its own provider via AddSalesforceCoreMvc -> AddSalesforceAuthentication
            // If nothing is found, we don't register anything, trusting the consumer knows what they are doing
            // or will call AddSalesforceAuthentication / AddSalesforceTokenProvider later.
            // However, to be robust as requested, we could register a placeholder that throws a helpful error.
            else
            {
                // Register a placeholder that throws a descriptive error if resolved
                services.AddScoped<ITokenProvider, MissingConfigurationTokenProvider>();
            }
        }

        // Configure common HttpClient defaults
        void ConfigureClient(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("Accept", SalesforceConstants.Headers.ContentTypeJson);
            client.DefaultRequestHeaders.Add("User-Agent", SalesforceConstants.Headers.UserAgent);
            client.Timeout = options.HttpTimeout;
            if (options.MaxResponseContentBufferSize > 0)
            {
                client.MaxResponseContentBufferSize = options.MaxResponseContentBufferSize;
            }
        }

        // Register HTTP client with resilience (Polly)
        services.AddHttpClient<ISalesforceClient, SalesforceClient>(ConfigureClient)
        .AddStandardResilienceHandler(resilienceOptions =>
        {
            // Configure retry policy for transient errors and rate limits
            // Note: 401 Unauthorized is intentionally EXCLUDED here because it requires token refresh logic
            // which is handled manually in SalesforceClient.cs
            resilienceOptions.Retry.ShouldHandle = args => ValueTask.FromResult(
                args.Outcome.Result?.StatusCode is HttpStatusCode.InternalServerError
                    or HttpStatusCode.BadGateway
                    or HttpStatusCode.ServiceUnavailable
                    or HttpStatusCode.GatewayTimeout
                    or HttpStatusCode.RequestTimeout
                    or HttpStatusCode.TooManyRequests // Handle Rate Limits via Polly
                || args.Outcome.Exception is HttpRequestException);

            // Use configurable retry settings
            resilienceOptions.Retry.MaxRetryAttempts = options.MaxRetries;
            resilienceOptions.Retry.Delay = options.RetryBaseDelay;
            resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
            resilienceOptions.Retry.UseJitter = true;

            // Use configurable circuit breaker settings
            resilienceOptions.CircuitBreaker.SamplingDuration = options.CircuitBreakerSamplingDuration;
            resilienceOptions.CircuitBreaker.BreakDuration = options.CircuitBreakerBreakDuration;
            resilienceOptions.CircuitBreaker.MinimumThroughput = 5;
            resilienceOptions.CircuitBreaker.FailureRatio = 0.5;

            // Use configurable total request timeout
            resilienceOptions.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;

            // Use configurable per-attempt timeout (separate from total timeout)
            // This controls the timeout for each individual HTTP request before Polly retries
            resilienceOptions.AttemptTimeout.Timeout = options.PerAttemptTimeout;
        });

        // Register named clients for Token Providers with resilience
        services.AddHttpClient("SalesforceJwt", ConfigureClient)
            .AddStandardResilienceHandler();
        services.AddHttpClient("SalesforceClientCredentials", ConfigureClient)
            .AddStandardResilienceHandler();

        // Register URL builder
        services.AddScoped<ISalesforceUrlBuilder, SalesforceUrlBuilder>();

        // Register services
        services.AddScoped<ISchemaService, SchemaService>();
        services.AddScoped<IDataService, DataService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IConfigurationService, ConfigurationService>();

        // Register batch and bulk services
        services.AddScoped<ICompositeService, CompositeService>();
        services.AddScoped<IBulkService, BulkService>();

        // Register typed data service for strongly-typed operations with LINQ support
        services.AddScoped<ITypedDataService, TypedDataService>();

        // Register new API services
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IToolingService, ToolingService>();
        services.AddScoped<IReplicationService, ReplicationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ILimitsService, LimitsService>();

        // Register file service
        services.AddScoped<IFileService, FileService>();

        // Register Apex REST service with resilience
        services.AddHttpClient<IApexService, ApexService>()
            .AddStandardResilienceHandler();

        // Register Validation, Tracking, and Schema services
        services.AddScoped<IFieldValidator, FieldValidator>();
        services.AddScoped<IValidationRuleEngine, ValidationRuleEngine>();
        services.AddScoped<IChangeTracker, ChangeTracker>();
        services.AddScoped<IRecordTypeManager, RecordTypeManager>();

        // Register Field Level Security service
        services.AddScoped<IFieldLevelSecurityService, FieldLevelSecurityService>();

        // Register default UserContextProvider if one is not already provided (ASP.NET overrides this)
        services.TryAddSingleton<IUserContextProvider, DefaultUserContextProvider>();

        // Add synchronization service
        services.AddSingleton<ISynchronizationService, SynchronizationService>();

        // Register background token refresh service
        services.AddHostedService<TokenRefreshBackgroundService>();

        // Register Dynamic UI services (Permission and Layout)
        RegisterDynamicUiServices(services, configuration);

        // Register Visibility services
        RegisterVisibilityServices(services, configuration);
    }

    /// <summary>
    /// Registers the Dynamic UI services for permission-aware rendering.
    /// </summary>
    private static void RegisterDynamicUiServices(IServiceCollection services, IConfiguration? configuration)
    {
        // Configure DynamicUiOptions
        if (configuration != null)
        {
            services.Configure<DynamicUiOptions>(
                configuration.GetSection(DynamicUiOptions.SectionName));
        }
        else
        {
            // Provide default options if no configuration is available
            services.Configure<DynamicUiOptions>(_ => { });
        }

        // Register dynamic UI config provider (loads/merges config file and watches for changes)
        services.TryAddSingleton<IDynamicUiConfigProvider, DynamicUiConfigProvider>();

        // Register Permission Service
        services.AddScoped<IPermissionService, PermissionService>();

        // Register Permission Guard for fluent permission checks
        services.AddScoped<IPermissionGuard, PermissionGuard>();

        // Register Layout Descriptor Service
        services.AddScoped<ILayoutDescriptorService, LayoutDescriptorService>();
    }

    private static void RegisterVisibilityServices(IServiceCollection services, IConfiguration? configuration)
    {
        if (configuration != null)
        {
            services.Configure<VisibilityOptions>(
                configuration.GetSection(VisibilityOptions.SectionName));
        }
        else
        {
             services.Configure<VisibilityOptions>(_ => { });
        }

        services.AddScoped<IVisibilityService, VisibilityService>();
        services.AddScoped<IVisibilityRequirementHandler, Services.Authorization.Handlers.RoleHandler>();
        services.AddScoped<IVisibilityRequirementHandler, Services.Authorization.Handlers.SalesforcePermissionHandler>();
    }

    /// <summary>
    /// Adds Dynamic UI services with custom configuration.
    /// Use this to configure navigation, forms, and theming.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration action for DynamicUiOptions.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceDynamicUi(
        this IServiceCollection services,
        Action<DynamicUiOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Adds Dynamic UI services with configuration from IConfiguration.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceDynamicUi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DynamicUiOptions>(
            configuration.GetSection(DynamicUiOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Registers the appropriate cache provider based on configuration.
    /// No fallback - user must explicitly choose the cache provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cache provider options:
    /// - Memory: In-memory cache (default). Best for single-instance apps.
    /// - Distributed: External IDistributedCache (Redis, NCache, etc.). User must register.
    /// - SqlServer: Encrypted SQL Server cache. Call AddSalesforceEncryptedSqlServerCache() first.
    /// </para>
    /// </remarks>
    private static void RegisterCacheProvider(IServiceCollection services, SalesforceOptions options)
    {
        switch (options.CacheProvider)
        {
            case Services.Caching.CacheProviderType.Memory:
                // Default: in-memory cache
                services.AddMemoryCache();
                services.AddScoped<ICacheProvider, MemoryCacheProvider>();
                break;

            case Services.Caching.CacheProviderType.Distributed:
                // User-provided IDistributedCache (Redis, NCache, etc.)
                var hasDistributedCache = services.Any(d => d.ServiceType == typeof(IDistributedCache));
                if (!hasDistributedCache)
                {
                    throw new InvalidOperationException(
                        "CacheProvider is set to 'Distributed' but no IDistributedCache implementation is registered. " +
                        "Please register a distributed cache before calling AddSalesforceCore(). " +
                        "For example: services.AddStackExchangeRedisCache(...) or services.AddDistributedMemoryCache() for testing.");
                }
                services.AddScoped<ICacheProvider, DistributedCacheProvider>();
                break;

            case Services.Caching.CacheProviderType.SqlServer:
                // SQL Server cache with mandatory encryption
                // The EncryptedSqlServerCache implements IDistributedCache, so we use DistributedCacheProvider
                var hasSqlServerCache = services.Any(d => d.ServiceType == typeof(IDistributedCache));
                if (!hasSqlServerCache)
                {
                    throw new InvalidOperationException(
                        "CacheProvider is set to 'SqlServer' but SQL Server cache is not configured. " +
                        "Please call services.AddSalesforceEncryptedSqlServerCache(configuration) before AddSalesforceCore(). " +
                        "Example:\n" +
                        "  services.AddSalesforceEncryptedSqlServerCache(configuration);\n" +
                        "  services.AddSalesforceCore(configuration);");
                }
                services.AddScoped<ICacheProvider, DistributedCacheProvider>();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options.CacheProvider),
                    options.CacheProvider,
                    "Unknown cache provider type. Valid values: Memory, Distributed, SqlServer");
        }
    }
}
