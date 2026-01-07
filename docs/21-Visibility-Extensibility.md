# Visibility System: Extensibility Guide

The SalesforceCore Visibility System is designed to be open for extension. You can create custom handlers to evaluate requirements that are not covered by the built-in `Role` or `SalesforcePermission` handlers (e.g., Feature Flags, Time-of-Day, IP Allow-listing).

## Creating a Custom Handler

To create a new handler, you must implement the `IVisibilityRequirementHandler` interface.

### 1. Define the Interface
**File:** `src/YourProject/Services/FeatureFlagHandler.cs`

```csharp
using System.Text.Json.Nodes;
using SalesforceCore.Services.Authorization;
using System.Security.Claims;
using System.Threading;

public class FeatureFlagHandler : IVisibilityRequirementHandler
{
    // 1. Define the unique Type string used in appsettings.json
    public string Type => "FeatureFlag";

    private readonly IFeatureManager _featureManager; // Example dependency

    public FeatureFlagHandler(IFeatureManager featureManager)
    {
        _featureManager = featureManager;
    }

    public async Task<bool> HandleAsync(JsonObject settings, ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        // 2. Parse the settings
        var featureName = settings["Feature"]?.ToString();
        if (string.IsNullOrEmpty(featureName))
        {
            return false;
        }

        // 3. Perform the check
        return await _featureManager.IsEnabledAsync(featureName);
    }
}
```

### 2. Register the Handler
Register your custom handler in `Startup.cs` or `Program.cs`. It is recommended to use the `Scoped` lifetime.

```csharp
services.AddScoped<IVisibilityRequirementHandler, FeatureFlagHandler>();
```

### 3. Use in Configuration
Now you can use your new handler type in your policies.

**File:** `appsettings.json`

```json
{
  "Salesforce": {
    "Visibility": {
      "Policies": {
        "NewDashboardAccess": {
          "Requirements": [
            {
              "Type": "FeatureFlag",
              "Settings": { "Feature": "BetaDashboard_v2" }
            },
            {
              "Type": "Role",
              "Settings": { "Role": "BetaTester" }
            }
          ]
        }
      }
    }
  }
}
```

## Advanced Scenarios

### Time-Based Access
Create a `TimeWindowHandler` that checks if the current time is within a configured range.

```json
{
  "Type": "TimeWindow",
  "Settings": {
    "StartHour": 9,
    "EndHour": 17,
    "Days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
  }
}
```

### Environment Check
Create an `EnvironmentHandler` to show debug tools only in Development.

```json
{
  "Type": "Environment",
  "Settings": {
    "Environment": "Development"
  }
}
```

### User Attribute Match
Create a `UserAttributeHandler` to match generic claims.

```json
{
  "Type": "ClaimMatch",
  "Settings": {
    "ClaimType": "Department",
    "Value": "Sales"
  }
}
```

## Operational Notes

- Register custom handlers with DI (`IVisibilityRequirementHandler`) so they are discovered by the evaluator.
- Handlers should be fail-safe: catch and log exceptions, return false to avoid leaking visibility.
- In non-HTTP environments, provide an `IUserContextProvider` implementation so handlers can access the current principal.
