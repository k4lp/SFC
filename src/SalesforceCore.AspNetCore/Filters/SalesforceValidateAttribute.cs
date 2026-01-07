using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.Attributes;
using SalesforceCore.Validation;

namespace SalesforceCore.AspNetCore.Filters;

/// <summary>
/// Action filter that automatically validates Salesforce models before action execution.
/// Adds validation errors to ModelState for seamless integration with ASP.NET Core validation.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute to controller actions that receive Salesforce model inputs.
/// It will validate the model against Salesforce schema.
/// </para>
/// <para>
/// Examples:
/// <code>
/// // Basic usage
/// [HttpPost]
/// [SalesforceValidate]
/// public async Task&lt;IActionResult&gt; Create(AccountViewModel model)
/// {
///     if (!ModelState.IsValid)
///         return View(model);
///
///     // Model is validated against Salesforce schema
///     await _dataService.CreateAsync("Account", model);
///     return RedirectToAction("Index");
/// }
///
/// // With options
/// [HttpPost]
/// [SalesforceValidate(IsCreate = true, StopOnFirstError = false)]
/// public async Task&lt;IActionResult&gt; Update(ContactViewModel model)
/// {
///     // ...
/// }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class SalesforceValidateAttribute : ActionFilterAttribute
{
    /// <summary>
    /// Whether this is a create operation (affects required field validation).
    /// Default: true.
    /// </summary>
    public bool IsCreate { get; set; } = true;

    /// <summary>
    /// Whether to stop validation on first error. Default: false.
    /// </summary>
    public bool StopOnFirstError { get; set; } = false;

    /// <summary>
    /// Whether to return BadRequest when validation fails (for API controllers).
    /// Default: false (lets action decide how to handle invalid state).
    /// </summary>
    public bool ReturnBadRequestOnFailure { get; set; } = false;

    /// <summary>
    /// The parameter name to validate. If not specified, validates the first complex type parameter.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <inheritdoc/>
    public override async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // Find the model to validate
        var model = FindModelToValidate(context);

        if (model == null)
        {
            await next();
            return;
        }

        // Get validation service
        var services = context.HttpContext.RequestServices;
        var fieldValidator = services.GetService<IFieldValidator>();

        if (fieldValidator == null)
        {
            await next();
            return;
        }

        // Validate the model
        var result = await fieldValidator.ValidateEntityAsync(model, IsCreate);

        // Add errors to ModelState
        foreach (var error in result.Errors)
        {
            var fieldName = error.FieldName ?? string.Empty;

            // Avoid duplicate errors
            if (context.ModelState.ContainsKey(fieldName))
            {
                var existingErrors = context.ModelState[fieldName]?.Errors;
                if (existingErrors != null && existingErrors.Any(e => e.ErrorMessage == error.Message))
                {
                    continue;
                }
            }

            context.ModelState.AddModelError(fieldName, error.Message);

            if (StopOnFirstError)
            {
                break;
            }
        }

        // Return BadRequest if configured and validation failed
        if (ReturnBadRequestOnFailure && !context.ModelState.IsValid)
        {
            context.Result = new BadRequestObjectResult(context.ModelState);
            return;
        }

        await next();
    }

    private object? FindModelToValidate(ActionExecutingContext context)
    {
        // If parameter name is specified, use it
        if (!string.IsNullOrEmpty(ParameterName))
        {
            if (context.ActionArguments.TryGetValue(ParameterName, out var paramModel))
            {
                return paramModel;
            }
            return null;
        }

        // Find first complex type parameter that isn't a primitive or system type
        foreach (var (key, value) in context.ActionArguments)
        {
            if (value == null) continue;

            var type = value.GetType();

            // Skip primitives, strings, and value types
            if (type.IsPrimitive ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type.IsValueType)
            {
                continue;
            }

            // Check if it has [SalesforceObject] attribute
            if (type.GetCustomAttribute<SalesforceObjectAttribute>() != null)
            {
                return value;
            }

            // Check if it has an Id property (common for Salesforce models)
            if (type.GetProperty("Id") != null)
            {
                return value;
            }

            // Use first complex class type
            if (type.IsClass)
            {
                return value;
            }
        }

        return null;
    }
}
