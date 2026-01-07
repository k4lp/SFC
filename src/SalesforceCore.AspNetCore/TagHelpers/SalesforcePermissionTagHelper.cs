using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Models.Security;
using SalesforceCore.Services.Metadata;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Tag helper that conditionally renders content based on Salesforce object and field permissions.
/// Uses ISchemaService to check Field-Level Security (FLS) and Object permissions.
/// </summary>
/// <remarks>
/// <para>
/// This tag helper enables declarative, permission-based UI rendering in Razor views.
/// Content is only rendered if the current user has the required permission.
/// </para>
/// <para>
/// Examples:
/// <code>
/// &lt;!-- Object-level: Hide form if user can't create Contacts --&gt;
/// &lt;sf-permission object="Contact" mode="Create"&gt;
///     &lt;form asp-action="Create"&gt;...&lt;/form&gt;
/// &lt;/sf-permission&gt;
///
/// &lt;!-- Field-level: Hide input if user can't edit Email --&gt;
/// &lt;sf-permission object="Contact" field="Email" mode="Update"&gt;
///     &lt;input asp-for="Email" /&gt;
/// &lt;/sf-permission&gt;
///
/// &lt;!-- Read permission check --&gt;
/// &lt;sf-permission object="Account" field="AnnualRevenue" mode="Read"&gt;
///     &lt;span&gt;@Model.AnnualRevenue&lt;/span&gt;
/// &lt;/sf-permission&gt;
/// </code>
/// </para>
/// </remarks>
[HtmlTargetElement("sf-permission", TagStructure = TagStructure.NormalOrSelfClosing)]
public class SalesforcePermissionTagHelper : TagHelper
{
    private readonly ISchemaService _schemaService;

    /// <summary>
    /// The Salesforce object API name (e.g., "Account", "Contact", "Custom_Object__c").
    /// Required.
    /// </summary>
    [HtmlAttributeName("object")]
    public string? ObjectName { get; set; }

    /// <summary>
    /// The field API name to check permissions for (e.g., "Email", "Phone", "Custom_Field__c").
    /// Optional. If not specified, only object-level permissions are checked.
    /// </summary>
    [HtmlAttributeName("field")]
    public string? FieldName { get; set; }

    /// <summary>
    /// The access mode to check. Defaults to Read.
    /// </summary>
    [HtmlAttributeName("mode")]
    public AccessMode Mode { get; set; } = AccessMode.Read;

    /// <summary>
    /// If true, content is rendered when permission is DENIED (inverse logic).
    /// Useful for showing "Access Denied" messages or alternative content.
    /// </summary>
    [HtmlAttributeName("negate")]
    public bool Negate { get; set; }

    /// <summary>
    /// Creates a new SalesforcePermissionTagHelper.
    /// </summary>
    public SalesforcePermissionTagHelper(ISchemaService schemaService)
    {
        _schemaService = schemaService;
    }

    /// <inheritdoc/>
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        // Remove the wrapper element - we only care about the content
        output.TagName = null;

        if (string.IsNullOrWhiteSpace(ObjectName))
        {
            // No object specified - suppress output
            output.SuppressOutput();
            return;
        }

        var hasPermission = await CheckPermissionAsync();

        // Apply negate logic
        var shouldRender = Negate ? !hasPermission : hasPermission;

        if (!shouldRender)
        {
            output.SuppressOutput();
        }
    }

    private async Task<bool> CheckPermissionAsync()
    {
        if (string.IsNullOrWhiteSpace(FieldName))
        {
            // Object-level permission check
            return await CheckObjectPermissionAsync();
        }
        else
        {
            // Field-level permission check
            return await CheckFieldPermissionAsync();
        }
    }

    private async Task<bool> CheckObjectPermissionAsync()
    {
        var describe = await _schemaService.GetDescribeAsync(ObjectName!);
        if (describe == null)
        {
            return false;
        }

        return Mode switch
        {
            AccessMode.Read => describe.Queryable,
            AccessMode.Create => describe.Createable,
            AccessMode.Update => describe.Updateable,
            AccessMode.Delete => describe.Deletable,
            _ => false
        };
    }

    private async Task<bool> CheckFieldPermissionAsync()
    {
        var fieldMap = await _schemaService.GetFieldMapAsync(ObjectName!);
        if (!fieldMap.TryGetValue(FieldName!, out var field))
        {
            return false;
        }

        // Skip deprecated fields
        if (field.DeprecatedAndHidden)
        {
            return false;
        }

        return Mode switch
        {
            AccessMode.Read => field.Accessible,
            AccessMode.Create => field.Createable && field.Accessible,
            AccessMode.Update => field.Updateable && field.Accessible,
            AccessMode.Delete => true, // Delete is object-level only
            _ => false
        };
    }
}
