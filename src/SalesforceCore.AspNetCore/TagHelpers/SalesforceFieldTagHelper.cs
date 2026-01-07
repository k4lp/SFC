using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Models.Security;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Tag helper that automatically adjusts input fields based on Salesforce field permissions.
/// Adds readonly, disabled, or hides fields based on FLS settings.
/// </summary>
/// <remarks>
/// <para>
/// This tag helper provides a more nuanced approach than sf-permission by modifying
/// the field behavior rather than hiding it completely.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Auto-readonly if user can't update --&gt;
/// &lt;input sf-object="Contact" sf-field="Email" sf-mode="Update" asp-for="Email" /&gt;
///
/// &lt;!-- Auto-disable if user can't create --&gt;
/// &lt;input sf-object="Account" sf-field="Industry" sf-mode="Create"
///        sf-behavior="Disable" asp-for="Industry" /&gt;
///
/// &lt;!-- Hide completely if no access --&gt;
/// &lt;input sf-object="Account" sf-field="AnnualRevenue" sf-mode="Read"
///        sf-behavior="Hide" asp-for="AnnualRevenue" /&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("input", Attributes = "sf-object,sf-field")]
[HtmlTargetElement("select", Attributes = "sf-object,sf-field")]
[HtmlTargetElement("textarea", Attributes = "sf-object,sf-field")]
public class SalesforceFieldTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;

    /// <summary>
    /// The Salesforce object API name.
    /// </summary>
    [HtmlAttributeName("sf-object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The field API name to check permissions for.
    /// </summary>
    [HtmlAttributeName("sf-field")]
    public string? FieldName { get; set; }

    /// <summary>
    /// The access mode to check. Defaults to Update (most common for forms).
    /// </summary>
    [HtmlAttributeName("sf-mode")]
    public AccessMode Mode { get; set; } = AccessMode.Update;

    /// <summary>
    /// The behavior when permission is denied.
    /// </summary>
    [HtmlAttributeName("sf-behavior")]
    public DeniedBehavior Behavior { get; set; } = DeniedBehavior.Readonly;

    /// <summary>
    /// Creates a new SalesforceFieldTagHelper.
    /// </summary>
    public SalesforceFieldTagHelper(ISchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(ObjectName) || string.IsNullOrWhiteSpace(FieldName))
        {
            return;
        }

        var hasPermission = await CheckFieldPermissionAsync();

        if (!hasPermission)
        {
            ApplyDeniedBehavior(output);
        }
    }

    private async Task<bool> CheckFieldPermissionAsync()
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName!);
        if (!fieldMap.TryGetValue(FieldName!, out var field))
        {
            return false;
        }

        if (field.DeprecatedAndHidden)
        {
            return false;
        }

        return Mode switch
        {
            AccessMode.Read => field.Accessible,
            AccessMode.Create => field.Createable && field.Accessible,
            AccessMode.Update => field.Updateable && field.Accessible,
            AccessMode.Delete => true,
            _ => false
        };
    }

    private void ApplyDeniedBehavior(TagHelperOutput output)
    {
        switch (Behavior)
        {
            case DeniedBehavior.Readonly:
                output.Attributes.SetAttribute("readonly", "readonly");
                break;

            case DeniedBehavior.Disable:
                output.Attributes.SetAttribute("disabled", "disabled");
                break;

            case DeniedBehavior.Hide:
                output.SuppressOutput();
                break;

            case DeniedBehavior.ReadonlyWithClass:
                output.Attributes.SetAttribute("readonly", "readonly");
                AddCssClass(output, "sf-readonly");
                break;

            case DeniedBehavior.DisableWithClass:
                output.Attributes.SetAttribute("disabled", "disabled");
                AddCssClass(output, "sf-disabled");
                break;
        }
    }

    private static void AddCssClass(TagHelperOutput output, string className)
    {
        var existingClass = output.Attributes["class"]?.Value?.ToString() ?? "";
        var newClass = string.IsNullOrEmpty(existingClass) ? className : $"{existingClass} {className}";
        output.Attributes.SetAttribute("class", newClass);
    }
}

/// <summary>
/// Defines how a field should behave when the user lacks permission.
/// </summary>
public enum DeniedBehavior
{
    /// <summary>
    /// Makes the field readonly (user can see but not edit).
    /// </summary>
    Readonly,

    /// <summary>
    /// Disables the field completely (grayed out, not submitted with form).
    /// </summary>
    Disable,

    /// <summary>
    /// Hides the field from the UI entirely.
    /// </summary>
    Hide,

    /// <summary>
    /// Makes the field readonly and adds 'sf-readonly' CSS class for styling.
    /// </summary>
    ReadonlyWithClass,

    /// <summary>
    /// Disables the field and adds 'sf-disabled' CSS class for styling.
    /// </summary>
    DisableWithClass
}
