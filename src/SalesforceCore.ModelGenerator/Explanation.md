# Model Generator Project Explanation

## 1. Overview
The `src/SalesforceCore.ModelGenerator` project is a Command Line Interface (CLI) tool designed to scaffold strongly-typed C# models from a Salesforce organization. Instead of manually writing C# classes and guessing field names, developers use this tool to "reverse engineer" their Salesforce schema into code.

## 2. Key Features

### Commands
- **`generate`**: The core command. Fetches metadata for specified objects (e.g., `Account`, `Contact`) and writes `.cs` files to disk.
- **`list`**: Lists all available SObjects in the org (supports filtering and wildcards).
- **`describe`**: dumps the raw metadata for an object to the console, useful for debugging schema issues.

### Smart Code Generation
- **Type Mapping**: Automatically converts Salesforce types to their C# equivalents:
    - `datetime` -> `DateTimeOffset`
    - `currency` -> `decimal`
    - `picklist` -> `string` (with `[SalesforcePicklist]` attributes containing valid values)
- **Attribute Decoration**: Adds `[SalesforceObject]`, `[SalesforceField]`, and `[SalesforceLookup]` attributes. This ensures that even if the C# property name is sanitized (e.g., `Class` -> `@Class`), the underlying API name is preserved.
- **Null Safety**: Generates nullable types (`string?`, `int?`) by default (`#nullable enable`).

## 3. Architecture
- **Dependency Injection**: Even though it's a console app, it sets up a minimal DI container to reuse the robust `ISalesforceClient` from the core library.
- **Static Auth**: Uses `StaticTokenProvider` because CLI tools typically run with a fixed Access Token (or a script that fetches one), rather than an interactive OAuth flow.

## 4. Usage Example
```bash
# Generate models for Account and Contact in the ./Models directory
dotnet run -- generate Account Contact --output ./Models --namespace MyApp.Salesforce
```
