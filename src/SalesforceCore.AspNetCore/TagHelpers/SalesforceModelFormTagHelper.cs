using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Defines the form mode which affects field rendering behavior.
/// </summary>
public enum FormMode
{
    /// <summary>Create mode - shows createable fields.</summary>
    Create,
    /// <summary>Edit mode - shows updateable fields.</summary>
    Edit,
    /// <summary>View mode - all fields are readonly.</summary>
    View
}

/// <summary>
/// Tag helper that generates a complete Salesforce form with all fields.
/// Automatically determines field types and renders appropriate input controls.
/// </summary>
/// <remarks>
/// <para>
/// This tag helper is ideal for rapid prototyping and internal tools where
/// you need a quick form without custom layout. It generates semantic HTML
/// that can be styled with CSS.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Basic create form --&gt;
/// &lt;sf-model-form sf-object="Account" sf-mode="Create"
///                asp-action="Create" asp-controller="Account" /&gt;
///
/// &lt;!-- Edit form with specific fields --&gt;
/// &lt;sf-model-form asp-model="Model" sf-object="Contact"
///                sf-mode="Edit"
///                sf-include-fields="FirstName,LastName,Email,Phone"
///                asp-action="Edit" /&gt;
///
/// &lt;!-- Multi-column layout --&gt;
/// &lt;sf-model-form sf-object="Opportunity" sf-mode="Create"
///                sf-columns="2" sf-css-framework="Bootstrap5" /&gt;
///
/// &lt;!-- Exclude system fields --&gt;
/// &lt;sf-model-form sf-object="Lead" sf-mode="Edit"
///                sf-exclude-fields="OwnerId,CreatedDate,LastModifiedDate" /&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("sf-model-form", TagStructure = TagStructure.NormalOrSelfClosing)]
public class SalesforceModelFormTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;

    /// <summary>
    /// The model to bind the form to.
    /// </summary>
    [HtmlAttributeName("asp-model")]
    public ModelExpression? Model { get; set; }

    /// <summary>
    /// The Salesforce object API name.
    /// </summary>
    [HtmlAttributeName("sf-object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The form mode (Create, Edit, View).
    /// </summary>
    [HtmlAttributeName("sf-mode")]
    public FormMode Mode { get; set; } = FormMode.Create;

    /// <summary>
    /// Number of columns for the form layout (1-4).
    /// </summary>
    [HtmlAttributeName("sf-columns")]
    public int Columns { get; set; } = 1;

    /// <summary>
    /// Comma-separated list of field API names to include.
    /// If specified, only these fields are shown.
    /// </summary>
    [HtmlAttributeName("sf-include-fields")]
    public string? IncludeFields { get; set; }

    /// <summary>
    /// Comma-separated list of field API names to exclude.
    /// </summary>
    [HtmlAttributeName("sf-exclude-fields")]
    public string? ExcludeFields { get; set; }

    /// <summary>
    /// Record type ID for record-type specific field behavior.
    /// </summary>
    [HtmlAttributeName("sf-record-type-id")]
    public string? RecordTypeId { get; set; }

    /// <summary>
    /// The form action URL or action name.
    /// </summary>
    [HtmlAttributeName("asp-action")]
    public string? Action { get; set; }

    /// <summary>
    /// The controller name for the form action.
    /// </summary>
    [HtmlAttributeName("asp-controller")]
    public string? Controller { get; set; }

    /// <summary>
    /// The HTTP method for form submission.
    /// </summary>
    [HtmlAttributeName("method")]
    public string Method { get; set; } = "post";

    /// <summary>
    /// The CSS framework to use for styling.
    /// </summary>
    [HtmlAttributeName("sf-css-framework")]
    public CssFramework Framework { get; set; } = CssFramework.Bootstrap5;

    /// <summary>
    /// CSS class to add to the form element.
    /// </summary>
    [HtmlAttributeName("class")]
    public string? CssClass { get; set; }

    /// <summary>
    /// Text for the submit button.
    /// </summary>
    [HtmlAttributeName("sf-submit-text")]
    public string SubmitText { get; set; } = "Save";

    /// <summary>
    /// Whether to show the cancel button.
    /// </summary>
    [HtmlAttributeName("sf-show-cancel")]
    public bool ShowCancel { get; set; } = true;

    /// <summary>
    /// Text for the cancel button.
    /// </summary>
    [HtmlAttributeName("sf-cancel-text")]
    public string CancelText { get; set; } = "Cancel";

    /// <summary>
    /// URL to navigate to when cancel is clicked.
    /// </summary>
    [HtmlAttributeName("sf-cancel-url")]
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Whether to include anti-forgery token.
    /// </summary>
    [HtmlAttributeName("sf-antiforgery")]
    public bool IncludeAntiforgery { get; set; } = true;

    /// <summary>
    /// Whether to show validation summary.
    /// </summary>
    [HtmlAttributeName("sf-show-validation-summary")]
    public bool ShowValidationSummary { get; set; } = true;

    /// <summary>
    /// The ViewContext for generating HTML.
    /// </summary>
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    /// <summary>
    /// Creates a new SalesforceModelFormTagHelper.
    /// </summary>
    public SalesforceModelFormTagHelper(ISchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "form";
        output.TagMode = TagMode.StartTagAndEndTag;

        // Set form attributes
        var formClasses = GetFormClasses();
        if (!string.IsNullOrEmpty(CssClass))
        {
            formClasses += " " + CssClass;
        }
        output.Attributes.SetAttribute("class", formClasses);
        output.Attributes.SetAttribute("method", Method);

        // Build action URL
        var actionUrl = BuildActionUrl();
        if (!string.IsNullOrEmpty(actionUrl))
        {
            output.Attributes.SetAttribute("action", actionUrl);
        }

        // Get fields to render
        var fields = await GetFieldsToRenderAsync();

        // Build form content
        var html = BuildFormHtml(fields);
        output.Content.SetHtmlContent(html);
    }

    private string GetFormClasses()
    {
        return Framework switch
        {
            CssFramework.Bootstrap5 => "sf-model-form",
            CssFramework.SLDS => "slds-form sf-model-form",
            CssFramework.None => "sf-model-form",
            _ => "sf-model-form"
        };
    }

    private string BuildActionUrl()
    {
        if (!string.IsNullOrEmpty(Action))
        {
            if (!string.IsNullOrEmpty(Controller))
            {
                return $"/{Controller}/{Action}";
            }
            return Action;
        }
        return "";
    }

    private async Task<List<Models.Metadata.SObjectField>> GetFieldsToRenderAsync()
    {
        if (string.IsNullOrWhiteSpace(ObjectName))
        {
            return new List<Models.Metadata.SObjectField>();
        }

        try
        {
            var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName);
            var allFields = fieldMap.Values.ToList();

            // Filter based on include/exclude lists
            var includeSet = ParseFieldList(IncludeFields);
            var excludeSet = ParseFieldList(ExcludeFields);

            // Always exclude system fields unless explicitly included
            var systemFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Id", "IsDeleted", "CreatedById", "CreatedDate",
                "LastModifiedById", "LastModifiedDate", "SystemModstamp"
            };

            var filteredFields = allFields.Where(f =>
            {
                // If include list is specified, only include those fields
                if (includeSet.Count > 0)
                {
                    return includeSet.Contains(f.Name);
                }

                // Exclude explicitly excluded fields
                if (excludeSet.Contains(f.Name))
                {
                    return false;
                }

                // Exclude system fields by default
                if (systemFields.Contains(f.Name))
                {
                    return false;
                }

                // Filter based on mode and field permissions
                return Mode switch
                {
                    FormMode.Create => f.Createable,
                    FormMode.Edit => f.Updateable,
                    FormMode.View => true,
                    _ => f.Createable || f.Updateable
                };
            })
            .OrderBy(f => includeSet.Count > 0 ? GetFieldOrder(f.Name, includeSet) : 0)
            .ThenBy(f => f.Label)
            .ToList();

            return filteredFields;
        }
        catch
        {
            return new List<Models.Metadata.SObjectField>();
        }
    }

    private HashSet<string> ParseFieldList(string? fieldList)
    {
        if (string.IsNullOrWhiteSpace(fieldList))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return fieldList
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private int GetFieldOrder(string fieldName, HashSet<string> includeSet)
    {
        // Preserve order from include list
        var list = IncludeFields?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToList() ?? new List<string>();

        var index = list.FindIndex(f => f.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    private string BuildFormHtml(List<Models.Metadata.SObjectField> fields)
    {
        var html = "";

        // Anti-forgery token
        if (IncludeAntiforgery)
        {
            html += "<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"\" data-sf-antiforgery=\"true\" />\n";
        }

        // Validation summary
        if (ShowValidationSummary)
        {
            html += BuildValidationSummaryHtml();
        }

        // Hidden Id field for edit mode
        if (Mode == FormMode.Edit && Model != null)
        {
            var idValue = GetModelPropertyValue("Id");
            if (!string.IsNullOrEmpty(idValue))
            {
                html += $"<input type=\"hidden\" name=\"Id\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(idValue)}\" />\n";
            }
        }

        // Start grid container
        html += BuildGridStartHtml();

        // Render fields
        var fieldIndex = 0;
        foreach (var field in fields)
        {
            var isReadonly = Mode == FormMode.View || !IsFieldEditable(field);
            html += BuildFieldHtml(field, fieldIndex, isReadonly);
            fieldIndex++;
        }

        // End grid container
        html += BuildGridEndHtml();

        // Buttons
        html += BuildButtonsHtml();

        return html;
    }

    private string BuildValidationSummaryHtml()
    {
        return Framework switch
        {
            CssFramework.Bootstrap5 => "<div class=\"alert alert-danger\" data-valmsg-summary=\"true\" style=\"display:none\"><ul></ul></div>\n",
            CssFramework.SLDS => "<div class=\"slds-notify slds-notify_alert slds-alert_error\" data-valmsg-summary=\"true\" style=\"display:none\"><ul></ul></div>\n",
            _ => "<div class=\"sf-validation-summary\" data-valmsg-summary=\"true\" style=\"display:none\"><ul></ul></div>\n"
        };
    }

    private string BuildGridStartHtml()
    {
        if (Columns <= 1)
        {
            return "<div class=\"sf-form-fields\">\n";
        }

        return Framework switch
        {
            CssFramework.Bootstrap5 => "<div class=\"row sf-form-fields\">\n",
            CssFramework.SLDS => "<div class=\"slds-grid slds-wrap sf-form-fields\">\n",
            _ => "<div class=\"sf-form-grid sf-form-fields\" style=\"display:grid;grid-template-columns:repeat(" + Columns + ",1fr);gap:1rem;\">\n"
        };
    }

    private string BuildGridEndHtml()
    {
        return "</div>\n";
    }

    private string BuildFieldHtml(Models.Metadata.SObjectField field, int index, bool isReadonly)
    {
        var columnClass = GetColumnClass();
        var fieldName = Model != null ? $"{Model.Name}.{field.Name}" : field.Name;
        var currentValue = GetModelPropertyValue(field.Name);
        var inputId = fieldName.Replace(".", "_");

        // Get framework-specific classes
        var (wrapperClass, labelClass, inputClass, helpClass, validationClass) = GetFrameworkClasses();

        var html = "";

        // Column wrapper for multi-column layout
        if (Columns > 1)
        {
            html += $"<div class=\"{columnClass}\">\n";
        }

        // Field group wrapper
        html += $"<div class=\"{wrapperClass}\">\n";

        // Label
        var requiredMark = GetRequiredMark(field);
        html += $"  <label for=\"{inputId}\" class=\"{labelClass}\">{System.Web.HttpUtility.HtmlEncode(field.Label)}{requiredMark}</label>\n";

        // Control wrapper for SLDS
        if (Framework == CssFramework.SLDS)
        {
            html += "  <div class=\"slds-form-element__control\">\n";
        }

        // Input element
        html += BuildInputElementHtml(field, fieldName, inputId, inputClass, currentValue, isReadonly);

        // Close control wrapper for SLDS
        if (Framework == CssFramework.SLDS)
        {
            html += "  </div>\n";
        }

        // Help text
        if (!string.IsNullOrEmpty(field.InlineHelpText))
        {
            html += $"  <div class=\"{helpClass}\">{System.Web.HttpUtility.HtmlEncode(field.InlineHelpText)}</div>\n";
        }

        // Validation message
        html += $"  <span class=\"{validationClass}\" data-valmsg-for=\"{fieldName}\" data-valmsg-replace=\"true\"></span>\n";

        // Close field group
        html += "</div>\n";

        // Close column wrapper
        if (Columns > 1)
        {
            html += "</div>\n";
        }

        return html;
    }

    private string GetColumnClass()
    {
        return Framework switch
        {
            CssFramework.Bootstrap5 => Columns switch
            {
                2 => "col-md-6",
                3 => "col-md-4",
                4 => "col-md-3",
                _ => "col-12"
            },
            CssFramework.SLDS => Columns switch
            {
                2 => "slds-col slds-size_1-of-2",
                3 => "slds-col slds-size_1-of-3",
                4 => "slds-col slds-size_1-of-4",
                _ => "slds-col slds-size_1-of-1"
            },
            _ => "sf-col"
        };
    }

    private (string wrapper, string label, string input, string help, string validation) GetFrameworkClasses()
    {
        return Framework switch
        {
            CssFramework.Bootstrap5 => ("mb-3", "form-label", "form-control", "form-text", "text-danger field-validation-valid"),
            CssFramework.SLDS => ("slds-form-element", "slds-form-element__label", "slds-input", "slds-form-element__help", "slds-form-element__help slds-text-color_error"),
            _ => ("sf-field-group", "sf-label", "sf-input", "sf-help-text", "sf-validation-message")
        };
    }

    private string GetRequiredMark(Models.Metadata.SObjectField field)
    {
        var isRequired = !field.Nillable && field.Createable;
        if (!isRequired) return "";

        return Framework switch
        {
            CssFramework.Bootstrap5 => " <span class=\"text-danger\">*</span>",
            CssFramework.SLDS => " <abbr class=\"slds-required\" title=\"required\">* </abbr>",
            _ => " *"
        };
    }

    private string BuildInputElementHtml(
        Models.Metadata.SObjectField field,
        string fieldName,
        string inputId,
        string inputClass,
        string? currentValue,
        bool isReadonly)
    {
        var readonlyAttr = isReadonly ? " readonly" : "";
        var disabledAttr = isReadonly ? " disabled" : "";
        var requiredAttr = !field.Nillable && field.Createable && !isReadonly ? " required" : "";
        var maxLengthAttr = field.Length > 0 ? $" maxlength=\"{field.Length}\"" : "";
        var encodedValue = System.Web.HttpUtility.HtmlAttributeEncode(currentValue ?? "");

        // Handle special field types
        if (field.IsPicklist)
        {
            var rtIdAttr = !string.IsNullOrEmpty(RecordTypeId) ? $" sf-record-type-id=\"{RecordTypeId}\"" : "";
            return $"  <sf-picklist asp-for=\"{fieldName}\" sf-object=\"{ObjectName}\" sf-picklist-field=\"{field.Name}\" class=\"{inputClass}\"{requiredAttr}{disabledAttr}{rtIdAttr}></sf-picklist>\n";
        }

        if (field.IsLookup)
        {
            return $"  <sf-lookup asp-for=\"{fieldName}\" sf-object=\"{ObjectName}\" sf-field=\"{field.Name}\" class=\"{inputClass}\"{requiredAttr}{disabledAttr}></sf-lookup>\n";
        }

        var fieldType = field.Type?.ToLowerInvariant() ?? "string";

        return fieldType switch
        {
            "textarea" or "longtextarea" or "richtextarea" =>
                $"  <textarea id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" rows=\"4\"{requiredAttr}{readonlyAttr}{maxLengthAttr}>{System.Web.HttpUtility.HtmlEncode(currentValue ?? "")}</textarea>\n",

            "boolean" =>
                BuildCheckboxHtml(inputId, fieldName, currentValue, readonlyAttr),

            "date" =>
                $"  <input type=\"date\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{FormatDateValue(currentValue, "yyyy-MM-dd")}\"{requiredAttr}{readonlyAttr} />\n",

            "datetime" =>
                $"  <input type=\"datetime-local\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{FormatDateValue(currentValue, "yyyy-MM-ddTHH:mm")}\"{requiredAttr}{readonlyAttr} />\n",

            "time" =>
                $"  <input type=\"time\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\"{requiredAttr}{readonlyAttr} />\n",

            "email" =>
                $"  <input type=\"email\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\"{requiredAttr}{readonlyAttr}{maxLengthAttr} />\n",

            "phone" =>
                $"  <input type=\"tel\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\"{requiredAttr}{readonlyAttr}{maxLengthAttr} />\n",

            "url" =>
                $"  <input type=\"url\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\"{requiredAttr}{readonlyAttr}{maxLengthAttr} />\n",

            "int" =>
                $"  <input type=\"number\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\" step=\"1\"{requiredAttr}{readonlyAttr} />\n",

            "double" or "currency" or "percent" =>
                BuildNumberInputHtml(inputId, fieldName, inputClass, encodedValue, requiredAttr, readonlyAttr, field),

            "encryptedstring" =>
                $"  <input type=\"password\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"\" placeholder=\"(encrypted)\" autocomplete=\"new-password\"{readonlyAttr}{maxLengthAttr} />\n",

            _ =>
                $"  <input type=\"text\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\"{requiredAttr}{readonlyAttr}{maxLengthAttr} />\n"
        };
    }

    private string BuildCheckboxHtml(string inputId, string fieldName, string? currentValue, string readonlyAttr)
    {
        var isChecked = currentValue?.ToLower() == "true" ? " checked" : "";

        return Framework switch
        {
            CssFramework.Bootstrap5 =>
                $"  <div class=\"form-check\">\n    <input type=\"checkbox\" id=\"{inputId}\" name=\"{fieldName}\" class=\"form-check-input\" value=\"true\"{isChecked}{readonlyAttr} />\n  </div>\n",
            CssFramework.SLDS =>
                $"  <div class=\"slds-checkbox\">\n    <input type=\"checkbox\" id=\"{inputId}\" name=\"{fieldName}\" value=\"true\"{isChecked}{readonlyAttr} />\n    <label class=\"slds-checkbox__label\" for=\"{inputId}\"><span class=\"slds-checkbox_faux\"></span></label>\n  </div>\n",
            _ =>
                $"  <input type=\"checkbox\" id=\"{inputId}\" name=\"{fieldName}\" value=\"true\"{isChecked}{readonlyAttr} />\n"
        };
    }

    private string BuildNumberInputHtml(
        string inputId,
        string fieldName,
        string inputClass,
        string encodedValue,
        string requiredAttr,
        string readonlyAttr,
        Models.Metadata.SObjectField field)
    {
        var step = "any";
        if (field.Scale > 0)
        {
            step = "0." + new string('0', field.Scale - 1) + "1";
        }

        var prefix = "";
        var suffix = "";
        if (field.Type?.ToLower() == "currency")
        {
            prefix = "$";
        }
        else if (field.Type?.ToLower() == "percent")
        {
            suffix = "%";
        }

        if (!string.IsNullOrEmpty(prefix) || !string.IsNullOrEmpty(suffix))
        {
            return Framework switch
            {
                CssFramework.Bootstrap5 =>
                    $"  <div class=\"input-group\">\n" +
                    (prefix != "" ? $"    <span class=\"input-group-text\">{prefix}</span>\n" : "") +
                    $"    <input type=\"number\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\" step=\"{step}\"{requiredAttr}{readonlyAttr} />\n" +
                    (suffix != "" ? $"    <span class=\"input-group-text\">{suffix}</span>\n" : "") +
                    "  </div>\n",
                _ =>
                    $"  <input type=\"number\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\" step=\"{step}\"{requiredAttr}{readonlyAttr} />\n"
            };
        }

        return $"  <input type=\"number\" id=\"{inputId}\" name=\"{fieldName}\" class=\"{inputClass}\" value=\"{encodedValue}\" step=\"{step}\"{requiredAttr}{readonlyAttr} />\n";
    }

    private string BuildButtonsHtml()
    {
        var html = "";

        html += Framework switch
        {
            CssFramework.Bootstrap5 => "<div class=\"d-flex gap-2 mt-4\">\n",
            CssFramework.SLDS => "<div class=\"slds-m-top_medium\">\n",
            _ => "<div class=\"sf-form-buttons\">\n"
        };

        // Submit button
        html += Framework switch
        {
            CssFramework.Bootstrap5 => $"  <button type=\"submit\" class=\"btn btn-primary\">{System.Web.HttpUtility.HtmlEncode(SubmitText)}</button>\n",
            CssFramework.SLDS => $"  <button type=\"submit\" class=\"slds-button slds-button_brand\">{System.Web.HttpUtility.HtmlEncode(SubmitText)}</button>\n",
            _ => $"  <button type=\"submit\" class=\"sf-btn-primary\">{System.Web.HttpUtility.HtmlEncode(SubmitText)}</button>\n"
        };

        // Cancel button
        if (ShowCancel)
        {
            var cancelUrl = CancelUrl ?? "javascript:history.back()";
            html += Framework switch
            {
                CssFramework.Bootstrap5 => $"  <a href=\"{cancelUrl}\" class=\"btn btn-secondary\">{System.Web.HttpUtility.HtmlEncode(CancelText)}</a>\n",
                CssFramework.SLDS => $"  <a href=\"{cancelUrl}\" class=\"slds-button slds-button_neutral\">{System.Web.HttpUtility.HtmlEncode(CancelText)}</a>\n",
                _ => $"  <a href=\"{cancelUrl}\" class=\"sf-btn-secondary\">{System.Web.HttpUtility.HtmlEncode(CancelText)}</a>\n"
            };
        }

        html += "</div>\n";

        return html;
    }

    private bool IsFieldEditable(Models.Metadata.SObjectField field)
    {
        return Mode switch
        {
            FormMode.Create => field.Createable,
            FormMode.Edit => field.Updateable,
            FormMode.View => false,
            _ => false
        };
    }

    private string? GetModelPropertyValue(string propertyName)
    {
        if (Model?.Model == null) return null;

        var modelType = Model.Model.GetType();
        var property = modelType.GetProperty(propertyName);
        return property?.GetValue(Model.Model)?.ToString();
    }

    private static string FormatDateValue(string? value, string format)
    {
        if (string.IsNullOrEmpty(value)) return "";

        if (DateTime.TryParse(value, out var parsed))
        {
            return parsed.ToString(format);
        }

        return value;
    }
}
