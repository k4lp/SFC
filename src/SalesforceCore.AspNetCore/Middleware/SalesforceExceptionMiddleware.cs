using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SalesforceCore.Models.Errors;
using System.Net;
using System.Text.Json;

namespace SalesforceCore.AspNetCore.Middleware;

/// <summary>
/// Middleware for handling Salesforce-specific exceptions.
/// </summary>
public class SalesforceExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SalesforceExceptionMiddleware> _logger;

    /// <summary>
    /// Creates new middleware instance.
    /// </summary>
    public SalesforceExceptionMiddleware(
        RequestDelegate next,
        ILogger<SalesforceExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (SalesforceAuthException ex)
        {
            _logger.LogWarning(ex, "Salesforce authentication error");
            await HandleAuthExceptionAsync(context, ex);
        }
        catch (SalesforceNotFoundException ex)
        {
            _logger.LogWarning(ex, "Salesforce not found: {ObjectType} {RecordId}", ex.ObjectType, ex.RecordId);
            await HandleNotFoundExceptionAsync(context, ex);
        }
        catch (SalesforcePermissionException ex)
        {
            _logger.LogWarning(ex, "Salesforce permission denied: {Operation} on {Target}", ex.Operation, ex.Target);
            await HandlePermissionExceptionAsync(context, ex);
        }
        catch (SalesforceValidationException ex)
        {
            _logger.LogWarning(ex, "Salesforce validation error");
            await HandleValidationExceptionAsync(context, ex);
        }
        catch (SalesforceRateLimitException ex)
        {
            _logger.LogWarning(ex, "Salesforce rate limit exceeded");
            await HandleRateLimitExceptionAsync(context, ex);
        }
        catch (SalesforceException ex)
        {
            _logger.LogError(ex, "Salesforce error: {ErrorCode}", ex.ErrorCode);
            await HandleSalesforceExceptionAsync(context, ex);
        }
    }

    private async Task HandleAuthExceptionAsync(HttpContext context, SalesforceAuthException ex)
    {
        if (IsAjaxRequest(context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Unauthorized",
                message = ex.Message,
                requiresReauth = ex.RequiresReauth
            }));
        }
        else
        {
            // Challenge using the configured authentication handler (typically OpenIdConnect).
            // This avoids redirecting to an app-specific login endpoint that may not exist.
            var returnUrl = context.Request.Path + context.Request.QueryString;
            await context.ChallengeAsync(new AuthenticationProperties
            {
                RedirectUri = returnUrl
            });
        }
    }

    private async Task HandleNotFoundExceptionAsync(HttpContext context, SalesforceNotFoundException ex)
    {
        if (IsAjaxRequest(context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "NotFound",
                message = ex.Message,
                objectType = ex.ObjectType,
                recordId = ex.RecordId
            }));
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(ex.Message);
        }
    }

    private async Task HandlePermissionExceptionAsync(HttpContext context, SalesforcePermissionException ex)
    {
        if (IsAjaxRequest(context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Forbidden",
                message = ex.Message,
                operation = ex.Operation,
                target = ex.Target
            }));
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(ex.Message);
        }
    }

    private async Task HandleValidationExceptionAsync(HttpContext context, SalesforceValidationException ex)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "ValidationError",
            message = ex.Message,
            errors = ex.ValidationErrors
        }));
    }

    private async Task HandleRateLimitExceptionAsync(HttpContext context, SalesforceRateLimitException ex)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        if (ex.RetryAfter.HasValue)
        {
            context.Response.Headers.Append("Retry-After", ex.RetryAfter.Value.TotalSeconds.ToString());
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = "RateLimitExceeded",
            message = ex.Message,
            retryAfterSeconds = ex.RetryAfter?.TotalSeconds
        }));
    }

    private async Task HandleSalesforceExceptionAsync(HttpContext context, SalesforceException ex)
    {
        var statusCode = ex.HttpStatusCode ?? (int)HttpStatusCode.InternalServerError;
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            error = ex.ErrorCode ?? "SalesforceError",
            message = ex.Message,
            fields = ex.Fields
        }));
    }

    private static bool IsAjaxRequest(HttpContext context)
    {
        return context.Request.Headers.ContainsKey("X-Requested-With") ||
               context.Request.Headers.ContainsKey("HX-Request") ||
               context.Request.Headers["Accept"].ToString().Contains("application/json");
    }
}
