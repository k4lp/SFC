using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Tag helper that renders a Salesforce lookup field with search functionality.
/// Provides a user-friendly way to select related records via AJAX search.
/// </summary>
/// <remarks>
/// <para>
/// This is the "must-have" component for any custom Salesforce form. It renders
/// a fully functional, AJAX-powered lookup component that searches Salesforce
/// records and allows selection.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Basic lookup --&gt;
/// &lt;sf-lookup asp-for="AccountId" sf-object="Contact" /&gt;
///
/// &lt;!-- With custom search fields --&gt;
/// &lt;sf-lookup asp-for="AccountId" sf-object="Contact"
///            sf-search-fields="Name,AccountNumber" /&gt;
///
/// &lt;!-- Polymorphic lookup (e.g., WhatId on Task) --&gt;
/// &lt;sf-lookup asp-for="WhatId" sf-object="Task"
///            sf-polymorphic-targets="Account,Opportunity,Case" /&gt;
///
/// &lt;!-- With placeholder and clear button --&gt;
/// &lt;sf-lookup asp-for="ParentId" sf-object="Account"
///            sf-placeholder="Search accounts..." sf-allow-clear="true" /&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("sf-lookup", TagStructure = TagStructure.NormalOrSelfClosing)]
public class SalesforceLookupTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;
    private readonly ILookupService _lookupService;
    private readonly IHtmlGenerator _htmlGenerator;

    /// <summary>
    /// The model expression for the lookup field value (the ID).
    /// </summary>
    [HtmlAttributeName("asp-for")]
    public ModelExpression? For { get; set; }

    /// <summary>
    /// The Salesforce object API name that contains this lookup field.
    /// Used to determine the target object from field metadata.
    /// </summary>
    [HtmlAttributeName("sf-object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The lookup field API name. If not specified, derived from asp-for.
    /// </summary>
    [HtmlAttributeName("sf-field")]
    public string? FieldName { get; set; }

    /// <summary>
    /// The target object to search. If not specified, derived from field metadata.
    /// </summary>
    [HtmlAttributeName("sf-target-object")]
    public string? TargetObject { get; set; }

    /// <summary>
    /// Comma-separated list of fields to search in the target object.
    /// Defaults to the Name field.
    /// </summary>
    [HtmlAttributeName("sf-search-fields")]
    public string? SearchFields { get; set; }

    /// <summary>
    /// Template for displaying search results. Use {FieldName} placeholders.
    /// Example: "{Name} ({AccountNumber})"
    /// </summary>
    [HtmlAttributeName("sf-display-template")]
    public string? DisplayTemplate { get; set; }

    /// <summary>
    /// Placeholder text for the search input.
    /// </summary>
    [HtmlAttributeName("sf-placeholder")]
    public string Placeholder { get; set; } = "Search...";

    /// <summary>
    /// Whether to show a clear button to remove the selection.
    /// </summary>
    [HtmlAttributeName("sf-allow-clear")]
    public bool AllowClear { get; set; } = true;

    /// <summary>
    /// Comma-separated list of target objects for polymorphic lookups.
    /// Example: "Account,Opportunity,Case"
    /// </summary>
    [HtmlAttributeName("sf-polymorphic-targets")]
    public string? PolymorphicTargets { get; set; }

    /// <summary>
    /// CSS class to apply to the container div.
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
    /// Minimum characters before search is triggered.
    /// </summary>
    [HtmlAttributeName("sf-min-chars")]
    public int MinChars { get; set; } = 2;

    /// <summary>
    /// Debounce delay in milliseconds for search input.
    /// </summary>
    [HtmlAttributeName("sf-debounce")]
    public int DebounceMs { get; set; } = 300;

    /// <summary>
    /// Maximum number of results to show.
    /// </summary>
    [HtmlAttributeName("sf-limit")]
    public int Limit { get; set; } = 10;

    /// <summary>
    /// The ViewContext for generating HTML.
    /// </summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// Creates a new SalesforceLookupTagHelper.
    /// </summary>
    public SalesforceLookupTagHelper(
        ISchemaService schemaService,
        ILookupService lookupService,
        IHtmlGenerator htmlGenerator)
    {
        _schemaService = schemaService;
        _lookupService = lookupService;
        _htmlGenerator = htmlGenerator;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Build CSS classes
        var containerClasses = "sf-lookup-container";
        if (!string.IsNullOrEmpty(CssClass))
        {
            containerClasses += " " + CssClass;
        }
        output.Attributes.SetAttribute("class", containerClasses);

        // Determine field name
        var fieldName = FieldName ?? For?.Name ?? "lookup";
        var inputId = fieldName.Replace(".", "_");

        // Get current value
        var currentValue = For?.Model?.ToString();
        var displayValue = "";

        // Determine target object(s)
        var targetObjects = await GetTargetObjectsAsync(fieldName);
        var isPolymorphic = targetObjects.Count > 1;

        // If there's a current value, resolve the display name
        if (!string.IsNullOrEmpty(currentValue))
        {
            displayValue = await ResolveDisplayNameAsync(currentValue, targetObjects);
        }

        // Build the HTML
        var html = BuildLookupHtml(
            fieldName,
            inputId,
            currentValue,
            displayValue,
            targetObjects,
            isPolymorphic);

        output.Content.SetHtmlContent(html);
    }

    private async Task<List<string>> GetTargetObjectsAsync(string fieldName)
    {
        // If polymorphic targets explicitly specified
        if (!string.IsNullOrWhiteSpace(PolymorphicTargets))
        {
            return PolymorphicTargets
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();
        }

        // If target object explicitly specified
        if (!string.IsNullOrWhiteSpace(TargetObject))
        {
            return new List<string> { TargetObject };
        }

        // Try to get from field metadata
        if (!string.IsNullOrWhiteSpace(ObjectName))
        {
            var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName);
            if (fieldMap.TryGetValue(fieldName, out var field) && field.ReferenceTo?.Count > 0)
            {
                return field.ReferenceTo;
            }
        }

        // Default to empty - will need to be specified
        return new List<string>();
    }

    private async Task<string> ResolveDisplayNameAsync(string recordId, List<string> targetObjects)
    {
        if (targetObjects.Count == 0)
            return recordId;

        try
        {
            // Try to resolve from each target object
            foreach (var targetObject in targetObjects)
            {
                var searchOptions = new LookupSearchOptions
                {
                    TargetObject = targetObject,
                    Query = recordId,
                    Limit = 1
                };

                var result = await _lookupService.SearchAsync(searchOptions);
                var item = result.Items.FirstOrDefault();
                if (item != null)
                {
                    return item.DisplayName ?? recordId;
                }
            }
        }
        catch
        {
            // If resolution fails, just show the ID
        }

        return recordId;
    }

    private string BuildLookupHtml(
        string fieldName,
        string inputId,
        string? currentValue,
        string displayValue,
        List<string> targetObjects,
        bool isPolymorphic)
    {
        var targetObjectsJson = string.Join(",", targetObjects);
        var searchFieldsAttr = !string.IsNullOrEmpty(SearchFields)
            ? $" data-sf-search-fields=\"{SearchFields}\""
            : "";
        var displayTemplateAttr = !string.IsNullOrEmpty(DisplayTemplate)
            ? $" data-sf-display-template=\"{System.Web.HttpUtility.HtmlAttributeEncode(DisplayTemplate)}\""
            : "";
        var disabledAttr = Disabled ? " disabled" : "";
        var requiredAttr = Required ? " required" : "";

        var html = $@"
<input type=""hidden""
       id=""{inputId}""
       name=""{fieldName}""
       value=""{System.Web.HttpUtility.HtmlAttributeEncode(currentValue ?? "")}""
       class=""sf-lookup-value""{requiredAttr} />
<div class=""sf-lookup-input-wrapper"">
    <input type=""text""
           id=""{inputId}_display""
           class=""sf-lookup-search form-control""
           placeholder=""{System.Web.HttpUtility.HtmlAttributeEncode(Placeholder)}""
           value=""{System.Web.HttpUtility.HtmlAttributeEncode(displayValue)}""
           autocomplete=""off""
           data-sf-lookup=""true""
           data-sf-target=""{inputId}""
           data-sf-target-objects=""{targetObjectsJson}""
           data-sf-polymorphic=""{isPolymorphic.ToString().ToLower()}""
           data-sf-min-chars=""{MinChars}""
           data-sf-debounce=""{DebounceMs}""
           data-sf-limit=""{Limit}""
           data-sf-search-url=""/Lookup/Search""{searchFieldsAttr}{displayTemplateAttr}{disabledAttr} />
    <span class=""sf-lookup-icon"">
        <svg xmlns=""http://www.w3.org/2000/svg"" width=""16"" height=""16"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"">
            <circle cx=""11"" cy=""11"" r=""8""></circle>
            <line x1=""21"" y1=""21"" x2=""16.65"" y2=""16.65""></line>
        </svg>
    </span>";

        if (AllowClear && !Disabled)
        {
            var clearDisplay = string.IsNullOrEmpty(currentValue) ? "none" : "flex";
            html += $@"
    <button type=""button""
            class=""sf-lookup-clear""
            data-sf-clear=""{inputId}""
            style=""display:{clearDisplay}""
            title=""Clear selection"">
        <svg xmlns=""http://www.w3.org/2000/svg"" width=""14"" height=""14"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"">
            <line x1=""18"" y1=""6"" x2=""6"" y2=""18""></line>
            <line x1=""6"" y1=""6"" x2=""18"" y2=""18""></line>
        </svg>
    </button>";
        }

        html += $@"
</div>
<div class=""sf-lookup-dropdown"" id=""{inputId}_dropdown"" style=""display:none"">
    <div class=""sf-lookup-loading"" style=""display:none"">
        <span class=""sf-lookup-spinner""></span> Searching...
    </div>
    <div class=""sf-lookup-results""></div>
    <div class=""sf-lookup-empty"" style=""display:none"">No results found</div>
</div>";

        return html;
    }
}
