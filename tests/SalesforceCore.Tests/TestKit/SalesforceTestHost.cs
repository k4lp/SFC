using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SalesforceCore.Extensions;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;

namespace SalesforceCore.Tests.TestKit;

/// <summary>
/// Real composition root (AddSalesforceCore… + the actual HTTP pipeline) wired to a <see cref="FakeSalesforceServer"/>.
/// Use it to test services end-to-end through DI exactly as consumers run them.
/// </summary>
public sealed class SalesforceTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    /// <summary>The fake server.</summary>
    public FakeSalesforceServer Server { get; }

    /// <summary>The token provider.</summary>
    public TestTokenProvider Tokens { get; }

    /// <summary>Scoped service provider.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    private SalesforceTestHost(FakeSalesforceServer server, TestTokenProvider tokens, ServiceProvider root)
    {
        Server = server; Tokens = tokens; _root = root; _scope = root.CreateAsyncScope();
    }

    /// <summary>Creates a host. Retries default to 0 and timeouts are short so error tests are fast.</summary>
    public static SalesforceTestHost Create(Action<SalesforceOptions>? configure = null, Action<IServiceCollection>? services = null)
    {
        var server = new FakeSalesforceServer();
        var tokens = new TestTokenProvider();
        var sc = new ServiceCollection();
        sc.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug));
        sc.AddSalesforceCoreWithoutAuth(o =>
        {
            o.MaxRetries = 0;
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            o.PartitionSchemaCacheByUser = false;
            configure?.Invoke(o);
        });
        sc.AddSingleton<ITokenProvider>(tokens);
        sc.ConfigureSalesforceHttpClient(b => b.ConfigurePrimaryHttpMessageHandler(() => server).SetHandlerLifetime(Timeout.InfiniteTimeSpan));
        services?.Invoke(sc);
        return new SalesforceTestHost(server, tokens, sc.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }));
    }

    /// <summary>Resolves a service from the test scope.</summary>
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }
}
