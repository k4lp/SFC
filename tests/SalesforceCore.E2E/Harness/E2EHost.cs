using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.Extensions;

namespace SalesforceCore.E2E.Harness;

/// <summary>Real DI composition (AddSalesforceCore + client-credentials flow) wired to the fake server.</summary>
public sealed class E2EHost : IAsyncDisposable
{
    public FakeSalesforce Fake { get; }
    public ServiceProvider Root { get; }
    public AsyncServiceScope Scope { get; }
    public T Get<T>() where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

    private E2EHost(FakeSalesforce fake, ServiceProvider root) { Fake = fake; Root = root; Scope = root.CreateAsyncScope(); }

    public static async Task<E2EHost> StartAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? services = null)
    {
        var fake = await FakeSalesforce.StartAsync();
        var values = new Dictionary<string, string?>
        {
            ["Salesforce:Domain"] = "https://login.salesforce.com",
            ["Salesforce:ClientId"] = "cid",
            ["Salesforce:ClientSecret"] = "secret",
            ["Salesforce:ApiVersion"] = "v62.0",
            ["Salesforce:MaxRetries"] = "3",
            ["Salesforce:RetryBaseDelay"] = "00:00:00.001",
            ["Salesforce:BulkPollInterval"] = "00:00:00.001",
            ["SalesforceClientCredentials:ClientId"] = "cid",
            ["SalesforceClientCredentials:ClientSecret"] = "secret",
        };
        foreach (var kv in settings ?? new()) values[kv.Key] = kv.Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton<IConfiguration>(config);
        sc.AddSalesforceCore(config);
        sc.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => fake.Handler));
        services?.Invoke(sc);
        return new E2EHost(fake, sc.BuildServiceProvider());
    }

    public async ValueTask DisposeAsync()
    {
        await Scope.DisposeAsync();
        await Root.DisposeAsync();
        await Fake.DisposeAsync();
    }
}
