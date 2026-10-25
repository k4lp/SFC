using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Infrastructure.Processing;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Caching.SqlServer;

internal sealed class SqlCacheAccessBatchHandler : IChannelBatchHandler<CacheAccessEvent>
{
    // SQL Server parameter limit is 2100; we use 4 parameters per row (id, lastAccessed, delta, expires).
    private const int SqlServerMaxParameters = 2100;
    private const int ParametersPerRow = 4;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlCacheAccessBatchHandler> _logger;
    private readonly SalesforceOptions _salesforceOptions;

    public SqlCacheAccessBatchHandler(
        IServiceScopeFactory scopeFactory,
        IOptions<SalesforceOptions> salesforceOptions,
        ILogger<SqlCacheAccessBatchHandler> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _salesforceOptions = salesforceOptions?.Value ?? throw new ArgumentNullException(nameof(salesforceOptions));
    }

    public async Task HandleBatchAsync(IReadOnlyList<CacheAccessEvent> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var aggregated = Aggregate(items);
        if (aggregated.Count == 0)
        {
            return;
        }

        var maxRowsBySqlLimit = SqlServerMaxParameters / ParametersPerRow;
        var configuredMax = _salesforceOptions.SqlCacheWriteBehind.MaxBatchSize;
        var maxRows = Math.Max(1, Math.Min(maxRowsBySqlLimit, configuredMax));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<EncryptedSqlServerCacheDbContext>();

        var entityType = context.Model.FindEntityType(typeof(EncryptedCacheEntry));
        var tableName = entityType?.GetTableName() ?? "SalesforceEncryptedCacheEntries";
        var schema = entityType?.GetSchema();
        var qualifiedTable = string.IsNullOrWhiteSpace(schema)
            ? $"[{tableName}]"
            : $"[{schema}].[{tableName}]";

        foreach (var chunk in Chunk(aggregated, maxRows))
        {
            await ExecuteBatchUpdateAsync(context, qualifiedTable, chunk, cancellationToken);
        }
    }

    private async Task ExecuteBatchUpdateAsync(
        EncryptedSqlServerCacheDbContext context,
        string qualifiedTable,
        IReadOnlyList<AggregatedAccess> batch,
        CancellationToken cancellationToken)
    {
        // Build VALUES list and parameters.
        var sql = new System.Text.StringBuilder();
        var parameters = new List<object>(batch.Count * ParametersPerRow);

        sql.AppendLine($"UPDATE ce");
        sql.AppendLine("SET");
        sql.AppendLine("  ce.LastAccessedAt = CASE WHEN v.LastAccessedAt > ce.LastAccessedAt THEN v.LastAccessedAt ELSE ce.LastAccessedAt END,");
        sql.AppendLine("  ce.AccessCount = ce.AccessCount + v.AccessCountDelta,");
        sql.AppendLine("  ce.ExpiresAtTime = CASE WHEN v.NewExpiresAtTime IS NOT NULL AND v.NewExpiresAtTime > ce.ExpiresAtTime THEN v.NewExpiresAtTime ELSE ce.ExpiresAtTime END");
        sql.AppendLine($"FROM {qualifiedTable} AS ce");
        sql.Append("INNER JOIN (VALUES ");

        for (var i = 0; i < batch.Count; i++)
        {
            if (i > 0) sql.Append(", ");

            var idParam = $"@p{i * ParametersPerRow + 0}";
            var lastAccessParam = $"@p{i * ParametersPerRow + 1}";
            var deltaParam = $"@p{i * ParametersPerRow + 2}";
            var expiresParam = $"@p{i * ParametersPerRow + 3}";

            sql.Append($"({idParam}, {lastAccessParam}, {deltaParam}, {expiresParam})");

            parameters.Add(batch[i].EntryId);
            parameters.Add(batch[i].LastAccessedAt);
            parameters.Add(batch[i].AccessCountDelta);
            parameters.Add((object?)batch[i].NewExpiresAtTime ?? DBNull.Value);
        }

        sql.AppendLine(") AS v(Id, LastAccessedAt, AccessCountDelta, NewExpiresAtTime) ON ce.Id = v.Id;");

        try
        {
            await context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CACHE_AUDIT: Failed to flush SQL cache access batch. DistinctKeys={DistinctKeys}",
                batch.Count);
        }
    }

    private static List<AggregatedAccess> Aggregate(IReadOnlyList<CacheAccessEvent> items)
    {
        var map = new Dictionary<string, AggregatedAccess>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.EntryId))
            {
                continue;
            }

            if (!map.TryGetValue(item.EntryId, out var aggregated))
            {
                aggregated = new AggregatedAccess
                {
                    EntryId = item.EntryId,
                    LastAccessedAt = item.AccessedAt,
                    AccessCountDelta = item.AccessCountDelta,
                    NewExpiresAtTime = item.NewExpiresAtTime
                };
                map[item.EntryId] = aggregated;
                continue;
            }

            aggregated.AccessCountDelta += item.AccessCountDelta;
            if (item.AccessedAt > aggregated.LastAccessedAt)
            {
                aggregated.LastAccessedAt = item.AccessedAt;
            }

            if (item.NewExpiresAtTime.HasValue &&
                (!aggregated.NewExpiresAtTime.HasValue || item.NewExpiresAtTime.Value > aggregated.NewExpiresAtTime.Value))
            {
                aggregated.NewExpiresAtTime = item.NewExpiresAtTime;
            }

            map[item.EntryId] = aggregated;
        }

        return map.Values.ToList();
    }

    private static IEnumerable<IReadOnlyList<AggregatedAccess>> Chunk(IReadOnlyList<AggregatedAccess> items, int chunkSize)
    {
        for (int i = 0; i < items.Count; i += chunkSize)
        {
            yield return items.Skip(i).Take(chunkSize).ToList();
        }
    }

    private sealed class AggregatedAccess
    {
        public required string EntryId { get; init; }
        public DateTimeOffset LastAccessedAt { get; set; }
        public long AccessCountDelta { get; set; }
        public DateTimeOffset? NewExpiresAtTime { get; set; }
    }
}
