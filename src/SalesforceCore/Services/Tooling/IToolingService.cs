using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Tooling;

/// <summary>
/// Service for interacting with the Salesforce Tooling API.
/// Provides access to metadata objects, Apex execution, debug logs, and more.
/// </summary>
public interface IToolingService
{
    #region Query Operations

    /// <summary>
    /// Executes a SOQL query against the Tooling API.
    /// Tooling SOQL can query metadata objects like ApexClass, ApexTrigger, ValidationRule, etc.
    /// </summary>
    /// <param name="soql">The SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with records.</returns>
    /// <example>
    /// <code>
    /// var result = await toolingService.QueryAsync(
    ///     "SELECT Id, Name, Body FROM ApexClass WHERE Name LIKE 'Test%'");
    /// </code>
    /// </example>
    Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a typed SOQL query against the Tooling API.
    /// </summary>
    /// <typeparam name="T">The type to deserialize records into.</typeparam>
    /// <param name="soql">The SOQL query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result with typed records.</returns>
    Task<QueryResult<T>> QueryAsync<T>(string soql, CancellationToken cancellationToken = default) where T : class;

    #endregion

    #region Apex Execution

    /// <summary>
    /// Executes anonymous Apex code.
    /// Useful for running maintenance scripts, testing, or administrative tasks.
    /// </summary>
    /// <param name="apexCode">The Apex code to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the execution including compilation and runtime status.</returns>
    /// <example>
    /// <code>
    /// var result = await toolingService.ExecuteAnonymousAsync(@"
    ///     List&lt;Account&gt; accounts = [SELECT Id, Name FROM Account LIMIT 10];
    ///     System.debug('Found ' + accounts.size() + ' accounts');
    /// ");
    ///
    /// if (!result.Success) {
    ///     Console.WriteLine($"Error: {result.GetErrorMessage()}");
    /// }
    /// </code>
    /// </example>
    Task<ExecuteAnonymousResult> ExecuteAnonymousAsync(string apexCode, CancellationToken cancellationToken = default);

    #endregion

    #region Apex Classes

    /// <summary>
    /// Gets all Apex classes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of Apex class information.</returns>
    Task<List<ApexClassInfo>> GetApexClassesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an Apex class by ID.
    /// </summary>
    /// <param name="classId">The Apex class ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Apex class information.</returns>
    Task<ApexClassInfo?> GetApexClassAsync(string classId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an Apex class by name.
    /// </summary>
    /// <param name="className">The Apex class name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Apex class information.</returns>
    Task<ApexClassInfo?> GetApexClassByNameAsync(string className, CancellationToken cancellationToken = default);

    #endregion

    #region Apex Triggers

    /// <summary>
    /// Gets all Apex triggers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of Apex trigger information.</returns>
    Task<List<ApexTriggerInfo>> GetApexTriggersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets Apex triggers for a specific SObject.
    /// </summary>
    /// <param name="sObjectType">The SObject type name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of Apex trigger information.</returns>
    Task<List<ApexTriggerInfo>> GetApexTriggersForObjectAsync(string sObjectType, CancellationToken cancellationToken = default);

    #endregion

    #region Validation Rules

    /// <summary>
    /// Gets all validation rules.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of validation rule information.</returns>
    Task<List<ValidationRuleInfo>> GetValidationRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets validation rules for a specific SObject.
    /// </summary>
    /// <param name="sObjectType">The SObject type name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of validation rule information.</returns>
    Task<List<ValidationRuleInfo>> GetValidationRulesForObjectAsync(string sObjectType, CancellationToken cancellationToken = default);

    #endregion

    #region Debug Logs

    /// <summary>
    /// Gets recent debug logs.
    /// </summary>
    /// <param name="limit">Maximum number of logs to return (default 20).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of debug log information.</returns>
    Task<List<ApexLogInfo>> GetDebugLogsAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content of a specific debug log.
    /// </summary>
    /// <param name="logId">The debug log ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw log content.</returns>
    Task<string> GetDebugLogContentAsync(string logId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the content of a specific debug log.
    /// Alias for <see cref="GetDebugLogContentAsync"/>.
    /// </summary>
    /// <param name="logId">The debug log ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw log content.</returns>
    Task<string> GetDebugLogAsync(string logId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a debug log.
    /// </summary>
    /// <param name="logId">The debug log ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteDebugLogAsync(string logId, CancellationToken cancellationToken = default);

    #endregion

    #region Trace Flags

    /// <summary>
    /// Creates a trace flag to enable debug logging for a user or class.
    /// </summary>
    /// <param name="request">The trace flag configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created trace flag ID.</returns>
    Task<string> CreateTraceFlagAsync(CreateTraceFlagRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active trace flags.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of trace flag information.</returns>
    Task<List<TraceFlagInfo>> GetTraceFlagsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a trace flag.
    /// </summary>
    /// <param name="traceFlagId">The trace flag ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteTraceFlagAsync(string traceFlagId, CancellationToken cancellationToken = default);

    #endregion

    #region Generic CRUD

    /// <summary>
    /// Creates a tooling object record.
    /// </summary>
    /// <param name="objectName">The tooling object type (e.g., "TraceFlag").</param>
    /// <param name="data">The record data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created record ID.</returns>
    Task<string> CreateAsync(string objectName, object data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a tooling object record.
    /// </summary>
    /// <param name="objectName">The tooling object type.</param>
    /// <param name="id">The record ID.</param>
    /// <param name="data">The fields to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(string objectName, string id, object data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tooling object record.
    /// </summary>
    /// <param name="objectName">The tooling object type.</param>
    /// <param name="id">The record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string objectName, string id, CancellationToken cancellationToken = default);

    #endregion
}
