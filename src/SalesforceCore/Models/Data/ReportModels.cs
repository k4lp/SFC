using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Represents a report descriptor returned when listing reports.
/// </summary>
public class ReportDescriptor
{
    /// <summary>
    /// The report ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The report name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The report description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// URL to access the report.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// URL to get report metadata/describe.
    /// </summary>
    [JsonPropertyName("describeUrl")]
    public string? DescribeUrl { get; set; }

    /// <summary>
    /// URL to get all instances of this report.
    /// </summary>
    [JsonPropertyName("instancesUrl")]
    public string? InstancesUrl { get; set; }
}

/// <summary>
/// Represents the metadata of a report.
/// </summary>
public class ReportMetadata
{
    /// <summary>
    /// The report ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The report name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The report format (TABULAR, SUMMARY, MATRIX, MULTI_BLOCK).
    /// </summary>
    [JsonPropertyName("reportFormat")]
    public string? ReportFormat { get; set; }

    /// <summary>
    /// The report type.
    /// </summary>
    [JsonPropertyName("reportType")]
    public ReportTypeInfo? ReportType { get; set; }

    /// <summary>
    /// Column information for the report.
    /// </summary>
    [JsonPropertyName("detailColumns")]
    public List<string>? DetailColumns { get; set; }

    /// <summary>
    /// Report filters.
    /// </summary>
    [JsonPropertyName("reportFilters")]
    public List<ReportFilter>? ReportFilters { get; set; }

    /// <summary>
    /// Standard date filter.
    /// </summary>
    [JsonPropertyName("standardDateFilter")]
    public StandardDateFilter? StandardDateFilter { get; set; }

    /// <summary>
    /// Whether the report has detail rows.
    /// </summary>
    [JsonPropertyName("hasDetailRows")]
    public bool HasDetailRows { get; set; }

    /// <summary>
    /// Whether the report has record count.
    /// </summary>
    [JsonPropertyName("hasRecordCount")]
    public bool HasRecordCount { get; set; }

    /// <summary>
    /// Groupings for the report.
    /// </summary>
    [JsonPropertyName("groupingsDown")]
    public List<ReportGrouping>? GroupingsDown { get; set; }

    /// <summary>
    /// Cross-groupings for matrix reports.
    /// </summary>
    [JsonPropertyName("groupingsAcross")]
    public List<ReportGrouping>? GroupingsAcross { get; set; }

    /// <summary>
    /// Aggregates/summaries for the report.
    /// </summary>
    [JsonPropertyName("aggregates")]
    public List<string>? Aggregates { get; set; }
}

/// <summary>
/// Report type information.
/// </summary>
public class ReportTypeInfo
{
    /// <summary>
    /// The report type label.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// The report type API name.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>
/// A filter applied to a report.
/// </summary>
public class ReportFilter
{
    /// <summary>
    /// The field being filtered.
    /// </summary>
    [JsonPropertyName("column")]
    public string? Column { get; set; }

    /// <summary>
    /// The filter operator (equals, notEqual, lessThan, etc).
    /// </summary>
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    /// <summary>
    /// The filter value.
    /// </summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>
    /// Whether this is a filter on a row limit field.
    /// </summary>
    [JsonPropertyName("isRunPageEditable")]
    public bool IsRunPageEditable { get; set; }
}

/// <summary>
/// Standard date filter for reports.
/// </summary>
public class StandardDateFilter
{
    /// <summary>
    /// The column being filtered.
    /// </summary>
    [JsonPropertyName("column")]
    public string? Column { get; set; }

    /// <summary>
    /// The duration value (THIS_MONTH, LAST_N_DAYS, etc).
    /// </summary>
    [JsonPropertyName("durationValue")]
    public string? DurationValue { get; set; }

    /// <summary>
    /// Start date for custom ranges.
    /// </summary>
    [JsonPropertyName("startDate")]
    public string? StartDate { get; set; }

    /// <summary>
    /// End date for custom ranges.
    /// </summary>
    [JsonPropertyName("endDate")]
    public string? EndDate { get; set; }
}

/// <summary>
/// Report grouping configuration.
/// </summary>
public class ReportGrouping
{
    /// <summary>
    /// The field name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Sort order (asc, desc).
    /// </summary>
    [JsonPropertyName("sortOrder")]
    public string? SortOrder { get; set; }

    /// <summary>
    /// Date granularity for date fields.
    /// </summary>
    [JsonPropertyName("dateGranularity")]
    public string? DateGranularity { get; set; }
}

/// <summary>
/// Represents the execution results of a report.
/// </summary>
public class ReportResults
{
    /// <summary>
    /// Whether all results were returned (not truncated).
    /// </summary>
    [JsonPropertyName("allData")]
    public bool AllData { get; set; }

    /// <summary>
    /// The report metadata.
    /// </summary>
    [JsonPropertyName("reportMetadata")]
    public ReportMetadata? ReportMetadata { get; set; }

