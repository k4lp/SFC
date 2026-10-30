using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalesforceCore.Extensions;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Metadata;

namespace SalesforceCore.ModelGenerator;

/// <summary>
/// Generates C# model classes from Salesforce object metadata.
/// Uses the core SalesforceClient with centralized retry policies and error handling.
/// </summary>
public class ModelGenerator : IDisposable
{
    private readonly ISalesforceClient _client;
    private readonly ServiceProvider? _serviceProvider;
    private bool _disposed;

    /// <summary>
    /// Creates a new ModelGenerator.
    /// </summary>
    /// <param name="instanceUrl">Salesforce instance URL.</param>
    /// <param name="accessToken">OAuth access token.</param>
    /// <param name="apiVersion">API version (e.g., "v62.0").</param>
    public ModelGenerator(string instanceUrl, string accessToken, string apiVersion)
    {
        // Setup DI container with the core SalesforceClient
        var services = new ServiceCollection();

        // Configure SalesforceCore without default auth (we provide our own token provider)
        services.AddSalesforceCoreWithoutAuth(options =>
        {
            options.ApiVersion = apiVersion;
            options.MaxRetries = 3;
            options.RetryBaseDelay = TimeSpan.FromSeconds(1);
        });

        // Register our static token provider
        services.AddScoped<ITokenProvider>(_ => new StaticTokenProvider(accessToken, instanceUrl));

        // Build provider
        _serviceProvider = services.BuildServiceProvider();
        _client = _serviceProvider.GetRequiredService<ISalesforceClient>();
    }

