using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SalesforceCore.Extensions;
using SalesforceCore.Http;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Security;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V2;

public class CompositionRootTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        register(sc);
        return sc.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void ActionOverload_HonoursHttpSettings()
    {
        using var sp = Build(s => s.AddSalesforceCore(o => { o.HttpTimeout = TimeSpan.FromSeconds(17); o.MaxRetries = 4; }));
        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(SalesforceHttpClientNames.Api);
        client.Timeout.Should().Be(TimeSpan.FromSeconds(17), "1.x silently ignored HttpTimeout set via the Action overload (ARC-01)");
        sp.GetRequiredService<IOptions<SalesforceOptions>>().Value.MaxRetries.Should().Be(4);
    }

    [Fact]
    public void ConfigurationOverload_BindsSection_AndNormalisesApiVersion()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Salesforce:ApiVersion"] = "61",
            ["Salesforce:ClientId"] = "cid"
        }).Build();
        using var sp = Build(s => s.AddSalesforceCore(config));
        var o = sp.GetRequiredService<IOptions<SalesforceOptions>>().Value;
        o.ApiVersion.Should().Be("v61.0");
        o.ClientId.Should().Be("cid");
    }

    [Fact]
    public void InvalidApiVersion_FailsOptionsValidation()
    {
        using var sp = Build(s => s.AddSalesforceCore(o => o.ApiVersion = "latest"));
        var act = () => sp.GetRequiredService<IOptions<SalesforceOptions>>().Value;
        act.Should().Throw<OptionsValidationException>().WithMessage("*ApiVersion*");
    }

    [Fact]
    public void AllCoreServices_Resolve()
    {
        using var sp = Build(s => { s.AddSalesforceCoreWithoutAuth(_ => { }); s.AddSalesforceTokenProvider<TestTokenProvider>(); });
        using var scope = sp.CreateScope();
        var types = new[]
        {
            typeof(ISalesforceClient), typeof(IDataService), typeof(ISchemaService), typeof(IBulkService), typeof(ICompositeService),
            typeof(ILookupService), typeof(ITypedDataService), typeof(ISearchService), typeof(IApexService), typeof(ILimitsService),
            typeof(ICacheProvider), typeof(ISalesforceApiUsage), typeof(Services.Files.IFileService), typeof(Services.Reports.IReportService),
            typeof(Services.Tooling.IToolingService), typeof(IReplicationService)
        };
        foreach (var t in types)
        {
            scope.ServiceProvider.GetRequiredService(t).Should().NotBeNull($"{t.Name} must be resolvable");
        }
        scope.ServiceProvider.GetRequiredService<ITokenProvider>().Should().BeOfType<TestTokenProvider>();
    }

    [Fact]
    public void ApexService_IsPlainScoped_NotATypedHttpClient()
    {
        var sc = new ServiceCollection();
        sc.AddSalesforceCore(_ => { });
        sc.Single(d => d.ServiceType == typeof(IApexService)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void CallingAddSalesforceCoreTwice_DoesNotDuplicatePipelineOrServices()
    {
        var sc = new ServiceCollection();
        sc.AddSalesforceCore(_ => { });
        sc.AddSalesforceCore(_ => { });
        sc.Count(d => d.ServiceType == typeof(IDataService)).Should().Be(1);
        sc.Count(d => d.ServiceType == typeof(ISalesforceApiUsage)).Should().Be(1);
    }

    [Fact]
    public void NoTokenProviderConfigured_RegistersHelpfulPlaceholder_AndNoBackgroundRefresh()
    {
        var sc = new ServiceCollection();
        sc.AddSalesforceCore(_ => { });
        sc.Single(d => d.ServiceType == typeof(ITokenProvider)).ImplementationType.Should().Be(typeof(MissingConfigurationTokenProvider));
        sc.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TokenRefreshBackgroundService)).Should().BeFalse();
    }

    [Fact]
    public void JwtSection_RegistersJwtProvider_AndBackgroundRefresh()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{JwtTokenProviderOptions.SectionName}:ClientId"] = "x"
        }).Build();
        var sc = new ServiceCollection();
        sc.AddSalesforceCore(config);
        sc.Single(d => d.ServiceType == typeof(ITokenProvider)).ImplementationType.Should().Be(typeof(JwtTokenProvider));
        sc.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(TokenRefreshBackgroundService)).Should().Be(1);
    }

    [Fact]
    public void AddSalesforceTokenProvider_ReplacesExisting()
    {
        var sc = new ServiceCollection();
        sc.AddSalesforceCore(_ => { });
        sc.AddSalesforceTokenProvider<TestTokenProvider>();
        sc.Where(d => d.ServiceType == typeof(ITokenProvider)).Should().ContainSingle().Which.ImplementationType.Should().Be(typeof(TestTokenProvider));
    }

    [Fact]
    public void DistributedCacheProvider_WithoutIDistributedCache_FailsFast_EvenWithActionOverload()
    {
        var act = () => new ServiceCollection().AddSalesforceCore(o => o.CacheProvider = CacheProviderType.Distributed);
        act.Should().Throw<InvalidOperationException>().WithMessage("*IDistributedCache*");
    }

    [Fact]
    public void DistributedCacheProvider_WithCache_UsesDistributedProvider()
    {
        using var sp = Build(s => { s.AddDistributedMemoryCache(); s.AddSalesforceCore(o => o.CacheProvider = CacheProviderType.Distributed); });
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICacheProvider>().Should().BeOfType<DistributedCacheProvider>();
    }
}

