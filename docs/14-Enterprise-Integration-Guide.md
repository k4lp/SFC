# Enterprise Integration Guide

Comprehensive guidelines for deploying SalesforceCore in enterprise environments - covering architecture, security, scalability, observability, and compliance for government-grade deployments.

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Deployment Patterns](#deployment-patterns)
3. [Security & Compliance](#security--compliance)
4. [Resilience & Reliability](#resilience--reliability)
5. [Scalability & Performance](#scalability--performance)
6. [Observability & Monitoring](#observability--monitoring)
7. [Data Governance](#data-governance)
8. [Disaster Recovery](#disaster-recovery)
9. [Multi-Environment Strategy](#multi-environment-strategy)
10. [Operational Runbook](#operational-runbook)

---

## Architecture Overview

### Enterprise Reference Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Load Balancer / API Gateway                       │
│                    (Azure Application Gateway / AWS ALB)                  │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
         ┌───────────────────────┼───────────────────────┐
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Web App       │    │   Web App       │    │   Web App       │
│   Instance 1    │    │   Instance 2    │    │   Instance N    │
│                 │    │                 │    │                 │
│ ┌─────────────┐ │    │ ┌─────────────┐ │    │ ┌─────────────┐ │
│ │SalesforceCore│ │    │ │SalesforceCore│ │    │ │SalesforceCore│ │
│ │ .AspNetCore │ │    │ │ .AspNetCore │ │    │ │ .AspNetCore │ │
│ └─────────────┘ │    │ └─────────────┘ │    │ └─────────────┘ │
└────────┬────────┘    └────────┬────────┘    └────────┬────────┘
         │                      │                      │
         └──────────────────────┼──────────────────────┘
                                │
                    ┌───────────┼───────────┐
                    │           │           │
                    ▼           ▼           ▼
           ┌───────────┐ ┌───────────┐ ┌───────────┐
           │   Redis   │ │   Vault   │ │   SQL     │
           │  Cluster  │ │  (Secrets)│ │  Database │
           └───────────┘ └───────────┘ └───────────┘
                                │
                    ┌───────────┴───────────┐
                    │                       │
                    ▼                       ▼
           ┌───────────────┐       ┌───────────────┐
           │  Background   │       │  Background   │
           │  Workers      │       │  Workers      │
           │               │       │               │
           │ SalesforceCore│       │ SalesforceCore│
           └───────┬───────┘       └───────┬───────┘
                   │                       │
                   └───────────┬───────────┘
                               │
                               ▼
                    ┌─────────────────────┐
                    │   Salesforce Org    │
                    │                     │
                    │ • Production        │
                    │ • Sandbox           │
                    │ • Developer         │
                    └─────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | SalesforceCore Package |
|-----------|---------------|----------------------|
| **Web Applications** | User-facing UI, OAuth flows | `SalesforceCore.AspNetCore` |
| **API Services** | REST endpoints, data access | `SalesforceCore` |
| **Background Workers** | Sync jobs, bulk operations | `SalesforceCore` |
| **Redis Cluster** | Distributed cache + server-side auth tickets | Via `IDistributedCache` / `ICacheProvider` |
| **Secret Vault** | Credentials, certificates, keys | Configuration binding |
| **SQL Database** | Application data, audit logs | Your choice |

### Package Separation Strategy

```csharp
// Web Layer (UI) - Use SalesforceCore.AspNetCore
// Includes tag helpers, controllers, middleware, OAuth
services.AddSalesforceCoreMvc(configuration);
services.AddDistributedMemoryCache(); // dev; use Redis in production
services.AddSalesforceAuthentication(configuration, useServerSideSessions: true);

// Service Layer (Business Logic) - Use SalesforceCore
// Core data access, no ASP.NET Core dependencies
services.AddSalesforceCore(configuration);

// Worker Layer (Background Jobs) - Use SalesforceCore
// JWT auth, no web dependencies
services.AddSalesforceCore(configuration);
// JWT provider auto-registered when SalesforceJwt config present
```

---

## Deployment Patterns

### Pattern 1: Multi-Node Web Application

For high-availability web applications with multiple instances:

```csharp
// Program.cs - Production Web App
var builder = WebApplication.CreateBuilder(args);

// 1. Load configuration from secure sources
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Url"]!),
    new DefaultAzureCredential());

// 2. Add distributed caching (Redis)
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SalesforceCore_Prod_";
});

// 3. Add Salesforce services + authentication
// In multi-node deployments, enable server-side auth tickets to avoid cookie size limits.
builder.Services.AddSalesforceCoreMvc(builder.Configuration);
builder.Services.AddSalesforceAuthentication(builder.Configuration, useServerSideSessions: true);

// 4. Add health checks
builder.Services.AddHealthChecks()
    .AddRedis(builder.Configuration.GetConnectionString("Redis")!)
    .AddCheck<SalesforceHealthCheck>("salesforce");

var app = builder.Build();

app.UseHttpsRedirection();
app.UseHsts();
app.UseAuthentication();
app.UseAuthorization();
app.UseSalesforceCore();
app.MapSalesforceRoutes();
app.MapHealthChecks("/health");
app.Run();
```

### Pattern 2: Background Worker Service

For scheduled jobs, data synchronization, and bulk operations:

```csharp
// Program.cs - Background Worker
var builder = Host.CreateApplicationBuilder(args);

// Load secrets
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["KeyVault:Url"]!),
    new DefaultAzureCredential());

// Add Salesforce services (JWT auth for server-to-server)
builder.Services.AddSalesforceCore(builder.Configuration);

// Add distributed cache for token sharing
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "SalesforceCore_Worker_";
});

// Add workers
builder.Services.AddHostedService<AccountSyncWorker>();
builder.Services.AddHostedService<OpportunitySyncWorker>();
builder.Services.AddHostedService<BulkDataCleanupWorker>();

var host = builder.Build();
host.Run();
```

### Pattern 3: API-Only Service

For microservices exposing Salesforce data via REST API:

```csharp
// Program.cs - API Service
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSalesforceCore(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add API authentication (your choice - JWT, API Key, etc.)
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => { /* ... */ });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapOpenApi();
app.Run();
```

---

## Security & Compliance

### Defense in Depth Strategy

```
┌─────────────────────────────────────────────────────────────────┐
│                    Network Layer                                  │
│   • TLS 1.3 required                                             │
│   • IP restrictions on Salesforce Connected App                  │
│   • WAF rules for common attacks                                 │
├─────────────────────────────────────────────────────────────────┤
│                    Authentication Layer                           │
│   • PKCE OAuth for web (no client secret in browser)             │
│   • JWT Bearer with certificate for services                     │
│   • MFA enforcement in Salesforce                                │
├─────────────────────────────────────────────────────────────────┤
│                    Authorization Layer                            │
│   • Salesforce Profile/Permission Set restrictions               │
│   • Field-Level Security (FLS) enforcement                       │
│   • Visibility policies for UI elements                          │
├─────────────────────────────────────────────────────────────────┤
│                    Data Layer                                     │
│   • SOQL injection prevention (SoqlBuilder/SoqlCondition)        │
│   • Input validation (SecurityUtils)                             │
│   • Secure serialization (System.Text.Json)                      │
├─────────────────────────────────────────────────────────────────┤
│                    Infrastructure Layer                           │
│   • Secrets in vault (never in config files)                     │
│   • Encrypted caches (Redis TLS)                                 │
│   • Secure cookies (__Host- prefix)                              │
└─────────────────────────────────────────────────────────────────┘
```

### Secret Management

**Never store secrets in configuration files:**

```json
// BAD - appsettings.json with secrets
{
  "Salesforce": {
    "ClientSecret": "actual-secret-here"  // NEVER DO THIS
  }
}
```

**Use secure vault integration:**

```csharp
// Azure Key Vault
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-vault.vault.azure.net/"),
    new DefaultAzureCredential());

// AWS Secrets Manager
builder.Configuration.AddSecretsManager(options =>
{
    options.SecretFilter = secret => secret.Name.StartsWith("salesforce/");
});

// HashiCorp Vault
builder.Configuration.AddVault(options =>
{
    options.Address = "https://vault.company.com:8200";
    options.Token = Environment.GetEnvironmentVariable("VAULT_TOKEN");
});
```

### Connected App Security Configuration

```
Salesforce Connected App Checklist:
├── Basic Settings
│   ├── Contact email for security notifications
│   └── API name without special characters
├── OAuth Settings
│   ├── Minimal required scopes only
│   │   ├── api (required)
│   │   ├── refresh_token (for web apps)
│   │   └── openid, profile, email (for identity)
│   ├── Callback URLs explicitly listed
│   └── PKCE required for web apps
├── OAuth Policies
│   ├── "Admin approved users are pre-authorized"
│   ├── IP restrictions enabled (production)
│   ├── Refresh token expires (e.g., 90 days)
│   └── Client Credentials "Run As" user restricted
├── JWT Settings (if used)
│   ├── Certificate uploaded (RSA 2048+)
│   ├── Pre-authorized users/profiles defined
│   └── Certificate expiry monitored
└── Monitoring
    ├── Connected App usage reports enabled
    ├── Login history monitoring
    └── Event monitoring (Shield)
```

### Compliance Configurations

```csharp
// appsettings.Production.json - Security Hardening
{
  "Salesforce": {
    "EnforceFieldLevelSecurity": true,
    "ValidateSoqlInputs": true,
    "ForceSecureCookie": true,
    "SessionCookieName": "__Host-SalesforceSession",
    "EnableDebugLogging": false
  },

  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "SalesforceCore": "Information"  // Audit trail
    }
  }
}
```

---

## Resilience & Reliability

### HTTP Client Resilience

SalesforceCore uses Polly for automatic retry and circuit breaker patterns:

```csharp
// Default resilience configuration
{
  "Salesforce": {
    "HttpTimeout": "00:00:30",
    "MaxRetries": 3,
    "RetryBaseDelay": "00:00:01",
    "TotalRequestTimeout": "00:01:00"
  }
}
```

### Retry Strategy

```
Request Flow with Retries:
┌──────────┐
│  Request │
└────┬─────┘
     │
     ▼
┌────────────────────────────────────────────────────────┐
│  Attempt 1                                              │
│  └── Success → Return response                          │
│  └── Transient error → Wait 1s → Retry                 │
│  └── 429 (Rate Limit) → Wait from Retry-After → Retry  │
│  └── 5xx → Wait 1s → Retry                             │
│  └── 4xx (except 429) → Throw immediately              │
├────────────────────────────────────────────────────────┤
│  Attempt 2 (after 1s)                                   │
│  └── Success → Return response                          │
│  └── Transient error → Wait 2s → Retry                 │
├────────────────────────────────────────────────────────┤
│  Attempt 3 (after 2s)                                   │
│  └── Success → Return response                          │
│  └── Any error → Throw exception                        │
└────────────────────────────────────────────────────────┘
```

### Circuit Breaker Pattern

```csharp
// Circuit breaker protects against cascading failures
{
  "Salesforce": {
    "CircuitBreakerSamplingDuration": "00:00:30",
    "CircuitBreakerBreakDuration": "00:00:30",
    "TotalRequestTimeout": "00:01:00"
  }
}
```

### API Limit Monitoring

```csharp
public class ApiLimitMonitorService : BackgroundService
{
    private readonly ILimitsService _limitsService;
    private readonly ILogger<ApiLimitMonitorService> _logger;
    private readonly IAlertService _alertService;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var limits = await _limitsService.GetLimitsAsync(stoppingToken);

            // Alert when approaching limits
            var dailyApiRemaining = limits.DailyApiRequests.Remaining;
            var dailyApiMax = limits.DailyApiRequests.Max;
            var usagePercent = (double)(dailyApiMax - dailyApiRemaining) / dailyApiMax * 100;

            if (usagePercent > 80)
            {
                await _alertService.SendWarningAsync(
                    $"Salesforce API usage at {usagePercent:F1}% ({dailyApiRemaining} remaining)");
            }

            if (usagePercent > 95)
            {
                await _alertService.SendCriticalAsync(
                    $"CRITICAL: Salesforce API usage at {usagePercent:F1}%");
            }

            _logger.LogInformation(
                "API Limits - Daily: {Used}/{Max} ({Percent:F1}%)",
                dailyApiMax - dailyApiRemaining,
                dailyApiMax,
                usagePercent);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

---

## Scalability & Performance

### Caching Strategy

```
Cache Hierarchy:
┌─────────────────────────────────────────────────────────────────┐
│                    Application Memory Cache                       │
│   • Hot data (frequently accessed schema)                        │
│   • Per-instance, fastest access                                 │
│   • Invalidated on app restart                                   │
├─────────────────────────────────────────────────────────────────┤
│           Distributed Cache (Redis OR SQL Server)                │
│   • Schema metadata (1 hour TTL)                                 │
│   • Tokens (matches token expiry)                                │
│   • Sessions (8 hour TTL)                                        │
│   • Shared across instances                                      │
│   • SQL Server option: AES-256-GCM encrypted                     │
├─────────────────────────────────────────────────────────────────┤
│                    Salesforce API                                 │
│   • Source of truth                                              │
│   • Called on cache miss                                         │
│   • Rate limited                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Cache Provider Options

| Provider | Best For | Characteristics |
|----------|----------|-----------------|
| `Memory` | Single-instance, dev | ~0.001ms, per-process |
| `Distributed` (Redis) | Multi-instance, high-perf | ~1ms, external server |
| `SqlServer` | Government, encryption-required | ~5-20ms, AES-256-GCM encrypted |

### Cache Configuration (Redis)

```json
{
  "Salesforce": {
    "CacheProvider": "Distributed",
    "CacheKeyPrefix": "PROD_SF_",
    "SchemaCacheDuration": "01:00:00",
    "LookupCacheDuration": "00:15:00",
    "PermissionCacheDuration": "00:05:00",
    "LayoutCacheDuration": "00:10:00"
  },
  "ConnectionStrings": {
    "Redis": "redis-cluster.company.com:6380,ssl=true,abortConnect=false,connectTimeout=5000"
  }
}
```

### Cache Configuration (SQL Server - Government Grade)

For environments requiring encryption at rest and full audit logging. All cached values are encrypted with AES-256-GCM before storage.

```json
{
  "Salesforce": {
    "CacheProvider": "SqlServer",
    "CacheKeyPrefix": "PROD_SF_",
    "SqlCacheEncryptionKey": "load-from-azure-key-vault",
    "AllowInsecureSqlCacheKeyDerivation": false,
    "SqlCacheWriteBehind": {
      "Enabled": true,
      "Capacity": 50000,
      "MaxBatchSize": 500,
      "FlushInterval": "00:00:05",
      "SlidingExpirationRefreshThresholdSeconds": 60,
      "CleanupGracePeriod": "00:00:10"
    },
    "CacheCleanupInterval": "00:30:00"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=sql-prod.company.com;Database=SalesforceCache;..."
  }
}
```

```csharp
// Program.cs - Government deployment (just 2 lines, same as Redis!)
builder.Services.AddSalesforceEncryptedSqlServerCache(builder.Configuration);
builder.Services.AddSalesforceCore(builder.Configuration);
// Table is auto-created on startup by default (AutoCreateTable=true).
```

**Government Cache Compliance Features:**
- AES-256-GCM encryption (mandatory, cannot be disabled)
- Full audit logging (operation, key, size, timing, success/failure)
- Access tracking (counts, timestamps) for compliance reporting
- Write-behind buffering for access/expiry metadata (prevents read-path write amplification; enabled by default)
- Background cleanup with batched deletions (prevents lock escalation)
- Optimistic concurrency control (prevents race conditions)

### Cache Stampede Prevention

SalesforceCore implements a two-level strategy to prevent cache stampede:
- Local striped locks prevent duplicate work within a single process.
- When an `IDistributedLockProvider` is available, a distributed lock prevents stampede across servers.
  - When using the encrypted SQL Server cache, SQL application locks (`sp_getapplock`) are used for cross-node coordination.

```csharp
// 32 lock stripes for fine-grained concurrency
private readonly SemaphoreSlim[] _lockStripes = new SemaphoreSlim[32];

// Hash-based stripe selection
var lockIndex = (uint)key.GetHashCode() & 0x1F;
var stripeLock = _lockStripes[lockIndex];

await stripeLock.WaitAsync(cancellationToken);
try
{
    // Check cache again (double-checked locking)
    var cached = await _cache.GetAsync(key);
    if (cached != null) return cached;

    // Execute factory
    var result = await factory();
    await _cache.SetAsync(key, result);
    return result;
}
finally
{
    stripeLock.Release();
}
```

### Bulk Operations for High Volume

```csharp
// Use Bulk API for large data volumes
public async Task SyncLargeDatasetAsync(IEnumerable<Account> accounts)
{
    var bulkService = _serviceProvider.GetRequiredService<IBulkService>();

    // Insert up to 10,000 records per batch
    var job = await bulkService.CreateJobAsync("Account", "insert");

    var batches = accounts.Chunk(10000);
    foreach (var batch in batches)
    {
        await bulkService.AddBatchAsync(job.Id, batch);
    }

    await bulkService.CloseJobAsync(job.Id);

    // Poll for completion
    var result = await bulkService.WaitForCompletionAsync(job.Id);

    _logger.LogInformation(
        "Bulk insert completed: {Processed} processed, {Failed} failed",
        result.NumberRecordsProcessed,
        result.NumberRecordsFailed);
}
```

---

## Observability & Monitoring

### Structured Logging

```csharp
// Program.cs - Configure structured logging
builder.Host.UseSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithEnvironmentName()
        .Enrich.WithProperty("Application", "SalesforceApp")
        .WriteTo.Console()
        .WriteTo.Seq("http://seq.company.com:5341");
});
```

### Correlation IDs

```csharp
// Middleware to add correlation ID
public class CorrelationIdMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await next(context);
        }
    }
}
```

### Health Checks

```csharp
public class SalesforceHealthCheck : IHealthCheck
{
    private readonly ISalesforceClient _client;
    private readonly ITokenProvider _tokenProvider;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify token can be obtained
            var token = await _tokenProvider.GetTokenAsync(cancellationToken);
            if (token == null)
            {
                return HealthCheckResult.Unhealthy("Cannot obtain Salesforce token");
            }

            // Verify API is accessible with a lightweight query
            var limits = await _client.GetLimitsAsync(cancellationToken);

            var remainingPercent = (double)limits.DailyApiRequests.Remaining /
                                   limits.DailyApiRequests.Max * 100;

            if (remainingPercent < 10)
            {
                return HealthCheckResult.Degraded(
                    $"API limit low: {remainingPercent:F1}% remaining");
            }

            return HealthCheckResult.Healthy($"Connected to {token.InstanceUrl}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Salesforce connection failed", ex);
        }
    }
}
```

### Metrics Collection

```csharp
// Custom metrics for Prometheus/Grafana
public class SalesforceMetrics
{
    private readonly Counter _apiCallsTotal;
    private readonly Histogram _apiCallDuration;
    private readonly Gauge _apiLimitRemaining;

    public SalesforceMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("SalesforceCore");

        _apiCallsTotal = meter.CreateCounter<long>(
            "salesforce_api_calls_total",
            "Total number of Salesforce API calls");

        _apiCallDuration = meter.CreateHistogram<double>(
            "salesforce_api_call_duration_seconds",
            "Duration of Salesforce API calls");

        _apiLimitRemaining = meter.CreateGauge<int>(
            "salesforce_api_limit_remaining",
            "Remaining daily API calls");
    }

    public void RecordApiCall(string operation, TimeSpan duration, bool success)
    {
        _apiCallsTotal.Add(1, new KeyValuePair<string, object?>("operation", operation),
                                new KeyValuePair<string, object?>("success", success));
        _apiCallDuration.Record(duration.TotalSeconds,
                                new KeyValuePair<string, object?>("operation", operation));
    }
}
```

---

## Data Governance

### Data Classification

```csharp
// Define field sensitivity levels
public enum DataSensitivity
{
    Public,           // Can be logged, cached freely
    Internal,         // Can be cached, not logged
    Confidential,     // Limited caching, never logged
    Restricted        // No caching, no logging, audit access
}

// Apply to fields
[SalesforceField("SSN__c", Sensitivity = DataSensitivity.Restricted)]
public string? SocialSecurityNumber { get; set; }
```

### Audit Logging

```csharp
public class AuditService
{
    private readonly ILogger<AuditService> _logger;
    private readonly IAuditRepository _repository;

    public async Task LogAccessAsync(string userId, string objectType, string recordId, string action)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            ObjectType = objectType,
            RecordId = recordId,
            Action = action,
            IpAddress = _contextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };

        await _repository.SaveAsync(entry);

        _logger.LogInformation(
            "AUDIT: User {UserId} performed {Action} on {ObjectType}/{RecordId}",
            userId, action, objectType, recordId);
    }
}
```

### Data Retention

```csharp
public class DataRetentionService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Use Bulk API for efficient deletion
            var bulkService = _serviceProvider.GetRequiredService<IBulkService>();

            // Find records older than retention period
            var cutoffDate = DateTime.UtcNow.AddDays(-90);
            var query = SoqlBuilder.From("ArchivedRecord__c")
                .Select("Id")
                .WhereCondition(SoqlCondition.LessThan("CreatedDate", cutoffDate))
                .Build();

            var records = await _dataService.QueryAllAsync(query);

            if (records.Any())
            {
                await bulkService.DeleteAsync("ArchivedRecord__c", records.Select(r => r["Id"]!.ToString()!));
                _logger.LogInformation("Deleted {Count} archived records", records.Count);
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
```

---

## Disaster Recovery

### Backup Strategy

```
Backup Components:
├── Application Configuration
│   ├── appsettings.json (non-sensitive)
│   ├── dynamic_ui.json (UI configuration)
│   └── Infrastructure as Code (Terraform/ARM)
├── Secrets (stored in vault)
│   ├── ClientId / ClientSecret
│   ├── JWT Private Keys
│   └── Redis connection strings
├── Redis Data
│   ├── Tokens (ephemeral - will be refreshed)
│   ├── Sessions (ephemeral - users re-login)
│   └── Schema cache (ephemeral - auto-repopulated)
└── Salesforce Data
    └── Managed by Salesforce backup policies
```

### Recovery Procedures

```csharp
// Cache warm-up on startup
public class CacheWarmupService : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming up schema cache...");

        var objects = new[] { "Account", "Contact", "Opportunity", "Lead", "Case" };

        foreach (var obj in objects)
        {
            try
            {
                await _schemaService.GetDescribeAsync(obj, cancellationToken);
                _logger.LogDebug("Cached schema for {Object}", obj);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache schema for {Object}", obj);
            }
        }

        _logger.LogInformation("Schema cache warm-up complete");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### Failover Configuration

```json
{
  "ConnectionStrings": {
    "Redis": "redis-primary.company.com:6380,redis-secondary.company.com:6380,ssl=true,abortConnect=false"
  },

  "Salesforce": {
    "Domain": "https://login.salesforce.com",
    "FallbackDomain": "https://login.salesforce.com"
  }
}
```

---

## Multi-Environment Strategy

### Environment Configuration

```
Environment Structure:
├── Development
│   ├── Salesforce: Developer Edition / Scratch Org
│   ├── Redis: Local Docker
│   ├── Secrets: User Secrets
│   └── Connected App: Dev-only, relaxed IP
├── Staging
│   ├── Salesforce: Sandbox (Full/Partial)
│   ├── Redis: Azure Cache for Redis (Basic)
│   ├── Secrets: Key Vault (Staging)
│   └── Connected App: Pre-prod settings
├── UAT
│   ├── Salesforce: Sandbox (Partial)
│   ├── Redis: Azure Cache for Redis (Standard)
│   ├── Secrets: Key Vault (UAT)
│   └── Connected App: Production-like
└── Production
    ├── Salesforce: Production Org
    ├── Redis: Azure Cache for Redis (Premium, Clustered)
    ├── Secrets: Key Vault (Production, HSM-backed)
    └── Connected App: Full security, IP restricted
```

### Per-Environment Configuration

```json
// appsettings.Development.json
{
  "Salesforce": {
    "Domain": "https://test.salesforce.com",
    "EnableDebugLogging": true,
    "CacheProvider": "Memory"
  }
}

// appsettings.Production.json
{
  "Salesforce": {
    "Domain": "https://login.salesforce.com",
    "EnableDebugLogging": false,
    "CacheProvider": "Distributed",
    "CacheKeyPrefix": "PROD_SF_"
  }
}
```

### Feature Flags

```csharp
// Use feature flags for gradual rollouts
public class FeatureFlags
{
    public bool UseNewQueryEngine { get; set; }
    public bool EnableBulkApiV2 { get; set; }
    public bool EnableCompositeGraph { get; set; }
}

// Usage
if (_featureFlags.UseNewQueryEngine)
{
    await _newQueryService.ExecuteAsync(query);
}
else
{
    await _legacyQueryService.ExecuteAsync(query);
}
```

---

## Operational Runbook

### Pre-Deployment Checklist

```markdown
## Deployment Checklist

### Configuration
- [ ] appsettings.{Environment}.json reviewed
- [ ] All secrets in vault (none in config files)
- [ ] Connection strings verified
- [ ] API version consistent across services

### Security
- [ ] Connected App scopes minimized
- [ ] IP restrictions configured
- [ ] Certificate expiry checked (JWT)
- [ ] FLS enforcement enabled

### Infrastructure
- [ ] Redis cluster healthy
- [ ] Database migrations applied
- [ ] Health endpoints accessible
- [ ] Load balancer configured

### Testing
- [ ] Integration tests passed
- [ ] Security scan passed
- [ ] Performance test completed
- [ ] Rollback procedure tested
```

### Incident Response

```markdown
## Salesforce Integration Incident Response

### Severity Levels
- **P1 (Critical)**: Complete integration failure, all users affected
- **P2 (High)**: Partial failure, significant user impact
- **P3 (Medium)**: Degraded performance, some features affected
- **P4 (Low)**: Minor issues, workarounds available

### Triage Steps

1. **Identify Scope**
   - Check health endpoints: /health, /health/salesforce
   - Review recent logs for errors
   - Check Salesforce status: status.salesforce.com

2. **Common Issues**
   - **401 Unauthorized**: Token expired, check token refresh
   - **429 Too Many Requests**: Rate limited, reduce load
   - **503 Service Unavailable**: Salesforce maintenance, wait
   - **Connection Timeout**: Network issue, check connectivity

3. **Escalation Path**
   - L1: Operations team (monitoring alerts)
   - L2: Development team (code issues)
   - L3: Salesforce Support (platform issues)

4. **Recovery Actions**
   - Clear token cache: redis-cli DEL "PROD_SF_token:*"
   - Restart application pool
   - Failover to secondary region
   - Contact Salesforce support
```

### Maintenance Windows

```csharp
// Check for Salesforce maintenance
public class MaintenanceCheckService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var status = await _httpClient.GetFromJsonAsync<SalesforceStatus>(
                "https://api.status.salesforce.com/v1/instances");

            var ourInstance = status?.Instances
                .FirstOrDefault(i => i.Key == _options.InstanceKey);

            if (ourInstance?.MaintenanceWindow != null)
            {
                _logger.LogWarning(
                    "Salesforce maintenance scheduled: {Start} to {End}",
                    ourInstance.MaintenanceWindow.StartTime,
                    ourInstance.MaintenanceWindow.EndTime);

                await _alertService.SendMaintenanceNotificationAsync(ourInstance.MaintenanceWindow);
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

---

## Next Steps

- **Security Deep Dive**: [09-Security.md](09-Security.md) - Comprehensive security guidance
- **Bulk/Composite Operations**: [07-Bulk-Composite-Services.md](07-Bulk-Composite-Services.md) - High-volume data handling
- **Infrastructure**: [16-Backbone-Infrastructure.md](16-Backbone-Infrastructure.md) - Core infrastructure services
- **Authentication**: [02-Authentication.md](02-Authentication.md) - OAuth flows and token management

Session timestamp: 2025-12-25T23:00:00Z
