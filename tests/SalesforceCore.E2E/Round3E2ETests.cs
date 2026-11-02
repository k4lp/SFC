using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.AspNetCore.Authentication;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Apex;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Files;
using SalesforceCore.Services.Metadata;
using Xunit;

namespace SalesforceCore.E2E;

public class ApexTotalsDto { public string? AccountName { get; set; } public decimal Total { get; set; } }

/// <summary>2.4.0 end-to-end regressions: JWT re-sign, describe errors, large downloads, Apex DTOs, bulk nulls, gzip, stores.</summary>
public class Round3E2ETests
{
    private static Task<E2EHost> StartJwtAsync(RSA rsa) => E2EHost.StartAsync(new()
    {
        ["SalesforceJwt:Username"] = "u@x.com",
        ["SalesforceJwt:PrivateKey"] = rsa.ExportPkcs8PrivateKeyPem(),
        ["SalesforceClientCredentials:ClientId"] = null,
        ["SalesforceClientCredentials:ClientSecret"] = null,
    });

    private static Task RunParallelQueries(E2EHost h, int n) => Task.WhenAll(Enumerable.Range(0, n).Select(async _ =>
    {
        await using var s = h.Root.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<IDataService>().QueryAsync("SELECT Id FROM Account");
    }));

    [Fact]
    public async Task Jwt_ColdStartAndEveryRefresh_SignSuccessfully_SingleFlight()
    {
        using var rsa = RSA.Create(2048);
        await using var h = await StartJwtAsync(rsa);
        h.Get<ITokenProvider>().GetType().Name.Should().Be("JwtTokenProvider");
        h.Fake.TokenDelay = TimeSpan.FromMilliseconds(50);

        await RunParallelQueries(h, 20);
        h.Fake.TokenRequests.Should().Be(1);
        h.Fake.LastGrantType.Should().Be("urn:ietf:params:oauth:grant-type:jwt-bearer");

        // 2.3.0: the second assertion reused a cached signature provider bound to the disposed RSA -> ObjectDisposedException.
        for (var round = 0; round < 3; round++)
        {
            h.Fake.InvalidateSessions();
            var before = h.Fake.TokenRequests;
            await RunParallelQueries(h, 20);
            (h.Fake.TokenRequests - before).Should().Be(1);
        }
    }

    [Fact]
    public async Task Describe_ServerError_Propagates_NotFound_ReturnsNull_CacheIsCaseInsensitive()
    {
        await using var h = await E2EHost.StartAsync();
        var schema = h.Get<ISchemaService>();
        (await schema.GetDescribeAsync("Account"))!.Fields.Should().NotBeEmpty();
        var reqs = h.Fake.DescribeRequests;
        await schema.GetDescribeAsync("account");
        h.Fake.DescribeRequests.Should().Be(reqs, "describe is cached case-insensitively");
        (await schema.GetDescribeAsync("Nope__c")).Should().BeNull();
        // Last: the failures may open the circuit breaker for this host.
        for (var i = 0; i < 10; i++) h.Fake.DescribeFailures.Enqueue(503);
        await FluentActions.Awaiting(() => schema.GetDescribeAsync("Contact")).Should().ThrowAsync<Exception>();
        (await Record.ExceptionAsync(() => schema.GetDescribeAsync("Contact")))?.GetType().Should().NotBe(typeof(SalesforceNotFoundException));

    }

    [Fact]
    public async Task GlobalDescribe_IsCached()
    {
        await using var h = await E2EHost.StartAsync();
        var schema = h.Get<ISchemaService>();
        await schema.GetAllObjectsAsync(); await schema.GetAllObjectsAsync();
        (await schema.GetAllObjectsAsync()).Should().NotBeEmpty();
        h.Fake.GlobalDescribeRequests.Should().Be(1);
    }

    [Fact]
    public async Task Download_FileOver10MB_Succeeds_BufferedAndStreamed()
    {
        await using var h = await E2EHost.StartAsync();
        var files = h.Get<IFileService>();
        var bytes = await files.DownloadAsync("068000000000001AAA");
        bytes.Should().Equal(h.Fake.FileBytes);
        using var ms = new MemoryStream();
        await files.DownloadToStreamAsync("068000000000001AAA", ms);
        ms.ToArray().Should().Equal(h.Fake.FileBytes);
    }

