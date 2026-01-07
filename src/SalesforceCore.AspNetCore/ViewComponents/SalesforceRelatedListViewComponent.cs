using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.AspNetCore.ViewComponents;

/// <summary>
/// View component that displays a related list of Salesforce records.
/// Shows child records related to a parent record.
/// </summary>
/// <remarks>
/// <para>
/// This component is useful for displaying related data on detail pages,
/// dashboards, or any view that needs to show child records.
/// </para>
/// <para>
/// Usage in Razor views:
/// <code>
/// &lt;!-- Basic usage --&gt;
/// @await Component.InvokeAsync("SalesforceRelatedList", new {
///     parentId = Model.Id,
///     childObject = "Contact",
///     relationshipField = "AccountId",
///     columns = "Name,Email,Phone"
/// })
///
/// &lt;!-- With customization --&gt;
/// @await Component.InvokeAsync("SalesforceRelatedList", new {
///     parentId = Model.Id,
///     childObject = "Opportunity",
///     relationshipField = "AccountId",
///     columns = "Name,StageName,Amount,CloseDate",
///     limit = 10,
///     orderBy = "CloseDate DESC",
///     showHeader = true,
///     allowCreate = true,
///     title = "Open Opportunities"
/// })
/// </code>
/// </para>
/// </remarks>
public class SalesforceRelatedListViewComponent : ViewComponent
{
    private readonly IDataService _dataService;
    private readonly ISchemaService _schemaService;

    /// <summary>
    /// Creates a new SalesforceRelatedListViewComponent.
    /// </summary>
    public SalesforceRelatedListViewComponent(
        IDataService dataService,
        ISchemaService schemaService)
    {
        _dataService = dataService;
        _schemaService = schemaService;
    }

    /// <summary>
    /// Renders the related list component.
    /// </summary>
    /// <param name="parentId">The parent record ID.</param>
    /// <param name="childObject">The child object API name.</param>
    /// <param name="relationshipField">The lookup field on the child pointing to the parent.</param>
    /// <param name="columns">Comma-separated list of field API names to display.</param>
    /// <param name="limit">Maximum records to display (default 5).</param>
    /// <param name="orderBy">Sort field and direction (default "CreatedDate DESC").</param>
    /// <param name="showHeader">Whether to show the header with title.</param>
    /// <param name="allowCreate">Whether to show a "New" button.</param>
    /// <param name="title">Custom title for the header (defaults to object plural label).</param>
    /// <param name="filter">Type-safe filter condition (use SoqlCondition.* methods).</param>
    /// <param name="viewAllUrl">URL for "View All" link.</param>
    /// <param name="createUrl">URL for the "New" button.</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string parentId,
        string childObject,
        string relationshipField,
        string columns,
        int limit = 5,
        string orderBy = "CreatedDate DESC",
        bool showHeader = true,
        bool allowCreate = false,
        string? title = null,
        SoqlCondition? filter = null,
        string? viewAllUrl = null,
        string? createUrl = null)
    {
        var model = new RelatedListViewModel
        {
            ParentId = parentId,
            ChildObject = childObject,
            RelationshipField = relationshipField,
            Limit = limit,
            ShowHeader = showHeader,
            AllowCreate = allowCreate,
            ViewAllUrl = viewAllUrl,
            CreateUrl = createUrl ?? $"/Salesforce/{childObject}/Create?{relationshipField}={parentId}"
        };

        // Validate parent ID
        if (string.IsNullOrEmpty(parentId) || !SecurityUtils.IsValidSalesforceId(parentId))
        {
            model.ErrorMessage = "Invalid parent record ID.";
            return View(model);
        }

        try
        {
            // Get object metadata for title and field labels
            var objectDescribe = await _schemaService.GetDescribeAsync(childObject);
            model.Title = title ?? objectDescribe?.LabelPlural ?? childObject;

            // Parse columns
            var columnList = columns
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .ToList();

            // Get field metadata for column headers
            var fieldMap = await _schemaService.GetFieldMapAsync(childObject);
            model.Columns = columnList.Select(c =>
            {
                var field = fieldMap.TryGetValue(c, out var f) ? f : null;
                return new RelatedListColumn
                {
                    FieldName = c,
                    Label = field?.Label ?? c,
                    Type = field?.Type ?? "string",
                    IsNameField = c.Equals("Name", StringComparison.OrdinalIgnoreCase)
                };
            }).ToList();

            // Build SOQL query using type-safe builder
            var allFields = new List<string> { "Id" };
            allFields.AddRange(columnList.Where(f => !f.Equals("Id", StringComparison.OrdinalIgnoreCase)));

            // Validate object name and relationship field
            if (!SecurityUtils.IsValidObjectName(childObject))
            {
                model.ErrorMessage = "Invalid child object name.";
                return View(model);
            }

            if (!SecurityUtils.IsValidFieldName(relationshipField))
            {
                model.ErrorMessage = "Invalid relationship field name.";
                return View(model);
            }

            // Parse and validate orderBy (format: "FieldName" or "FieldName DESC")
            var (orderField, isDescending) = ParseOrderBy(orderBy);
            if (!SecurityUtils.IsValidFieldName(orderField))
            {
                model.ErrorMessage = "Invalid order by field name.";
                return View(model);
            }

            var queryBuilder = SoqlBuilder.From(childObject)
                .Select(allFields)
                .WhereEquals(relationshipField, parentId);

            // Add type-safe filter if provided
            if (filter != null)
            {
                queryBuilder.WhereCondition(filter);
            }

            // Add order by
            if (isDescending)
                queryBuilder.OrderByDescending(orderField);
            else
                queryBuilder.OrderBy(orderField);

            // Fetch one extra to check if there are more
            queryBuilder.Limit(limit + 1);

            var soql = queryBuilder.Build();

            // Execute query
            var result = await _dataService.QueryAsync(soql);
            var records = result.Records.ToList();

            // Check if there are more records
            model.HasMoreRecords = records.Count > limit;
            if (model.HasMoreRecords)
            {
                records = records.Take(limit).ToList();
            }

            // Process records (JsonObject records from QueryResult)
            model.Records = records.Select(r =>
            {
                var row = new RelatedListRow
                {
                    Id = r["Id"]?.ToString() ?? ""
                };

                foreach (var col in model.Columns)
                {
                    var value = r[col.FieldName];
                    row.Values[col.FieldName] = FormatFieldValue(value, col.Type);
                }

                return row;
            }).ToList();

            model.TotalCount = model.Records.Count;
        }
        catch (Exception ex)
        {
            model.ErrorMessage = $"Error loading related records: {ex.Message}";
        }

        return View(model);
    }

    private static string FormatFieldValue(System.Text.Json.Nodes.JsonNode? value, string fieldType)
    {
        if (value == null || value.GetValueKind() == System.Text.Json.JsonValueKind.Null) return "";

        var stringValue = value.ToString();

        return fieldType.ToLowerInvariant() switch
        {
            "date" when DateTime.TryParse(stringValue, out var date) =>
                date.ToString("MMM d, yyyy"),

            "datetime" when DateTime.TryParse(stringValue, out var dt) =>
                dt.ToString("MMM d, yyyy h:mm tt"),

            "currency" when decimal.TryParse(stringValue, out var currency) =>
                currency.ToString("C2"),

            "percent" when decimal.TryParse(stringValue, out var pct) =>
                pct.ToString("P0"),

            "boolean" => stringValue.ToLower() == "true" ? "Yes" : "No",

            _ => stringValue
        };
    }

    /// <summary>
    /// Parses an orderBy string like "CreatedDate DESC" into field name and direction.
    /// </summary>
    private static (string Field, bool Descending) ParseOrderBy(string orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
            return ("CreatedDate", true);

        var parts = orderBy.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var field = parts[0];
        var descending = parts.Length > 1 &&
            parts[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

        return (field, descending);
    }
}

