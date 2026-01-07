using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Configuration;

/// <summary>
/// Service for managing Salesforce module configuration.
/// Supports both file-based and runtime configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the current configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current configuration.</returns>
    Task<SalesforceConfig> GetConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets configuration for a specific module.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Module configuration or null.</returns>
    Task<ModuleConfig?> GetModuleConfigAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all visible modules for navigation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of visible modules.</returns>
    Task<List<ModuleConfig>> GetVisibleModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets modules grouped by category.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Modules grouped by category.</returns>
    Task<Dictionary<string, List<ModuleConfig>>> GetModulesByCategoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-discovers modules from Salesforce.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of discovered modules.</returns>
    Task<List<ModuleConfig>> AutoDiscoverModulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves configuration to file.
    /// </summary>
    /// <param name="config">Configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveConfigurationAsync(SalesforceConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads configuration from file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReloadConfigurationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a module configuration.
    /// </summary>
    /// <param name="module">Module configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddOrUpdateModuleAsync(ModuleConfig module, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a module configuration.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveModuleAsync(string sObject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets field override for a specific field.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="fieldName">Field name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Field override or null.</returns>
    Task<FieldOverride?> GetFieldOverrideAsync(string sObject, string fieldName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets relationship configuration for a field.
    /// </summary>
    /// <param name="sObject">Object API name.</param>
    /// <param name="fieldName">Field name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relationship configuration or null.</returns>
    Task<RelationshipConfig?> GetRelationshipConfigAsync(string sObject, string fieldName, CancellationToken cancellationToken = default);
}
