using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SalesforceCore.AspNetCore.Authentication;
using SalesforceCore.AspNetCore.Middleware;
using SalesforceCore.AspNetCore.TokenProviders;
using SalesforceCore.Extensions;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Authorization;
using System.Reflection;
using Microsoft.Extensions.Http.Resilience;

namespace SalesforceCore.AspNetCore.Extensions;

/// <summary>
/// Extension methods for registering SalesforceCore.AspNetCore services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds SalesforceCore MVC services including controllers, views, and tag helpers.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCoreMvc(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add core services
        services.AddSalesforceCore(configuration);

        var mvcOptions = new SalesforceMvcOptions();
        configuration.GetSection("SalesforceMvc").Bind(mvcOptions);

        // Configure MVC options
        services.Configure<SalesforceMvcOptions>(
            configuration.GetSection("SalesforceMvc"));

        // Add HttpContextAccessor for token provider
        services.AddHttpContextAccessor();
        services.AddDataProtection();

        // Register UserContextProvider for Visibility Service
        services.AddScoped<IUserContextProvider, AspNetCoreUserContextProvider>();

        // Add HttpClient for token refresh
        services.AddHttpClient("SalesforceTokenRefresh")
            .AddStandardResilienceHandler();

        // Add token provider
        services.AddScoped<ITokenProvider, AspNetCoreTokenProvider>();

        // Register controllers from this assembly
        var mvcBuilder = services.AddControllersWithViews()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        if (!mvcOptions.UseEmbeddedViews)
        {
            mvcBuilder.ConfigureApplicationPartManager(manager =>
            {
                var partsToRemove = manager.ApplicationParts
                    .OfType<CompiledRazorAssemblyPart>()
                    .Where(part => part.Assembly == typeof(ServiceCollectionExtensions).Assembly)
                    .ToList();

                foreach (var part in partsToRemove)
                {
                    manager.ApplicationParts.Remove(part);
                }
            });
        }

        return services;
    }

    /// <summary>
    /// Adds SalesforceCore MVC services with configuration action.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configureSalesforce">Salesforce options configuration.</param>
    /// <param name="configureMvc">MVC options configuration.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceCoreMvc(
        this IServiceCollection services,
        Action<SalesforceOptions>? configureSalesforce = null,
        Action<SalesforceMvcOptions>? configureMvc = null)
    {
        // Add core services
        if (configureSalesforce != null)
        {
            services.AddSalesforceCore(configureSalesforce);
        }
        else
        {
            services.AddSalesforceCore();
        }

        // Configure MVC options
        var mvcOptions = new SalesforceMvcOptions();
        if (configureMvc != null)
        {
            configureMvc(mvcOptions);
            services.Configure(configureMvc);
        }

        // Add HttpContextAccessor for token provider
        services.AddHttpContextAccessor();
        services.AddDataProtection();

        // Register UserContextProvider for Visibility Service
        services.AddScoped<IUserContextProvider, AspNetCoreUserContextProvider>();

        // Add HttpClient for token refresh
        services.AddHttpClient("SalesforceTokenRefresh");

        // Add token provider
        services.AddScoped<ITokenProvider, AspNetCoreTokenProvider>();

        // Register controllers from this assembly
        var mvcBuilder = services.AddControllersWithViews()
            .AddApplicationPart(typeof(ServiceCollectionExtensions).Assembly);

        if (!mvcOptions.UseEmbeddedViews)
        {
            mvcBuilder.ConfigureApplicationPartManager(manager =>
            {
                var partsToRemove = manager.ApplicationParts
                    .OfType<CompiledRazorAssemblyPart>()
                    .Where(part => part.Assembly == typeof(ServiceCollectionExtensions).Assembly)
                    .ToList();

                foreach (var part in partsToRemove)
                {
                    manager.ApplicationParts.Remove(part);
                }
            });
        }

        return services;
    }

    /// <summary>
    /// Adds Salesforce OAuth2 PKCE authentication.
    /// Uses Authorization Code Flow with Proof Key for Code Exchange (PKCE).
    /// This is the recommended flow for web applications as it doesn't require a client secret.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration root.</param>
    /// <param name="useServerSideSessions">
    /// When true, stores authentication tickets server-side using IDistributedCache.
    /// This prevents cookie size limits (4KB) from being exceeded when storing OAuth tokens.
    /// Requires AddDistributedMemoryCache(), AddStackExchangeRedisCache(), or similar to be called first.
    /// </param>
    /// <returns>Authentication builder for chaining.</returns>
    public static AuthenticationBuilder AddSalesforceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useServerSideSessions = false)
    {
        var salesforceConfig = configuration.GetSection(SalesforceOptions.SectionName);
        var domain = salesforceConfig["Domain"] ?? "https://login.salesforce.com";
        var clientId = salesforceConfig["ClientId"] ?? throw new InvalidOperationException("Salesforce:ClientId is required");
        var callbackPath = salesforceConfig["CallbackPath"] ?? "/salesforce/callback";
        var sessionTimeoutStr = salesforceConfig["SessionTimeout"];
        var sessionTimeout = string.IsNullOrEmpty(sessionTimeoutStr)
            ? TimeSpan.FromHours(8)
            : TimeSpan.Parse(sessionTimeoutStr);

        // Parse ForceSecureCookie - defaults to true for production safety
        var forceSecureCookieStr = salesforceConfig["ForceSecureCookie"];
        var forceSecureCookie = string.IsNullOrEmpty(forceSecureCookieStr) || bool.Parse(forceSecureCookieStr);

        // Parse PromptLogin - defaults to false
        var promptLoginStr = salesforceConfig["PromptLogin"];
        var promptLogin = !string.IsNullOrEmpty(promptLoginStr) && bool.Parse(promptLoginStr);

        // Parse SlidingExpiration - defaults to true
        var slidingExpirationStr = salesforceConfig["SlidingExpiration"];
        var slidingExpiration = string.IsNullOrEmpty(slidingExpirationStr) || bool.Parse(slidingExpirationStr);

        // Parse SessionCookieName - defaults to __Host-SalesforceSession
        var sessionCookieName = salesforceConfig["SessionCookieName"] ?? "__Host-SalesforceSession";

        // Ensure required infrastructure for web token handling is present.
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddHttpClient("SalesforceTokenRefresh")
            .AddStandardResilienceHandler();
        services.AddHttpClient("SalesforceTokenRevoke")
            .AddStandardResilienceHandler();

        // Register the default web token provider (cookie/OIDC-based) if the app hasn't chosen one already.
        // Consumers can still override by registering their own ITokenProvider after calling this method.
        if (!services.Any(s => s.ServiceType == typeof(ITokenProvider)))
        {
            services.AddScoped<ITokenProvider, AspNetCoreTokenProvider>();
        }

        var builder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        // Convenience scheme name for consumers who expect a Salesforce-named scheme.
        // This forwards challenges to the OpenIdConnect handler and uses Cookies for auth persistence.
        .AddPolicyScheme("Salesforce", "Salesforce", options =>
        {
            options.ForwardChallenge = OpenIdConnectDefaults.AuthenticationScheme;
            options.ForwardAuthenticate = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ForwardSignIn = CookieAuthenticationDefaults.AuthenticationScheme;
            options.ForwardSignOut = CookieAuthenticationDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = sessionCookieName;
            options.Cookie.HttpOnly = true;
            // Use ForceSecureCookie setting - critical for reverse proxy deployments
            options.Cookie.SecurePolicy = forceSecureCookie
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = sessionTimeout;
            options.SlidingExpiration = slidingExpiration;
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
        })
        .AddOpenIdConnect(options =>
        {
            options.Authority = domain;
            options.ClientId = clientId;
            options.CallbackPath = callbackPath;

            // PKCE Configuration - Authorization Code Flow with PKCE
            // No client secret required - PKCE provides the security
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;  // Enable PKCE

            options.SaveTokens = true;
            options.GetClaimsFromUserInfoEndpoint = true;

            // Salesforce-specific scopes
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.Scope.Add("api");
            options.Scope.Add("refresh_token");

            options.TokenValidationParameters.NameClaimType = "name";

            // Handle redirect to authorization - add prompt=login if configured
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                if (promptLogin)
                {
                    // Force user to re-authenticate even if they have an existing Salesforce session
                    context.ProtocolMessage.Prompt = "login";
                }
                return Task.CompletedTask;
            };

            // Capture Salesforce-specific parameters from the token response.
            // Salesforce returns instance_url in the token response body, not as a stable claim.
            options.Events.OnTokenResponseReceived = context =>
            {
                if (context.Properties != null)
                {
                    var instanceUrl = context.TokenEndpointResponse?.GetParameter("instance_url")?.ToString();
                    if (!string.IsNullOrWhiteSpace(instanceUrl))
                    {
                        context.Properties.Items["instance_url"] = instanceUrl;
                        context.Properties.UpdateTokenValue("instance_url", instanceUrl);
                    }
                }

                return Task.CompletedTask;
            };

            // Handle token validation for Salesforce
            options.Events.OnTokenValidated = context =>
            {
                // Persist a stable per-session identifier in auth properties. This is used for
                // cross-server token refresh coordination and must remain stable across refreshes.
                if (context.Properties != null &&
                    !context.Properties.Items.ContainsKey("sf_session_id"))
                {
                    context.Properties.Items["sf_session_id"] = Guid.NewGuid().ToString("N");
                }

                // Additional validation can be performed here.
                return Task.CompletedTask;
            };
        });

        // Use server-side session storage if enabled
        // This prevents cookie size limits (4KB) from being exceeded
        if (useServerSideSessions)
        {
            var hasDistributedCache = services.Any(s => s.ServiceType == typeof(IDistributedCache));
            if (!hasDistributedCache)
            {
                throw new InvalidOperationException(
                    "useServerSideSessions is true but no IDistributedCache is registered. " +
                    "Register a distributed cache first (e.g., services.AddDistributedMemoryCache() for development " +
                    "or services.AddStackExchangeRedisCache(...) for production).");
            }

            services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
                .Configure<IDistributedCache, IDataProtectionProvider, ILogger<DistributedCacheTicketStore>>((options, cache, dataProtectionProvider, logger) =>
                {
                    options.SessionStore = new DistributedCacheTicketStore(cache, logger, sessionTimeout, dataProtectionProvider);
                });
        }

        return builder;
    }

    /// <summary>
    /// Adds server-side session storage for Salesforce authentication using IDistributedCache.
    /// Call this before AddSalesforceAuthentication when you need server-side sessions.
    ///
    /// Example usage:
    /// <code>
    /// // For in-memory (development)
    /// services.AddDistributedMemoryCache();
    ///
    /// // For Redis (production)
    /// services.AddStackExchangeRedisCache(options => {
    ///     options.Configuration = "localhost:6379";
    /// });
    ///
    /// services.AddSalesforceSessionStorage();
    /// services.AddSalesforceAuthentication(configuration, useServerSideSessions: true);
    /// </code>
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceSessionStorage(this IServiceCollection services)
    {
        services.AddSingleton<ITicketStore, DistributedCacheTicketStore>();
        return services;
    }

    /// <summary>
    /// Adds SalesforceCore middleware and static files.
    /// </summary>
    /// <param name="app">Application builder.</param>
    /// <returns>Application builder for chaining.</returns>
    public static IApplicationBuilder UseSalesforceCore(this IApplicationBuilder app)
    {
        // Add security headers
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Add exception handling middleware
        app.UseMiddleware<SalesforceExceptionMiddleware>();

        var mvcOptions = app.ApplicationServices.GetService<IOptions<SalesforceMvcOptions>>()?.Value
            ?? new SalesforceMvcOptions();

        if (mvcOptions.UseEmbeddedStaticFiles)
        {
            var assembly = typeof(ServiceCollectionExtensions).Assembly;
            var embeddedProvider = new EmbeddedFileProvider(assembly, "SalesforceCore.AspNetCore.wwwroot");

            var requestPath = string.IsNullOrWhiteSpace(mvcOptions.StaticFilesPath)
                ? "/_salesforce"
                : mvcOptions.StaticFilesPath.Trim();

            if (!requestPath.StartsWith('/'))
            {
                requestPath = "/" + requestPath;
            }

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = embeddedProvider,
                RequestPath = requestPath
            });
        }

        return app;
    }

    /// <summary>
    /// Adds session-based token storage for Salesforce authentication.
    /// Use this for simple deployments where ASP.NET Core session is already configured.
    /// </summary>
    /// <remarks>
    /// Requires session to be configured first:
    /// <code>
    /// builder.Services.AddDistributedMemoryCache(); // or Redis for production
    /// builder.Services.AddSession(options =>
    /// {
    ///     options.IdleTimeout = TimeSpan.FromHours(8);
    ///     options.Cookie.HttpOnly = true;
    ///     options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    /// });
    /// builder.Services.AddSalesforceSessionTokenStorage();
    ///
    /// // In middleware:
    /// app.UseSession();
    /// </code>
    /// </remarks>
    /// <param name="services">Service collection.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceSessionTokenStorage(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITokenProvider, SessionTokenProvider>();
        return services;
    }

    /// <summary>
    /// Adds distributed cache-based token storage for Salesforce authentication.
    /// Use this for production deployments with multiple servers.
    /// </summary>
    /// <remarks>
    /// Requires a distributed cache to be configured first:
    /// <code>
    /// // For development:
    /// builder.Services.AddDistributedMemoryCache();
    ///
    /// // For production (Redis):
    /// builder.Services.AddStackExchangeRedisCache(options =>
    /// {
    ///     options.Configuration = "localhost:6379";
    /// });
    ///
    /// builder.Services.AddSalesforceDistributedCacheTokenStorage();
    /// </code>
    /// </remarks>
    /// <param name="services">Service collection.</param>
    /// <param name="configureOptions">Optional configuration for the token provider.</param>
    /// <returns>Service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceDistributedCacheTokenStorage(
        this IServiceCollection services,
        Action<DistributedCacheTokenProviderOptions>? configureOptions = null)
    {
        services.AddHttpContextAccessor();

        var options = new DistributedCacheTokenProviderOptions();
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddScoped<ITokenProvider, DistributedCacheTokenProvider>();
        return services;
    }

    /// <summary>
    /// Maps SalesforceCore routes.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <param name="routePrefix">Route prefix (default: "sf").</param>
    /// <returns>Endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapSalesforceRoutes(
        this IEndpointRouteBuilder endpoints,
        string? routePrefix = null)
    {
        var resolvedPrefix = routePrefix;
        if (string.IsNullOrWhiteSpace(resolvedPrefix))
        {
            resolvedPrefix = endpoints.ServiceProvider
                .GetService<IOptions<SalesforceMvcOptions>>()?.Value.RoutePrefix;
        }

        resolvedPrefix = string.IsNullOrWhiteSpace(resolvedPrefix)
            ? "sf"
            : resolvedPrefix.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(resolvedPrefix))
        {
            resolvedPrefix = "sf";
        }

        // Main CRUD routes
        endpoints.MapControllerRoute(
            name: "salesforce_index",
            pattern: $"{resolvedPrefix}/{{sObject}}",
            defaults: new { controller = "Salesforce", action = "Index" });

        endpoints.MapControllerRoute(
            name: "salesforce_details",
            pattern: $"{resolvedPrefix}/{{sObject}}/Details/{{id}}",
            defaults: new { controller = "Salesforce", action = "Details" });

        endpoints.MapControllerRoute(
            name: "salesforce_create",
            pattern: $"{resolvedPrefix}/{{sObject}}/Create",
            defaults: new { controller = "Salesforce", action = "Create" });

        endpoints.MapControllerRoute(
            name: "salesforce_edit",
            pattern: $"{resolvedPrefix}/{{sObject}}/Edit/{{id}}",
            defaults: new { controller = "Salesforce", action = "Edit" });

        endpoints.MapControllerRoute(
            name: "salesforce_delete",
            pattern: $"{resolvedPrefix}/{{sObject}}/Delete/{{id}}",
            defaults: new { controller = "Salesforce", action = "Delete" });

        endpoints.MapControllerRoute(
            name: "salesforce_upload",
            pattern: $"{resolvedPrefix}/{{sObject}}/Upload/{{id}}",
            defaults: new { controller = "Salesforce", action = "Upload" });

        // Lookup routes
        endpoints.MapControllerRoute(
            name: "salesforce_lookup",
            pattern: $"{resolvedPrefix}/lookup/search",
            defaults: new { controller = "Lookup", action = "Search" });

        endpoints.MapControllerRoute(
            name: "salesforce_lookup_recent",
            pattern: $"{resolvedPrefix}/lookup/recent",
            defaults: new { controller = "Lookup", action = "Recent" });

        // File routes
        endpoints.MapControllerRoute(
            name: "salesforce_file_image",
            pattern: $"{resolvedPrefix}/file/image/{{versionId}}",
            defaults: new { controller = "File", action = "GetImage" });

        endpoints.MapControllerRoute(
            name: "salesforce_file_download",
            pattern: $"{resolvedPrefix}/file/download/{{versionId}}/{{filename?}}",
            defaults: new { controller = "File", action = "Download" });

        return endpoints;
    }

    /// <summary>
    /// Maps Dynamic UI API routes.
    /// These endpoints serve JSON descriptors for SPAs and dynamic rendering.
    /// </summary>
    /// <param name="endpoints">Endpoint route builder.</param>
    /// <returns>Endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapDynamicUiRoutes(this IEndpointRouteBuilder endpoints)
    {
        // The DynamicUiController uses attribute routing [Route("api/dynamic-ui")]
        // This method ensures the controller routes are mapped
        endpoints.MapControllers();
        return endpoints;
    }
}
