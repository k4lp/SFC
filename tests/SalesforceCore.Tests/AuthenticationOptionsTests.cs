using System.Collections.Generic;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.AspNetCore.Authentication;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.Tests;

public class AuthenticationOptionsTests
{
    private sealed class StubTokenProvider : ITokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task RevokeTokenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    [Fact]
    public void AddSalesforceAuthentication_WhenUseServerSideSessions_ConfiguresCookieSessionStoreOnCookieScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var cookieOptions = options.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookieOptions.SessionStore.Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceAuthentication_ConfiguresInstanceUrlCaptureEvent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        var oidcOptions = options.Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidcOptions.Events.Should().NotBeNull();
        oidcOptions.Events.OnTokenResponseReceived.Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceAuthentication_BrowserEncryptedModeDoesNotUseServerSideSessionStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        var cookieOptions = options.Get(CookieAuthenticationDefaults.AuthenticationScheme);
        cookieOptions.SessionStore.Should().BeNull();
    }

    [Fact]
    public void SalesforceOptions_DisablesServerSideTokenRefreshCoordinatorByDefault()
    {
        var options = new SalesforceOptions();

        options.EnableServerSideTokenRefreshCoordinator.Should().BeFalse();
    }

    [Fact]
    public void AddSalesforceAuthentication_DoesNotConfigureClientSecretForBrowserEncryptedPkceFlow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:ClientSecret"] = "do-not-use-for-pkce-browser-ticket-mode",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        var oidcOptions = options.Get(OpenIdConnectDefaults.AuthenticationScheme);
        oidcOptions.UsePkce.Should().BeTrue();
        oidcOptions.SaveTokens.Should().BeTrue();
        oidcOptions.ClientSecret.Should().BeNull();
    }

    [Fact]
    public async Task AddSalesforceAuthentication_RegistersSalesforcePolicyScheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: false);

        using var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemeProvider.GetSchemeAsync("Salesforce");
        scheme.Should().NotBeNull();
    }

    [Fact]
    public void AddSalesforceAuthentication_DoesNotOverrideExistingTokenProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddScoped<ITokenProvider, StubTokenProvider>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com",
                ["Salesforce:ClientId"] = "client-id",
                ["Salesforce:CallbackPath"] = "/salesforce/callback"
            })
            .Build();

        services.AddSalesforceAuthentication(config, useServerSideSessions: false);

        using var provider = services.BuildServiceProvider();
        var tokenProvider = provider.GetRequiredService<ITokenProvider>();

        tokenProvider.Should().BeOfType<StubTokenProvider>();
    }

    [Fact]
    public async Task DistributedCacheTicketStore_ProtectsTicketBytes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddDataProtection();

        using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<IDistributedCache>();
        var dataProtectionProvider = provider.GetRequiredService<IDataProtectionProvider>();
        var logger = provider.GetRequiredService<ILogger<DistributedCacheTicketStore>>();

        var store = new DistributedCacheTicketStore(
            cache,
            logger,
            sessionTimeout: TimeSpan.FromMinutes(30),
            dataProtectionProvider: dataProtectionProvider);

        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "user") },
                authenticationType: "test"));

        var properties = new AuthenticationProperties();
        properties.UpdateTokenValue("access_token", "access");
        properties.UpdateTokenValue("refresh_token", "refresh");
        properties.Items["instance_url"] = "https://example.my.salesforce.com";
        properties.UpdateTokenValue("instance_url", "https://example.my.salesforce.com");

        var ticket = new AuthenticationTicket(
            principal,
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var rawBytes = TicketSerializer.Default.Serialize(ticket);
        var key = await store.StoreAsync(ticket);

        var storedBytes = await cache.GetAsync($"SalesforceAuth:{key}");
        storedBytes.Should().NotBeNull();
        storedBytes!.Should().NotEqual(rawBytes);

        var retrieved = await store.RetrieveAsync(key);
        retrieved.Should().NotBeNull();
    }
}
