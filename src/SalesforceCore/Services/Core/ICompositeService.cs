using SalesforceCore.Models.Data;

namespace SalesforceCore.Services.Core;

/// <summary>
/// Service for executing composite (batch) operations against Salesforce.
/// Supports the standard Composite API (25 sub-requests) and Composite Graph API (500 nodes).
/// </summary>
public interface ICompositeService
{
    #region Composite Graph API

    /// <summary>
    /// Executes a Composite Graph request for complex, high-volume transactional operations.
    /// Supports up to 500 nodes across up to 25 graphs with interdependencies.
    /// </summary>
    /// <param name="request">The composite graph request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The composite graph response.</returns>
    /// <example>
    /// <code>
    /// var builder = compositeService.CreateGraphBuilder()
    ///     .StartGraph("accountGraph")
    ///     .Create("Account", new Dictionary&lt;string, object?&gt; { ["Name"] = "Acme Corp" }, "newAccount")
    ///     .CreateWithReference("Contact", new Dictionary&lt;string, object?&gt;
    ///     {
    ///         ["FirstName"] = "John",
    ///         ["LastName"] = "Doe",
    ///         ["AccountId"] = "@{newAccount.id}"
    ///     }, "newContact");
    ///
    /// var response = await compositeService.ExecuteGraphAsync(builder.Build());
    /// </code>
    /// </example>
    Task<CompositeGraphResponse> ExecuteGraphAsync(
        CompositeGraphRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a Composite Graph using a fluent builder.
    /// </summary>
    /// <param name="builder">The composite graph builder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The composite graph response.</returns>
    Task<CompositeGraphResponse> ExecuteGraphAsync(
        CompositeGraphBuilder builder,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new composite graph builder.
    /// </summary>
    /// <returns>A new graph builder instance.</returns>
    CompositeGraphBuilder CreateGraphBuilder();

    #endregion

    #region Standard Composite API

    /// <summary>
    /// Executes a composite request with multiple sub-requests.
    /// </summary>
    /// <param name="request">The composite request containing sub-requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The composite response with all sub-responses.</returns>
    Task<CompositeResponse> ExecuteAsync(CompositeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new composite batch builder.
    /// </summary>
    /// <returns>A new batch builder instance.</returns>
    ICompositeBatchBuilder CreateBatch();

    /// <summary>
    /// Creates multiple records in a single composite request.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="records">The records to create.</param>
    /// <param name="allOrNone">Whether to rollback all changes if any fail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each record creation.</returns>
    Task<List<CompositeOperationResult>> CreateRecordsAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates multiple records in a single composite request.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="records">The records to update (must include Id).</param>
    /// <param name="allOrNone">Whether to rollback all changes if any fail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each record update.</returns>
    Task<List<CompositeOperationResult>> UpdateRecordsAsync(
        string objectName,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes multiple records in a single composite request.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="ids">The record IDs to delete.</param>
    /// <param name="allOrNone">Whether to rollback all changes if any fail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each record deletion.</returns>
    Task<List<CompositeOperationResult>> DeleteRecordsAsync(
        string objectName,
        IEnumerable<string> ids,
        bool allOrNone = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts multiple records using an external ID field.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="externalIdField">The external ID field name.</param>
    /// <param name="records">The records to upsert (must include the external ID field).</param>
    /// <param name="allOrNone">Whether to rollback all changes if any fail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for each record upsert.</returns>
    Task<List<CompositeOperationResult>> UpsertRecordsAsync(
        string objectName,
        string externalIdField,
        IEnumerable<Dictionary<string, object?>> records,
        bool allOrNone = false,
        CancellationToken cancellationToken = default);

    #endregion
}

/// <summary>
/// Builder for creating composite batch operations.
/// </summary>
public interface ICompositeBatchBuilder
{
    /// <summary>
    /// Adds a create operation to the batch.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="data">The record data.</param>
    /// <param name="referenceId">Optional reference ID for this operation.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder Create(string objectName, Dictionary<string, object?> data, string? referenceId = null);

    /// <summary>
    /// Adds an update operation to the batch.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="id">The record ID to update.</param>
    /// <param name="data">The fields to update.</param>
    /// <param name="referenceId">Optional reference ID for this operation.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder Update(string objectName, string id, Dictionary<string, object?> data, string? referenceId = null);

    /// <summary>
    /// Adds a delete operation to the batch.
    /// </summary>
    /// <param name="objectName">The Salesforce object type.</param>
    /// <param name="id">The record ID to delete.</param>
    /// <param name="referenceId">Optional reference ID for this operation.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder Delete(string objectName, string id, string? referenceId = null);

    /// <summary>
    /// Adds a query operation to the batch.
    /// </summary>
    /// <param name="soql">The SOQL query.</param>
    /// <param name="referenceId">Optional reference ID for this operation.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder Query(string soql, string? referenceId = null);

    /// <summary>
    /// Adds a custom sub-request to the batch.
    /// </summary>
    /// <param name="subRequest">The sub-request to add.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder Add(CompositeSubRequest subRequest);

    /// <summary>
    /// Sets whether all operations should succeed or all should fail (transaction).
    /// </summary>
    /// <param name="allOrNone">True for transactional behavior.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder WithAllOrNone(bool allOrNone = true);

    /// <summary>
    /// Sets whether sub-requests should be collated for efficiency.
    /// </summary>
    /// <param name="collate">True to collate sub-requests.</param>
    /// <returns>This builder for chaining.</returns>
    ICompositeBatchBuilder WithCollation(bool collate = true);

    /// <summary>
    /// Gets the number of operations in the batch.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Executes the batch and returns results.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Results for all operations.</returns>
    Task<List<CompositeOperationResult>> ExecuteAsync(CancellationToken cancellationToken = default);
}