    /// <summary>
    /// Creates a generator over an existing <see cref="ISalesforceClient"/> (any auth, handler pipeline or test fake).
    /// The caller owns the client's lifetime.
    /// </summary>
    public ModelGenerator(ISalesforceClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <summary>
    /// Generates model classes for the specified Salesforce objects.
    /// </summary>
    public async Task GenerateAsync(string[] objectNames, GeneratorOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectNames);
        ArgumentNullException.ThrowIfNull(options);
        if (!ValidNamespace.IsMatch(options.Namespace ?? ""))
            throw new ArgumentException($"Invalid namespace '{options.Namespace}'.", nameof(options));

        // Ensure output directory exists
        Directory.CreateDirectory(options.OutputDirectory);

        // Handle wildcard
        if (objectNames.Contains("*"))
        {
            var allObjects = await ListObjectsAsync(null, false, cancellationToken);
            objectNames = allObjects
                .Where(o => o.Queryable && (options.IncludeCustomObjects || !o.Custom))
                .Select(o => o.Name)
                .ToArray();

            Console.WriteLine($"Generating models for {objectNames.Length} objects...");
        }

        foreach (var objectName in objectNames)
        {
            try
            {
                Console.Write($"Generating {objectName}...");
                var describe = await GetDescribeAsync(objectName, cancellationToken);
                var code = GenerateClass(describe, options);

                var fileName = $"{SanitizeClassName(objectName)}.cs";
                var filePath = GeneratorEscaping.SafeOutputPath(options.OutputDirectory, fileName);
                await File.WriteAllTextAsync(filePath, code, cancellationToken);

                Console.WriteLine(" Done.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($" Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lists available Salesforce objects.
    /// </summary>
    public async Task<List<SObjectInfo>> ListObjectsAsync(string? filter, bool customOnly, CancellationToken cancellationToken = default)
    {
        var result = await _client.GetAsync<GlobalDescribeResult>("/sobjects/", cancellationToken);

        var objects = result.SObjects
            .Where(o => !customOnly || o.Custom)
            .ToList();

        if (!string.IsNullOrEmpty(filter))
        {
            var pattern = "^" + Regex.Escape(filter).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);
            objects = objects.Where(o => regex.IsMatch(o.Name)).ToList();
        }

        return objects;
    }

    /// <summary>
    /// Describes a Salesforce object with field details.
    /// </summary>
    public async Task DescribeObjectAsync(string objectName, bool fieldsOnly, CancellationToken cancellationToken = default)
    {
        var describe = await GetDescribeAsync(objectName, cancellationToken);

        if (!fieldsOnly)
        {
            Console.WriteLine($"\n=== {describe.Label} ({describe.Name}) ===\n");
            Console.WriteLine($"  Custom: {describe.Custom}");
            Console.WriteLine($"  Queryable: {describe.Queryable}");
            Console.WriteLine($"  Createable: {describe.Createable}");
            Console.WriteLine($"  Updateable: {describe.Updateable}");
            Console.WriteLine($"  Deletable: {describe.Deletable}");
            Console.WriteLine($"  Fields: {describe.Fields.Count}");
            Console.WriteLine();
        }

        Console.WriteLine("Fields:");
        Console.WriteLine(new string('-', 100));
        Console.WriteLine($"{"Name",-40} {"Type",-15} {"Label",-30} {"Flags"}");
        Console.WriteLine(new string('-', 100));

        foreach (var field in describe.Fields.OrderBy(f => f.Name))
        {
            var flags = new List<string>();
            if (field.Nillable) flags.Add("nullable");
            if (field.Createable) flags.Add("create");
            if (field.Updateable) flags.Add("update");
            if (field.Custom) flags.Add("custom");
            if (field.ExternalId) flags.Add("extId");

            Console.WriteLine($"{field.Name,-40} {field.Type,-15} {field.Label,-30} {string.Join(",", flags)}");
        }
    }

    private async Task<SObjectDescribe> GetDescribeAsync(string objectName, CancellationToken cancellationToken)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(objectName ?? "", "^[A-Za-z][A-Za-z0-9_]{0,79}$"))
            throw new ArgumentException($"Invalid sObject name '{objectName}'.", nameof(objectName));
        return await _client.GetAsync<SObjectDescribe>($"/sobjects/{objectName}/describe", cancellationToken);
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _serviceProvider?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private static readonly Regex ValidNamespace = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);

    internal string GenerateClass(SObjectDescribe describe, GeneratorOptions options)
    {
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"// Generated by SalesforceCore.ModelGenerator on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"// Salesforce Object: {GeneratorEscaping.Comment(describe.Name)}");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();

        // Usings
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        if (!options.AttributesOnly)
        {
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Text.Json.Serialization;");
        }
        sb.AppendLine("using SalesforceCore.Attributes;");
        sb.AppendLine();

        // Nullable enable
        if (options.UseNullableTypes)
        {
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
        }

        // Namespace (validated: it is emitted verbatim, so a crafted value could otherwise inject code)
        if (!ValidNamespace.IsMatch(options.Namespace ?? ""))
            throw new ArgumentException($"Invalid namespace '{options.Namespace}'.", nameof(options));
        sb.AppendLine($"namespace {options.Namespace};");
        sb.AppendLine();

        // Class documentation
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Represents the Salesforce {GeneratorEscaping.Xml(describe.Label)} object.");
        if (!string.IsNullOrEmpty(describe.Name) && describe.Name != describe.Label)
        {
            sb.AppendLine($"/// API Name: {GeneratorEscaping.Xml(describe.Name)}");
        }
        sb.AppendLine("/// </summary>");

        // SalesforceObject attribute
        var className = SanitizeClassName(describe.Name);
        if (className != describe.Name)
        {
            sb.AppendLine($"[SalesforceObject(\"{GeneratorEscaping.CSharpString(describe.Name)}\", Queryable = {describe.Queryable.ToString().ToLower()}, Createable = {describe.Createable.ToString().ToLower()}, Updateable = {describe.Updateable.ToString().ToLower()}, Deletable = {describe.Deletable.ToString().ToLower()})]");
        }
        else
        {
            sb.AppendLine($"[SalesforceObject(\"{GeneratorEscaping.CSharpString(describe.Name)}\")]");
        }

        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        // Generate properties
        foreach (var field in describe.Fields.OrderBy(f => f.Name == "Id" ? 0 : 1).ThenBy(f => f.Name))
        {
            GenerateProperty(sb, field, options);
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private void GenerateProperty(StringBuilder sb, SObjectField field, GeneratorOptions options)
    {
        var propertyName = SanitizePropertyName(field.Name);
        var csharpType = MapSalesforceTypeToCSharp(field, options.UseNullableTypes);

        // Property documentation
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// {GeneratorEscaping.Xml(field.Label)}");
        if (field.InlineHelpText != null)
        {
            sb.AppendLine($"    /// <para>{GeneratorEscaping.Xml(field.InlineHelpText)}</para>");
        }
        sb.AppendLine("    /// </summary>");

        // Attributes
        var needsSalesforceFieldAttr = propertyName != field.Name ||
                                        !field.Createable ||
                                        !field.Updateable ||
                                        field.Length > 0;

        if (needsSalesforceFieldAttr)
        {
            var attrParts = new List<string> { $"\"{GeneratorEscaping.CSharpString(field.Name)}\"" };

            if (!field.Createable && field.Name != "Id")
                attrParts.Add("Createable = false");
            if (!field.Updateable && field.Name != "Id")
                attrParts.Add("Updateable = false");
            if (!field.Createable && !field.Updateable && field.Name != "Id")
                attrParts.Add("ReadOnly = true");
            if (field.Length > 0)
                attrParts.Add($"MaxLength = {field.Length}");
            if (field.Precision > 0)
                attrParts.Add($"Precision = {field.Precision}");
            if (field.Scale > 0)
                attrParts.Add($"Scale = {field.Scale}");
            if (!field.Nillable && field.Name != "Id")
                attrParts.Add("Required = true");
            if (field.Type == "reference" && field.ReferenceTo?.Count > 0)
            {
                attrParts.Add($"ReferenceTo = \"{GeneratorEscaping.CSharpString(field.ReferenceTo[0])}\"");
                if (field.RelationshipName != null)
                    attrParts.Add($"RelationshipName = \"{GeneratorEscaping.CSharpString(field.RelationshipName)}\"");
            }

            sb.AppendLine($"    [SalesforceField({string.Join(", ", attrParts)})]");
        }

        // JSON attribute (unless attributes only mode)
        if (!options.AttributesOnly && propertyName != field.Name)
        {
            // System.Text.Json attribute: the file imports System.Text.Json.Serialization, so the old Newtonsoft
            // [JsonProperty] did not compile in generated code.
            sb.AppendLine($"    [JsonPropertyName(\"{GeneratorEscaping.CSharpString(field.Name)}\")]");
        }

        // Special attributes
        if (field.Name == "Id")
        {
            sb.AppendLine("    [SalesforceId]");
        }
        else if (field.ExternalId)
        {
            sb.AppendLine("    [SalesforceExternalId]");
        }

        // Lookup attribute
        if (field.Type == "reference" && field.ReferenceTo?.Count > 0)
        {
            var refTo = field.ReferenceTo[0];
            var rel = string.IsNullOrEmpty(field.RelationshipName)
                ? ""
                : $", RelationshipName = \"{GeneratorEscaping.CSharpString(field.RelationshipName)}\"";
            var poly = field.ReferenceTo.Count > 1 ? ", Polymorphic = true" : "";
            sb.AppendLine($"    [SalesforceLookup(\"{GeneratorEscaping.CSharpString(refTo)}\"{rel}{poly})]");
        }

        // Picklist attribute
        if ((field.Type == "picklist" || field.Type == "multipicklist") && field.PicklistValues?.Count > 0)
        {
            var values = field.PicklistValues
                .Where(p => p.Active)
                .Select(p => $"\"{GeneratorEscaping.CSharpString(p.Value)}\"")
                .Take(20); // Limit to first 20 values
            var multiSelect = field.Type == "multipicklist" ? ", MultiSelect = true" : "";
            sb.AppendLine($"    [SalesforcePicklist({string.Join(", ", values)}{multiSelect})]");
        }

        // Property declaration
        sb.AppendLine($"    public {csharpType} {propertyName} {{ get; set; }}");
    }

    private static string MapSalesforceTypeToCSharp(SObjectField field, bool nullable)
    {
        var nullableSuffix = nullable && field.Nillable ? "?" : "";

        return field.Type.ToLowerInvariant() switch
        {
            "id" => "string" + (nullable && field.Name != "Id" ? "?" : ""),
            "string" or "textarea" or "phone" or "url" or "email" or "encryptedstring" => "string" + (nullable ? "?" : ""),
            "picklist" or "combobox" => "string" + (nullable ? "?" : ""),
            "multipicklist" => "string" + (nullable ? "?" : ""), // Semicolon-separated values
            "boolean" => "bool" + nullableSuffix,
            "int" => "int" + nullableSuffix,
            "double" or "currency" or "percent" => "decimal" + nullableSuffix,
            "date" => "DateOnly" + nullableSuffix,
            "datetime" => "DateTimeOffset" + nullableSuffix,
            "time" => "TimeOnly" + nullableSuffix,
            "base64" => "byte[]" + (nullable ? "?" : ""),
            "reference" => "string" + (nullable ? "?" : ""), // ID of related record
            "location" => "string" + (nullable ? "?" : ""), // Geolocation as JSON string
            "address" => "string" + (nullable ? "?" : ""), // Compound address as JSON
            "anytype" => "object" + (nullable ? "?" : ""),
            "long" => "long" + nullableSuffix,
            _ => "string" + (nullable ? "?" : "") // Default to string for unknown types
        };
    }

    private static string SanitizeClassName(string name)
    {
        // Remove __c, __mdt, etc. suffixes for cleaner names (optional)
        // For now, keep them as-is but make C# compatible
        var sanitized = name;

        // Replace invalid characters
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9_]", "_");

        // Ensure doesn't start with a number
        if (char.IsDigit(sanitized[0]))
        {
            sanitized = "_" + sanitized;
        }

        return sanitized;
    }

    private static string SanitizePropertyName(string name)
    {
        var sanitized = SanitizeClassName(name);

        // Handle C# reserved keywords
        var reserved = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        if (reserved.Contains(sanitized.ToLower()))
        {
            sanitized = "@" + sanitized;
        }

        return sanitized;
    }

}

/// <summary>
/// Options for model generation.
/// </summary>
public class GeneratorOptions
{
    /// <summary>
    /// Namespace for generated classes.
    /// </summary>
    public string Namespace { get; set; } = "SalesforceModels";

    /// <summary>
    /// Output directory for generated files.
    /// </summary>
    public string OutputDirectory { get; set; } = ".";

    /// <summary>
    /// Include custom objects when using wildcard.
    /// </summary>
    public bool IncludeCustomObjects { get; set; }

    /// <summary>
    /// Generate only SalesforceCore attributes (no JSON attributes).
    /// </summary>
    public bool AttributesOnly { get; set; }

    /// <summary>
    /// Use nullable reference types.
    /// </summary>
    public bool UseNullableTypes { get; set; } = true;
}



/// <summary>
/// Escaping helpers for emitting untrusted Salesforce metadata (names, labels, help text, picklist values)
/// into generated C# source without allowing code injection or writes outside the output directory.
/// </summary>
public static class GeneratorEscaping
{
    /// <summary>Escapes text for an XML doc comment and collapses line breaks so it cannot leave the comment.</summary>
    public static string Xml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return SingleLine(text)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    /// <summary>Makes text safe for a single-line // comment.</summary>
    public static string Comment(string? text) => string.IsNullOrEmpty(text) ? "" : SingleLine(text);

    /// <summary>Escapes text for a regular C# string literal (quotes, backslashes, control characters).</summary>
    public static string CSharpString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                case '\u2028': sb.Append("\\u2028"); break;
                case '\u2029': sb.Append("\\u2029"); break;
                case '\u0085': sb.Append("\\u0085"); break;
                default:
                    if (char.IsControl(c)) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Combines the output directory and file name and verifies the result stays inside the directory.
    /// </summary>
    public static string SafeOutputPath(string outputDirectory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName is "." or "..")
        {
            throw new InvalidOperationException($"Refusing to write unsafe file name '{Comment(fileName)}'.");
        }

        var root = Path.GetFullPath(outputDirectory);
        var full = Path.GetFullPath(Path.Combine(root, fileName));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to write outside the output directory: '{Comment(fileName)}'.");
        }

        return full;
    }

    private static string SingleLine(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c is '\r' or '\n' or '\u2028' or '\u2029' or '\u0085' || char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString();
    }
}
