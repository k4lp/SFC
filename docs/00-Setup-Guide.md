# SalesforceCore Setup Guide

Opinionated checklist to get a SalesforceCore project running on .NET 10 with predictable defaults. Use this when bootstrapping a new app or verifying an environment.

## Scope & Audience
- **Audience**: Developers bootstrapping a new service or web app.
- **Covers**: Packages, connected app prep, minimal Program.cs, config shape, and production readiness.

## Requirements
- **Required**: .NET 10.0 SDK/runtime; Salesforce Connected App with OAuth 2.0 scopes `openid`, `profile`, `email`, `api` (add `refresh_token` for web apps); API access in the org.
- **Recommended**: Distributed cache (Redis/IDistributedCache) for tokens/sessions/schema; PKCE auth for web apps.
- **Optional**: SalesforceCore.AspNetCore (controllers, tag helpers, middleware); SalesforceCore.ModelGenerator (`sf-gen`); Client Credentials/JWT flows for workers; preview NuGet feeds to satisfy `10.0.0-*` references.

## Connected App Checklist (Salesforce)
- Enable OAuth settings; callback URL: `https://localhost:5001/salesforce/callback` (adjust per env).
- Scopes: `openid`, `profile`, `email`, `api`, `refresh_token` (recommended), `offline_access` (if available).
- For JWT flow: upload certificate and pre-authorize users/profiles.

## Install Packages
```bash
# Core (required)
dotnet add package SalesforceCore

# ASP.NET Core integration (optional web helpers)
dotnet add package SalesforceCore.AspNetCore

# Model generator CLI (optional)
dotnet tool install -g SalesforceCore.ModelGenerator
```

## Minimal Config (appsettings.json)
```json
{
  "Salesforce": {
    "ClientId": "YOUR_CONNECTED_APP_CONSUMER_KEY",
    "Domain": "https://login.salesforce.com",
    "CallbackPath": "/salesforce/callback",
    "ApiVersion": "v60.0"
  }
}
```

## Minimal Web App (PKCE, recommended)
```csharp
using SalesforceCore.AspNetCore.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSalesforceCoreMvc(builder.Configuration);
builder.Services.AddDistributedMemoryCache(); // dev only; use Redis in production
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseSalesforceCore();
app.MapSalesforceRoutes(routePrefix: "sf"); // optional built-ins
app.Run();
```

## Minimal Worker/Background (JWT or Client Credentials)
```csharp
using SalesforceCore.Extensions;
using SalesforceCore.Services.Core;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddSalesforceCore(context.Configuration);
        services.AddHttpClient("SalesforceJwt");
        services.AddScoped<ITokenProvider, JwtTokenProvider>(); // or ClientCredentialsTokenProvider
    });
```

## Production Readiness Checklist
- **Auth**: PKCE for web; JWT/Client Credentials for headless services. Verify refresh/revoke paths.
- **Caching**: Redis-backed IDistributedCache; set `CacheProvider = Distributed` and `CacheKeyPrefix`.
- **Timeouts/Resilience**: Tune `HttpTimeout`, `MaxRetries`, `RetryBaseDelay`, `TotalRequestTimeout`; ensure circuit breaker defaults fit your SLAs.
- **Security**: `EnforceFieldLevelSecurity = true`; `ValidateSoqlInputs = true`; HTTPS + secure cookies.
- **Files**: Validate `MaxFileUploadSize` and allowed extensions for your org’s limits.
- **Observability**: Enable structured logging; consider enabling debug logging only in non-prod.
- **NuGet feeds**: Ensure preview feeds are available to satisfy `10.0.0-*` dependencies.

## Common Issues
- **401/invalid_client**: Check Connected App Consumer Key/Secret and callback URLs.
- **Missing tokens**: Confirm chosen `ITokenProvider` is registered and the required HttpClient name exists.
- **Restore failures**: Add .NET 10 preview feed if wildcard packages aren’t resolving.

## Next Steps
- Read [01-Getting-Started.md](01-Getting-Started.md) for a guided walkthrough.
- Dive into [03-Configuration.md](03-Configuration.md) for all options and defaults.

Session timestamp: 2025-12-25T23:00:00Z
