using System.Threading;
using System.Threading.Tasks;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Layout;

/// <summary>
/// Provides a centralized, reloadable source of Dynamic UI configuration.
/// </summary>
public interface IDynamicUiConfigProvider
{
    /// <summary>
    /// Gets the current merged configuration (appsettings + dynamic_ui.json).
    /// </summary>
    DynamicUiOptions Current { get; }

    /// <summary>
    /// Forces a reload from disk if configured.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
