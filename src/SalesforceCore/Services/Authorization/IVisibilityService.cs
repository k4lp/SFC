using System.Threading;
using System.Threading.Tasks;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Service for evaluating granular visibility policies.
/// </summary>
public interface IVisibilityService
{
    /// <summary>
    /// Evaluates the specified policy against the current user context.
    /// </summary>
    /// <param name="policyName">The name of the policy to evaluate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the policy is met (or if the policy doesn't exist/is empty); otherwise, false.</returns>
    Task<bool> EvaluatePolicyAsync(string? policyName, CancellationToken cancellationToken = default);
}
