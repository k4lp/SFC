using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Authorization;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Layout;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Implementation of permission service that evaluates object and field-level permissions.
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly ISchemaService _schemaService;
    private readonly ICacheProvider _cacheProvider;
    private readonly IUserContextProvider _userContextProvider;
    private readonly IDynamicUiConfigProvider? _configProvider;
    private readonly ILogger<PermissionService> _logger;
    private readonly DynamicUiOptions _options;
    private static readonly ConcurrentDictionary<string, byte> _knownUserSuffixes = new(StringComparer.OrdinalIgnoreCase);

    private const string CacheKeyPrefix = "perm_";

    public PermissionService(
        ISchemaService schemaService,
        ICacheProvider cacheProvider,
        IUserContextProvider userContextProvider,
        IOptions<DynamicUiOptions> options,
        IDynamicUiConfigProvider? configProvider,
        ILogger<PermissionService> logger)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
        _cacheProvider = cacheProvider ?? throw new ArgumentNullException(nameof(cacheProvider));
        _userContextProvider = userContextProvider ?? throw new ArgumentNullException(nameof(userContextProvider));
        _configProvider = configProvider;
        _options = options?.Value ?? new DynamicUiOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ObjectPermissionSnapshot> GetPermissionsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            throw new ArgumentNullException(nameof(objectName));

        var options = Options;
        var cacheKey = $"{CacheKeyPrefix}{objectName.ToLowerInvariant()}{GetUserCacheKeySuffix()}";

        if (options.BypassCache)
        {
            return await BuildPermissionSnapshotAsync(objectName, cancellationToken);
        }

        var snapshot = await _cacheProvider.GetOrCreateAsync(
            cacheKey,
            async ct => await BuildPermissionSnapshotAsync(objectName, ct),
            options.PermissionCacheDuration,
            cancellationToken);

        return snapshot ?? throw new InvalidOperationException($"Failed to get permissions for {objectName}");
    }

    /// <inheritdoc/>
    public async Task<PermissionResult> GetPermissionsAsync(
        PermissionRequestContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var result = new PermissionResult();

        // Process objects in parallel
        var tasks = context.Objects.Select(async objectName =>
        {
            try
            {
                var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
                return (objectName, snapshot, error: (PermissionError?)null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get permissions for {ObjectName}", objectName);
                return (objectName, snapshot: (ObjectPermissionSnapshot?)null, error: new PermissionError
                {
                    ObjectName = objectName,
                    Message = ex.Message,
                    ErrorCode = ex is Models.Errors.SalesforceException sfEx ? sfEx.ErrorCode : null
                });
            }
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (objectName, snapshot, error) in results)
        {
            if (snapshot != null)
            {
                result.Snapshots[objectName] = snapshot;
            }
            if (error != null)
            {
                result.Errors.Add(error);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<bool> CanPerformActionAsync(
        string objectName,
        PermissionAction action,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
        return action switch
        {
            PermissionAction.Create => snapshot.CanCreate,
            PermissionAction.Read => snapshot.CanRead,
            PermissionAction.Update => snapshot.CanUpdate,
            PermissionAction.Delete => snapshot.CanDelete,
            _ => false
        };
    }

    /// <inheritdoc/>
    public async Task<bool> CanAccessFieldAsync(
        string objectName,
        string fieldName,
        PermissionAction action,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);

        if (!snapshot.FieldPermissions.TryGetValue(fieldName, out var fieldPerm))
            return false;

        return action switch
        {
            PermissionAction.Read => fieldPerm.CanRead,
            PermissionAction.Create => fieldPerm.CanCreate,
            PermissionAction.Update => fieldPerm.CanUpdate,
            _ => false
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PermissionCheckResult>> CheckPermissionsAsync(
        IEnumerable<(string ObjectName, PermissionAction Action, string? FieldName)> checks,
        CancellationToken cancellationToken = default)
    {
        var checkList = checks.ToList();
        var results = new List<PermissionCheckResult>(checkList.Count);

        // Group by object to minimize API calls
        var objectGroups = checkList.GroupBy(c => c.ObjectName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in objectGroups)
        {
            var objectName = group.Key;
            ObjectPermissionSnapshot? snapshot = null;

            try
            {
                snapshot = await GetPermissionsAsync(objectName, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get permissions for {ObjectName}", objectName);
            }

            foreach (var check in group)
            {
                if (snapshot == null)
                {
                    results.Add(PermissionCheckResult.Denied(
                        objectName,
                        check.Action,
                        "Object not accessible",
                        check.FieldName));
                    continue;
                }

                if (string.IsNullOrEmpty(check.FieldName))
                {
                    // Object-level check
                    var allowed = check.Action switch
                    {
                        PermissionAction.Create => snapshot.CanCreate,
                        PermissionAction.Read => snapshot.CanRead,
                        PermissionAction.Update => snapshot.CanUpdate,
                        PermissionAction.Delete => snapshot.CanDelete,
                        _ => false
                    };

                    results.Add(allowed
                        ? PermissionCheckResult.Allowed(objectName, check.Action)
                        : PermissionCheckResult.Denied(objectName, check.Action, $"No {check.Action} permission on {objectName}"));
                }
                else
                {
                    // Field-level check
                    if (!snapshot.FieldPermissions.TryGetValue(check.FieldName, out var fieldPerm))
                    {
                        results.Add(PermissionCheckResult.Denied(
                            objectName,
                            check.Action,
                            $"Field {check.FieldName} not found",
                            check.FieldName));
                        continue;
                    }

                    var fieldAllowed = check.Action switch
                    {
                        PermissionAction.Read => fieldPerm.CanRead,
                        PermissionAction.Create => fieldPerm.CanCreate,
                        PermissionAction.Update => fieldPerm.CanUpdate,
                        _ => false
                    };

                    results.Add(fieldAllowed
                        ? PermissionCheckResult.Allowed(objectName, check.Action, check.FieldName)
                        : PermissionCheckResult.Denied(objectName, check.Action, $"No {check.Action} permission on {check.FieldName}", check.FieldName));
                }
            }
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetReadableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
        return snapshot.FieldPermissions
            .Where(kvp => kvp.Value.CanRead)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetCreateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
        return snapshot.FieldPermissions
            .Where(kvp => kvp.Value.CanCreate)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetUpdateableFieldsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
        return snapshot.FieldPermissions
            .Where(kvp => kvp.Value.CanUpdate)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PermissionAction>> GetAllowedActionsAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetPermissionsAsync(objectName, cancellationToken);
        var actions = new List<PermissionAction>();

        if (snapshot.CanRead) actions.Add(PermissionAction.Read);
        if (snapshot.CanCreate) actions.Add(PermissionAction.Create);
        if (snapshot.CanUpdate) actions.Add(PermissionAction.Update);
        if (snapshot.CanDelete) actions.Add(PermissionAction.Delete);

        return actions;
    }

    /// <inheritdoc/>
    public async Task InvalidateCacheAsync(
        string? objectName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            _logger.LogInformation("Invalidating all permission cache entries");
            // For distributed cache, we'd need to track keys or use a prefix pattern
            // For now, we invalidate schema cache which will force permission refresh
            await _schemaService.InvalidateCacheAsync(null);
        }
        else
        {
            var keys = _knownUserSuffixes.Keys.ToList();
            if (keys.Count == 0)
            {
                keys.Add(GetUserCacheKeySuffix());
            }

            foreach (var suffix in keys)
            {
                var cacheKey = $"{CacheKeyPrefix}{objectName.ToLowerInvariant()}{suffix}";
                await _cacheProvider.RemoveAsync(cacheKey, cancellationToken);
            }
            _logger.LogInformation("Invalidated permission cache for {ObjectName}", objectName);
        }
    }

    /// <inheritdoc/>
    public async Task PreloadPermissionsAsync(
        IEnumerable<string> objectNames,
        CancellationToken cancellationToken = default)
    {
        var objects = objectNames.ToList();
        _logger.LogInformation("Preloading permissions for {Count} objects", objects.Count);

        var tasks = objects.Select(obj => GetPermissionsAsync(obj, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("Permission preload complete");
    }

    private async Task<ObjectPermissionSnapshot> BuildPermissionSnapshotAsync(
        string objectName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Building permission snapshot for {ObjectName}", objectName);

        try
        {
            // Create a timeout-bound cancellation token
            using var timeoutCts = new CancellationTokenSource(Options.PermissionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            var describe = await _schemaService.GetDescribeAsync(objectName, linkedCts.Token);

            if (describe == null)
            {
                throw new InvalidOperationException($"Object '{objectName}' not found or not accessible");
            }

            return ObjectPermissionSnapshot.FromDescribe(describe);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout occurred (not user cancellation)
            _logger.LogWarning(
                "Permission fetch for {ObjectName} timed out after {TimeoutMs}ms. Using fallback mode: {FallbackMode}",
                objectName, Options.PermissionTimeout.TotalMilliseconds, Options.PermissionFallbackMode);

            return Options.PermissionFallbackMode switch
            {
                PermissionFallbackMode.AllowReadOnly => ObjectPermissionSnapshot.ReadOnly(objectName),
                PermissionFallbackMode.UseCachedOrDeny => ObjectPermissionSnapshot.DenyAll(objectName),
                _ => ObjectPermissionSnapshot.DenyAll(objectName) // DenyAll is the default (fail secure)
            };
        }
    }

    private DynamicUiOptions Options => _configProvider?.Current ?? _options;

    private string GetUserCacheKeySuffix()
    {
        var user = _userContextProvider.GetUser();
        if (user?.Identity?.IsAuthenticated != true)
        {
            return "_anon";
        }

        var id = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? user.FindFirst("sub")?.Value
                 ?? user.Identity.Name
                 ?? "_unknown";
        var suffix = $"_{id}";
        _knownUserSuffixes.TryAdd(suffix, 0);
        return suffix;
    }
}
