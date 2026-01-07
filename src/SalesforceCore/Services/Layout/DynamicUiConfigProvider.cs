using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Services.Layout;

/// <summary>
/// Loads Dynamic UI options from configuration and optional JSON file with optional file watching.
/// </summary>
public class DynamicUiConfigProvider : IDynamicUiConfigProvider, IDisposable
{
    private readonly IOptionsMonitor<DynamicUiOptions> _optionsMonitor;
    private readonly ILogger<DynamicUiConfigProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _watchedFiles = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private string? _watchedFilePath;
    private DynamicUiOptions _current;

    public DynamicUiConfigProvider(
        IOptionsMonitor<DynamicUiOptions> optionsMonitor,
        ILogger<DynamicUiConfigProvider> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _current = CloneOptions(optionsMonitor.CurrentValue);
        _optionsMonitor.OnChange((options, name) =>
        {
            _ = RefreshAsync().ContinueWith(
                t => _logger.LogError(t.Exception, "Failed to refresh Dynamic UI configuration after options change"),
                TaskContinuationOptions.OnlyOnFaulted);
        });
        _ = InitializeAsync();
    }

    public DynamicUiOptions Current => _current;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var baseOptions = CloneOptions(_optionsMonitor.CurrentValue);
            var path = baseOptions.ConfigFilePath;
            string? fullPath = null;

            if (!string.IsNullOrEmpty(path))
            {
                fullPath = Path.GetFullPath(path);
                // Track the configured file path even if it doesn't exist yet so Created events trigger refresh.
                _watchedFiles.TryAdd(fullPath, 0);
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
                        var fileOptions = JsonSerializer.Deserialize<DynamicUiOptions>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                        });
                        if (fileOptions != null)
                        {
                            baseOptions = MergeOptions(baseOptions, fileOptions);
                        }
                        _logger.LogInformation("Loaded Dynamic UI configuration from {Path}", fullPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load Dynamic UI configuration file {Path}", fullPath);
                    }
                }
                else
                {
                    _logger.LogDebug("Dynamic UI configuration file {Path} not found, using in-memory options.", fullPath);
                }
            }

            EnsureWatcher(baseOptions, fullPath);
            _current = baseOptions;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Dynamic UI configuration provider");
        }
    }

    private void EnsureWatcher(DynamicUiOptions options, string? fullPath)
    {
        if (!options.WatchConfigFile || string.IsNullOrEmpty(fullPath))
        {
            StopWatcher();
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                StopWatcher();
                return;
            }

            // Only watch a single file at a time. Replace any previous watch target.
            _watchedFilePath = fullPath;
            foreach (var existing in _watchedFiles.Keys)
            {
                if (!string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    _watchedFiles.TryRemove(existing, out _);
                }
            }

            if (_watcher != null &&
                (!string.Equals(_watcher.Path, directory, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(_watcher.Filter, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                StopWatcher();
            }

            if (_watcher == null)
            {
                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _watcher.Changed += OnConfigFileChanged;
                _watcher.Created += OnConfigFileChanged;
                _watcher.Renamed += OnConfigFileChanged;
                _watcher.Deleted += OnConfigFileChanged;
            }
            else
            {
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start watcher for Dynamic UI config file");
        }
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        // Avoid tight loop on multiple change notifications.
        if (string.IsNullOrEmpty(_watchedFilePath) ||
            !string.Equals(e.FullPath, _watchedFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = RefreshAsync().ContinueWith(
            t => _logger.LogError(t.Exception, "Failed to refresh Dynamic UI configuration after file change"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private void StopWatcher()
    {
        _watchedFilePath = null;
        _watchedFiles.Clear();

        if (_watcher == null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnConfigFileChanged;
            _watcher.Created -= OnConfigFileChanged;
            _watcher.Renamed -= OnConfigFileChanged;
            _watcher.Deleted -= OnConfigFileChanged;
        }
        catch
        {
            // Ignore handler cleanup issues; we still dispose the watcher.
        }

        _watcher.Dispose();
        _watcher = null;
    }

    private static DynamicUiOptions CloneOptions(DynamicUiOptions options)
    {
        return JsonSerializer.Deserialize<DynamicUiOptions>(
                   JsonSerializer.Serialize(options)) ?? new DynamicUiOptions();
    }

    private static DynamicUiOptions MergeOptions(DynamicUiOptions baseOptions, DynamicUiOptions fileOptions)
    {
        var merged = CloneOptions(baseOptions);

        // Simple overrides
        merged.ConfigFilePath = fileOptions.ConfigFilePath ?? merged.ConfigFilePath;
        merged.WatchConfigFile = fileOptions.WatchConfigFile;
        merged.PermissionCacheDuration = fileOptions.PermissionCacheDuration != default ? fileOptions.PermissionCacheDuration : merged.PermissionCacheDuration;
        merged.LayoutCacheDuration = fileOptions.LayoutCacheDuration != default ? fileOptions.LayoutCacheDuration : merged.LayoutCacheDuration;
        merged.BypassCache = fileOptions.BypassCache;
        merged.HideInaccessibleNavItems = fileOptions.HideInaccessibleNavItems;
        merged.HideInaccessibleFields = fileOptions.HideInaccessibleFields;
        merged.HideUnauthorizedActions = fileOptions.HideUnauthorizedActions;
        merged.DefaultFormColumns = fileOptions.DefaultFormColumns > 0 ? fileOptions.DefaultFormColumns : merged.DefaultFormColumns;
        merged.DefaultPageSize = fileOptions.DefaultPageSize > 0 ? fileOptions.DefaultPageSize : merged.DefaultPageSize;
        merged.MaxPageSize = fileOptions.MaxPageSize > 0 ? fileOptions.MaxPageSize : merged.MaxPageSize;

        // Navigation
        if (fileOptions.Navigation != null)
        {
            merged.Navigation = fileOptions.Navigation;
        }

        // Objects merge/override
        foreach (var kvp in fileOptions.Objects)
        {
            merged.Objects[kvp.Key] = kvp.Value;
        }

        // Feature flags merge
        foreach (var kvp in fileOptions.FeatureFlags)
        {
            merged.FeatureFlags[kvp.Key] = kvp.Value;
        }

        // Theming
        if (fileOptions.Theming != null)
        {
            merged.Theming = fileOptions.Theming;
        }

        return merged;
    }

    public void Dispose()
    {
        StopWatcher();
        _gate.Dispose();
    }
}
