using System.CommandLine;
using SalesforceCore.ModelGenerator;
using SalesforceCore.Models.Errors;

// Create the root command
var rootCommand = new RootCommand("Salesforce Model Generator - Generate C# classes from Salesforce object metadata")
{
    Name = "sf-gen"
};

// Add global options
var instanceUrlOption = new Option<string>(
    aliases: ["--instance-url", "-u"],
    description: "Salesforce instance URL (e.g., https://mycompany.salesforce.com)")
{
    IsRequired = false
};

var accessTokenOption = new Option<string>(
    aliases: ["--token", "-t"],
    description: "Salesforce access token for authentication")
{
    IsRequired = false
};

var outputDirOption = new Option<DirectoryInfo>(
    aliases: ["--output", "-o"],
    getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory()),
    description: "Output directory for generated files");

var namespaceOption = new Option<string>(
    aliases: ["--namespace", "-n"],
    getDefaultValue: () => "SalesforceModels",
    description: "Namespace for generated classes");

var apiVersionOption = new Option<string>(
    aliases: ["--api-version", "-v"],
    getDefaultValue: () => "v62.0",
    description: "Salesforce API version");

// Generate command - generate models from Salesforce
var generateCommand = new Command("generate", "Generate C# model classes from Salesforce objects")
{
    instanceUrlOption,
    accessTokenOption,
    outputDirOption,
    namespaceOption,
    apiVersionOption
};

var objectsArgument = new Argument<string[]>(
    name: "objects",
    description: "Salesforce object names to generate (e.g., Account Contact Lead). Use '*' for all standard objects.")
{
    Arity = ArgumentArity.OneOrMore
};
generateCommand.AddArgument(objectsArgument);

var includeCustomOption = new Option<bool>(
    aliases: ["--include-custom", "-c"],
    getDefaultValue: () => false,
    description: "Include custom objects when using '*'");
generateCommand.AddOption(includeCustomOption);

var attributesOnlyOption = new Option<bool>(
    aliases: ["--attributes-only", "-a"],
    getDefaultValue: () => false,
    description: "Generate only with SalesforceCore attributes (no JSON attributes)");
generateCommand.AddOption(attributesOnlyOption);

var nullableOption = new Option<bool>(
    aliases: ["--nullable"],
    getDefaultValue: () => true,
    description: "Use nullable reference types");
generateCommand.AddOption(nullableOption);

generateCommand.SetHandler(async (context) =>
{
    var instanceUrl = context.ParseResult.GetValueForOption(instanceUrlOption);
    var token = context.ParseResult.GetValueForOption(accessTokenOption);
    var outputDir = context.ParseResult.GetValueForOption(outputDirOption)!;
    var ns = context.ParseResult.GetValueForOption(namespaceOption)!;
    var apiVersion = context.ParseResult.GetValueForOption(apiVersionOption)!;
    var objects = context.ParseResult.GetValueForArgument(objectsArgument);
    var includeCustom = context.ParseResult.GetValueForOption(includeCustomOption);
    var attributesOnly = context.ParseResult.GetValueForOption(attributesOnlyOption);
    var nullable = context.ParseResult.GetValueForOption(nullableOption);

    // Check for environment variables if not provided
    instanceUrl ??= Environment.GetEnvironmentVariable("SF_INSTANCE_URL");
    token ??= Environment.GetEnvironmentVariable("SF_ACCESS_TOKEN");

    if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(token))
    {
        Console.WriteLine("Error: Instance URL and access token are required.");
        Console.WriteLine("Provide via --instance-url and --token options, or set SF_INSTANCE_URL and SF_ACCESS_TOKEN environment variables.");
        context.ExitCode = 1;
        return;
    }

    using var generator = new ModelGenerator(instanceUrl, token, apiVersion);
    var options = new GeneratorOptions
    {
        Namespace = ns,
        OutputDirectory = outputDir.FullName,
        IncludeCustomObjects = includeCustom,
        AttributesOnly = attributesOnly,
        UseNullableTypes = nullable
    };

    try
    {
        await generator.GenerateAsync(objects, options);
        Console.WriteLine($"Models generated successfully in {outputDir.FullName}");
    }
    catch (SalesforceException ex)
    {
        Console.WriteLine($"Salesforce API Error ({ex.HttpStatusCode}): {ex.Message}");
        context.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        context.ExitCode = 1;
    }
});

