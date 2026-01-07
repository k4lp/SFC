using Microsoft.AspNetCore.Razor.TagHelpers;
using SalesforceCore.Services.Authorization;
using System.Threading.Tasks;

namespace SalesforceCore.AspNetCore.TagHelpers;

/// <summary>
/// Tag helper that controls the visibility of an element based on a configured policy.
/// </summary>
[HtmlTargetElement(Attributes = "sfc-policy")]
public class VisibilityTagHelper : TagHelper
{
    private readonly IVisibilityService _visibilityService;

    /// <summary>
    /// The name of the visibility policy to enforce.
    /// If the policy evaluates to false, the element and its content are suppressed.
    /// </summary>
    [HtmlAttributeName("sfc-policy")]
    public string Policy { get; set; } = string.Empty;

    public VisibilityTagHelper(IVisibilityService visibilityService)
    {
        _visibilityService = visibilityService;
    }

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrEmpty(Policy))
        {
            return;
        }

        bool isVisible = await _visibilityService.EvaluatePolicyAsync(Policy);
        if (!isVisible)
        {
            output.SuppressOutput();
        }
    }
}
