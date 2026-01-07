using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SalesforceCore.AspNetCore.ViewComponents;

/// <summary>
/// View component that displays toast notifications from TempData.
/// Supports success, error, warning, and info messages.
/// </summary>
/// <remarks>
/// <para>
/// This component reads messages from TempData and displays them as toast notifications.
/// It supports multiple message types and can display Salesforce API errors.
/// </para>
/// <para>
/// Usage in Razor views (typically in layout):
/// <code>
/// &lt;!-- In _Layout.cshtml --&gt;
/// @await Component.InvokeAsync("SalesforceToast")
///
/// &lt;!-- With options --&gt;
/// @await Component.InvokeAsync("SalesforceToast", new {
///     position = "top-right",
///     autoDismissSeconds = 5
/// })
/// </code>
/// </para>
/// <para>
/// Setting messages in controller:
/// <code>
/// TempData["SuccessMessage"] = "Record saved successfully!";
/// TempData["ErrorMessage"] = "Failed to save record.";
/// TempData["WarningMessage"] = "Some fields were not updated.";
/// TempData["InfoMessage"] = "Record has been updated by another user.";
///
/// // For Salesforce API errors:
/// TempData["SalesforceErrors"] = JsonSerializer.Serialize(errors);
/// </code>
/// </para>
/// </remarks>
public class SalesforceToastViewComponent : ViewComponent
{
    /// <summary>
    /// Renders the toast container with any pending messages.
    /// </summary>
    /// <param name="position">Toast position: top-right, top-left, bottom-right, bottom-left.</param>
    /// <param name="autoDismissSeconds">Seconds before auto-dismiss (0 to disable).</param>
    /// <param name="closable">Whether toasts can be manually closed.</param>
    public IViewComponentResult Invoke(
        string position = "top-right",
        int autoDismissSeconds = 5,
        bool closable = true)
    {
        var model = new ToastViewModel
        {
            Position = position,
            AutoDismissMs = autoDismissSeconds * 1000,
            Closable = closable,
            Messages = new List<ToastMessage>()
        };

        // Check for success message
        if (TempData.TryGetValue("SuccessMessage", out var successMsg) && successMsg != null)
        {
            model.Messages.Add(new ToastMessage
            {
                Type = "success",
                Text = successMsg.ToString() ?? ""
            });
        }

        // Check for error message
        if (TempData.TryGetValue("ErrorMessage", out var errorMsg) && errorMsg != null)
        {
            model.Messages.Add(new ToastMessage
            {
                Type = "error",
                Text = errorMsg.ToString() ?? ""
            });
        }

        // Check for warning message
        if (TempData.TryGetValue("WarningMessage", out var warningMsg) && warningMsg != null)
        {
            model.Messages.Add(new ToastMessage
            {
                Type = "warning",
                Text = warningMsg.ToString() ?? ""
            });
        }

        // Check for info message
        if (TempData.TryGetValue("InfoMessage", out var infoMsg) && infoMsg != null)
        {
            model.Messages.Add(new ToastMessage
            {
                Type = "info",
                Text = infoMsg.ToString() ?? ""
            });
        }

        // Check for Salesforce API errors
        if (TempData.TryGetValue("SalesforceErrors", out var sfErrors) && sfErrors != null)
        {
            try
            {
                var errorsJson = sfErrors.ToString();
                if (!string.IsNullOrEmpty(errorsJson))
                {
                    var errors = System.Text.Json.JsonSerializer.Deserialize<List<SalesforceApiError>>(errorsJson);
                    if (errors != null)
                    {
                        foreach (var error in errors)
                        {
                            var message = FormatApiError(error);
                            model.Messages.Add(new ToastMessage
                            {
                                Type = "error",
                                Text = message
                            });
                        }
                    }
                }
            }
            catch
            {
                // If parsing fails, show raw error
                model.Messages.Add(new ToastMessage
                {
                    Type = "error",
                    Text = sfErrors.ToString() ?? "An error occurred"
                });
            }
        }

        // Check for multiple messages array
        if (TempData.TryGetValue("ToastMessages", out var toastMsgs) && toastMsgs != null)
        {
            try
            {
                var msgsJson = toastMsgs.ToString();
                if (!string.IsNullOrEmpty(msgsJson))
                {
                    var messages = System.Text.Json.JsonSerializer.Deserialize<List<ToastMessage>>(msgsJson);
                    if (messages != null)
                    {
                        model.Messages.AddRange(messages);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
        }

        return View(model);
    }

    private string FormatApiError(SalesforceApiError error)
    {
        var message = error.Message ?? "An error occurred";

        // Add field information if available
        if (error.Fields?.Count > 0)
        {
            message += $" (Fields: {string.Join(", ", error.Fields)})";
        }

        // Map common error codes to user-friendly messages
        return error.ErrorCode switch
        {
            "REQUIRED_FIELD_MISSING" => $"Required field is missing: {message}",
            "DUPLICATE_VALUE" => $"Duplicate value: {message}",
            "INVALID_FIELD" => $"Invalid field value: {message}",
            "FIELD_CUSTOM_VALIDATION_EXCEPTION" => message,
            "ENTITY_IS_DELETED" => "This record has been deleted.",
            "INSUFFICIENT_ACCESS_OR_READONLY" => "You don't have permission to perform this action.",
            "INVALID_CROSS_REFERENCE_KEY" => "Invalid reference: The related record doesn't exist.",
            "STRING_TOO_LONG" => $"Text is too long: {message}",
            "MALFORMED_ID" => "Invalid record ID format.",
            _ => message
        };
    }
}

/// <summary>
/// View model for the toast component.
/// </summary>
public class ToastViewModel
{
    /// <summary>Toast position (top-right, top-left, bottom-right, bottom-left).</summary>
    public string Position { get; set; } = "top-right";

    /// <summary>Milliseconds before auto-dismiss (0 to disable).</summary>
    public int AutoDismissMs { get; set; }

    /// <summary>Whether toasts can be manually closed.</summary>
    public bool Closable { get; set; }

    /// <summary>The messages to display.</summary>
    public List<ToastMessage> Messages { get; set; } = new();
}

/// <summary>
/// Represents a single toast message.
/// </summary>
public class ToastMessage
{
    /// <summary>Message type: success, error, warning, info.</summary>
    public string Type { get; set; } = "info";

    /// <summary>The message text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Optional title for the toast.</summary>
    public string? Title { get; set; }
}

/// <summary>
/// Represents a Salesforce API error.
/// </summary>
internal class SalesforceApiError
{
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public List<string>? Fields { get; set; }
}