[CollectionDefinition("GlobalMapperState", DisableParallelization = true)]
public class GlobalMapperStateCollection { }

[Collection("GlobalMapperState")]
public class MapperOptionsTests
{
    [Fact]
    public void RequireSalesforceFieldAttribute_FromActionOverload_IsApplied()
    {
        try
        {
            using var sp = CompositionRootTestsBuild(s => s.AddSalesforceCore(o => o.RequireSalesforceFieldAttribute = true));
            _ = sp.GetRequiredService<IOptions<SalesforceOptions>>().Value;
            Mapping.SalesforceMapper.IsSalesforceFieldAttributeRequired.Should().BeTrue();
        }
        finally
        {
            Mapping.SalesforceMapper.Configure(false);
        }
    }

    private static ServiceProvider CompositionRootTestsBuild(Action<IServiceCollection> register)
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        register(sc);
        return sc.BuildServiceProvider();
    }
}

public class ResilienceDefaultsTests
{
    [Theory]
    [InlineData(30, 30)]
    [InlineData(10, 45)]
    public async Task AnyPerAttemptTimeout_ProducesAValidPipeline(int samplingSeconds, int attemptSeconds)
    {
        await using var host = SalesforceCore.Tests.TestKit.SalesforceTestHost.Create(o =>
        {
            o.MaxRetries = 2;
            o.CircuitBreakerSamplingDuration = TimeSpan.FromSeconds(samplingSeconds);
            o.PerAttemptTimeout = TimeSpan.FromSeconds(attemptSeconds);
            o.TotalRequestTimeout = TimeSpan.FromSeconds(attemptSeconds * 3);
        });
        host.Server.OnJson(HttpMethod.Get, ".*/limits", new { });
        var resp = await host.Get<SalesforceCore.Services.Core.ISalesforceClient>().SendAsync(SalesforceCore.Http.SalesforceRequest.Get("limits"));
        Assert.True(resp.IsSuccess);
    }

    [Fact]
    public void Defaults_AreValidForPolly()
    {
        var o = new SalesforceCore.Models.Configuration.SalesforceOptions();
        Assert.True(o.CircuitBreakerSamplingDuration >= o.PerAttemptTimeout * 2);
    }
}
