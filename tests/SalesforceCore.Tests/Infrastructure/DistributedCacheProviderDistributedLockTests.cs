using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Infrastructure.Locking;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Caching;
using Xunit;

namespace SalesforceCore.Tests.Infrastructure;

public class DistributedCacheProviderDistributedLockTests
{
    [Fact]
    public async Task GetOrCreateAsync_ShouldUseDistributedLockProvider_WhenAvailable()
    {
        var lockCalls = new List<string>();
        var lockProvider = new StubLockProvider(resource =>
        {
            lockCalls.Add(resource);
            return new StubLockHandle(resource);
        });

        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new SalesforceOptions { CacheKeyPrefix = "TEST_" });
        var provider = new DistributedCacheProvider(
            cache.Object,
            options,
            NullLogger<DistributedCacheProvider>.Instance,
            lockProvider);

        var value = await provider.GetOrCreateAsync("k", _ => Task.FromResult<string?>("v"));

        value.Should().Be("v");
        lockCalls.Should().ContainSingle()
            .Which.Should().Be("sf_cache:TEST_k");
    }

    private sealed class StubLockProvider : IDistributedLockProvider
    {
        private readonly Func<string, IDistributedLockHandle?> _acquire;

        public StubLockProvider(Func<string, IDistributedLockHandle?> acquire)
        {
            _acquire = acquire;
        }

        public Task<IDistributedLockHandle?> TryAcquireAsync(string resourceName, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_acquire(resourceName));
        }
    }

    private sealed class StubLockHandle : IDistributedLockHandle
    {
        public StubLockHandle(string resourceName)
        {
            ResourceName = resourceName;
        }

        public string ResourceName { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

