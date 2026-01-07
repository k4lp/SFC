using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Utilities;

namespace SalesforceCore.Schema;

/// <summary>
/// Represents the dependency relationships between fields.
/// </summary>
public interface IFieldDependencyGraph
{
    /// <summary>
    /// Gets all fields that this field depends on.
    /// </summary>
    IEnumerable<string> GetDependencies(string fieldName);

    /// <summary>
    /// Gets all fields that depend on this field.
    /// </summary>
    IEnumerable<string> GetDependents(string fieldName);

    /// <summary>
    /// Gets all controlling fields (fields that control dependent picklists).
    /// </summary>
    IEnumerable<string> GetControllingFields();

    /// <summary>
    /// Gets all dependent picklist fields.
    /// </summary>
    IEnumerable<string> GetDependentFields();

    /// <summary>
    /// Gets the controlling field for a dependent picklist.
    /// </summary>
    string? GetControllerFor(string dependentFieldName);

    /// <summary>
    /// Gets valid picklist values for a dependent field given controller value.
    /// </summary>
    IEnumerable<string> GetValidValues(string dependentFieldName, string controllerValue);

    /// <summary>
    /// Checks if changing a field requires recalculating other fields.
    /// </summary>
    bool RequiresRecalculation(string fieldName);

    /// <summary>
    /// Gets the optimal field loading order based on dependencies.
    /// </summary>
    IEnumerable<string> GetLoadOrder();

    /// <summary>
    /// Gets formula fields that reference a given field.
    /// </summary>
    IEnumerable<string> GetFormulasDependingOn(string fieldName);
}

/// <summary>
/// Implementation of field dependency graph.
/// </summary>
public class FieldDependencyGraph : IFieldDependencyGraph
{
    private readonly string _objectName;
    private readonly Dictionary<string, HashSet<string>> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _dependents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _controllerMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, List<string>>> _validValueMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _formulaFields = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _formulaDependencies = new(StringComparer.OrdinalIgnoreCase);

    public FieldDependencyGraph(string objectName)
    {
        _objectName = objectName;
    }

    /// <summary>
    /// Creates a dependency graph from object describe metadata.
    /// </summary>
    public static FieldDependencyGraph FromDescribe(SObjectDescribe describe)
    {
        var graph = new FieldDependencyGraph(describe.Name);
        graph.BuildFromFields(describe.Fields);
        return graph;
    }

