using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Utilities;

namespace SalesforceCore.Services.Configuration;

/// <summary>
/// Implementation of module configuration management.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ISchemaService _schemaService;
    private readonly SalesforceOptions _options;
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configFilePath;
    private SalesforceConfig? _cachedConfig;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Creates a new ConfigurationService.
    /// </summary>
    public ConfigurationService(
        ISchemaService schemaService,
        IOptions<SalesforceOptions> options,
        ILogger<ConfigurationService> logger)
    {
        _schemaService = schemaService;
        _options = options.Value;
        _logger = logger;
        _configFilePath = _options.ConfigFilePath;
    }

    /// <inheritdoc/>
    public async Task<SalesforceConfig> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedConfig != null)
        {
            return _cachedConfig;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedConfig != null)
            {
                return _cachedConfig;
            }

            _cachedConfig = await LoadConfigurationAsync(cancellationToken);
            return _cachedConfig;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ModuleConfig?> GetModuleConfigAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigurationAsync(cancellationToken);
        return config.Modules.FirstOrDefault(m =>
            m.SObject.Equals(sObject, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public async Task<List<ModuleConfig>> GetVisibleModulesAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetConfigurationAsync(cancellationToken);
        return config.Modules
            .Where(m => m.IsVisible)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Label ?? m.SObject)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, List<ModuleConfig>>> GetModulesByCategoryAsync(CancellationToken cancellationToken = default)
    {
        var modules = await GetVisibleModulesAsync(cancellationToken);
        return modules
            .GroupBy(m => m.Category)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(m => m.SortOrder).ThenBy(m => m.Label ?? m.SObject).ToList());
    }

    /// <inheritdoc/>
    public async Task<List<ModuleConfig>> AutoDiscoverModulesAsync(CancellationToken cancellationToken = default)
    {
        var discovered = new List<ModuleConfig>();

        try
        {
            var allObjects = await _schemaService.GetAllObjectsAsync(cancellationToken);

            foreach (var obj in allObjects.Where(o => o.Queryable && o.Retrieveable))
            {
                var category = SalesforceConventions.CategorizeObject(obj.Name);
                var icon = SalesforceConventions.GetDefaultIcon(obj.Name);

                discovered.Add(new ModuleConfig
                {
                    SObject = obj.Name,
                    Label = obj.Label,
                    PluralLabel = obj.LabelPlural,
                    Category = category,
                    Icon = icon,
                    IsVisible = true,
                    ListFields = new List<string> { "Name", "CreatedDate" },
                    DetailFields = new List<string> { "Name", "CreatedById", "CreatedDate", "LastModifiedDate" }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to auto-discover modules");
        }

        return discovered;
    }

    /// <inheritdoc/>
    public async Task SaveConfigurationAsync(SalesforceConfig config, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);
            _cachedConfig = config;
            _logger.LogInformation("Configuration saved to {Path}", _configFilePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ReloadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _cachedConfig = await LoadConfigurationAsync(cancellationToken);
            _logger.LogInformation("Configuration reloaded from {Path}", _configFilePath);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task AddOrUpdateModuleAsync(ModuleConfig module, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigurationAsync(cancellationToken);

        var existing = config.Modules.FirstOrDefault(m =>
            m.SObject.Equals(module.SObject, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            config.Modules.Remove(existing);
        }

        config.Modules.Add(module);
        await SaveConfigurationAsync(config, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RemoveModuleAsync(string sObject, CancellationToken cancellationToken = default)
    {
        var config = await GetConfigurationAsync(cancellationToken);

        var existing = config.Modules.FirstOrDefault(m =>
            m.SObject.Equals(sObject, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            config.Modules.Remove(existing);
            await SaveConfigurationAsync(config, cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<FieldOverride?> GetFieldOverrideAsync(string sObject, string fieldName, CancellationToken cancellationToken = default)
    {
        var module = await GetModuleConfigAsync(sObject, cancellationToken);
        if (module?.FieldOverrides == null)
        {
            return null;
        }

        return module.FieldOverrides.TryGetValue(fieldName, out var fieldOverride) ? fieldOverride : null;
    }

    /// <inheritdoc/>
    public async Task<RelationshipConfig?> GetRelationshipConfigAsync(string sObject, string fieldName, CancellationToken cancellationToken = default)
    {
        var module = await GetModuleConfigAsync(sObject, cancellationToken);
        return module?.RelationshipConfigs?.FirstOrDefault(r =>
            r.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<SalesforceConfig> LoadConfigurationAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
                var config = JsonSerializer.Deserialize<SalesforceConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration from {Path}", _configFilePath);
        }

        // Return default configuration
        return new SalesforceConfig
        {
            GlobalSettings = new GlobalSettings(),
            Modules = new List<ModuleConfig>()
        };
    }
}
