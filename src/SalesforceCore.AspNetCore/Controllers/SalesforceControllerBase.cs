using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using SalesforceCore.Attributes;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Tracking;
using SalesforceCore.Validation;

namespace SalesforceCore.AspNetCore.Controllers;

/// <summary>
/// Abstract base controller for Salesforce-backed MVC controllers.
/// Provides pre-wired services for data access, change tracking, and validation.
/// </summary>
/// <typeparam name="TViewModel">The view model type representing a Salesforce object.</typeparam>
/// <remarks>
/// <para>
/// Inherit from this base class to get automatic integration with:
/// </para>
/// <list type="bullet">
/// <item><description>TypedDataService for CRUD operations</description></item>
/// <item><description>ChangeTracker for detecting modified fields</description></item>
/// <item><description>FieldValidator for schema validation</description></item>
/// </list>
/// <para>
/// Example:
/// <code>
/// public class AccountController : SalesforceControllerBase&lt;AccountViewModel&gt;
/// {
///     public AccountController(
///         ITypedDataService dataService,
///         IChangeTracker changeTracker,
///         IFieldValidator fieldValidator,
///         ISchemaService schemaService)
///         : base(dataService, changeTracker, fieldValidator, schemaService)
///     {
///     }
///
///     public async Task&lt;IActionResult&gt; Create(AccountViewModel model)
///     {
///         if (!await ValidateAsync(model))
///             return View(model);
///
///         var id = await CreateAsync(model);
///         return RedirectToAction("Details", new { id });
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class SalesforceControllerBase<TViewModel> : Controller
    where TViewModel : class, new()
{
    /// <summary>
    /// The typed data service for CRUD operations.
    /// </summary>
    protected readonly ITypedDataService DataService;

    /// <summary>
    /// The change tracker for detecting modified fields.
    /// </summary>
    protected readonly IChangeTracker ChangeTracker;

    /// <summary>
    /// The field validator for schema validation.
    /// </summary>
    protected readonly IFieldValidator FieldValidator;

    /// <summary>
    /// The schema service for metadata access.
    /// </summary>
    protected readonly ISchemaService SchemaService;

    /// <summary>
    /// Creates a new SalesforceControllerBase.
    /// </summary>
    protected SalesforceControllerBase(
        ITypedDataService dataService,
        IChangeTracker changeTracker,
        IFieldValidator fieldValidator,
        ISchemaService schemaService)
    {
        DataService = dataService;
        ChangeTracker = changeTracker;
        FieldValidator = fieldValidator;
        SchemaService = schemaService;
    }

    /// <summary>
    /// Gets the Salesforce object API name from the view model type.
    /// Override this to customize the object name lookup.
    /// </summary>
    protected virtual string GetObjectName()
    {
        // Check for [SalesforceObject] attribute
        var attr = typeof(TViewModel).GetCustomAttribute<SalesforceObjectAttribute>();
        if (attr != null && !string.IsNullOrEmpty(attr.ObjectName))
        {
            return attr.ObjectName;
        }

        // Fall back to type name (remove common suffixes)
        var name = typeof(TViewModel).Name;
        var suffixes = new[] { "ViewModel", "Model", "Dto", "Entity" };
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }

    /// <summary>
    /// Gets the ID property value from a model.
    /// </summary>
    protected virtual string? GetId(TViewModel model)
    {
        var idProperty = typeof(TViewModel).GetProperty("Id");
        return idProperty?.GetValue(model)?.ToString();
    }

    /// <summary>
    /// Sets the ID property value on a model.
    /// </summary>
    protected virtual void SetId(TViewModel model, string id)
    {
        var idProperty = typeof(TViewModel).GetProperty("Id");
        idProperty?.SetValue(model, id);
    }

    /// <summary>
    /// Gets a record by ID and tracks it for change detection.
    /// </summary>
    /// <param name="id">The Salesforce record ID.</param>
    /// <returns>The view model or null if not found.</returns>
    protected virtual async Task<TViewModel?> GetByIdAsync(string id)
    {
        var entity = await DataService.GetByIdAsync<TViewModel>(id);

        if (entity != null)
        {
            ChangeTracker.Track(entity);
        }

        return entity;
    }

    /// <summary>
    /// Creates a new record in Salesforce.
    /// </summary>
    /// <param name="model">The model to create.</param>
    /// <returns>The new record ID.</returns>
    protected virtual async Task<string> CreateAsync(TViewModel model)
    {
        var id = await DataService.CreateAsync(model);
        SetId(model, id);
        return id;
    }

    /// <summary>
    /// Updates an existing record in Salesforce.
    /// Uses change tracking to send only modified fields.
    /// </summary>
    /// <param name="model">The model to update.</param>
    protected virtual async Task UpdateAsync(TViewModel model)
    {
        var id = GetId(model);

        if (string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException("Cannot update a model without an ID.");
        }

        // Check for changes using GetState
        var state = ChangeTracker.GetState(model);
        if (state == EntityState.Modified)
        {
            // Get only changed fields
            var changes = ChangeTracker.GetModifiedFields(model);
            if (changes.Count > 0)
            {
                // Update the model using typed data service
                await DataService.UpdateAsync(model);
                ChangeTracker.AcceptChanges(model);
            }
        }
        else
        {
            // Entity not being tracked or no changes, perform full update
            await DataService.UpdateAsync(model);
        }
    }

    /// <summary>
    /// Deletes a record from Salesforce.
    /// </summary>
    /// <param name="id">The record ID to delete.</param>
    protected virtual async Task DeleteAsync(string id)
    {
        await DataService.DeleteAsync<TViewModel>(id);
    }

    /// <summary>
    /// Validates the model against Salesforce schema asynchronously.
    /// Adds validation errors to ModelState.
    /// </summary>
    /// <param name="model">The model to validate.</param>
    /// <param name="isCreate">Whether this is a create operation.</param>
    /// <returns>True if validation passes.</returns>
    protected virtual async Task<bool> ValidateAsync(TViewModel model, bool isCreate = true)
    {
        var result = await FieldValidator.ValidateEntityAsync(model, isCreate);

        foreach (var error in result.Errors)
        {
            var fieldName = error.FieldName ?? string.Empty;
            ModelState.AddModelError(fieldName, error.Message);
        }

        return ModelState.IsValid;
    }

    /// <summary>
    /// Saves the model by creating or updating based on ID presence.
    /// </summary>
    /// <param name="model">The model to save.</param>
    /// <returns>The record ID.</returns>
    protected virtual async Task<string> SaveAsync(TViewModel model)
    {
        var id = GetId(model);

        if (string.IsNullOrEmpty(id))
        {
            return await CreateAsync(model);
        }

        await UpdateAsync(model);
        return id;
    }

    /// <summary>
    /// Sets a success message in TempData for display as a toast.
    /// </summary>
    protected void SetSuccessMessage(string message)
    {
        TempData["SuccessMessage"] = message;
    }

    /// <summary>
    /// Sets an error message in TempData for display as a toast.
    /// </summary>
    protected void SetErrorMessage(string message)
    {
        TempData["ErrorMessage"] = message;
    }

    /// <summary>
    /// Sets a warning message in TempData for display as a toast.
    /// </summary>
    protected void SetWarningMessage(string message)
    {
        TempData["WarningMessage"] = message;
    }

    /// <summary>
    /// Sets an info message in TempData for display as a toast.
    /// </summary>
    protected void SetInfoMessage(string message)
    {
        TempData["InfoMessage"] = message;
    }

    /// <summary>
    /// Stores Salesforce API errors in TempData for display as toasts.
    /// </summary>
    protected void SetSalesforceErrors(IEnumerable<object> errors)
    {
        TempData["SalesforceErrors"] = System.Text.Json.JsonSerializer.Serialize(errors);
    }
}
