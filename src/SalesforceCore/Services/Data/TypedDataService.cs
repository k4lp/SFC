using System.Linq.Expressions;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Data;
using SalesforceCore.Query; // Keep this
using SalesforceCore.Services.Query; // Add this for SoqlBuilder if namespaces differ?
// Wait, TypedDataService has `using SalesforceCore.Query;` which contains `SalesforceQueryable`.
// `SoqlBuilder` is in `SalesforceCore.Services.Query`.
using SoqlBuilder = SalesforceCore.Services.Query.SoqlBuilder; // Explicit alias or using

namespace SalesforceCore.Services.Data;

/// <summary>
/// Provides strongly-typed data operations using model classes with SalesforceCore attributes.
/// This service complements IDataService with type-safe operations that leverage
/// the SalesforceMapper for serialization and LINQ expressions for querying.
/// </summary>
public interface ITypedDataService
{
    /// <summary>
    /// Creates a LINQ queryable for the specified Salesforce object type.
    /// </summary>
    /// <typeparam name="T">The model type decorated with SalesforceCore attributes.</typeparam>
    /// <returns>A queryable that translates LINQ to SOQL.</returns>
    SalesforceQueryable<T> Query<T>() where T : class, new();

    /// <summary>
    /// Gets a single record by ID.
    /// </summary>
    Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Gets a single record matching the predicate.
    /// </summary>
    Task<T?> GetAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Gets all records matching the predicate.
    /// </summary>
    Task<List<T>> GetAllAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Creates a new record from a strongly-typed model.
    /// </summary>
    Task<string> CreateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Updates an existing record from a strongly-typed model.
    /// The model must have a non-null Id property.
    /// </summary>
    Task UpdateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes a record by ID.
    /// </summary>
    Task DeleteAsync<T>(string id, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Upserts a record using the external ID field.
    /// </summary>
    Task<string> UpsertAsync<T>(T record, string? externalIdField = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Counts records matching the predicate.
    /// </summary>
    Task<int> CountAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new();

    /// <summary>
    /// Checks if any records match the predicate.
    /// </summary>
    Task<bool> AnyAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new();
}

/// <summary>
/// Implementation of ITypedDataService.
/// </summary>
public class TypedDataService : ITypedDataService
{
    private readonly IDataService _dataService;

    public TypedDataService(IDataService dataService)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
    }

    public SalesforceQueryable<T> Query<T>() where T : class, new()
    {
        return new SalesforceQueryable<T>(_dataService);
    }

    public async Task<T?> GetByIdAsync<T>(string id, CancellationToken cancellationToken = default) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required", nameof(id));

        // Normalize the ID - Salesforce IDs are case-sensitive!
        // 15-char IDs can be converted to 18-char case-insensitive IDs
        // Always use the ID as provided - Salesforce handles case properly
        var normalizedId = id.Trim();

        var objectName = SalesforceMapper.GetObjectName<T>();
        var fields = SalesforceMapper.GetQueryableFields<T>().ToList();

        // Ensure Id is always included in fields
        if (!fields.Contains("Id", StringComparer.OrdinalIgnoreCase))
        {
            fields.Insert(0, "Id");
        }

        try
        {
            var record = await _dataService.GetRecordAsync(objectName, normalizedId, fields, cancellationToken);
            return SalesforceMapper.FromSalesforce<T>(record);
        }
        catch (Models.Errors.SalesforceNotFoundException)
        {
            // Record not found - return null (expected behavior)
            return null;
        }
        catch (Models.Errors.SalesforceException ex) when (ex.ErrorCode == "NOT_FOUND" || ex.ErrorCode == "MALFORMED_ID")
        {
            // Invalid or not found ID - return null
            return null;
        }
        // All other exceptions should bubble up to caller for proper handling
    }

