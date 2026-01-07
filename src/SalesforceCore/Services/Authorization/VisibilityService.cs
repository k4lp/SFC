using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Authorization;

using System.Threading;

/// <summary>
/// Default implementation of the atomic visibility policy evaluator.
/// </summary>
public class VisibilityService : IVisibilityService
{
    private readonly IOptionsSnapshot<VisibilityOptions> _options;
    private readonly IEnumerable<IVisibilityRequirementHandler> _handlers;
    private readonly IUserContextProvider _userProvider;
    private readonly ILogger<VisibilityService> _logger;

    public VisibilityService(
        IOptionsSnapshot<VisibilityOptions> options,
        IEnumerable<IVisibilityRequirementHandler> handlers,
        IUserContextProvider userProvider,
        ILogger<VisibilityService> logger)
    {
        _options = options;
        _handlers = handlers;
        _userProvider = userProvider;
        _logger = logger;
    }

    public async Task<bool> EvaluatePolicyAsync(string? policyName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(policyName))
        {
            return true; // No policy means visible by default
        }

        var config = _options.Value;
        if (!config.Policies.TryGetValue(policyName, out var policy))
        {
            _logger.LogWarning("Visibility policy '{PolicyName}' not found in configuration. defaulting to Hidden.", policyName);
            return false; // Fail safe: if policy missing, hide it.
        }

        if (policy.Requirements == null || policy.Requirements.Count == 0)
        {
            return true; // Empty policy = visible
        }

        var user = _userProvider.GetUser();
        if (user == null)
        {
            _logger.LogDebug("Policy '{PolicyName}' evaluation failed: No user context.", policyName);
            return false;
        }

        bool isAnyStrategy = string.Equals(policy.Strategy, "Any", StringComparison.OrdinalIgnoreCase);

        if (isAnyStrategy)
        {
            foreach (var req in policy.Requirements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await EvaluateRequirementAsync(req, user, cancellationToken))
                {
                    return true;
                }
            }
            return false;
        }
        else // All
        {
            foreach (var req in policy.Requirements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await EvaluateRequirementAsync(req, user, cancellationToken))
                {
                    return false;
                }
            }
            return true;
        }
    }

    private async Task<bool> EvaluateRequirementAsync(
        VisibilityRequirementConfig req,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var handler = _handlers.FirstOrDefault(h => string.Equals(h.Type, req.Type, StringComparison.OrdinalIgnoreCase));
        if (handler == null)
        {
            _logger.LogError("No handler registered for visibility requirement type '{Type}'.", req.Type);
            return false; // Unknown requirement type = fail
        }

        try
        {
            return await handler.HandleAsync(req.Settings ?? new JsonObject(), user, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating visibility requirement '{Type}'.", req.Type);
            return false;
        }
    }
}
