using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Attributes;

namespace SalesforceCore.RazorTestHost;

/// <summary>Real Razor views that use SFC tag helpers; rendered end to end by the E2E tests.</summary>
public enum IndustryPick
{
    None,
    [SalesforceValue("Tech\"<b>")] Tech,
}

public class ProbeModel
{
    public string? Industry { get; set; }
    public IndustryPick IndustryEnum { get; set; }
    public string? ParentId { get; set; }
    public string? Name { get; set; }
    public decimal? AnnualRevenue { get; set; }
}

[Route("razor-probe")]
public class RazorProbeController : Controller
{
    [HttpGet("picklist-string")]
    public IActionResult PicklistString() => View("Picklist", new ProbeModel { Industry = "Tech\"<b>" });

    [HttpGet("field-group-picklist")]
    public IActionResult FieldGroupPicklist() => View("FieldGroup", new ProbeModel { Industry = "Tech\"<b>" });

    [HttpGet("prefixed")]
    public IActionResult Prefixed() => View("Prefixed", new ProbeModel { Industry = "Tech\"<b>", IndustryEnum = IndustryPick.Tech });

    [HttpGet("picklist-enum")]
    public IActionResult PicklistEnum() => View("PicklistEnum", new ProbeModel { IndustryEnum = IndustryPick.Tech });

    [HttpGet("lookup")]
    public IActionResult Lookup() => View("Lookup", new ProbeModel { ParentId = "001000000000001AAA" });

    [HttpGet("model-form")]
    public IActionResult ModelForm() => View("ModelForm", new ProbeModel { Name = "Acme \"<i>", Industry = "Tech\"<b>", AnnualRevenue = 12.5m });

    [HttpGet("permission")]
    public IActionResult Permission() => View("Permission", new ProbeModel());
}