    [Fact]
    public async Task Apex_TypedDto_BindsCamelCaseCaseInsensitively()
    {
        await using var h = await E2EHost.StartAsync();
        var dto = await h.Get<IApexService>().GetAsync<ApexTotalsDto>("echo/object");
        dto.AccountName.Should().Be("Acme");
        dto.Total.Should().Be(12.5m);
        (await h.Get<IApexService>().GetAsync<string>("echo/string")).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, "001000000000001AAA,#N/A,")]
    [InlineData("false", "001000000000001AAA,,")]
    public async Task BulkUpdate_ExplicitNull_SentAsNA_MissingKeyLeftEmpty(string? setting, string expectedRow)
    {
        await using var h = await E2EHost.StartAsync(setting == null ? null : new() { ["Salesforce:BulkNullsAsNA"] = setting });
        h.Fake.BulkResultsFor = _ => ("sf__Id,sf__Created\n", "sf__Id,sf__Error\n", 2, 0);
        await h.Get<IBulkService>().UpdateAsync("Account", new[]
        {
            new Dictionary<string, object?> { ["Id"] = "001000000000001AAA", ["Phone"] = null },
            new Dictionary<string, object?> { ["Id"] = "001000000000002AAA", ["Phone"] = "1", ["Fax"] = "2" },
        });
        var lines = h.Fake.LastBulkUpload!.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        lines[1].Should().Be(expectedRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compression_IsOptIn_AndDecompressesTransparently(bool enabled)
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:EnableResponseCompression"] = enabled.ToString() });
        h.Fake.GzipResponses = true;
        var result = await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        result.Should().NotBeNull();
        (h.Fake.LastAcceptEncoding ?? "").Contains("gzip").Should().Be(enabled);
    }

    [Fact]
    public async Task TicketStore_RoundTrips_AndStoresCiphertextOnly()
    {
        var cache = new RecordingCache();
        var store = new DistributedCacheTicketStore(cache, NullLogger<DistributedCacheTicketStore>.Instance,
            TimeSpan.FromMinutes(5), new EphemeralDataProtectionProvider());
        var props = new AuthenticationProperties();
        props.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = "00DSECRETTOKEN" } });
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u") }, "sf")), props, "sf");

        var key = await store.StoreAsync(ticket);
        Encoding.UTF8.GetString(cache.LastValue!).Should().NotContain("SECRETTOKEN");
        (await store.RetrieveAsync(key))!.Properties.GetTokenValue("access_token").Should().Be("00DSECRETTOKEN");
        await store.RemoveAsync(key);
        (await store.RetrieveAsync(key)).Should().BeNull();

        // A store with a different key ring cannot read the entry (tampered/foreign data is rejected, not thrown).
        var key2 = await store.StoreAsync(ticket);
        var other = new DistributedCacheTicketStore(cache, NullLogger<DistributedCacheTicketStore>.Instance, null, new EphemeralDataProtectionProvider());
        (await other.RetrieveAsync(key2)).Should().BeNull();
    }

    [Fact]
    public async Task DistributedCacheProvider_RoundTrip_GetOrCreateSingleFlight()
    {
        var provider = new DistributedCacheProvider(new RecordingCache(), Options.Create(new SalesforceOptions()), NullLogger<DistributedCacheProvider>.Instance);
        await provider.SetAsync("k", new ApexTotalsDto { AccountName = "A", Total = 1 });
        (await provider.GetAsync<ApexTotalsDto>("k"))!.AccountName.Should().Be("A");
        var calls = 0;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetOrCreateAsync("g", async ct =>
        {
            Interlocked.Increment(ref calls); await Task.Delay(30, ct); return (string?)"v";
        })));
        calls.Should().Be(1);
        await provider.RemoveAsync("k");
        (await provider.GetAsync<ApexTotalsDto>("k")).Should().BeNull();
    }

    [Fact]
    public async Task Host_DisposesCleanly_AfterTraffic()
    {
        var h = await E2EHost.StartAsync();
        await h.Get<IDataService>().QueryAsync("SELECT Id FROM Account");
        await FluentActions.Awaiting(async () => await h.DisposeAsync()).Should().NotThrowAsync();
    }

    private sealed class RecordingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
        public byte[]? LastValue;
        public byte[]? Get(string key) => _inner.Get(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);
        public void Refresh(string key) => _inner.Refresh(key);
        public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
        public void Remove(string key) => _inner.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { LastValue = value; _inner.Set(key, value, options); }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { LastValue = value; return _inner.SetAsync(key, value, options, token); }
    }
}
