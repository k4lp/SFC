# Reports Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Reports` directory wraps the **Salesforce Analytics API**. It allows the application to list, filter, and execute reports defined in Salesforce, effectively using Salesforce as a "BI Engine" for the app.

## 2. Key Components

### `IReportService`
**Purpose**: Interact with the `/analytics/reports` endpoints.
**Capabilities**:
- **Discovery**: List reports by folder or search by name.
- **Execution**: `RunReportAsync` executes a report synchronously.
- **Filtering**: `RunReportWithFiltersAsync` allows applying *dynamic filters* at runtime. For example, a "My Accounts" report can be filtered to a specific region by the C# application before execution.
- **Async Execution**: `StartReportAsync` handles long-running reports by triggering them and providing a mechanism to poll for completion.

## 3. Use Cases
- **Dashboard Embedding**: Rendering chart data fetched directly from Salesforce reports.
- **Data Export**: `ExportReportToCsvAsync` allows users to download report results directly from the custom app.