    /// <summary>
    /// Builds the dependency graph from field list.
    /// </summary>
    public void BuildFromFields(IEnumerable<SObjectField> fields)
    {
        var fieldList = fields.ToList();

        foreach (var field in fieldList)
        {
            // Initialize sets
            if (!_dependencies.ContainsKey(field.Name))
                _dependencies[field.Name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!_dependents.ContainsKey(field.Name))
                _dependents[field.Name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Handle dependent picklists
            if (field.DependentPicklist && !string.IsNullOrEmpty(field.ControllerName))
            {
                _controllerMap[field.Name] = field.ControllerName;
                _dependencies[field.Name].Add(field.ControllerName);

                if (!_dependents.ContainsKey(field.ControllerName))
                    _dependents[field.ControllerName] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _dependents[field.ControllerName].Add(field.Name);

                // Build valid values map from ValidFor bitmaps
                BuildValidValuesMap(field, fieldList);
            }

            // Track formula fields
            if (field.Calculated && !string.IsNullOrEmpty(field.DefaultValueFormula))
            {
                _formulaFields.Add(field.Name);
                // Parse formula to find referenced fields
                var referencedFields = ParseFormulaReferences(field.DefaultValueFormula);
                _formulaDependencies[field.Name] = referencedFields;

                foreach (var refField in referencedFields)
                {
                    _dependencies[field.Name].Add(refField);

                    if (!_dependents.ContainsKey(refField))
                        _dependents[refField] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _dependents[refField].Add(field.Name);
                }
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetDependencies(string fieldName)
    {
        return _dependencies.TryGetValue(fieldName, out var deps)
            ? deps
            : Enumerable.Empty<string>();
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetDependents(string fieldName)
    {
        return _dependents.TryGetValue(fieldName, out var deps)
            ? deps
            : Enumerable.Empty<string>();
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetControllingFields()
    {
        return _controllerMap.Values.Distinct();
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetDependentFields()
    {
        return _controllerMap.Keys;
    }

    /// <inheritdoc/>
    public string? GetControllerFor(string dependentFieldName)
    {
        return _controllerMap.TryGetValue(dependentFieldName, out var controller)
            ? controller
            : null;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetValidValues(string dependentFieldName, string controllerValue)
    {
        if (_validValueMaps.TryGetValue(dependentFieldName, out var valueMap) &&
            valueMap.TryGetValue(controllerValue, out var validValues))
        {
            return validValues;
        }
        return Enumerable.Empty<string>();
    }

    /// <inheritdoc/>
    public bool RequiresRecalculation(string fieldName)
    {
        // A field requires recalculation if it has dependents
        return _dependents.TryGetValue(fieldName, out var deps) && deps.Count > 0;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetLoadOrder()
    {
        // Topological sort based on dependencies
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in _dependencies.Keys)
        {
            TopologicalSort(field, visited, visiting, result);
        }

        return result;
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetFormulasDependingOn(string fieldName)
    {
        return _formulaDependencies
            .Where(kvp => kvp.Value.Contains(fieldName))
            .Select(kvp => kvp.Key);
    }

    private void TopologicalSort(string field, HashSet<string> visited, HashSet<string> visiting, List<string> result)
    {
        if (visited.Contains(field))
            return;

        if (visiting.Contains(field))
        {
            // Circular dependency - just return (Salesforce shouldn't allow this)
            return;
        }

        visiting.Add(field);

        if (_dependencies.TryGetValue(field, out var deps))
        {
            foreach (var dep in deps)
            {
                TopologicalSort(dep, visited, visiting, result);
            }
        }

        visiting.Remove(field);
        visited.Add(field);
        result.Add(field);
    }

    private void BuildValidValuesMap(SObjectField dependentField, List<SObjectField> allFields)
    {
        if (string.IsNullOrEmpty(dependentField.ControllerName))
            return;

        // Find controller field
        var controllerField = allFields.FirstOrDefault(f =>
            f.Name.Equals(dependentField.ControllerName, StringComparison.OrdinalIgnoreCase));

        if (controllerField == null)
            return;

        var valueMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        _validValueMaps[dependentField.Name] = valueMap;

        // Get controller values
        List<string> controllerValues;
        if (controllerField.IsPicklist)
        {
            controllerValues = controllerField.PicklistValues
                .Where(p => p.Active)
                .Select(p => p.Value)
                .ToList();
        }
        else if (controllerField.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase))
        {
            controllerValues = new List<string> { "false", "true" };
        }
        else
        {
            return;
        }

        // Parse ValidFor bitmaps
        foreach (var picklistValue in dependentField.PicklistValues.Where(p => p.Active))
        {
            if (string.IsNullOrEmpty(picklistValue.ValidFor))
                continue;

            var validIndices = BitmaskUtils.DecodeValidForBitmap(picklistValue.ValidFor);

            foreach (var idx in validIndices)
            {
                if (idx < controllerValues.Count)
                {
                    var controllerValue = controllerValues[idx];
                    if (!valueMap.ContainsKey(controllerValue))
                        valueMap[controllerValue] = new List<string>();
                    valueMap[controllerValue].Add(picklistValue.Value);
                }
            }
        }
    }

    private static HashSet<string> ParseFormulaReferences(string formula)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Simple regex-based parsing for field references
        // This is a simplified version - Salesforce formulas can be complex
        var regex = new System.Text.RegularExpressions.Regex(
            @"\b([A-Za-z_][A-Za-z0-9_]*(?:__c)?)\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        // Common formula function names to exclude
        var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "AND", "OR", "NOT", "TRUE", "FALSE", "NULL", "ISBLANK", "ISNULL",
            "TEXT", "VALUE", "NUMBER", "DATE", "DATEVALUE", "DATETIMEVALUE",
            "NOW", "TODAY", "MONTH", "YEAR", "DAY", "HOUR", "MINUTE", "SECOND",
            "ABS", "CEILING", "FLOOR", "ROUND", "MOD", "MAX", "MIN", "SQRT", "EXP", "LN", "LOG",
            "CONTAINS", "BEGINS", "ENDS", "LEFT", "RIGHT", "MID", "LEN", "TRIM",
            "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "BR", "HYPERLINK", "IMAGE",
            "CASE", "NULLVALUE", "BLANKVALUE", "PRIORVALUE", "ISCHANGED", "ISNEW",
            "REGEX", "DISTANCE", "GEOLOCATION"
        };

        foreach (System.Text.RegularExpressions.Match match in regex.Matches(formula))
        {
            var name = match.Groups[1].Value;
            if (!excludedNames.Contains(name))
            {
                references.Add(name);
            }
        }

        return references;
    }
}

/// <summary>
/// Service for managing field dependencies.
/// </summary>
public interface IFieldDependencyService
{
    /// <summary>
    /// Gets the dependency graph for an object.
    /// </summary>
    Task<IFieldDependencyGraph> GetDependencyGraphAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets valid dependent picklist values.
    /// </summary>
    Task<IEnumerable<PicklistEntry>> GetDependentValuesAsync(
        string objectName,
        string dependentFieldName,
        string controllerValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates field values considering dependencies.
    /// </summary>
    Task<bool> ValidateDependentFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of field dependency service.
/// </summary>
public class FieldDependencyService : IFieldDependencyService
{
    private readonly ISchemaService _schemaService;
    private readonly Dictionary<string, IFieldDependencyGraph> _graphCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public FieldDependencyService(ISchemaService schemaService)
    {
        _schemaService = schemaService ?? throw new ArgumentNullException(nameof(schemaService));
    }

    /// <inheritdoc/>
    public async Task<IFieldDependencyGraph> GetDependencyGraphAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (_graphCache.TryGetValue(objectName, out var cached))
            return cached;

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_graphCache.TryGetValue(objectName, out cached))
                return cached;

            var describe = await _schemaService.GetDescribeAsync(objectName, cancellationToken);
            if (describe == null)
                throw new InvalidOperationException($"Could not retrieve schema for {objectName}");

            var graph = FieldDependencyGraph.FromDescribe(describe);
            _graphCache[objectName] = graph;
            return graph;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<PicklistEntry>> GetDependentValuesAsync(
        string objectName,
        string dependentFieldName,
        string controllerValue,
        CancellationToken cancellationToken = default)
    {
        var fields = await _schemaService.GetFieldMapAsync(objectName, cancellationToken);

        if (!fields.TryGetValue(dependentFieldName, out var dependentField))
            return Enumerable.Empty<PicklistEntry>();

        var graph = await GetDependencyGraphAsync(objectName, cancellationToken);
        var validValues = graph.GetValidValues(dependentFieldName, controllerValue);
        var validSet = validValues.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return dependentField.PicklistValues
            .Where(p => p.Active && validSet.Contains(p.Value));
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateDependentFieldsAsync(
        string objectName,
        IDictionary<string, object?> record,
        CancellationToken cancellationToken = default)
    {
        var graph = await GetDependencyGraphAsync(objectName, cancellationToken);

        foreach (var dependentField in graph.GetDependentFields())
        {
            if (!record.TryGetValue(dependentField, out var dependentValue) ||
                dependentValue == null ||
                string.IsNullOrEmpty(dependentValue.ToString()))
            {
                continue; // Empty value is valid
            }

            var controllerField = graph.GetControllerFor(dependentField);
            if (controllerField == null)
                continue;

            if (!record.TryGetValue(controllerField, out var controllerValue) ||
                controllerValue == null)
            {
                continue; // No controller value
            }

            var validValues = graph.GetValidValues(dependentField, controllerValue.ToString()!);
            if (!validValues.Contains(dependentValue.ToString()!, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
