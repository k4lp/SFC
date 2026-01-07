using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Background service that proactively refreshes Salesforce tokens before they expire.
/// This ensures that the application always has a valid token and avoids latency spikes
/// from reactive refreshing during user requests.
/// </summary>
public class TokenRefreshBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TokenRefreshBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public TokenRefreshBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<TokenRefreshBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Salesforce Token Refresh Background Service starting.");

        using var timer = new PeriodicTimer(_checkInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckAndRefreshTokenAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking token expiration.");
            }
        }

        _logger.LogInformation("Salesforce Token Refresh Background Service stopping.");
    }

    private async Task CheckAndRefreshTokenAsync(CancellationToken cancellationToken)
    {
        // Token providers are usually scoped, so we create a scope to resolve them.
        using var scope = _serviceProvider.CreateScope();
        var tokenProvider = scope.ServiceProvider.GetService<ITokenProvider>();

        if (tokenProvider == null)
        {
            // No provider registered, or it's not one we manage (e.g. web auth might be different).
            // For Jwt/ClientCredentials, they are registered as ITokenProvider.
            return;
        }

        // We only want to auto-refresh server-side flows (JWT, ClientCreds).
        // Check if the provider is one of our managed types.
        if (tokenProvider is JwtTokenProvider || tokenProvider is ClientCredentialsTokenProvider)
        {
            // The GetAccessTokenAsync implementation in these providers already includes the proactive check logic.
            // By calling it, we trigger the refresh if needed.
            // We use a shorter buffer here implicitly because the provider has its own buffer (e.g. 5 mins).
            // If we call this every minute, we ensure we hit that 5-minute window effectively.

            await tokenProvider.GetAccessTokenAsync(cancellationToken);
        }
    }
}
