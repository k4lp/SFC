using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Defines the CSS framework to use for rendering form elements.
/// </summary>
public enum CssFramework
{
    /// <summary>Bootstrap 5 classes.</summary>
    Bootstrap5,
    /// <summary>Salesforce Lightning Design System classes.</summary>
    SLDS,
    /// <summary>No framework-specific classes.</summary>
    None
}

/// <summary>
/// Tag helper that renders a complete form field group with label, input, help text, and validation.
/// Automatically determines the appropriate input type based on Salesforce field metadata.
/// </summary>
/// <remarks>
/// <para>
/// This tag helper eliminates boilerplate by generating the complete
/// "Label + Input + Validation + Help Text" block for a Salesforce field.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Basic field group --&gt;
/// &lt;sf-field-group asp-for="Name" sf-object="Account" /&gt;
///
/// &lt;!-- With custom label --&gt;
/// &lt;sf-field-group asp-for="Email" sf-object="Contact"
///                  sf-label="Email Address" /&gt;
///
/// &lt;!-- Using SLDS framework --&gt;
/// &lt;sf-field-group asp-for="Industry" sf-object="Account"
///                  sf-css-framework="SLDS" /&gt;
///
/// &lt;!-- Readonly field --&gt;
/// &lt;sf-field-group asp-for="CreatedDate" sf-object="Account"
///                  sf-readonly="true" /&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("sf-field-group", TagStructure = TagStructure.NormalOrSelfClosing)]
public class SalesforceFieldGroupTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;

    /// <summary>
    /// The model expression for the field value.
    /// </summary>
    [HtmlAttributeName("asp-for")]
    public ModelExpression? For { get; set; }

    /// <summary>
    /// The Salesforce object API name.
    /// </summary>
    [HtmlAttributeName("sf-object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The field API name. If not specified, derived from asp-for.
    /// </summary>
    [HtmlAttributeName("sf-field")]
    public string? FieldName { get; set; }

    /// <summary>
    /// Custom label text. If not specified, uses field label from metadata.
    /// </summary>
    [HtmlAttributeName("sf-label")]
    public string? Label { get; set; }

    /// <summary>
    /// Custom help text. If not specified, uses InlineHelpText from metadata.
    /// </summary>
    [HtmlAttributeName("sf-help-text")]
    public string? HelpText { get; set; }

    /// <summary>
    /// Whether the field should be rendered as readonly.
    /// </summary>
    [HtmlAttributeName("sf-readonly")]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Whether the field is required (overrides metadata).
    /// </summary>
    [HtmlAttributeName("sf-required")]
    public bool? Required { get; set; }

    /// <summary>
    /// The CSS framework to use for styling.
    /// </summary>
    [HtmlAttributeName("sf-css-framework")]
    public CssFramework Framework { get; set; } = CssFramework.Bootstrap5;

    /// <summary>
    /// CSS class to add to the input element.
    /// </summary>
    [HtmlAttributeName("sf-input-class")]
    public string? InputClass { get; set; }

    /// <summary>
    /// CSS class to add to the wrapper element.
    /// </summary>
    [HtmlAttributeName("class")]
    public string? WrapperClass { get; set; }

    /// <summary>
    /// Record type ID for picklist fields.
    /// </summary>
    [HtmlAttributeName("sf-record-type-id")]
    public string? RecordTypeId { get; set; }

    /// <summary>
    /// The ViewContext for generating HTML.
    /// </summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// Creates a new SalesforceFieldGroupTagHelper.
    /// </summary>
    public SalesforceFieldGroupTagHelper(ISchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Determine field name
        var fieldName = FieldName ?? For?.Name ?? "field";
        var inputId = fieldName.Replace(".", "_");

        // Get field metadata
        var fieldMeta = await GetFieldMetadataAsync(fieldName);

        // Determine label and help text
        var label = Label ?? fieldMeta?.Label ?? fieldName;
        var helpText = HelpText ?? fieldMeta?.InlineHelpText;
        var isRequired = Required ?? (fieldMeta != null && !fieldMeta.Nillable && fieldMeta.Createable);

        // Set wrapper class based on framework
        var wrapperClasses = GetWrapperClasses();
        if (!string.IsNullOrEmpty(WrapperClass))
        {
            wrapperClasses += " " + WrapperClass;
        }
        output.Attributes.SetAttribute("class", wrapperClasses);

        // Build the HTML
        var html = BuildFieldGroupHtml(
            fieldName,
            inputId,
            label,
            helpText,
            isRequired,
            fieldMeta);

        output.Content.SetHtmlContent(html);
    }

    private async Task<Models.Metadata.SObjectField?> GetFieldMetadataAsync(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(ObjectName))
            return null;

        try
        {
            var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName);
            return fieldMap.TryGetValue(fieldName, out var field) ? field : null;
        }
        catch
        {
            return null;
        }
    }

    private string GetWrapperClasses()
    {
        return Framework switch
        {
            CssFramework.Bootstrap5 => "mb-3",
            CssFramework.SLDS => "slds-form-element",
            CssFramework.None => "sf-field-group",
            _ => "mb-3"
        };
    }

    private string BuildFieldGroupHtml(
        string fieldName,
        string inputId,
        string label,
        string? helpText,
        bool isRequired,
        Models.Metadata.SObjectField? fieldMeta)
    {
        var currentValue = For?.Model;

        return Framework switch
        {
            CssFramework.Bootstrap5 => BuildBootstrapHtml(fieldName, inputId, label, helpText, isRequired, fieldMeta, currentValue),
            CssFramework.SLDS => BuildSldsHtml(fieldName, inputId, label, helpText, isRequired, fieldMeta, currentValue),
            CssFramework.None => BuildPlainHtml(fieldName, inputId, label, helpText, isRequired, fieldMeta, currentValue),
            _ => BuildBootstrapHtml(fieldName, inputId, label, helpText, isRequired, fieldMeta, currentValue)
        };
    }

    private string BuildBootstrapHtml(
        string fieldName,
        string inputId,
        string label,
        string? helpText,
        bool isRequired,
        Models.Metadata.SObjectField? fieldMeta,
        object? currentValue)
    {
        var requiredMark = isRequired ? " <span class=\"text-danger\">*</span>" : "";
        var inputClasses = "form-control" + (!string.IsNullOrEmpty(InputClass) ? " " + InputClass : "");

        var html = $@"<label for=""{inputId}"" class=""form-label"">{System.Web.HttpUtility.HtmlEncode(label)}{requiredMark}</label>
";

        html += BuildInputHtml(fieldName, inputId, inputClasses, isRequired, fieldMeta, currentValue);

        if (!string.IsNullOrEmpty(helpText))
        {
            html += $@"<div class=""form-text"">{System.Web.HttpUtility.HtmlEncode(helpText)}</div>
";
        }

        html += $@"<span class=""text-danger field-validation-valid"" data-valmsg-for=""{fieldName}"" data-valmsg-replace=""true""></span>
";

        return html;
    }

    private string BuildSldsHtml(
        string fieldName,
        string inputId,
        string label,
        string? helpText,
        bool isRequired,
        Models.Metadata.SObjectField? fieldMeta,
        object? currentValue)
    {
        var requiredMark = isRequired ? " <abbr class=\"slds-required\" title=\"required\">* </abbr>" : "";
        var inputClasses = "slds-input" + (!string.IsNullOrEmpty(InputClass) ? " " + InputClass : "");

        var html = $@"<label class=""slds-form-element__label"" for=""{inputId}"">{requiredMark}{System.Web.HttpUtility.HtmlEncode(label)}</label>
<div class=""slds-form-element__control"">
";

        html += BuildInputHtml(fieldName, inputId, inputClasses, isRequired, fieldMeta, currentValue);

        html += "</div>\n";

        if (!string.IsNullOrEmpty(helpText))
        {
            html += $@"<div class=""slds-form-element__help"">{System.Web.HttpUtility.HtmlEncode(helpText)}</div>
";
        }

        return html;
    }

    private string BuildPlainHtml(
        string fieldName,
        string inputId,
        string label,
        string? helpText,
        bool isRequired,
        Models.Metadata.SObjectField? fieldMeta,
        object? currentValue)
    {
        var requiredMark = isRequired ? " *" : "";
        var inputClasses = !string.IsNullOrEmpty(InputClass) ? InputClass : "";

        var html = $@"<label for=""{inputId}"">{System.Web.HttpUtility.HtmlEncode(label)}{requiredMark}</label>
";

        html += BuildInputHtml(fieldName, inputId, inputClasses, isRequired, fieldMeta, currentValue);

        if (!string.IsNullOrEmpty(helpText))
        {
            html += $@"<small class=""sf-help-text"">{System.Web.HttpUtility.HtmlEncode(helpText)}</small>
";
        }

        html += $@"<span class=""sf-validation-message"" data-valmsg-for=""{fieldName}""></span>
";

        return html;
    }

    private string BuildInputHtml(
        string fieldName,
        string inputId,
        string inputClasses,
        bool isRequired,
        Models.Metadata.SObjectField? fieldMeta,
        object? currentValue)
    {
        var fieldType = fieldMeta?.Type?.ToLowerInvariant() ?? "string";
        var readonlyAttr = ReadOnly ? " readonly" : "";
        var requiredAttr = isRequired ? " required" : "";
        var maxLengthAttr = fieldMeta?.Length > 0 ? $" maxlength=\"{fieldMeta.Length}\"" : "";
        var valueStr = currentValue?.ToString() ?? "";
        var encodedValue = System.Web.HttpUtility.HtmlAttributeEncode(valueStr);

        // Handle special field types
        if (fieldMeta?.IsPicklist == true)
        {
            // Return a placeholder that will be replaced by sf-picklist
            var rtIdAttr = !string.IsNullOrEmpty(RecordTypeId) ? $" sf-record-type-id=\"{RecordTypeId}\"" : "";
            return $@"<sf-picklist asp-for=""{For?.Name ?? fieldName}"" sf-object=""{ObjectName}"" sf-picklist-field=""{fieldMeta.Name}"" class=""{inputClasses}""{requiredAttr}{readonlyAttr}{rtIdAttr}></sf-picklist>
";
        }

        if (fieldMeta?.IsLookup == true)
        {
            // Return a placeholder that will be replaced by sf-lookup
            return $@"<sf-lookup asp-for=""{For?.Name ?? fieldName}"" sf-object=""{ObjectName}"" sf-field=""{fieldMeta.Name}"" class=""{inputClasses}""{requiredAttr}></sf-lookup>
";
        }

        // Map field type to input type
        return fieldType switch
        {
            "textarea" or "longtextarea" or "richtextarea" =>
                $@"<textarea id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" rows=""4""{requiredAttr}{readonlyAttr}{maxLengthAttr}>{System.Web.HttpUtility.HtmlEncode(valueStr)}</textarea>
",

            "boolean" =>
                $@"<div class=""form-check"">
    <input type=""checkbox"" id=""{inputId}"" name=""{fieldName}"" class=""form-check-input"" value=""true""{(currentValue?.ToString()?.ToLower() == "true" ? " checked" : "")}{readonlyAttr} />
</div>
",

            "date" =>
                $@"<input type=""date"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{FormatDateValue(currentValue, "yyyy-MM-dd")}""{requiredAttr}{readonlyAttr} />
",

            "datetime" =>
                $@"<input type=""datetime-local"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{FormatDateValue(currentValue, "yyyy-MM-ddTHH:mm")}""{requiredAttr}{readonlyAttr} />
",

            "time" =>
                $@"<input type=""time"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{FormatTimeValue(currentValue)}""{requiredAttr}{readonlyAttr} />
",

            "email" =>
                $@"<input type=""email"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}""{requiredAttr}{readonlyAttr}{maxLengthAttr} />
",

            "phone" =>
                $@"<input type=""tel"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}""{requiredAttr}{readonlyAttr}{maxLengthAttr} />
