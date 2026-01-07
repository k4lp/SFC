using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using SalesforceCore.Utilities;

namespace SalesforceCore.Models.Data;

/// <summary>
/// Result of executing anonymous Apex code via the Tooling API.
/// </summary>
public class ExecuteAnonymousResult
{
    /// <summary>
    /// Whether the Apex code compiled successfully.
    /// </summary>
    [JsonPropertyName("compiled")]
    public bool Compiled { get; set; }

    /// <summary>
    /// Whether the Apex code executed successfully.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// The line number where a compilation error occurred.
    /// </summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>
    /// The column number where a compilation error occurred.
    /// </summary>
    [JsonPropertyName("column")]
    public int Column { get; set; }

    /// <summary>
    /// Compilation error message if compilation failed.
    /// </summary>
    [JsonPropertyName("compileProblem")]
    public string? CompileProblem { get; set; }

    /// <summary>
    /// Exception message if execution failed.
    /// </summary>
    [JsonPropertyName("exceptionMessage")]
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Stack trace if execution failed.
    /// </summary>
    [JsonPropertyName("exceptionStackTrace")]
    public string? ExceptionStackTrace { get; set; }

    /// <summary>
    /// Gets the combined error message from compilation or execution errors.
    /// </summary>
    public string? GetErrorMessage()
    {
        if (!Compiled && !string.IsNullOrEmpty(CompileProblem))
        {
            return $"Compilation error at line {Line}, column {Column}: {CompileProblem}";
        }

        if (!Success && !string.IsNullOrEmpty(ExceptionMessage))
        {
            return $"Execution error: {ExceptionMessage}";
        }

        return null;
    }
}

/// <summary>
/// Represents an Apex Class metadata record from the Tooling API.
/// </summary>
public class ApexClassInfo
{
    /// <summary>
    /// The Apex Class ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The Apex Class name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The namespace prefix (if in a managed package).
    /// </summary>
    [JsonPropertyName("NamespacePrefix")]
    public string? NamespacePrefix { get; set; }

    /// <summary>
    /// The Apex Class body (source code).
    /// </summary>
    [JsonPropertyName("Body")]
    public string? Body { get; set; }

    /// <summary>
    /// The API version this class was written for.
    /// </summary>
    [JsonPropertyName("ApiVersion")]
    public double ApiVersion { get; set; }

    /// <summary>
    /// The status of the class (Active, Deleted).
    /// </summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>
    /// Whether this is a test class.
    /// </summary>
    [JsonPropertyName("IsValid")]
    public bool IsValid { get; set; }

    /// <summary>
    /// Length of the class body in bytes.
    /// </summary>
    [JsonPropertyName("LengthWithoutComments")]
    public int LengthWithoutComments { get; set; }

    /// <summary>
    /// Date the class was created.
    /// </summary>
    [JsonPropertyName("CreatedDate")]
    [JsonConverter(typeof(SalesforceNullableDateTimeConverter))]
    public DateTime? CreatedDate { get; set; }

    /// <summary>
    /// Date the class was last modified.
    /// </summary>
    [JsonPropertyName("LastModifiedDate")]
    [JsonConverter(typeof(SalesforceNullableDateTimeConverter))]
    public DateTime? LastModifiedDate { get; set; }
}

/// <summary>
/// Represents an Apex Trigger metadata record from the Tooling API.
/// </summary>
public class ApexTriggerInfo
{
    /// <summary>
    /// The Apex Trigger ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The Apex Trigger name.
    /// </summary>
    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The namespace prefix (if in a managed package).
    /// </summary>
    [JsonPropertyName("NamespacePrefix")]
    public string? NamespacePrefix { get; set; }

    /// <summary>
    /// The SObject this trigger is attached to.
    /// </summary>
    [JsonPropertyName("TableEnumOrId")]
    public string? TableEnumOrId { get; set; }

    /// <summary>
    /// The trigger body (source code).
    /// </summary>
    [JsonPropertyName("Body")]
    public string? Body { get; set; }

    /// <summary>
    /// The API version this trigger was written for.
    /// </summary>
    [JsonPropertyName("ApiVersion")]
    public double ApiVersion { get; set; }

    /// <summary>
    /// The status of the trigger (Active, Inactive, Deleted).
    /// </summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>
    /// Whether the trigger is valid.
    /// </summary>
    [JsonPropertyName("IsValid")]
    public bool IsValid { get; set; }
}

/// <summary>
/// Represents a Validation Rule metadata record from the Tooling API.
/// </summary>
public class ValidationRuleInfo
{
    /// <summary>
    /// The Validation Rule ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The full name of the validation rule (ObjectName.RuleName).
    /// </summary>
    [JsonPropertyName("FullName")]
    public string? FullName { get; set; }

    /// <summary>
    /// The validation rule name.
    /// </summary>
    [JsonPropertyName("ValidationName")]
    public string? ValidationName { get; set; }

    /// <summary>
    /// Whether the validation rule is active.
    /// </summary>
    [JsonPropertyName("Active")]
    public bool Active { get; set; }

