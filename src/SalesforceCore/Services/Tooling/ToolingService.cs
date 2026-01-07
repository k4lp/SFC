using SalesforceCore.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Query;

namespace SalesforceCore.Services.Tooling;

/// <summary>
/// Implementation of the Salesforce Tooling API service.
/// </summary>
public class ToolingService : IToolingService
{
    private readonly ISalesforceClient _client;
    private readonly SalesforceOptions _options;
    private readonly ILogger<ToolingService> _logger;

    /// <summary>
    /// Creates a new ToolingService.
    /// </summary>
    public ToolingService(
        ISalesforceClient client,
        IOptions<SalesforceOptions> options,
        ILogger<ToolingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    #region Query Operations

    /// <inheritdoc/>
    public async Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(soql))
        {
            throw new ArgumentException("SOQL query cannot be empty.", nameof(soql));
        }

        _logger.LogDebug("Executing Tooling API query: {Query}", soql);

        var encodedSoql = UrlUtils.Escape(soql);
        var endpoint = $"{SalesforceConstants.Paths.ToolingQuery}/?q={encodedSoql}";

        return await _client.GetAsync<QueryResult>(endpoint, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<QueryResult<T>> QueryAsync<T>(string soql, CancellationToken cancellationToken = default) where T : class
    {
        var result = await QueryAsync(soql, cancellationToken);

        return new QueryResult<T>
        {
            TotalSize = result.TotalSize,
            Done = result.Done,
            NextRecordsUrl = result.NextRecordsUrl,
            Records = result.Records.Select(r => JsonSerializer.Deserialize<T>(r)!).ToList()
        };
    }

    #endregion

    #region Apex Execution

    /// <inheritdoc/>
    public async Task<ExecuteAnonymousResult> ExecuteAnonymousAsync(string apexCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apexCode))
        {
            throw new ArgumentException("Apex code cannot be empty.", nameof(apexCode));
        }

        _logger.LogDebug("Executing anonymous Apex ({Length} characters)", apexCode.Length);

        // Use GET with URL-encoded body for shorter code, POST for longer code
        var encodedCode = UrlUtils.Escape(apexCode);
        var endpoint = $"{SalesforceConstants.Paths.ToolingExecuteAnonymous}/?anonymousBody={encodedCode}";

        var result = await _client.GetAsync<ExecuteAnonymousResult>(endpoint, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Anonymous Apex execution failed: {Error}", result.GetErrorMessage());
        }

