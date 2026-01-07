# Services Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services` directory acts as the main container for all business logic and API interaction components within the library. It is organized hierarchically by functional domain, mirroring the structure of the Salesforce REST API families.

## 2. Directory Structure & Design
This directory uses a **folder-by-feature** organization strategy. Each subfolder corresponds to a specific area of functionality:

- **`Apex/`**: Services for interacting with Salesforce Apex REST services and executing anonymous Apex.
- **`Authorization/`**: Handles OAuth flows, token management, and session maintenance.
- **`Caching/`**: implementations of caching strategies (Memory, Distributed, SQL) to improve performance and reduce API calls.
- **`Configuration/`**: Services for managing and validating library configuration.
- **`Core/`**: Base classes and core infrastructure shared across multiple services.
- **`Data/`**: The primary home for CRUD (Create, Read, Update, Delete) operations on SObjects.
- **`Files/`**: Services for handling Salesforce Files, ContentVersions, and Attachments.
- **`Layout/`**: Services for retrieving and parsing page layouts and Compact layouts.
- **`Metadata/`**: Services for interacting with the Metadata API (deployments, retrievals).
- **`Query/`**: Services specifically for executing SOQL and SOSL queries.
- **`Reports/`**: Services for the Analytics API (running reports, getting dashboards).
- **`Tooling/`**: Services for the Tooling API (interacting with code, validation rules, etc.).

## 3. Design Principles
- **Separation of Concerns**: Each folder contains services dedicated to a single responsibility.
- **Interface-Based Design**: Services defined in these folders typically implement interfaces (e.g., `IDataService`, `IQueryService`) to support Dependency Injection.
