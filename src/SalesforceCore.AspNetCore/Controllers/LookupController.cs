using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SalesforceCore.Models.Data;
using SalesforceCore.Services.Configuration;
using SalesforceCore.Services.Data;
using SalesforceCore.Utilities;

namespace SalesforceCore.AspNetCore.Controllers;

/// <summary>
/// Controller for lookup field search operations.
/// </summary>
[Authorize]
[Route("[controller]")]
public class LookupController : Controller
{
    private readonly ILookupService _lookupService;
    private readonly IConfigurationService _configService;
    private readonly ILogger<LookupController> _logger;

    /// <summary>
    /// Creates a new LookupController.
    /// </summary>
    public LookupController(
        ILookupService lookupService,
        IConfigurationService configService,
        ILogger<LookupController> logger)
    {
        _lookupService = lookupService;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// Searches for lookup records.
    /// </summary>
    /// <param name="targetObject">Target object to search.</param>
    /// <param name="q">Search query.</param>
    /// <param name="parentField">Parent field for dependent lookups.</param>
    /// <param name="parentValue">Parent value for dependent lookups.</param>
    /// <param name="polymorphicTargets">Comma-separated list of target objects for polymorphic lookups.</param>
    /// <param name="limit">Maximum results.</param>
    [HttpGet("Search")]
    public async Task<IActionResult> Search(
        string targetObject,
        string? q,
        string? parentField = null,
        string? parentValue = null,
        string? polymorphicTargets = null,
        int limit = 15)
    {
        LookupSearchResult result;

        // Handle polymorphic lookups
        if (!string.IsNullOrWhiteSpace(polymorphicTargets))
        {
            var targets = polymorphicTargets.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();

            if (targets.Count > 0)
            {
                result = await _lookupService.SearchPolymorphicAsync(
                    targets,
                    q ?? string.Empty,
                    limit / targets.Count + 1);

                return PartialView("_LookupResults", result);
            }
        }

        // Standard lookup search
        var options = new LookupSearchOptions
        {
            TargetObject = targetObject,
            Query = q ?? string.Empty,
            Limit = limit,
            ParentField = parentField,
            ParentValue = parentValue
        };

        // Get relationship config if available
        // Note: We'd need to know the source object for this, which would require additional context
        // For now, we use default search behavior

        result = await _lookupService.SearchAsync(options);

        // Add object type info for display
        foreach (var item in result.Items)
        {
            item.ObjectLabel = item.ObjectType;
            item.IconClass = SalesforceConventions.GetDefaultIcon(item.ObjectType ?? targetObject);
        }

        return PartialView("_LookupResults", result);
    }

    /// <summary>
    /// Gets recent items for a target object.
    /// </summary>
    /// <param name="targetObject">Target object.</param>
    /// <param name="limit">Maximum items.</param>
    [HttpGet("Recent")]
    public async Task<IActionResult> Recent(string targetObject, int limit = 5)
    {
        var items = await _lookupService.GetRecentItemsAsync(targetObject, limit);

        var result = new LookupSearchResult
        {
            TargetObject = targetObject,
            Items = items
        };

        return PartialView("_LookupResults", result);
    }

    /// <summary>
    /// Resolves a single lookup ID to display name.
    /// </summary>
    /// <param name="targetObject">Target object.</param>
    /// <param name="id">Record ID.</param>
    [HttpGet("Resolve")]
    public async Task<IActionResult> Resolve(string targetObject, string id)
    {
        var options = new LookupSearchOptions
        {
            TargetObject = targetObject,
            Query = id,
            Limit = 1
        };

        // Search by ID directly
        var result = await _lookupService.SearchAsync(options);
        var item = result.Items.FirstOrDefault();

        if (item != null)
        {
            return Json(new
            {
                id = item.Id,
                displayName = item.DisplayName,
                objectType = item.ObjectType
            });
        }

        return NotFound();
    }
}
