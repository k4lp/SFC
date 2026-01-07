using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using SalesforceCore.AspNetCore.ViewModels;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Configuration;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;

namespace SalesforceCore.AspNetCore.Controllers;

/// <summary>
/// Generic CRUD controller for any Salesforce object.
/// Handles Index, Details, Create, Edit, and Delete operations.
/// </summary>
[Authorize]
[Route("[controller]")]
public class SalesforceController : Controller
{
    private readonly ISchemaService _schemaService;
    private readonly IDataService _dataService;
    private readonly IConfigurationService _configService;
    private readonly IVisibilityService _visibilityService;
    private readonly SalesforceOptions _options;
    private readonly SalesforceMvcOptions _mvcOptions;
    private readonly ILogger<SalesforceController> _logger;

    /// <summary>
    /// Creates a new SalesforceController.
    /// </summary>
    public SalesforceController(
        ISchemaService schemaService,
        IDataService dataService,
        IConfigurationService configService,
        IVisibilityService visibilityService,
        IOptions<SalesforceOptions> options,
        IOptions<SalesforceMvcOptions> mvcOptions,
        ILogger<SalesforceController> logger)
    {
        _schemaService = schemaService;
        _dataService = dataService;
        _configService = configService;
        _visibilityService = visibilityService;
        _options = options.Value;
        _mvcOptions = mvcOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// List view with pagination and search.
    /// </summary>
    [HttpGet("{sObject}")]
    public async Task<IActionResult> Index(
        string sObject,
        string? q = null,
        int page = 1,
        int? pageSize = null,
        string? orderBy = null,
        bool desc = false)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);

        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                _logger.LogWarning("Access denied to object {SObject} due to visibility policy {Policy}", sObject, moduleConfig.VisibilityPolicy);
                return NotFound($"Object '{sObject}' not found or not accessible.");
            }
        }

        var describe = await _schemaService.GetDescribeAsync(sObject);

        if (describe == null)
        {
            return NotFound($"Object '{sObject}' not found or not accessible.");
        }

        // Determine fields to display
        var listFields = moduleConfig?.ListFields.Count > 0
            ? moduleConfig.ListFields
            : new List<string> { "Name", "CreatedDate" };

        var validFields = _options.EnforceFieldLevelSecurity
            ? await _schemaService.SanitizeFieldListWithFlsAsync(sObject, listFields)
            : await _schemaService.SanitizeFieldListAsync(sObject, listFields);

        // Build WHERE condition for search using type-safe condition
        SoqlCondition? filter = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var nameField = await _schemaService.GetNameFieldAsync(sObject);
            // Use type-safe LIKE condition with wildcards
            filter = SoqlCondition.Like(nameField, $"%{q}%");
        }

        // Ensure valid page size
        var effectivePageSize = pageSize ?? _options.DefaultPageSize;
        effectivePageSize = Math.Min(Math.Max(effectivePageSize, 1), _options.MaxPageSize);

        var result = await _dataService.QueryPagedAsync(
            sObject,
            validFields,
            filter,
            orderBy ?? moduleConfig?.DefaultSortField ?? "CreatedDate",
            desc || (moduleConfig?.DefaultSortDescending ?? true),
            page,
            effectivePageSize);

        var fieldDefs = await _schemaService.GetFieldMapAsync(sObject);

        var viewModel = new ListViewModel();
        
        viewModel.SObject = sObject;
        viewModel.ObjectLabel = moduleConfig?.Label ?? describe.Label;
        viewModel.ObjectLabelPlural = moduleConfig?.PluralLabel ?? describe.LabelPlural;
        viewModel.Records = result.Records;
        viewModel.Fields = validFields;
        viewModel.FieldDefinitions = fieldDefs;
        
        viewModel.CurrentPage = page;
        viewModel.PageSize = effectivePageSize;
        viewModel.HasNextPage = result.HasNextPage;
        viewModel.HasPreviousPage = page > 1;
        
        viewModel.SearchQuery = q;
        viewModel.OrderBy = orderBy;
        viewModel.OrderDescending = desc;
        viewModel.CanCreate = describe.Createable;
        viewModel.ModuleConfig = moduleConfig;
        viewModel.MvcOptions = _mvcOptions;

        // Handle HTMX partial requests
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            return PartialView("_ListPartial", viewModel);
        }

        return View(viewModel);
    }

    /// <summary>
    /// Record detail view.
    /// </summary>
    [HttpGet("{sObject}/Details/{id}")]
    public async Task<IActionResult> Details(string sObject, string id)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        if (!SecurityUtils.IsValidSalesforceId(id))
        {
            return BadRequest("Invalid record ID.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);
        var describe = await _schemaService.GetDescribeAsync(sObject);

        if (describe == null)
        {
            return NotFound($"Object '{sObject}' not found.");
        }

        // Get queryable fields
        var queryableFields = _options.EnforceFieldLevelSecurity
            ? await _schemaService.GetAccessibleFieldsAsync(sObject)
            : await _schemaService.GetQueryableFieldsAsync(sObject);
        var fieldNames = queryableFields.Take(200).Select(f => f.Name).ToList();

        var record = await _dataService.GetRecordAsync(sObject, id, fieldNames);

        // Hydrate lookup fields
        var lookupFields = queryableFields.Where(f => f.IsLookup).ToList();
        var hydratedLookups = await _dataService.HydrateLookupsAsync(record, lookupFields);

        // Get attached files
        var attachedFiles = await _dataService.GetAttachedFilesAsync(id);

        var viewModel = new DetailsViewModel
        {
            SObject = sObject,
            ObjectLabel = moduleConfig?.Label ?? describe.Label,
            RecordId = id,
            Record = record.AsObject(),
            Fields = queryableFields,
            FieldDefinitions = queryableFields.ToDictionary(f => f.Name, f => f),
            HydratedLookups = hydratedLookups,
            AttachedFiles = attachedFiles,
            CanEdit = describe.Updateable,
            CanDelete = describe.Deletable,
            ModuleConfig = moduleConfig,
            MvcOptions = _mvcOptions
        };

        return View(viewModel);
    }

    /// <summary>
    /// Create form view.
    /// </summary>
    [HttpGet("{sObject}/Create")]
    public async Task<IActionResult> Create(string sObject)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);
        var describe = await _schemaService.GetDescribeAsync(sObject);

        if (describe == null)
        {
            return NotFound($"Object '{sObject}' not found.");
        }

        if (!describe.Createable)
        {
            return Forbid();
        }

        var createableFields = await _schemaService.GetCreateableFieldsAsync(sObject);

        var viewModel = new FormViewModel
        {
            SObject = sObject,
            ObjectLabel = moduleConfig?.Label ?? describe.Label,
            Mode = FormMode.Create,
            Record = new JsonObject(),
            Fields = createableFields,
            FieldDefinitions = createableFields.ToDictionary(f => f.Name, f => f),
            ModuleConfig = moduleConfig,
            MvcOptions = _mvcOptions
        };

        return View("Form", viewModel);
    }

    /// <summary>
    /// Create form submission.
    /// </summary>
    [HttpPost("{sObject}/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string sObject, IFormCollection form)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);

        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                return NotFound();
            }
        }

        var describe = await _schemaService.GetDescribeAsync(sObject);
        if (describe == null || !describe.Createable)
        {
            return NotFound();
        }

        try
        {
            var createableFields = await _schemaService.GetCreateableFieldsAsync(sObject);
            var payload = BuildPayloadFromForm(form, createableFields);

            var newId = await _dataService.CreateRecordAsync(sObject, payload);

            TempData["SuccessMessage"] = $"{describe.Label} created successfully.";

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers.Append("HX-Redirect", Url.Action("Details", new { sObject, id = newId }));
                return Ok();
            }

            return RedirectToAction("Details", new { sObject, id = newId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create {SObject}", sObject);

            TempData["ErrorMessage"] = ex.Message;

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                // Return 400 for validation/Salesforce errors, 500 for others
                Response.StatusCode = ex is Models.Errors.SalesforceException ? 400 : 500;
                return Content(ex.Message);
            }

            return RedirectToAction("Create", new { sObject });
        }
    }

    /// <summary>
    /// Edit form view.
    /// </summary>
    [HttpGet("{sObject}/Edit/{id}")]
    public async Task<IActionResult> Edit(string sObject, string id)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        if (!SecurityUtils.IsValidSalesforceId(id))
        {
            return BadRequest("Invalid record ID.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);

        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                return NotFound($"Object '{sObject}' not found.");
            }
        }

        var describe = await _schemaService.GetDescribeAsync(sObject);

        if (describe == null)
        {
            return NotFound($"Object '{sObject}' not found.");
        }

        if (!describe.Updateable)
        {
            return Forbid();
        }

        var updateableFields = await _schemaService.GetUpdateableFieldsAsync(sObject);
        var fieldNames = updateableFields.Select(f => f.Name).ToList();
        fieldNames.Add("Id"); // Always include ID

        var record = await _dataService.GetRecordAsync(sObject, id, fieldNames);

        // Hydrate lookups for display
        var lookupFields = updateableFields.Where(f => f.IsLookup).ToList();
        var hydratedLookups = await _dataService.HydrateLookupsAsync(record, lookupFields);

        var viewModel = new FormViewModel
        {
            SObject = sObject,
            ObjectLabel = moduleConfig?.Label ?? describe.Label,
            Mode = FormMode.Edit,
            RecordId = id,
            Record = record.AsObject(),
            Fields = updateableFields,
            FieldDefinitions = updateableFields.ToDictionary(f => f.Name, f => f),
            HydratedLookups = hydratedLookups,
            ModuleConfig = moduleConfig,
            MvcOptions = _mvcOptions
        };

        return View("Form", viewModel);
    }

    /// <summary>
    /// Edit form submission.
    /// </summary>
    [HttpPost("{sObject}/Edit/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string sObject, string id, IFormCollection form)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        if (!SecurityUtils.IsValidSalesforceId(id))
        {
            return BadRequest("Invalid record ID.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);
        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                return NotFound();
            }
        }

        var describe = await _schemaService.GetDescribeAsync(sObject);
        if (describe == null || !describe.Updateable)
        {
            return NotFound();
        }

        try
        {
            var updateableFields = await _schemaService.GetUpdateableFieldsAsync(sObject);
            var payload = BuildPayloadFromForm(form, updateableFields);

            await _dataService.UpdateRecordAsync(sObject, id, payload);

            TempData["SuccessMessage"] = $"{describe.Label} updated successfully.";

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers.Append("HX-Redirect", Url.Action("Details", new { sObject, id }));
                return Ok();
            }

            return RedirectToAction("Details", new { sObject, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update {SObject} {Id}", sObject, id);

            TempData["ErrorMessage"] = ex.Message;

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.StatusCode = ex is Models.Errors.SalesforceException ? 400 : 500;
                return Content(ex.Message);
            }

            return RedirectToAction("Edit", new { sObject, id });
        }
    }

    /// <summary>
    /// Delete record.
    /// </summary>
    [HttpPost("{sObject}/Delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string sObject, string id)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        if (!SecurityUtils.IsValidSalesforceId(id))
        {
            return BadRequest("Invalid record ID.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);
        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                return NotFound();
            }
        }

        var describe = await _schemaService.GetDescribeAsync(sObject);
        if (describe == null || !describe.Deletable)
        {
            return NotFound();
        }

        try
        {
            await _dataService.DeleteRecordAsync(sObject, id);

            TempData["SuccessMessage"] = $"{describe.Label} deleted successfully.";

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers.Append("HX-Redirect", Url.Action("Index", new { sObject }));
                return Ok();
            }

            return RedirectToAction("Index", new { sObject });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {SObject} {Id}", sObject, id);

            TempData["ErrorMessage"] = ex.Message;

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.StatusCode = ex is Models.Errors.SalesforceException ? 400 : 500;
                return Content(ex.Message);
            }

            return RedirectToAction("Details", new { sObject, id });
        }
    }

    /// <summary>
    /// Upload file to record.
    /// </summary>
    [HttpPost("{sObject}/Upload/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(string sObject, string id, IFormFile file)
    {
        if (!SecurityUtils.IsValidObjectName(sObject))
        {
            return BadRequest("Invalid object name.");
        }

        if (!SecurityUtils.IsValidSalesforceId(id))
        {
            return BadRequest("Invalid record ID.");
        }

        if (!_mvcOptions.EnableFileUploads)
        {
            return NotFound("File uploads are disabled.");
        }

        var moduleConfig = await _configService.GetModuleConfigAsync(sObject);
        // Enforce Visibility Policy
        if (moduleConfig != null && !string.IsNullOrEmpty(moduleConfig.VisibilityPolicy))
        {
            if (!await _visibilityService.EvaluatePolicyAsync(moduleConfig.VisibilityPolicy))
            {
                return NotFound("Object not found or not accessible.");
            }
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest("No file provided.");
        }

        if (file.Length > _options.MaxFileUploadSize)
        {
            return BadRequest($"File size exceeds maximum of {_options.MaxFileUploadSize / (1024 * 1024)}MB.");
        }

        if (!SecurityUtils.IsAllowedExtension(file.FileName, _mvcOptions.AllowedFileExtensions))
        {
            return BadRequest("File type not allowed.");
        }

        try
        {
            // Use stream-based upload to avoid double buffering
            using var stream = file.OpenReadStream();
            var versionId = await _dataService.UploadFileAsync(id, file.FileName, stream, file.Length);

            TempData["SuccessMessage"] = "File uploaded successfully.";

            if (Request.Headers.ContainsKey("HX-Request"))
            {
                Response.Headers.Append("HX-Refresh", "true");
                return Ok();
            }

            return RedirectToAction("Details", new { sObject, id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to {SObject} {Id}", sObject, id);

            TempData["ErrorMessage"] = ex.Message;
            return RedirectToAction("Details", new { sObject, id });
        }
    }

    private Dictionary<string, object?> BuildPayloadFromForm(IFormCollection form, List<SObjectField> fields)
    {
        var payload = new Dictionary<string, object?>();
        var fieldMap = fields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var key in form.Keys)
        {
            if (key.StartsWith("__") || key == "RequestVerificationToken")
            {
                continue;
            }

            if (!fieldMap.TryGetValue(key, out var field))
            {
                continue;
            }

            var value = form[key].ToString();
            var converted = FieldTypeConverter.ConvertToApiValue(field, value);

            // Skip DBNull (encrypted fields that shouldn't be updated)
            if (converted != DBNull.Value)
            {
                payload[key] = converted;
            }
        }

        return payload;
    }
}
