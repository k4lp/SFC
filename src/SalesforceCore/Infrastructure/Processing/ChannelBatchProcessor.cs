using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SalesforceCore.Infrastructure.Processing;

/// <summary>
/// High-throughput, bounded, in-memory batch processor built on <see cref="Channel{T}"/>.
/// </summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed class ChannelBatchProcessor<T> : BackgroundService, IBatchProcessor<T>
{
    private readonly Channel<T> _channel;
    private readonly IChannelBatchHandler<T> _handler;
    private readonly ChannelBatchProcessorOptions _options;
    private readonly ILogger<ChannelBatchProcessor<T>> _logger;

    public ChannelBatchProcessor(
        IChannelBatchHandler<T> handler,
        IOptions<ChannelBatchProcessorOptions> options,
        ILogger<ChannelBatchProcessor<T>> logger)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_options.Capacity <= 0) throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be > 0.");
        if (_options.BatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(options), "BatchSize must be > 0.");
        if (_options.FlushInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "FlushInterval must be > 0.");

        _channel = Channel.CreateBounded<T>(new BoundedChannelOptions(_options.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    /// <inheritdoc/>
    public bool TryEnqueue(T item)
    {
        // We intentionally use TryWrite (non-blocking) to keep request paths safe.
        return _channel.Writer.TryWrite(item);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new items and drain what we have.
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<T>(_options.BatchSize);
        using var timer = new PeriodicTimer(_options.FlushInterval);
        var waitToReadTask = _channel.Reader.WaitToReadAsync(stoppingToken).AsTask();
        var tickTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(waitToReadTask, tickTask);

                if (completed == tickTask)
                {
                    var ticked = await tickTask;
                    if (!ticked)
                    {
                        break;
                    }

                    if (batch.Count > 0)
                    {
                        await FlushAsync(batch, stoppingToken);
                        batch.Clear();
                    }

                    tickTask = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                    continue;
                }

                var canRead = await waitToReadTask;
                if (!canRead)
                {
                    break; // channel completed
                }

                while (batch.Count < _options.BatchSize && _channel.Reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count >= _options.BatchSize)
                {
                    await FlushAsync(batch, stoppingToken);
                    batch.Clear();
                }

                waitToReadTask = _channel.Reader.WaitToReadAsync(stoppingToken).AsTask();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal during shutdown.
        }
        finally
        {
            // Drain remaining items after completion.
            while (_channel.Reader.TryRead(out var item))
            {
                batch.Add(item);
                if (batch.Count >= _options.BatchSize)
                {
                    await FlushAsync(batch, CancellationToken.None);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await FlushAsync(batch, CancellationToken.None);
                batch.Clear();
            }
        }
    }

    private async Task FlushAsync(List<T> batch, CancellationToken cancellationToken)
    {
        try
        {
            // Snapshot the batch so handlers can't observe the internal list being cleared/reused.
            // This avoids subtle bugs where a consumer stores the reference and sees an empty list later.
            var snapshot = batch.ToArray();
            await _handler.HandleBatchAsync(snapshot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal during shutdown or cancellation.
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Batch handler failed for {ItemType}. BatchSize={BatchSize}. Backing off {BackoffMs}ms.",
                typeof(T).Name,
                batch.Count,
                (int)_options.FailureBackoff.TotalMilliseconds);

            try
            {
                await Task.Delay(_options.FailureBackoff, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore.
            }
        }
    }
}
