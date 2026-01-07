using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Schema;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Tag helper that renders a Salesforce picklist field with metadata-driven options.
/// Supports standard picklists, dependent picklists, and record-type specific values.
/// </summary>
/// <remarks>
/// <para>
/// This tag helper automatically populates select options from Salesforce metadata,
/// ensuring that picklist values are always in sync with your org configuration.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Basic picklist --&gt;
/// &lt;sf-picklist asp-for="Industry" sf-object="Account" /&gt;
///
/// &lt;!-- With blank option --&gt;
/// &lt;sf-picklist asp-for="Status" sf-object="Lead"
///              sf-include-blank="true" sf-blank-text="-- Select Status --" /&gt;
///
/// &lt;!-- Record type specific values --&gt;
/// &lt;sf-picklist asp-for="Type" sf-object="Opportunity"
///              sf-record-type-id="@Model.RecordTypeId" /&gt;
///
/// &lt;!-- Dependent picklist --&gt;
/// &lt;sf-picklist asp-for="State" sf-object="Lead"
///              sf-controlling-field="Country" /&gt;
///
/// &lt;!-- Multi-select picklist --&gt;
/// &lt;sf-picklist asp-for="Industries" sf-object="Account"
///              sf-multiple="true" /&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("sf-picklist", TagStructure = TagStructure.NormalOrSelfClosing)]
[HtmlTargetElement("select", Attributes = "sf-picklist-field")]
public class SalesforcePicklistTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;
    private readonly IRecordTypeManager _recordTypeManager;

    /// <summary>
    /// The model expression for the picklist field value.
    /// </summary>
    [HtmlAttributeName("asp-for")]
    public ModelExpression? For { get; set; }

    /// <summary>
    /// The Salesforce object API name.
    /// </summary>
    [HtmlAttributeName("sf-object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The picklist field API name. If not specified, derived from asp-for.
    /// </summary>
    [HtmlAttributeName("sf-picklist-field")]
    public string? FieldName { get; set; }

    /// <summary>
    /// Record type ID for record-type specific picklist values.
    /// </summary>
    [HtmlAttributeName("sf-record-type-id")]
    public string? RecordTypeId { get; set; }

    /// <summary>
    /// The controlling field name for dependent picklists.
    /// </summary>
    [HtmlAttributeName("sf-controlling-field")]
    public string? ControllingField { get; set; }

    /// <summary>
    /// Whether to include a blank option at the beginning.
    /// </summary>
    [HtmlAttributeName("sf-include-blank")]
    public bool IncludeBlank { get; set; } = true;

    /// <summary>
    /// Text for the blank option.
    /// </summary>
    [HtmlAttributeName("sf-blank-text")]
    public string BlankText { get; set; } = "-- Select --";

    /// <summary>
    /// Whether this is a multi-select picklist.
    /// </summary>
    [HtmlAttributeName("sf-multiple")]
    public bool Multiple { get; set; }

    /// <summary>
    /// CSS class to apply to the select element.
    /// </summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    /// <summary>
    /// Whether the field is disabled.
    /// </summary>
    [HtmlAttributeName("sf-disabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// Whether the field is required.
    /// </summary>
    [HtmlAttributeName("sf-required")]
    public bool Required { get; set; }

    /// <summary>
    /// Size attribute for multi-select (number of visible options).
    /// </summary>
    [HtmlAttributeName("sf-size")]
    public int? Size { get; set; }

    /// <summary>
    /// The ViewContext for generating HTML.
    /// </summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// Creates a new SalesforcePicklistTagHelper.
    /// </summary>
    public SalesforcePicklistTagHelper(
        ISchemaService schemaService,
        IRecordTypeManager recordTypeManager)
    {
        _schemaService = schemaService;
        _recordTypeManager = recordTypeManager;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "select";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Determine field name
        var fieldName = FieldName ?? For?.Name ?? "picklist";
        var inputId = fieldName.Replace(".", "_");

        // Get current value(s)
        var currentValue = For?.Model?.ToString() ?? "";
        var currentValues = Multiple
            ? currentValue.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { currentValue };

        // Set basic attributes
        output.Attributes.SetAttribute("id", inputId);
        output.Attributes.SetAttribute("name", fieldName);

        var cssClasses = "form-select sf-picklist";
        if (!string.IsNullOrEmpty(CssClass))
        {
            cssClasses += " " + CssClass;
        }
        output.Attributes.SetAttribute("class", cssClasses);

        if (Multiple)
        {
            output.Attributes.SetAttribute("multiple", "multiple");
            if (Size.HasValue)
            {
                output.Attributes.SetAttribute("size", Size.Value.ToString());
            }
        }

        if (Disabled)
        {
            output.Attributes.SetAttribute("disabled", "disabled");
        }

        if (Required)
        {
            output.Attributes.SetAttribute("required", "required");
        }

        // Get picklist values and dependency info
        var (picklistValues, isDependentPicklist, controllerName, dependencyMap) = await GetPicklistDataAsync(fieldName);

        // Add dependent picklist data attributes
        if (isDependentPicklist)
        {
            output.Attributes.SetAttribute("data-sf-dependent", "true");
            output.Attributes.SetAttribute("data-sf-controller", controllerName ?? ControllingField ?? "");

            if (dependencyMap != null && dependencyMap.Count > 0)
            {
                var dependencyJson = JsonSerializer.Serialize(dependencyMap);
                output.Attributes.SetAttribute("data-sf-dependency-map", dependencyJson);
            }
        }

        // Build options HTML
        var optionsHtml = BuildOptionsHtml(picklistValues, currentValues, isDependentPicklist);
        output.Content.SetHtmlContent(optionsHtml);
    }

    private async Task<(List<PicklistOption> Values, bool IsDependent, string? ControllerName, Dictionary<string, List<string>>? DependencyMap)>
        GetPicklistDataAsync(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(ObjectName))
        {
            return (new List<PicklistOption>(), false, null, null);
        }

        var values = new List<PicklistOption>();
        var isDependentPicklist = false;
        string? controllerName = null;
        Dictionary<string, List<string>>? dependencyMap = null;

        try
        {
            // Get field metadata to check if dependent
            var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName);
            if (fieldMap.TryGetValue(fieldName, out var field))
            {
                isDependentPicklist = field.DependentPicklist;
                controllerName = field.ControllerName;
            }

            // Get picklist values - either record type specific or general
            if (!string.IsNullOrEmpty(RecordTypeId))
            {
                var rtValues = await _recordTypeManager.GetPicklistValuesForRecordTypeAsync(
                    ObjectName, RecordTypeId, fieldName);

                values = rtValues.Select(v => new PicklistOption
                {
                    Value = v.Value,
                    Label = v.Label,
                    IsDefault = v.DefaultValue,
                    IsActive = v.Active
                }).ToList();
            }
            else
            {
                var result = await _schemaService.GetPicklistValuesAsync(ObjectName, fieldName);
                values = result.Values.Select(v => new PicklistOption
                {
                    Value = v.Value,
                    Label = v.Label,
                    IsDefault = v.DefaultValue,
                    IsActive = v.Active
                }).ToList();

                // Get dependency map if this is a dependent picklist
                if (isDependentPicklist && result.DependencyMap != null)
                {
                    dependencyMap = result.DependencyMap;
                }
            }
        }
        catch
        {
            // If metadata fetch fails, return empty list
        }

        return (values, isDependentPicklist, controllerName, dependencyMap);
    }

    private string BuildOptionsHtml(
        List<PicklistOption> picklistValues,
        HashSet<string> currentValues,
        bool isDependentPicklist)
    {
        var html = "";

        // Add blank option if requested
        if (IncludeBlank && !Multiple)
        {
            var blankSelected = string.IsNullOrEmpty(currentValues.FirstOrDefault());
            html += $"<option value=\"\"{(blankSelected ? " selected" : "")}>{System.Web.HttpUtility.HtmlEncode(BlankText)}</option>\n";
        }

        // Add picklist options
        foreach (var option in picklistValues.Where(v => v.IsActive))
        {
            var isSelected = currentValues.Contains(option.Value);
            var selectedAttr = isSelected ? " selected" : "";

            // For dependent picklists, we might want to show all options but disable some
            // The JavaScript will handle filtering based on the controlling value
            html += $"<option value=\"{System.Web.HttpUtility.HtmlAttributeEncode(option.Value)}\"{selectedAttr}>" +
                   $"{System.Web.HttpUtility.HtmlEncode(option.Label)}</option>\n";
        }

        return html;
    }

    private class PicklistOption
    {
        public string Value { get; set; } = "";
        public string Label { get; set; } = "";
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
