# Middleware Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Middleware` directory contains middleware components that sit in the ASP.NET Core request pipeline.

## 2. Key Components

### `SalesforceExceptionMiddleware`
**Purpose**: Centralized error handling for Salesforce interactions.
**Behavior**:
- **`SalesforceAuthException`**: If the token is invalid/expired and refresh fails, this middleware catches the exception and triggers an OAuth Challenge (redirects the user to login).
- **`SalesforceNotFoundException`**: Converts to a standard HTTP 404 response.
- **`SalesforceRateLimitException`**: Converts to HTTP 429 and sets the `Retry-After` header.
- **Ajax Awareness**: Detects if the request is an AJAX/API call. If so, returns a JSON error object instead of an HTML error page.

### `SecurityHeadersMiddleware`
**Purpose**: Adds security headers (CSP, X-Frame-Options) appropriate for an app integrating with Salesforce (e.g., allowing iframing if configured for Canvas).
