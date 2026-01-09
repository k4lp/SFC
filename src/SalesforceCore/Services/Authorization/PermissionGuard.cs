using SalesforceCore.Models.Authorization;

namespace SalesforceCore.Services.Authorization;

/// <summary>
/// Implementation of fluent permission guard for declarative permission checks.
/// Batches requirements by object to minimize API calls during evaluation.
/// </summary>
public class PermissionGuard : IPermissionGuard
{
    private readonly IPermissionService _permissionService;

    public PermissionGuard(IPermissionService permissionService)
    {
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder Require(string objectName, PermissionAction action)
    {
        return new PermissionGuardBuilder(_permissionService)
            .Require(objectName, action);
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireField(string objectName, string fieldName, PermissionAction action)
    {
        return new PermissionGuardBuilder(_permissionService)
            .RequireField(objectName, fieldName, action);
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireAny(string objectName, params PermissionAction[] actions)
    {
        return new PermissionGuardBuilder(_permissionService)
            .RequireAny(objectName, actions);
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireAll(string objectName, params PermissionAction[] actions)
    {
        return new PermissionGuardBuilder(_permissionService)
            .RequireAll(objectName, actions);
    }
}

/// <summary>
/// Builder implementation for chaining permission requirements.
/// </summary>
internal class PermissionGuardBuilder : IPermissionGuardBuilder
{
    private readonly IPermissionService _permissionService;
    private readonly List<RequirementGroup> _requirementGroups = new();
    private RequirementGroup _currentGroup;

    internal PermissionGuardBuilder(IPermissionService permissionService)
    {
        _permissionService = permissionService;
        _currentGroup = new RequirementGroup();
        _requirementGroups.Add(_currentGroup);
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder Require(string objectName, PermissionAction action)
    {
        _currentGroup.Requirements.Add(new PermissionRequirement
        {
            ObjectName = objectName,
            Action = action,
            RequirementType = RequirementType.Object
        });
        return this;
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireField(string objectName, string fieldName, PermissionAction action)
    {
        _currentGroup.Requirements.Add(new PermissionRequirement
        {
            ObjectName = objectName,
            FieldName = fieldName,
            Action = action,
            RequirementType = RequirementType.Field
        });
        return this;
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireAny(string objectName, params PermissionAction[] actions)
    {
        _currentGroup.Requirements.Add(new PermissionRequirement
        {
            ObjectName = objectName,
            Actions = actions.ToList(),
            RequirementType = RequirementType.AnyAction
        });
        return this;
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder RequireAll(string objectName, params PermissionAction[] actions)
    {
        _currentGroup.Requirements.Add(new PermissionRequirement
        {
            ObjectName = objectName,
            Actions = actions.ToList(),
            RequirementType = RequirementType.AllActions
        });
        return this;
    }

    /// <inheritdoc/>
    public IPermissionGuardBuilder Or()
    {
        _currentGroup = new RequirementGroup();
        _requirementGroups.Add(_currentGroup);
        return this;
    }

    /// <inheritdoc/>
    public async Task<PermissionGuardResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (_requirementGroups.All(g => g.Requirements.Count == 0))
        {
            return PermissionGuardResult.Success();
        }

        // Get all unique objects across all groups for batch loading
        var allObjects = _requirementGroups
            .SelectMany(g => g.Requirements)
            .Select(r => r.ObjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Batch load permissions for all objects
        var context = PermissionRequestContext.ForObjects(allObjects.ToArray());
        var permissionResult = await _permissionService.GetPermissionsAsync(context, cancellationToken);

        // Evaluate groups with OR logic between groups
        foreach (var group in _requirementGroups)
        {
            var groupResult = EvaluateGroup(group, permissionResult);
            if (groupResult.IsAllowed)
            {
                // Any group passing means overall success (OR logic between groups)
                return PermissionGuardResult.Success();
            }
        }

        // All groups failed - collect all violations from the last group
        var lastGroup = _requirementGroups.Last();
        var violations = CollectViolations(lastGroup, permissionResult);
        return PermissionGuardResult.Denied(violations);
    }

    private PermissionGuardResult EvaluateGroup(RequirementGroup group, PermissionResult permissionResult)
    {
        var violations = new List<PermissionViolation>();

        // AND logic within a group - all requirements must pass
        foreach (var req in group.Requirements)
        {
            var snapshot = permissionResult.GetSnapshot(req.ObjectName);
            if (snapshot == null)
            {
                violations.Add(new PermissionViolation
                {
                    ObjectName = req.ObjectName,
                    Action = req.Action,
                    Reason = $"Object '{req.ObjectName}' not accessible"
                });
                continue;
            }

            switch (req.RequirementType)
            {
                case RequirementType.Object:
                    if (!CheckObjectAction(snapshot, req.Action))
                    {
                        violations.Add(new PermissionViolation
                        {
                            ObjectName = req.ObjectName,
                            Action = req.Action,
                            Reason = $"No {req.Action} permission on {req.ObjectName}"
                        });
                    }
                    break;

                case RequirementType.Field:
                    if (!CheckFieldAction(snapshot, req.FieldName!, req.Action))
                    {
                        violations.Add(new PermissionViolation
                        {
                            ObjectName = req.ObjectName,
                            FieldName = req.FieldName,
                            Action = req.Action,
                            Reason = $"No {req.Action} permission on {req.ObjectName}.{req.FieldName}"
                        });
                    }
                    break;

                case RequirementType.AnyAction:
                    if (!req.Actions.Any(a => CheckObjectAction(snapshot, a)))
                    {
                        violations.Add(new PermissionViolation
                        {
                            ObjectName = req.ObjectName,
                            Action = req.Actions.FirstOrDefault(),
                            Reason = $"None of the required actions allowed on {req.ObjectName}"
                        });
                    }
                    break;

                case RequirementType.AllActions:
                    var missingActions = req.Actions.Where(a => !CheckObjectAction(snapshot, a)).ToList();
                    if (missingActions.Count > 0)
                    {
                        foreach (var missing in missingActions)
                        {
                            violations.Add(new PermissionViolation
                            {
                                ObjectName = req.ObjectName,
                                Action = missing,
                                Reason = $"No {missing} permission on {req.ObjectName}"
                            });
                        }
                    }
                    break;
            }
        }

        return violations.Count == 0
            ? PermissionGuardResult.Success()
            : PermissionGuardResult.Denied(violations);
    }

    private List<PermissionViolation> CollectViolations(RequirementGroup group, PermissionResult permissionResult)
    {
        var result = EvaluateGroup(group, permissionResult);
        return result.Violations.ToList();
    }

    private static bool CheckObjectAction(ObjectPermissionSnapshot snapshot, PermissionAction action)
    {
        return action switch
        {
            PermissionAction.Create => snapshot.CanCreate,
            PermissionAction.Read => snapshot.CanRead,
            PermissionAction.Update => snapshot.CanUpdate,
            PermissionAction.Delete => snapshot.CanDelete,
            _ => false
        };
    }

    private static bool CheckFieldAction(ObjectPermissionSnapshot snapshot, string fieldName, PermissionAction action)
    {
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

    private class RequirementGroup
    {
        public List<PermissionRequirement> Requirements { get; } = new();
    }

    private class PermissionRequirement
    {
        public string ObjectName { get; set; } = string.Empty;
        public string? FieldName { get; set; }
        public PermissionAction Action { get; set; }
        public List<PermissionAction> Actions { get; set; } = new();
        public RequirementType RequirementType { get; set; }
    }

    private enum RequirementType
    {
        Object,
        Field,
        AnyAction,
        AllActions
    }
}
