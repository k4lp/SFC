namespace SalesforceCore.Models.Security;

/// <summary>
/// Specifies the type of access being requested for permission checks.
/// Used by UI components to determine visibility based on user permissions.
/// </summary>
public enum AccessMode
{
    /// <summary>
    /// Read access - checks if the user can view the object/field.
    /// For objects: checks if queryable.
    /// For fields: checks Accessible property (FLS read permission).
    /// </summary>
    Read,

    /// <summary>
    /// Create access - checks if the user can create records.
    /// For objects: checks Createable property.
    /// For fields: checks Createable property.
    /// </summary>
    Create,

    /// <summary>
    /// Update access - checks if the user can modify existing records.
    /// For objects: checks Updateable property.
    /// For fields: checks Updateable property.
    /// </summary>
    Update,

    /// <summary>
    /// Delete access - checks if the user can delete records.
    /// For objects: checks Deletable property.
    /// For fields: N/A (delete is object-level only).
    /// </summary>
    Delete
}
