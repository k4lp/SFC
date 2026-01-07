using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace SalesforceCore.AspNetCore.Middleware;

/// <summary>
/// Middleware to add standard security headers to responses.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            if (!headers.ContainsKey("X-Content-Type-Options"))
            {
                headers["X-Content-Type-Options"] = "nosniff";
            }

            if (!headers.ContainsKey("X-Frame-Options"))
            {
                headers["X-Frame-Options"] = "SAMEORIGIN";
            }

            if (!headers.ContainsKey("X-XSS-Protection"))
            {
                headers["X-XSS-Protection"] = "1; mode=block";
            }

            if (!headers.ContainsKey("Referrer-Policy"))
            {
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            }

            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                // Get CSP from configuration or use default
                // Allow overriding 'unsafe-inline' and 'unsafe-eval' via configuration
                var csp = _configuration["Salesforce:Security:ContentSecurityPolicy"];

                if (string.IsNullOrEmpty(csp))
                {
                    // Strict default CSP - blocks unsafe-eval.
                    // Allows 'unsafe-inline' as it is often required for TagHelpers/Razor,
                    // but 'unsafe-eval' is removed to improve security significantly.
                    csp = "default-src 'self'; img-src 'self' data: https:; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline';";
                }

                headers["Content-Security-Policy"] = csp;
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