        return result;
    }

    #endregion

    #region Apex Classes

    /// <inheritdoc/>
    public async Task<List<ApexClassInfo>> GetApexClassesAsync(CancellationToken cancellationToken = default)
    {
        var soql = SoqlBuilder.From("ApexClass")
            .Select("Id", "Name", "NamespacePrefix", "Body", "ApiVersion", "Status", "IsValid", "LengthWithoutComments", "CreatedDate", "LastModifiedDate")
            .OrderBy("Name")
            .Build();
        var result = await QueryAsync<ApexClassInfo>(soql, cancellationToken);
        return result.Records;
    }

    /// <inheritdoc/>
    public async Task<ApexClassInfo?> GetApexClassAsync(string classId, CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidSalesforceId(classId))
            throw new ArgumentException("Invalid Salesforce ID", nameof(classId));

        var soql = SoqlBuilder.From("ApexClass")
            .Select("Id", "Name", "NamespacePrefix", "Body", "ApiVersion", "Status", "IsValid", "LengthWithoutComments", "CreatedDate", "LastModifiedDate")
            .WhereEquals("Id", classId)
            .Build();
        var result = await QueryAsync<ApexClassInfo>(soql, cancellationToken);
        return result.Records.FirstOrDefault();
    }

    /// <inheritdoc/>
    public async Task<ApexClassInfo?> GetApexClassByNameAsync(string className, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Class name is required", nameof(className));

        var soql = SoqlBuilder.From("ApexClass")
            .Select("Id", "Name", "NamespacePrefix", "Body", "ApiVersion", "Status", "IsValid", "LengthWithoutComments", "CreatedDate", "LastModifiedDate")
            .WhereEquals("Name", className)
            .Build();
        var result = await QueryAsync<ApexClassInfo>(soql, cancellationToken);
        return result.Records.FirstOrDefault();
    }

    #endregion

    #region Apex Triggers

    /// <inheritdoc/>
    public async Task<List<ApexTriggerInfo>> GetApexTriggersAsync(CancellationToken cancellationToken = default)
    {
        var soql = SoqlBuilder.From("ApexTrigger")
            .Select("Id", "Name", "NamespacePrefix", "TableEnumOrId", "Body", "ApiVersion", "Status", "IsValid")
            .OrderBy("Name")
            .Build();
        var result = await QueryAsync<ApexTriggerInfo>(soql, cancellationToken);
        return result.Records;
    }

    /// <inheritdoc/>
    public async Task<List<ApexTriggerInfo>> GetApexTriggersForObjectAsync(string sObjectType, CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObjectType))
            throw new ArgumentException("Invalid object name", nameof(sObjectType));

        var soql = SoqlBuilder.From("ApexTrigger")
            .Select("Id", "Name", "NamespacePrefix", "TableEnumOrId", "Body", "ApiVersion", "Status", "IsValid")
            .WhereEquals("TableEnumOrId", sObjectType)
            .OrderBy("Name")
            .Build();
        var result = await QueryAsync<ApexTriggerInfo>(soql, cancellationToken);
        return result.Records;
    }

    #endregion

    #region Validation Rules

    /// <inheritdoc/>
    public async Task<List<ValidationRuleInfo>> GetValidationRulesAsync(CancellationToken cancellationToken = default)
    {
        var soql = SoqlBuilder.From("ValidationRule")
            .Select("Id", "FullName", "ValidationName", "Active", "ErrorConditionFormula", "ErrorMessage", "ErrorDisplayField", "EntityDefinitionId")
            .OrderBy("FullName")
            .Build();
        var result = await QueryAsync<ValidationRuleInfo>(soql, cancellationToken);
        return result.Records;
    }

    /// <inheritdoc/>
    public async Task<List<ValidationRuleInfo>> GetValidationRulesForObjectAsync(string sObjectType, CancellationToken cancellationToken = default)
    {
        if (!SecurityUtils.IsValidObjectName(sObjectType))
            throw new ArgumentException("Invalid object name", nameof(sObjectType));

        var soql = SoqlBuilder.From("ValidationRule")
            .Select("Id", "FullName", "ValidationName", "Active", "ErrorConditionFormula", "ErrorMessage", "ErrorDisplayField", "EntityDefinitionId")
            .WhereEquals("EntityDefinition.QualifiedApiName", sObjectType)
            .OrderBy("FullName")
            .Build();
        var result = await QueryAsync<ValidationRuleInfo>(soql, cancellationToken);
        return result.Records;
    }

    #endregion

    #region Debug Logs

    /// <inheritdoc/>
    public async Task<List<ApexLogInfo>> GetDebugLogsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > 2000)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 2000");

        var soql = SoqlBuilder.From("ApexLog")
            .Select("Id", "Application", "DurationMilliseconds", "Location", "LogLength", "LogUserId", "Operation", "Request", "StartTime", "Status")
            .OrderByDescending("StartTime")
            .Limit(limit)
            .Build();
        var result = await QueryAsync<ApexLogInfo>(soql, cancellationToken);
        return result.Records;
    }

    /// <inheritdoc/>
    public async Task<string> GetDebugLogContentAsync(string logId, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.Tooling}/sobjects/ApexLog/{logId}/Body";
        var bytes = await _client.GetBytesAsync(endpoint, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <inheritdoc/>
    public Task<string> GetDebugLogAsync(string logId, CancellationToken cancellationToken = default)
    {
        return GetDebugLogContentAsync(logId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteDebugLogAsync(string logId, CancellationToken cancellationToken = default)
    {
        await DeleteAsync("ApexLog", logId, cancellationToken);
    }

    #endregion

    #region Trace Flags

    /// <inheritdoc/>
    public async Task<string> CreateTraceFlagAsync(CreateTraceFlagRequest request, CancellationToken cancellationToken = default)
    {
        return await CreateAsync("TraceFlag", request, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<TraceFlagInfo>> GetTraceFlagsAsync(CancellationToken cancellationToken = default)
    {
        var soql = SoqlBuilder.From("TraceFlag")
            .Select("Id", "DebugLevelId", "ExpirationDate", "LogType", "StartDate", "TracedEntityId")
            .WhereDateLiteralCompare("ExpirationDate", ">", DateLiteral.TODAY)
            .OrderBy("ExpirationDate")
            .Build();
        var result = await QueryAsync<TraceFlagInfo>(soql, cancellationToken);
        return result.Records;
    }

    /// <inheritdoc/>
    public async Task DeleteTraceFlagAsync(string traceFlagId, CancellationToken cancellationToken = default)
    {
        await DeleteAsync("TraceFlag", traceFlagId, cancellationToken);
    }

    #endregion

    #region Generic CRUD

    /// <inheritdoc/>
    public async Task<string> CreateAsync(string objectName, object data, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.Tooling}/sobjects/{objectName}/";
        var result = await _client.PostAsync<CreateResult>(endpoint, data, cancellationToken);

        if (!result.Success && result.Errors.Count > 0)
        {
            throw Models.Errors.SalesforceException.FromErrors(result.Errors);
        }

        return result.Id;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(string objectName, string id, object data, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.Tooling}/sobjects/{objectName}/{id}";
        await _client.PatchAsync(endpoint, data, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string objectName, string id, CancellationToken cancellationToken = default)
    {
        var endpoint = $"{SalesforceConstants.Paths.Tooling}/sobjects/{objectName}/{id}";
        await _client.DeleteAsync(endpoint, cancellationToken);
    }

    #endregion
}
