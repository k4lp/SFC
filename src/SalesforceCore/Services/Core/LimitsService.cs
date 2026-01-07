using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Implementation of Salesforce API usage limits service.
/// Queries the /limits endpoint to provide visibility into API consumption.
/// </summary>
public class LimitsService : ILimitsService
{
    private readonly ISalesforceClient _client;
    private readonly ILogger<LimitsService> _logger;

    /// <summary>
    /// Creates a new LimitsService.
    /// </summary>
    public LimitsService(
        ISalesforceClient client,
        ILogger<LimitsService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, LimitInfo>> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching API limits");

        var response = await _client.GetAsync(SalesforceConstants.Paths.Limits, cancellationToken);
        var limits = new Dictionary<string, LimitInfo>();

        // Response is a JSON object where keys are limit names
        var responseObj = response.AsObject();

        foreach (var property in responseObj)
        {
            var limitData = property.Value as JsonObject;
            if (limitData == null) continue;

            var limitInfo = new LimitInfo
            {
                Max = limitData["Max"]?.GetValue<int>() ?? 0,
                Remaining = limitData["Remaining"]?.GetValue<int>() ?? 0
            };

            limits[property.Key] = limitInfo;
        }

        _logger.LogDebug("Retrieved {Count} API limits", limits.Count);
        return limits;
    }

    /// <inheritdoc/>
    public async Task<LimitInfo?> GetLimitAsync(string limitName, CancellationToken cancellationToken = default)
    {
        var limits = await GetLimitsAsync(cancellationToken);
        return limits.TryGetValue(limitName, out var limit) ? limit : null;
    }

    /// <inheritdoc/>
    public async Task<List<LimitWarning>> CheckLimitsAsync(int thresholdPercentage = 80, CancellationToken cancellationToken = default)
    {
        if (thresholdPercentage < 0 || thresholdPercentage > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdPercentage), "Threshold must be between 0 and 100");
        }

        var limits = await GetLimitsAsync(cancellationToken);
        var warnings = new List<LimitWarning>();

        foreach (var (name, limit) in limits)
        {
            // Skip limits with Max = 0 (unlimited or not applicable)
            if (limit.Max <= 0) continue;

            var usagePercentage = limit.UsagePercentage;

            if (usagePercentage >= 100)
            {
                warnings.Add(new LimitWarning
                {
                    Name = name,
                    Limit = limit,
                    Severity = LimitSeverity.Exceeded,
                    Message = $"EXCEEDED: {name} has reached its limit ({limit.Used:N0}/{limit.Max:N0})"
                });
                _logger.LogError("API limit exceeded: {LimitName} ({Used}/{Max})", name, limit.Used, limit.Max);
            }
            else if (usagePercentage >= 90)
            {
                warnings.Add(new LimitWarning
                {
                    Name = name,
                    Limit = limit,
                    Severity = LimitSeverity.Critical,
                    Message = $"CRITICAL: {name} is at {usagePercentage:F1}% ({limit.Used:N0}/{limit.Max:N0})"
                });
                _logger.LogWarning("API limit critical: {LimitName} at {Percentage}% ({Used}/{Max})",
                    name, usagePercentage, limit.Used, limit.Max);
            }
            else if (usagePercentage >= thresholdPercentage)
            {
                warnings.Add(new LimitWarning
                {
                    Name = name,
                    Limit = limit,
                    Severity = LimitSeverity.Warning,
                    Message = $"WARNING: {name} is at {usagePercentage:F1}% ({limit.Used:N0}/{limit.Max:N0})"
                });
                _logger.LogWarning("API limit warning: {LimitName} at {Percentage}% ({Used}/{Max})",
                    name, usagePercentage, limit.Used, limit.Max);
            }
        }

        return warnings.OrderByDescending(w => w.Severity).ThenByDescending(w => w.Limit.UsagePercentage).ToList();
    }

    /// <inheritdoc/>
    public async Task<double?> GetUsagePercentageAsync(string limitName, CancellationToken cancellationToken = default)
    {
        var limit = await GetLimitAsync(limitName, cancellationToken);
        return limit?.UsagePercentage;
    }
}
