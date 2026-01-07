namespace SalesforceCore.Utilities;

/// <summary>
/// Contains Salesforce conventions, constants, and standard mappings.
/// </summary>
public static class SalesforceConventions
{
    /// <summary>
    /// Field types that cannot be directly queried in SOQL.
    /// </summary>
    public static readonly HashSet<string> NonQueryableFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "location",
        "complexvalue",
        "anytype"
    };

    /// <summary>
    /// Field types that should be excluded from create operations.
    /// </summary>
    public static readonly HashSet<string> NonCreateableFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "address",
        "location",
        "formula",
        "rollup",
        "summary"
    };

    /// <summary>
    /// Override mappings for objects that don't use standard "Name" field.
    /// </summary>
    public static readonly Dictionary<string, string> ObjectNameFieldOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        // Security objects
        { "User", "Name" },
        { "Profile", "Name" },
        { "UserRole", "Name" },
        { "PermissionSet", "Label" },
        { "Group", "Name" },
        { "Queue", "Name" },

        // Standard objects with Name variants
        { "Contact", "Name" },
        { "Lead", "Name" },
        { "Task", "Subject" },
        { "Event", "Subject" },
        { "Case", "CaseNumber" },
        { "Order", "OrderNumber" },
        { "Contract", "ContractNumber" },
        { "Quote", "QuoteNumber" },
        { "Solution", "SolutionName" },
        { "Asset", "Name" },
        { "Product2", "Name" },
        { "Pricebook2", "Name" },
        { "RecordType", "Name" },

        // Content objects
        { "ContentDocument", "Title" },
        { "ContentVersion", "Title" },
        { "Document", "Name" },
        { "Attachment", "Name" },
        { "Note", "Title" },
        { "FeedItem", "Body" },

        // Other standard objects
        { "EmailTemplate", "Name" },
        { "Folder", "Name" },
        { "Report", "Name" },
        { "Dashboard", "Title" },
        { "ListView", "Name" },
        { "ApexClass", "Name" },
        { "ApexTrigger", "Name" },
        { "ApexPage", "Name" },
        { "ApexComponent", "Name" },
        { "StaticResource", "Name" },
        { "CustomTab", "Name" },
        { "CustomField", "DeveloperName" },
        { "CustomObject", "DeveloperName" }
    };

    /// <summary>
    /// Candidate field names to try when looking for a display name field.
    /// Ordered by priority.
    /// </summary>
    public static readonly string[] NameFieldCandidates =
    {
        "Name",
        "Subject",
        "Title",
        "Label",
        "DeveloperName",
        "FullName",
        "Username",
        "Email",
        "CaseNumber",
        "OrderNumber",
        "ContractNumber",
        "QuoteNumber",
        "SolutionName",
        "Body",
        "Description",
        "Id"
    };

    /// <summary>
    /// Mapping of 3-character ID prefixes to object types.
    /// Used for polymorphic lookup resolution.
    /// </summary>
    public static readonly Dictionary<string, string> IdPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core CRM
        { "001", "Account" },
        { "003", "Contact" },
        { "00Q", "Lead" },
        { "006", "Opportunity" },
        { "500", "Case" },
        { "701", "Campaign" },
        { "00T", "Task" },
        { "00U", "Event" },

        // Sales Objects
        { "800", "Contract" },
        { "801", "Order" },
        { "802", "OrderItem" },
        { "0Q0", "Quote" },
        { "0QL", "QuoteLineItem" },

        // Products & Pricing
        { "01t", "Product2" },
        { "01s", "Pricebook2" },
        { "01u", "PricebookEntry" },
        { "02i", "Asset" },

        // Security & Users
        { "005", "User" },
        { "00e", "Profile" },  // Note: UserRole also uses 00E (same prefix, case-insensitive)
        { "0PS", "PermissionSet" },
        { "00G", "Group" },
        { "012", "RecordType" },

        // Content
        { "069", "ContentDocument" },
        { "068", "ContentVersion" },
        { "015", "Document" },
        { "00P", "Attachment" },
        { "002", "Note" },
        { "0D5", "FeedItem" },

        // Other Standard
        { "00D", "Organization" },
        { "0EM", "EmailMessage" },
        { "00l", "Folder" },  // Note: CustomTab also uses 00b (same prefix case-insensitively)
        { "00O", "Report" },
        { "01Z", "Dashboard" },
        { "00B", "ListView" },
        { "01p", "ApexClass" },
        { "01q", "ApexTrigger" },
        { "066", "ApexPage" },
        { "099", "ApexComponent" },
        { "081", "StaticResource" },
        { "00N", "CustomField" },
        { "01I", "CustomObject" },
        { "0Af", "BusinessProcess" },
        { "0ka", "KnowledgeArticle" }
    };

    /// <summary>
    /// Standard CRM objects for categorization.
    /// </summary>
    public static readonly HashSet<string> CrmObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account", "Contact", "Lead", "Opportunity", "Campaign",
        "Case", "Task", "Event", "Contract", "Order", "Asset",
        "Product2", "Pricebook2", "Solution"
    };

    /// <summary>
    /// Security and user-related objects.
    /// </summary>
    public static readonly HashSet<string> SecurityObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "User", "Profile", "UserRole", "PermissionSet", "PermissionSetAssignment",
        "Group", "GroupMember", "LoginHistory", "SetupAuditTrail"
    };

    /// <summary>
    /// Development and code-related objects.
    /// </summary>
    public static readonly HashSet<string> DevelopmentObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "ApexClass", "ApexTrigger", "ApexPage", "ApexComponent",
        "StaticResource", "CustomObject", "CustomField", "CustomTab",
        "ValidationRule", "WorkflowRule", "Flow", "FlowDefinition"
    };

    /// <summary>
    /// Content and file-related objects.
    /// </summary>
    public static readonly HashSet<string> ContentObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContentDocument", "ContentVersion", "ContentDocumentLink",
        "Document", "Attachment", "Note", "ContentWorkspace", "ContentFolder"
    };

    /// <summary>
    /// Object patterns that indicate system/internal objects.
    /// </summary>
    public static readonly string[] SystemObjectPatterns =
    {
        "__History", "__Share", "__Feed", "__ChangeEvent",
        "Setup", "Cron", "Async", "Batch", "Apex", "Login",
        "Session", "Permission", "Org", "Network", "Auth"
    };

    /// <summary>
    /// Default search fields for common objects.
    /// </summary>
    public static readonly Dictionary<string, string[]> ObjectSearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Account", new[] { "Name", "AccountNumber", "Phone", "Website" } },
        { "Contact", new[] { "Name", "FirstName", "LastName", "Email", "Phone", "Title" } },
        { "Lead", new[] { "Name", "FirstName", "LastName", "Email", "Phone", "Company" } },
        { "Opportunity", new[] { "Name", "StageName", "Type" } },
        { "Case", new[] { "CaseNumber", "Subject", "Description" } },
        { "User", new[] { "Name", "Username", "Email", "Title" } },
        { "Product2", new[] { "Name", "ProductCode", "Description" } },
        { "Task", new[] { "Subject", "Description" } },
        { "Event", new[] { "Subject", "Description", "Location" } }
    };

    /// <summary>
    /// Default context fields to display in lookup results.
    /// </summary>
    public static readonly Dictionary<string, string[]> ObjectContextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Account", new[] { "Industry", "Type", "BillingCity" } },
        { "Contact", new[] { "Title", "Department", "Account.Name" } },
        { "Lead", new[] { "Company", "Status", "Industry" } },
        { "Opportunity", new[] { "StageName", "Amount", "CloseDate" } },
        { "Case", new[] { "Status", "Priority", "Account.Name" } },
        { "User", new[] { "Title", "Department", "Email" } },
        { "Product2", new[] { "ProductCode", "Family" } }
    };

    /// <summary>
    /// Default icons for object categories.
    /// </summary>
    public static readonly Dictionary<string, string> CategoryIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        { "CRM", "fas fa-briefcase" },
        { "Security & Users", "fas fa-user-shield" },
        { "Development & Code", "fas fa-code" },
        { "Files & Content", "fas fa-file-alt" },
        { "System Internals", "fas fa-cogs" },
        { "Custom Objects", "fas fa-cube" },
        { "Managed Packages", "fas fa-plug" },
        { "Standard Objects", "fas fa-database" }
    };

    /// <summary>
    /// Resolves object type from Salesforce ID prefix.
    /// </summary>
    /// <param name="id">Salesforce record ID.</param>
    /// <returns>Object type or null if unknown.</returns>
    public static string? GetObjectTypeFromId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length < 3)
        {
            return null;
        }

        var prefix = id.Substring(0, 3);
        return IdPrefixes.TryGetValue(prefix, out var objectType) ? objectType : null;
    }

    /// <summary>
    /// Categorizes an object by its API name.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <returns>Category name.</returns>
    public static string CategorizeObject(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return "Standard Objects";
        }

        // Managed package objects
        if (objectName.Contains("__") && !objectName.EndsWith("__c"))
        {
            return "Managed Packages";
        }

        // Custom objects
        if (objectName.EndsWith("__c"))
        {
            return "Custom Objects";
        }

        // CRM objects
        if (CrmObjects.Contains(objectName))
        {
            return "CRM";
        }

        // Security objects
        if (SecurityObjects.Contains(objectName))
        {
            return "Security & Users";
        }

        // Development objects
        if (DevelopmentObjects.Contains(objectName))
        {
            return "Development & Code";
        }

        // Content objects
        if (ContentObjects.Contains(objectName))
        {
            return "Files & Content";
        }

        // System objects by pattern
        if (SystemObjectPatterns.Any(p => objectName.Contains(p)))
        {
            return "System Internals";
        }

        return "Standard Objects";
    }

    /// <summary>
    /// Gets the default icon for an object or category.
    /// </summary>
    /// <param name="objectName">Object API name.</param>
    /// <returns>FontAwesome icon class.</returns>
    public static string GetDefaultIcon(string objectName)
    {
        var category = CategorizeObject(objectName);
        return CategoryIcons.TryGetValue(category, out var icon) ? icon : "fas fa-cube";
    }
}