/// <summary>
/// View model for the related list component.
/// </summary>
public class RelatedListViewModel
{
    /// <summary>The parent record ID.</summary>
    public string ParentId { get; set; } = "";

    /// <summary>The child object API name.</summary>
    public string ChildObject { get; set; } = "";

    /// <summary>The relationship field name.</summary>
    public string RelationshipField { get; set; } = "";

    /// <summary>The title to display in the header.</summary>
    public string Title { get; set; } = "";

    /// <summary>The columns to display.</summary>
    public List<RelatedListColumn> Columns { get; set; } = new();

    /// <summary>The records to display.</summary>
    public List<RelatedListRow> Records { get; set; } = new();

    /// <summary>Total count of records returned.</summary>
    public int TotalCount { get; set; }

    /// <summary>Maximum records displayed.</summary>
    public int Limit { get; set; }

    /// <summary>Whether there are more records beyond the limit.</summary>
    public bool HasMoreRecords { get; set; }

    /// <summary>Whether to show the header.</summary>
    public bool ShowHeader { get; set; }

    /// <summary>Whether to show the create button.</summary>
    public bool AllowCreate { get; set; }

    /// <summary>URL for the "View All" link.</summary>
    public string? ViewAllUrl { get; set; }

    /// <summary>URL for the "New" button.</summary>
    public string? CreateUrl { get; set; }

    /// <summary>Error message if loading failed.</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents a column in the related list.
/// </summary>
public class RelatedListColumn
{
    /// <summary>The field API name.</summary>
    public string FieldName { get; set; } = "";

    /// <summary>The display label.</summary>
    public string Label { get; set; } = "";

    /// <summary>The field type.</summary>
    public string Type { get; set; } = "";

    /// <summary>Whether this is the Name field (for linking).</summary>
    public bool IsNameField { get; set; }
}

/// <summary>
/// Represents a row in the related list.
/// </summary>
public class RelatedListRow
{
    /// <summary>The record ID.</summary>
    public string Id { get; set; } = "";

    /// <summary>The field values keyed by field name.</summary>
    public Dictionary<string, string> Values { get; set; } = new();
}
