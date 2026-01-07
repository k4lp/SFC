using System.Text.Json.Nodes;

namespace SalesforceCore.Models.Configuration;

/// <summary>
/// Configuration options for the Atomic View Visibility System.
/// This allows defining reusable, granular visibility policies that can be applied
/// to any UI element (Razor view, Dynamic UI field, Navigation item).
/// </summary>
public class VisibilityOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Salesforce:Visibility";

    /// <summary>
    /// Dictionary of reusable policies.
    /// Key is the Policy Name (e.g., "CanEditSensitiveData", "IsManagerAndCanCreateAccount").
    /// </summary>
    public Dictionary<string, VisibilityPolicy> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Represents a named visibility policy composed of one or more requirements.
/// </summary>
public class VisibilityPolicy
{
    /// <summary>
    /// The strategy for combining requirements.
    /// "All" = All requirements must pass (AND).
    /// "Any" = At least one requirement must pass (OR).
    /// Default: "All".
    /// </summary>
    public string Strategy { get; set; } = "All";

    /// <summary>
    /// The list of atomic requirements that make up this policy.
    /// </summary>
    public List<VisibilityRequirementConfig> Requirements { get; set; } = new();
}

/// <summary>
/// Configuration for a single atomic visibility requirement.
/// </summary>
public class VisibilityRequirementConfig
{
    /// <summary>
    /// The discriminator for the handler (e.g., "Role", "SalesforcePermission").
    /// This must match the 'Type' property of a registered IVisibilityRequirementHandler.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Generic settings for the handler.
    /// This allows each handler to define its own configuration structure.
    /// </summary>
    public JsonObject Settings { get; set; } = new JsonObject();
}
