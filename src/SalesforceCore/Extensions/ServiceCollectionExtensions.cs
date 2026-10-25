using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Microsoft.Extensions.Hosting;
using SalesforceCore.Http;
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
    /// <param name="configure">Configuration action. Every option (HTTP timeouts, retries, cache provider, API version...) is honoured.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(
        this IServiceCollection services,
        Action<SalesforceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        RegisterCoreServices(services, null, configure, registerDefaultTokenProvider: true);
        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services with configuration from IConfiguration (section <c>Salesforce</c>).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<SalesforceOptions>(configuration.GetSection(SalesforceOptions.SectionName));
        RegisterCoreServices(services, configuration, null, registerDefaultTokenProvider: true);
        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services with default configuration (configure <see cref="SalesforceOptions"/> elsewhere).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCore(this IServiceCollection services)
    {
        RegisterCoreServices(services, null, null, registerDefaultTokenProvider: true);
        return services;
    }

    /// <summary>
    /// Adds SalesforceCore services without registering a token provider (bring your own via
    /// <see cref="AddSalesforceTokenProvider{TTokenProvider}"/>). Registers the same full service set as AddSalesforceCore.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Configuration action.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCoreWithoutAuth(
        this IServiceCollection services,
        Action<SalesforceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        RegisterCoreServices(services, null, configure, registerDefaultTokenProvider: false);
        return services;
    }

    /// <summary>
    /// Adds a custom token provider (replaces any previously registered one).
    /// </summary>
    /// <typeparam name="TTokenProvider">Token provider implementation type.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceTokenProvider<TTokenProvider>(
        this IServiceCollection services)
        where TTokenProvider : class, ITokenProvider
    {
        services.RemoveAll<ITokenProvider>();
        services.AddScoped<ITokenProvider, TTokenProvider>();
        return services;
    }

    /// <summary>
    /// Appends a <see cref="DelegatingHandler"/> to the Salesforce API HTTP pipeline (innermost, i.e. it runs once per
    /// attempt, after retry/circuit-breaker, logging and API-usage handlers). Use it for tracing, correlation ids,
    /// custom headers, metrics, or test interception.
    /// </summary>
    /// <typeparam name="THandler">Handler type (resolved from DI, registered transient if not already registered).</typeparam>
    public static IServiceCollection AddSalesforceHttpHandler<THandler>(this IServiceCollection services)
        where THandler : DelegatingHandler
    {
        services.TryAddTransient<THandler>();
        services.AddHttpClient(SalesforceHttpClientNames.Api).AddHttpMessageHandler<THandler>();
        return services;
    }

    /// <summary>
    /// Customises the Salesforce API HttpClient (e.g. <c>ConfigurePrimaryHttpMessageHandler</c> for proxies or client certificates).
    /// </summary>
    public static IServiceCollection ConfigureSalesforceHttpClient(this IServiceCollection services, Action<IHttpClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(services.AddHttpClient(SalesforceHttpClientNames.Api));
        return services;
    }

    /// <summary>
    /// Registers the background service that proactively renews server-to-server tokens (JWT bearer / client credentials).
    /// Registered automatically when one of those providers is configured from IConfiguration.
    /// </summary>
    public static IServiceCollection AddSalesforceBackgroundTokenRefresh(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TokenRefreshBackgroundService>());
        return services;
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        IConfiguration? configuration,
        Action<SalesforceOptions>? configure,
        bool registerDefaultTokenProvider)
    {
        // Registration-time snapshot: used ONLY for fail-fast checks and choosing the cache provider.
        // Runtime settings (HTTP timeouts, retries, API version...) are read lazily from IOptions so that later
        // Configure/PostConfigure calls are honoured too.
        var snapshot = new SalesforceOptions();
        configuration?.GetSection(SalesforceOptions.SectionName).Bind(snapshot);
        configure?.Invoke(snapshot);

        services.AddOptions<SalesforceOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<SalesforceOptions>, SalesforceOptionsPostConfigure>());

        RegisterCacheProvider(services, snapshot);

        // Process-wide mapper setting; re-applied from IOptions by SalesforceOptionsPostConfigure.
        Mapping.SalesforceMapper.Configure(snapshot.RequireSalesforceFieldAttribute);

        if (registerDefaultTokenProvider && !services.Any(s => s.ServiceType == typeof(ITokenProvider)))
        {
            if (configuration != null && configuration.GetSection(JwtTokenProviderOptions.SectionName).Exists())
            {
                services.Configure<JwtTokenProviderOptions>(configuration.GetSection(JwtTokenProviderOptions.SectionName));
                services.AddScoped<ITokenProvider, JwtTokenProvider>();
                services.AddSalesforceBackgroundTokenRefresh();
            }
            else if (configuration != null && configuration.GetSection(ClientCredentialsOptions.SectionName).Exists())
            {
                services.Configure<ClientCredentialsOptions>(configuration.GetSection(ClientCredentialsOptions.SectionName));
                services.AddScoped<ITokenProvider, ClientCredentialsTokenProvider>();
                services.AddSalesforceBackgroundTokenRefresh();
            }
            else
            {
                // Placeholder that throws a descriptive error if used before a real provider is registered.
                services.AddScoped<ITokenProvider, MissingConfigurationTokenProvider>();
            }
        }

        // ---- HTTP pipeline -------------------------------------------------------------------------------
        static void ConfigureClient(IServiceProvider sp, HttpClient client)
        {
            var o = sp.GetRequiredService<IOptions<SalesforceOptions>>().Value;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(SalesforceConstants.Headers.UserAgent);
            client.Timeout = o.HttpTimeout;
            if (o.MaxResponseContentBufferSize > 0)
            {
                client.MaxResponseContentBufferSize = o.MaxResponseContentBufferSize;
            }
        }

        services.TryAddSingleton<ISalesforceApiUsage, SalesforceApiUsage>();
        services.TryAddTransient<SalesforceApiUsageHandler>();
        services.TryAddTransient<SalesforceLoggingHandler>();

        if (!services.Any(d => d.ServiceType == typeof(SalesforceCoreMarker)))
        {
            services.AddSingleton<SalesforceCoreMarker>();

            var api = services.AddHttpClient<ISalesforceClient, SalesforceClient>(SalesforceHttpClientNames.Api, ConfigureClient);
            api.AddStandardResilienceHandler().Configure((resilience, sp) => ConfigureResilience(resilience, sp.GetRequiredService<IOptions<SalesforceOptions>>().Value));
            api.AddHttpMessageHandler<SalesforceLoggingHandler>();
            api.AddHttpMessageHandler<SalesforceApiUsageHandler>();

            services.AddHttpClient(SalesforceHttpClientNames.Jwt, ConfigureClient).AddStandardResilienceHandler();
            services.AddHttpClient(SalesforceHttpClientNames.ClientCredentials, ConfigureClient).AddStandardResilienceHandler();
        }

        // ---- Services ------------------------------------------------------------------------------------
        services.TryAddScoped<ISalesforceUrlBuilder, SalesforceUrlBuilder>();
        services.TryAddScoped<ISchemaService, SchemaService>();
        services.TryAddScoped<IDataService, DataService>();
        services.TryAddScoped<ILookupService, LookupService>();
        services.TryAddScoped<IConfigurationService, ConfigurationService>();
        services.TryAddScoped<ICompositeService, CompositeService>();
        services.TryAddScoped<IBulkService, BulkService>();
        services.TryAddScoped<ITypedDataService, TypedDataService>();
        services.TryAddScoped<ISearchService, SearchService>();
        services.TryAddScoped<IToolingService, ToolingService>();
        services.TryAddScoped<IReplicationService, ReplicationService>();
        services.TryAddScoped<IReportService, ReportService>();
        services.TryAddScoped<ILimitsService, LimitsService>();
        services.TryAddScoped<IFileService, FileService>();
        services.TryAddScoped<IApexService, ApexService>();
        services.TryAddScoped<IFieldValidator, FieldValidator>();
        services.TryAddScoped<IValidationRuleEngine, ValidationRuleEngine>();
        services.TryAddScoped<IChangeTracker, ChangeTracker>();
        services.TryAddScoped<IRecordTypeManager, RecordTypeManager>();
        services.TryAddSingleton<IUserContextProvider, DefaultUserContextProvider>();
        services.TryAddSingleton<ISynchronizationService, SynchronizationService>();

        RegisterDynamicUiServices(services, configuration);
    }

    internal static void ConfigureResilience(HttpStandardResilienceOptions resilienceOptions, SalesforceOptions options)
    {
        // 401 is excluded on purpose: it needs a token refresh, which SalesforceClient performs.
        resilienceOptions.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
            || args.Outcome.Exception is HttpRequestException);
        resilienceOptions.Retry.MaxRetryAttempts = Math.Max(0, options.MaxRetries) == 0 ? 1 : options.MaxRetries;
        if (options.MaxRetries == 0)
        {
            resilienceOptions.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
        }
        resilienceOptions.Retry.Delay = options.RetryBaseDelay;
        resilienceOptions.Retry.BackoffType = DelayBackoffType.Exponential;
        resilienceOptions.Retry.UseJitter = true;
        resilienceOptions.Retry.ShouldRetryAfterHeader = true;

        resilienceOptions.CircuitBreaker.SamplingDuration = options.CircuitBreakerSamplingDuration;
        resilienceOptions.CircuitBreaker.BreakDuration = options.CircuitBreakerBreakDuration;
        resilienceOptions.CircuitBreaker.MinimumThroughput = 5;
        resilienceOptions.CircuitBreaker.FailureRatio = 0.5;

        resilienceOptions.TotalRequestTimeout.Timeout = options.TotalRequestTimeout;
        resilienceOptions.AttemptTimeout.Timeout = options.PerAttemptTimeout;
    }

    /// <summary>Marker preventing duplicate HttpClient pipeline registration when AddSalesforceCore is called twice.</summary>
    private sealed class SalesforceCoreMarker { }

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
        services.TryAddScoped<IPermissionService, PermissionService>();


        // Register Layout Descriptor Service
        services.TryAddScoped<ILayoutDescriptorService, LayoutDescriptorService>();
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
                services.TryAddScoped<ICacheProvider, MemoryCacheProvider>();
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
                services.TryAddScoped<ICacheProvider, DistributedCacheProvider>();
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
                services.TryAddScoped<ICacheProvider, DistributedCacheProvider>();
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options.CacheProvider),
                    options.CacheProvider,
                    "Unknown cache provider type. Valid values: Memory, Distributed, SqlServer");
        }
    }
}