    public async Task<T?> GetAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) where T : class, new()
    {
        var results = await Query<T>()
            .Where(predicate)
            .Take(1)
            .ToListAsync(cancellationToken);

        return results.FirstOrDefault();
    }

    public async Task<List<T>> GetAllAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new()
    {
        var query = Query<T>();

        if (predicate != null)
        {
            query = (SalesforceQueryable<T>)query.Where(predicate);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<string> CreateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        var objectName = SalesforceMapper.GetObjectName<T>();
        var data = SalesforceMapper.ToSalesforceDictionary(record, forCreate: true);

        return await _dataService.CreateRecordAsync(objectName, data, cancellationToken);
    }

    public async Task UpdateAsync<T>(T record, CancellationToken cancellationToken = default) where T : class
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        var objectName = SalesforceMapper.GetObjectName<T>();
        var data = SalesforceMapper.ToSalesforceDictionary(record, forUpdate: true);

        // Get the Id
        var idFieldName = SalesforceMapper.GetIdFieldName<T>() ?? "Id";
        if (!data.TryGetValue(idFieldName, out var idValue) || idValue == null)
        {
            // Try to get Id from the dictionary with "Id" key
            if (!data.TryGetValue("Id", out idValue) || idValue == null)
            {
                throw new InvalidOperationException("Record must have a non-null Id for update operations");
            }
        }

        var id = idValue.ToString()!;

        // Remove Id from update payload
        data.Remove(idFieldName);
        data.Remove("Id");

        await _dataService.UpdateRecordAsync(objectName, id, data, cancellationToken);
    }

    public async Task DeleteAsync<T>(string id, CancellationToken cancellationToken = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required", nameof(id));

        var objectName = SalesforceMapper.GetObjectName<T>();
        await _dataService.DeleteRecordAsync(objectName, id, cancellationToken);
    }

    public async Task<string> UpsertAsync<T>(T record, string? externalIdField = null, CancellationToken cancellationToken = default) where T : class
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        var objectName = SalesforceMapper.GetObjectName<T>();
        var data = SalesforceMapper.ToSalesforceDictionary(record, forCreate: true);

        // Determine external ID field
        externalIdField ??= SalesforceMapper.GetExternalIdFieldName<T>();

        if (string.IsNullOrEmpty(externalIdField))
        {
            throw new InvalidOperationException(
                "No external ID field specified. Either pass externalIdField parameter " +
                "or decorate a property with [SalesforceExternalId].");
        }

        // Check if record has the external ID
        if (!data.TryGetValue(externalIdField, out var externalIdValue) || externalIdValue == null)
        {
            throw new InvalidOperationException($"Record must have a non-null value for external ID field '{externalIdField}'");
        }

        // For upsert, we use PATCH with the external ID in the URL
        // Note: This requires custom implementation or using composite/bulk APIs
        // For now, we'll do a simple check-and-create/update pattern

        var externalId = externalIdValue.ToString()!;

        // Use SoqlBuilder to prevent injection
        var builder = new SoqlBuilder(objectName)
            .Select("Id")
            .Where(externalIdField, externalId)
            .Limit(1);

        var result = await _dataService.QueryAsync(builder.Build(), cancellationToken);

        if (result.Records.Count > 0)
        {
            // Update existing
            var existingId = result.Records[0]["Id"]!.ToString()!;
            data.Remove(externalIdField); // Don't try to update the external ID
            await _dataService.UpdateRecordAsync(objectName, existingId, data, cancellationToken);
            return existingId;
        }
        else
        {
            // Create new
            return await _dataService.CreateRecordAsync(objectName, data, cancellationToken);
        }
    }

    public async Task<int> CountAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new()
    {
        var query = Query<T>();

        if (predicate != null)
        {
            query = (SalesforceQueryable<T>)query.Where(predicate);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<bool> AnyAsync<T>(Expression<Func<T, bool>>? predicate = null, CancellationToken cancellationToken = default) where T : class, new()
    {
        var query = Query<T>();

        if (predicate != null)
        {
            query = (SalesforceQueryable<T>)query.Where(predicate);
        }

        // Use Queryable.Take explicitly to avoid ambiguity with IAsyncEnumerable.Take
        query = (SalesforceQueryable<T>)System.Linq.Queryable.Take(query, 1);

        var results = await query.ToListAsync(cancellationToken);
        return results.Count > 0;
    }
}

/// <summary>
/// Extension methods for IDataService to provide typed operations.
/// </summary>
public static class TypedDataServiceExtensions
{
    // Note: Query<T> extension method is defined in SalesforceQueryExtensions (SalesforceQueryable.cs)

    /// <summary>
    /// Gets a record by ID and maps it to a strongly-typed model.
    /// </summary>
    /// <remarks>
    /// This method returns null for not-found records but allows other exceptions to propagate.
    /// Salesforce IDs are case-sensitive (15-char) or case-insensitive (18-char).
    /// </remarks>
    public static async Task<T?> GetByIdAsync<T>(this IDataService dataService, string id, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        // Normalize the ID - trim whitespace
        var normalizedId = id.Trim();

        var objectName = SalesforceMapper.GetObjectName<T>();
        var fields = SalesforceMapper.GetQueryableFields<T>().ToList();

        // Ensure Id is always included in fields
        if (!fields.Contains("Id", StringComparer.OrdinalIgnoreCase))
        {
            fields.Insert(0, "Id");
        }

        try
        {
            var record = await dataService.GetRecordAsync(objectName, normalizedId, fields, cancellationToken);
            return SalesforceMapper.FromSalesforce<T>(record);
        }
        catch (Models.Errors.SalesforceNotFoundException)
        {
            // Record not found - return null (expected behavior)
            return null;
        }
        catch (Models.Errors.SalesforceException ex) when (ex.ErrorCode == "NOT_FOUND" || ex.ErrorCode == "MALFORMED_ID")
        {
            // Invalid or not found ID - return null
            return null;
        }
        // All other exceptions should bubble up to caller for proper handling
    }

    /// <summary>
    /// Creates a record from a strongly-typed model.
    /// </summary>
    public static async Task<string> CreateAsync<T>(this IDataService dataService, T record, CancellationToken cancellationToken = default)
        where T : class
    {
        var objectName = SalesforceMapper.GetObjectName<T>();
        var data = SalesforceMapper.ToSalesforceDictionary(record, forCreate: true);
        return await dataService.CreateRecordAsync(objectName, data, cancellationToken);
    }

    /// <summary>
    /// Updates a record from a strongly-typed model.
    /// </summary>
    public static async Task UpdateAsync<T>(this IDataService dataService, string id, T record, CancellationToken cancellationToken = default)
        where T : class
    {
        var objectName = SalesforceMapper.GetObjectName<T>();
        var data = SalesforceMapper.ToSalesforceDictionary(record, forUpdate: true);
        data.Remove("Id"); // Remove Id from payload
        await dataService.UpdateRecordAsync(objectName, id, data, cancellationToken);
    }

    /// <summary>
    /// Deletes a record of the specified type.
    /// </summary>
    public static async Task DeleteAsync<T>(this IDataService dataService, string id, CancellationToken cancellationToken = default)
        where T : class
    {
        var objectName = SalesforceMapper.GetObjectName<T>();
        await dataService.DeleteRecordAsync(objectName, id, cancellationToken);
    }

    /// <summary>
    /// Queries records and maps them to strongly-typed models.
    /// </summary>
    public static async Task<List<T>> QueryAsync<T>(this IDataService dataService, string soql, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var result = await dataService.QueryAsync(soql, cancellationToken);
        return SalesforceMapper.FromSalesforce<T>(result.Records);
    }
}