    /// <summary>
    /// Extended metadata with column information.
    /// </summary>
    [JsonPropertyName("reportExtendedMetadata")]
    public ReportExtendedMetadata? ReportExtendedMetadata { get; set; }

    /// <summary>
    /// The fact map containing report data.
    /// Keys represent grouping positions (e.g., "0!0", "T!T" for grand total).
    /// </summary>
    [JsonPropertyName("factMap")]
    public Dictionary<string, FactMapEntry>? FactMap { get; set; }

    /// <summary>
    /// Grouping information for rows.
    /// </summary>
    [JsonPropertyName("groupingsDown")]
    public GroupingData? GroupingsDown { get; set; }

    /// <summary>
    /// Grouping information for columns (matrix reports).
    /// </summary>
    [JsonPropertyName("groupingsAcross")]
    public GroupingData? GroupingsAcross { get; set; }

    /// <summary>
    /// Gets the grand total row from the fact map.
    /// </summary>
    public FactMapEntry? GetGrandTotal()
    {
        if (FactMap == null) return null;

        // For tabular reports, use "T!T"
        if (FactMap.TryGetValue("T!T", out var grandTotal))
        {
            return grandTotal;
        }

        // For summary reports without groupings
        if (FactMap.TryGetValue("0!T", out var summaryTotal))
        {
            return summaryTotal;
        }

        return null;
    }

    /// <summary>
    /// Gets all detail rows from a tabular report.
    /// </summary>
    public List<ReportRow> GetDetailRows()
    {
        if (FactMap == null) return new List<ReportRow>();

        if (FactMap.TryGetValue("T!T", out var entry) && entry.Rows != null)
        {
            return entry.Rows;
        }

        return new List<ReportRow>();
    }
}

/// <summary>
/// Extended metadata containing column information.
/// </summary>
public class ReportExtendedMetadata
{
    /// <summary>
    /// Detail column information keyed by column API name.
    /// </summary>
    [JsonPropertyName("detailColumnInfo")]
    public Dictionary<string, ColumnInfo>? DetailColumnInfo { get; set; }

    /// <summary>
    /// Aggregate column information keyed by aggregate API name.
    /// </summary>
    [JsonPropertyName("aggregateColumnInfo")]
    public Dictionary<string, ColumnInfo>? AggregateColumnInfo { get; set; }

    /// <summary>
    /// Grouping column information.
    /// </summary>
    [JsonPropertyName("groupingColumnInfo")]
    public Dictionary<string, ColumnInfo>? GroupingColumnInfo { get; set; }
}

/// <summary>
/// Information about a report column.
/// </summary>
public class ColumnInfo
{
    /// <summary>
    /// The column label.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// The data type of the column.
    /// </summary>
    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }
}

/// <summary>
/// An entry in the fact map.
/// </summary>
public class FactMapEntry
{
    /// <summary>
    /// Aggregate values for this grouping level.
    /// </summary>
    [JsonPropertyName("aggregates")]
    public List<AggregateValue>? Aggregates { get; set; }

    /// <summary>
    /// Detail rows for this grouping level.
    /// </summary>
    [JsonPropertyName("rows")]
    public List<ReportRow>? Rows { get; set; }
}

/// <summary>
/// An aggregate (summary) value.
/// </summary>
public class AggregateValue
{
    /// <summary>
    /// The aggregate value.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// The display label.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>
/// A detail row in the report.
/// </summary>
public class ReportRow
{
    /// <summary>
    /// Cell values in the row.
    /// </summary>
    [JsonPropertyName("dataCells")]
    public List<DataCell>? DataCells { get; set; }
}

/// <summary>
/// A cell in a report row.
/// </summary>
public class DataCell
{
    /// <summary>
    /// The cell value.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// The display label.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }
}

/// <summary>
/// Grouping data for report results.
/// </summary>
public class GroupingData
{
    /// <summary>
    /// The groupings.
    /// </summary>
    [JsonPropertyName("groupings")]
    public List<GroupingValue>? Groupings { get; set; }
}

/// <summary>
/// A grouping value.
/// </summary>
public class GroupingValue
{
    /// <summary>
    /// The key for this grouping in the fact map.
    /// </summary>
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    /// <summary>
    /// The grouping value.
    /// </summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; }

    /// <summary>
    /// The display label.
    /// </summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>
    /// Sub-groupings.
    /// </summary>
    [JsonPropertyName("groupings")]
    public List<GroupingValue>? SubGroupings { get; set; }
}

/// <summary>
/// Options for running a report.
/// </summary>
public class ReportRunOptions
{
    /// <summary>
    /// Whether to include detail rows.
    /// </summary>
    public bool IncludeDetails { get; set; } = true;

    /// <summary>
    /// Dynamic filters to apply (override report filters).
    /// </summary>
    public List<ReportFilter>? Filters { get; set; }

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static ReportRunOptions Default => new();

    /// <summary>
    /// Creates options with only summary data (no details).
    /// </summary>
    public static ReportRunOptions SummaryOnly => new() { IncludeDetails = false };
}
