# Tooling Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Tooling` directory provides access to the **Tooling API**. This API is distinct from the standard REST API and is used for metadata management, development tools, and advanced administrative tasks.

## 2. Key Components

### `IToolingService`
**Purpose**: Interact with `/services/data/vXX.0/tooling` endpoints.
**Capabilities**:
- **Execute Anonymous Apex**: `ExecuteAnonymousAsync` allows running arbitrary Apex code snippets. This is powerful for maintenance tasks or complex logic that exists only in snippets.
- **Metadata Querying**: `QueryAsync` (using Tooling SOQL) can query objects like `ApexClass`, `ApexTrigger`, `ValidationRule`, and `TraceFlag`.
- **Debug Logs**: Can retrieve and manage debug logs, useful for troubleshooting issues in the org from the external app.

## 3. Use Cases
- **System Health Checks**: Querying `AsyncApexJob` or `CronTrigger` to monitor background jobs.
- **Code Management**: Retrieving Apex class bodies for display or analysis.
- **Validation Rule extraction**: Fetching validation rules metadata to replicate them client-side (used by `ValidationRuleEngine`).
