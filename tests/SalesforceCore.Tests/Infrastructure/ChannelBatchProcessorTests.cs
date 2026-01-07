using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SalesforceCore.Infrastructure.Processing;
using Xunit;

namespace SalesforceCore.Tests.Infrastructure;

public class ChannelBatchProcessorTests
{
    [Fact]
    public async Task Processor_ShouldFlushByBatchSize()
    {
        var received = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new DelegateBatchHandler<int>(items =>
        {
            received.TrySetResult(items);
            return Task.CompletedTask;
        });

        var options = Options.Create(new ChannelBatchProcessorOptions
        {
            Capacity = 10,
            BatchSize = 3,
            FlushInterval = TimeSpan.FromMinutes(1)
        });

        var processor = new ChannelBatchProcessor<int>(handler, options, NullLogger<ChannelBatchProcessor<int>>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await processor.StartAsync(cts.Token);

        processor.TryEnqueue(1).Should().BeTrue();
        processor.TryEnqueue(2).Should().BeTrue();
        processor.TryEnqueue(3).Should().BeTrue();

        var batch = await received.Task.WaitAsync(cts.Token);
        batch.Should().BeEquivalentTo(new[] { 1, 2, 3 }, o => o.WithStrictOrdering());

        await processor.StopAsync(cts.Token);
    }

    [Fact]
    public async Task Processor_ShouldFlushByInterval()
    {
        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new DelegateBatchHandler<string>(items =>
        {
            received.TrySetResult(items);
            return Task.CompletedTask;
        });

        var options = Options.Create(new ChannelBatchProcessorOptions
        {
            Capacity = 10,
            BatchSize = 100,
            FlushInterval = TimeSpan.FromMilliseconds(50)
        });

        var processor = new ChannelBatchProcessor<string>(handler, options, NullLogger<ChannelBatchProcessor<string>>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await processor.StartAsync(cts.Token);

        processor.TryEnqueue("a").Should().BeTrue();

        var batch = await received.Task.WaitAsync(cts.Token);
        batch.Should().ContainSingle().Which.Should().Be("a");

        await processor.StopAsync(cts.Token);
    }

    [Fact]
    public async Task TryEnqueue_ShouldReturnFalseWhenCapacityIsExceeded()
    {
        var handler = new DelegateBatchHandler<int>(_ => Task.CompletedTask);

        var options = Options.Create(new ChannelBatchProcessorOptions
        {
            Capacity = 1,
            BatchSize = 100,
            FlushInterval = TimeSpan.FromMinutes(1)
        });

        var processor = new ChannelBatchProcessor<int>(handler, options, NullLogger<ChannelBatchProcessor<int>>.Instance);

        // Don't start the background worker; nothing drains the channel.
        processor.TryEnqueue(1).Should().BeTrue();
        processor.TryEnqueue(2).Should().BeFalse();
    }

    private sealed class DelegateBatchHandler<T> : IChannelBatchHandler<T>
    {
        private readonly Func<IReadOnlyList<T>, Task> _handler;

        public DelegateBatchHandler(Func<IReadOnlyList<T>, Task> handler)
        {
            _handler = handler;
        }

        public Task HandleBatchAsync(IReadOnlyList<T> items, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _handler(items);
        }
    }
}