    /// <summary>
    /// The error condition formula.
    /// </summary>
    [JsonPropertyName("ErrorConditionFormula")]
    public string? ErrorConditionFormula { get; set; }

    /// <summary>
    /// The error message displayed when validation fails.
    /// </summary>
    [JsonPropertyName("ErrorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The field where the error should display.
    /// </summary>
    [JsonPropertyName("ErrorDisplayField")]
    public string? ErrorDisplayField { get; set; }

    /// <summary>
    /// The entity definition ID (SObject type).
    /// </summary>
    [JsonPropertyName("EntityDefinitionId")]
    public string? EntityDefinitionId { get; set; }
}

/// <summary>
/// Represents a debug log record from the Tooling API.
/// </summary>
public class ApexLogInfo
{
    /// <summary>
    /// The debug log ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The application type (Browser, Application, etc).
    /// </summary>
    [JsonPropertyName("Application")]
    public string? Application { get; set; }

    /// <summary>
    /// Duration of the request in milliseconds.
    /// </summary>
    [JsonPropertyName("DurationMilliseconds")]
    public int DurationMilliseconds { get; set; }

    /// <summary>
    /// The location of the log (SystemLog, ProfilerLog, etc).
    /// </summary>
    [JsonPropertyName("Location")]
    public string? Location { get; set; }

    /// <summary>
    /// Size of the log in bytes.
    /// </summary>
    [JsonPropertyName("LogLength")]
    public int LogLength { get; set; }

    /// <summary>
    /// ID of the user who generated the log.
    /// </summary>
    [JsonPropertyName("LogUserId")]
    public string? LogUserId { get; set; }

    /// <summary>
    /// The operation that generated the log.
    /// </summary>
    [JsonPropertyName("Operation")]
    public string? Operation { get; set; }

    /// <summary>
    /// The request identifier.
    /// </summary>
    [JsonPropertyName("Request")]
    public string? Request { get; set; }

    /// <summary>
    /// Start time of the operation.
    /// </summary>
    [JsonPropertyName("StartTime")]
    [JsonConverter(typeof(SalesforceNullableDateTimeConverter))]
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// Status of the operation (Success, Failure).
    /// </summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }
}

/// <summary>
/// Represents a TraceFlag record for enabling debug logging.
/// </summary>
public class TraceFlagInfo
{
    /// <summary>
    /// The TraceFlag ID.
    /// </summary>
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The debug level ID.
    /// </summary>
    [JsonPropertyName("DebugLevelId")]
    public string? DebugLevelId { get; set; }

    /// <summary>
    /// Expiration date of the trace flag.
    /// </summary>
    [JsonPropertyName("ExpirationDate")]
    [JsonConverter(typeof(SalesforceNullableDateTimeConverter))]
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Log type (USER_DEBUG, DEVELOPER_LOG, CLASS_TRACING).
    /// </summary>
    [JsonPropertyName("LogType")]
    public string? LogType { get; set; }

    /// <summary>
    /// Start date of the trace flag.
    /// </summary>
    [JsonPropertyName("StartDate")]
    [JsonConverter(typeof(SalesforceNullableDateTimeConverter))]
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// ID of the traced entity (User, ApexClass, etc).
    /// </summary>
    [JsonPropertyName("TracedEntityId")]
    public string? TracedEntityId { get; set; }
}

/// <summary>
/// Request object for creating a TraceFlag.
/// </summary>
public class CreateTraceFlagRequest
{
    /// <summary>
    /// The debug level ID.
    /// </summary>
    [JsonPropertyName("DebugLevelId")]
    public string DebugLevelId { get; set; } = string.Empty;

    /// <summary>
    /// Expiration date of the trace flag.
    /// </summary>
    [JsonPropertyName("ExpirationDate")]
    public DateTime ExpirationDate { get; set; }

    /// <summary>
    /// Log type (USER_DEBUG, DEVELOPER_LOG, CLASS_TRACING).
    /// </summary>
    [JsonPropertyName("LogType")]
    public string LogType { get; set; } = "DEVELOPER_LOG";

    /// <summary>
    /// Start date of the trace flag.
    /// </summary>
    [JsonPropertyName("StartDate")]
    public DateTime StartDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// ID of the traced entity (User, ApexClass, etc).
    /// </summary>
    [JsonPropertyName("TracedEntityId")]
    public string TracedEntityId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a symbol table entry from code completion.
/// </summary>
public class SymbolTableEntry
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The type of the symbol.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Access modifiers (public, private, etc).
    /// </summary>
    [JsonPropertyName("modifiers")]
    public List<string>? Modifiers { get; set; }

    /// <summary>
    /// Location in the source code.
    /// </summary>
    [JsonPropertyName("location")]
    public SourceLocation? Location { get; set; }
}

/// <summary>
/// Represents a source code location.
/// </summary>
public class SourceLocation
{
    /// <summary>
    /// Line number.
    /// </summary>
    [JsonPropertyName("line")]
    public int Line { get; set; }

    /// <summary>
    /// Column number.
    /// </summary>
    [JsonPropertyName("column")]
    public int Column { get; set; }
}
