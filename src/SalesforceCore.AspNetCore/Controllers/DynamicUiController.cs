using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Models.Layout;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Layout;

namespace SalesforceCore.AspNetCore.Controllers;

/// <summary>
/// API endpoints for Dynamic UI descriptors.
/// Provides navigation, form, list, and detail descriptors for SPAs and Razor views.
/// </summary>
[ApiController]
[Route("api/dynamic-ui")]
[Authorize]
public class DynamicUiController : ControllerBase
{
    private readonly ILayoutDescriptorService _layoutService;
    private readonly IPermissionService _permissionService;

    public DynamicUiController(
        ILayoutDescriptorService layoutService,
        IPermissionService permissionService)
    {
        _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
    }

    /// <summary>
    /// Gets the navigation descriptor with permission-filtered items.
    /// </summary>
    /// <param name="currentPath">Current page path for active state highlighting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Navigation descriptor.</returns>
    [HttpGet("navigation")]
    [ProducesResponseType(typeof(NavigationDescriptor), 200)]
    public async Task<ActionResult<NavigationDescriptor>> GetNavigation(
        [FromQuery] string? currentPath = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = await _layoutService.GetNavigationAsync(currentPath, cancellationToken);
        return Ok(descriptor);
    }

    /// <summary>
    /// Gets a form descriptor for the specified object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="mode">Form mode (Create, Edit, View).</param>
    /// <param name="recordTypeId">Optional record type ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Form descriptor.</returns>
    [HttpGet("forms/{sObject}")]
    [ProducesResponseType(typeof(FormDescriptor), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<FormDescriptor>> GetForm(
        string sObject,
        [FromQuery] FormMode mode = FormMode.Create,
        [FromQuery] string? recordTypeId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var descriptor = await _layoutService.GetFormAsync(sObject, mode, recordTypeId, cancellationToken);
            if (!descriptor.IsVisible)
            {
                return NotFound(new { error = $"Object '{sObject}' not found or not accessible" });
            }
            return Ok(descriptor);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a list view descriptor for the specified object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List descriptor.</returns>
    [HttpGet("lists/{sObject}")]
    [ProducesResponseType(typeof(ListDescriptor), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<ListDescriptor>> GetList(
        string sObject,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var descriptor = await _layoutService.GetListAsync(sObject, cancellationToken);
            if (!descriptor.IsVisible)
            {
                return NotFound(new { error = $"Object '{sObject}' not found or not accessible" });
            }
            return Ok(descriptor);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets a detail view descriptor for the specified object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detail descriptor.</returns>
    [HttpGet("details/{sObject}")]
    [ProducesResponseType(typeof(DetailDescriptor), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<DetailDescriptor>> GetDetail(
        string sObject,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var descriptor = await _layoutService.GetDetailAsync(sObject, cancellationToken);
            if (!descriptor.IsVisible)
            {
                return NotFound(new { error = $"Object '{sObject}' not found or not accessible" });
            }
            return Ok(descriptor);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets permissions for the specified object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Object permission snapshot.</returns>
    [HttpGet("permissions/{sObject}")]
    [ProducesResponseType(typeof(Models.Authorization.ObjectPermissionSnapshot), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<Models.Authorization.ObjectPermissionSnapshot>> GetPermissions(
        string sObject,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await _permissionService.GetPermissionsAsync(sObject, cancellationToken);
            return Ok(snapshot);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Gets permissions for multiple objects.
    /// </summary>
    /// <param name="objects">Comma-separated list of object API names.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Permission result with snapshots.</returns>
    [HttpGet("permissions")]
    [ProducesResponseType(typeof(Models.Authorization.PermissionResult), 200)]
    public async Task<ActionResult<Models.Authorization.PermissionResult>> GetPermissionsBatch(
        [FromQuery] string objects,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objects))
            return BadRequest(new { error = "Objects parameter is required" });

        var objectList = objects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var context = Models.Authorization.PermissionRequestContext.ForObjects(objectList);
        var result = await _permissionService.GetPermissionsAsync(context, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Gets available actions for the specified object and context.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="context">Action context (List, Detail, Form, RowAction, BulkAction).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of available actions.</returns>
    [HttpGet("actions/{sObject}")]
    [ProducesResponseType(typeof(IReadOnlyList<FormAction>), 200)]
    public async Task<ActionResult<IReadOnlyList<FormAction>>> GetActions(
        string sObject,
        [FromQuery] UiActionContext context = UiActionContext.Detail,
        CancellationToken cancellationToken = default)
    {
        var actions = await _layoutService.GetAvailableActionsAsync(sObject, context, cancellationToken);
        return Ok(actions);
    }

    /// <summary>
    /// Gets related lists for the specified object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of related list descriptors.</returns>
    [HttpGet("related-lists/{sObject}")]
    [ProducesResponseType(typeof(IReadOnlyList<RelatedListDescriptor>), 200)]
    public async Task<ActionResult<IReadOnlyList<RelatedListDescriptor>>> GetRelatedLists(
        string sObject,
        CancellationToken cancellationToken = default)
    {
        var relatedLists = await _layoutService.GetRelatedListsAsync(sObject, cancellationToken);
        return Ok(relatedLists);
    }

    /// <summary>
    /// Gets picklist options for a field.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="controllingValue">Controlling field value (for dependent picklists).</param>
    /// <param name="recordTypeId">Record type ID (for record type-specific values).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of picklist options.</returns>
    [HttpGet("picklist/{sObject}/{fieldName}")]
    [ProducesResponseType(typeof(IReadOnlyList<PicklistOption>), 200)]
    public async Task<ActionResult<IReadOnlyList<PicklistOption>>> GetPicklistOptions(
        string sObject,
        string fieldName,
        [FromQuery] string? controllingValue = null,
        [FromQuery] string? recordTypeId = null,
        CancellationToken cancellationToken = default)
    {
        var options = await _layoutService.GetPicklistOptionsAsync(
            sObject, fieldName, controllingValue, recordTypeId, cancellationToken);
        return Ok(options);
    }

    /// <summary>
    /// Gets the record type selector for an object.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record type selector or null.</returns>
    [HttpGet("record-types/{sObject}")]
    [ProducesResponseType(typeof(RecordTypeSelector), 200)]
    [ProducesResponseType(204)]
    public async Task<ActionResult<RecordTypeSelector?>> GetRecordTypes(
        string sObject,
        CancellationToken cancellationToken = default)
    {
        var selector = await _layoutService.GetRecordTypeSelectorAsync(sObject, cancellationToken);
        if (selector == null)
            return NoContent();
        return Ok(selector);
    }

    /// <summary>
    /// Refreshes cached descriptors for an object.
    /// </summary>
    /// <param name="sObject">Object to refresh, or null for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("refresh")]
    [ProducesResponseType(200)]
    public async Task<ActionResult> RefreshCache(
        [FromQuery] string? sObject = null,
        CancellationToken cancellationToken = default)
    {
        await _layoutService.RefreshAsync(sObject, cancellationToken);
        return Ok(new { message = string.IsNullOrEmpty(sObject) ? "All caches refreshed" : $"Cache refreshed for {sObject}" });
    }

    /// <summary>
    /// Gets a field descriptor for a specific field.
    /// </summary>
    /// <param name="sObject">Salesforce object API name.</param>
    /// <param name="fieldName">Field API name.</param>
    /// <param name="mode">Form mode for determining editability.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Field descriptor or 404.</returns>
    [HttpGet("fields/{sObject}/{fieldName}")]
    [ProducesResponseType(typeof(FieldDescriptor), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<FieldDescriptor>> GetFieldDescriptor(
        string sObject,
        string fieldName,
        [FromQuery] FormMode mode = FormMode.Edit,
        CancellationToken cancellationToken = default)
    {
        var descriptor = await _layoutService.GetFieldDescriptorAsync(sObject, fieldName, mode, cancellationToken);
        if (descriptor == null)
            return NotFound(new { error = $"Field '{fieldName}' not found or not accessible" });
        return Ok(descriptor);
    }
}