rootCommand.AddCommand(generateCommand);

// List command - list available objects
var listCommand = new Command("list", "List available Salesforce objects")
{
    instanceUrlOption,
    accessTokenOption,
    apiVersionOption
};

var filterOption = new Option<string?>(
    aliases: ["--filter", "-f"],
    description: "Filter objects by name pattern (supports wildcards)");
listCommand.AddOption(filterOption);

var customOnlyOption = new Option<bool>(
    aliases: ["--custom-only"],
    getDefaultValue: () => false,
    description: "Show only custom objects");
listCommand.AddOption(customOnlyOption);

listCommand.SetHandler(async (context) =>
{
    var instanceUrl = context.ParseResult.GetValueForOption(instanceUrlOption);
    var token = context.ParseResult.GetValueForOption(accessTokenOption);
    var apiVersion = context.ParseResult.GetValueForOption(apiVersionOption)!;
    var filter = context.ParseResult.GetValueForOption(filterOption);
    var customOnly = context.ParseResult.GetValueForOption(customOnlyOption);

    instanceUrl ??= Environment.GetEnvironmentVariable("SF_INSTANCE_URL");
    token ??= Environment.GetEnvironmentVariable("SF_ACCESS_TOKEN");

    if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(token))
    {
        Console.WriteLine("Error: Instance URL and access token are required.");
        context.ExitCode = 1;
        return;
    }

    using var generator = new ModelGenerator(instanceUrl, token, apiVersion);

    try
    {
        var objects = await generator.ListObjectsAsync(filter, customOnly);
        Console.WriteLine($"Found {objects.Count} objects:\n");
        foreach (var obj in objects.OrderBy(o => o.Name))
        {
            var customFlag = obj.Custom ? " (custom)" : "";
            Console.WriteLine($"  {obj.Name}{customFlag} - {obj.Label}");
        }
    }
    catch (SalesforceException ex)
    {
        Console.WriteLine($"Salesforce API Error ({ex.HttpStatusCode}): {ex.Message}");
        context.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        context.ExitCode = 1;
    }
});

rootCommand.AddCommand(listCommand);

// Describe command - show details about an object
var describeCommand = new Command("describe", "Show detailed information about a Salesforce object")
{
    instanceUrlOption,
    accessTokenOption,
    apiVersionOption
};

var objectArgument = new Argument<string>("object", "The Salesforce object name to describe");
describeCommand.AddArgument(objectArgument);

var fieldsOnlyOption = new Option<bool>(
    aliases: ["--fields-only"],
    getDefaultValue: () => false,
    description: "Show only field information");
describeCommand.AddOption(fieldsOnlyOption);

describeCommand.SetHandler(async (context) =>
{
    var instanceUrl = context.ParseResult.GetValueForOption(instanceUrlOption);
    var token = context.ParseResult.GetValueForOption(accessTokenOption);
    var apiVersion = context.ParseResult.GetValueForOption(apiVersionOption)!;
    var objectName = context.ParseResult.GetValueForArgument(objectArgument);
    var fieldsOnly = context.ParseResult.GetValueForOption(fieldsOnlyOption);

    instanceUrl ??= Environment.GetEnvironmentVariable("SF_INSTANCE_URL");
    token ??= Environment.GetEnvironmentVariable("SF_ACCESS_TOKEN");

    if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(token))
    {
        Console.WriteLine("Error: Instance URL and access token are required.");
        context.ExitCode = 1;
        return;
    }

    using var generator = new ModelGenerator(instanceUrl, token, apiVersion);

    try
    {
        await generator.DescribeObjectAsync(objectName, fieldsOnly);
    }
    catch (SalesforceException ex)
    {
        Console.WriteLine($"Salesforce API Error ({ex.HttpStatusCode}): {ex.Message}");
        context.ExitCode = 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        context.ExitCode = 1;
    }
});

rootCommand.AddCommand(describeCommand);

// Run the CLI
return await rootCommand.InvokeAsync(args);