",

            "url" =>
                $@"<input type=""url"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}""{requiredAttr}{readonlyAttr}{maxLengthAttr} />
",

            "int" =>
                $@"<input type=""number"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}"" step=""1""{requiredAttr}{readonlyAttr} />
",

            "double" or "currency" or "percent" =>
                BuildNumberInput(inputId, fieldName, inputClasses, encodedValue, requiredAttr, readonlyAttr, fieldMeta),

            "encryptedstring" =>
                $@"<input type=""password"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value="""" placeholder=""(encrypted)"" autocomplete=""new-password""{readonlyAttr}{maxLengthAttr} />
",

            _ =>
                $@"<input type=""text"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}""{requiredAttr}{readonlyAttr}{maxLengthAttr} />
"
        };
    }

    private string BuildNumberInput(
        string inputId,
        string fieldName,
        string inputClasses,
        string encodedValue,
        string requiredAttr,
        string readonlyAttr,
        Models.Metadata.SObjectField? fieldMeta)
    {
        var step = "any";
        if (fieldMeta?.Scale > 0)
        {
            step = "0." + new string('0', fieldMeta.Scale - 1) + "1";
        }

        var prefix = "";
        var suffix = "";
        if (fieldMeta?.Type?.ToLower() == "currency")
        {
            prefix = "$";
        }
        else if (fieldMeta?.Type?.ToLower() == "percent")
        {
            suffix = "%";
        }

        if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
        {
            return $@"<div class=""input-group"">
    {(prefix != "" ? $"<span class=\"input-group-text\">{prefix}</span>" : "")}
    <input type=""number"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}"" step=""{step}""{requiredAttr}{readonlyAttr} />
    {(suffix != "" ? $"<span class=\"input-group-text\">{suffix}</span>" : "")}
</div>
";
        }

        return $@"<input type=""number"" id=""{inputId}"" name=""{fieldName}"" class=""{inputClasses}"" value=""{encodedValue}"" step=""{step}""{requiredAttr}{readonlyAttr} />
";
    }

    private static string FormatDateValue(object? value, string format)
    {
        if (value == null) return "";

        if (value is DateTime dt)
            return dt.ToString(format);

        if (value is DateOnly d)
            return d.ToString(format);

        if (value is DateTimeOffset dto)
            return dto.ToString(format);

        if (DateTime.TryParse(value.ToString(), out var parsed))
            return parsed.ToString(format);

        return value.ToString() ?? "";
    }

    private static string FormatTimeValue(object? value)
    {
        if (value == null) return "";

        if (value is TimeOnly t)
            return t.ToString("HH:mm");

        if (value is TimeSpan ts)
            return $"{ts.Hours:D2}:{ts.Minutes:D2}";

        if (value is DateTime dt)
            return dt.ToString("HH:mm");

        return value.ToString() ?? "";
    }
}
