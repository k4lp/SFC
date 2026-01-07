# SalesforceCore.AspNetCore Project Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore` project is the integration layer that makes `SalesforceCore` work seamlessly within an ASP.NET Core web application (MVC, Razor Pages, or Web API).

## 2. Key Features

### Authentication & Identity
It implements the **OAuth 2.0 Authorization Code Flow with PKCE**, which is the security best practice for server-side web apps. It handles the "Login with Salesforce" redirects, callback processing, and token storage.

### Token Management
Unlike a console app that might use a hardcoded username/password (not recommended) or a fixed JWT certificate, a web app serves multiple users. This project provides an `ITokenProvider` that retrieves the specific Access Token for the **current logged-in user** from their session/cookie.

### Dynamic UI API
It exposes the `DynamicUiController`, which serves JSON descriptors for forms and navigation. This allows building Single Page Applications (SPAs) or dynamic Razor views that adapt automatically to Salesforce metadata changes.

### UI Tag Helpers
It includes Tag Helpers (like `<input sf-field="Name" ... />`) that automatically apply Field Level Security (FLS) to HTML forms, setting fields to `readonly` or hidden based on the user's permissions.

## 3. Design Decisions
- **Session Handling**: Salesforce tokens (Access + Refresh + ID Token) are large and often exceed the 4KB browser cookie limit. This library provides `DistributedCacheTicketStore` to store the actual tokens in Redis/SQL and only send a small Session ID cookie to the browser.
- **Middleware**: Custom middleware handles global exception translation (e.g., converting Salesforce 404s to ASP.NET 404 pages) and enforces security headers.
