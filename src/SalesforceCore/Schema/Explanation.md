# Schema Directory Explanation

## 1. Overview
The `src/SalesforceCore/Schema` directory contains services and models responsible for understanding and managing the Salesforce data structure (Metadata) at runtime. This is crucial for the "Metadata-Driven" architecture of the library.

## 2. Key Components

### `RecordTypeManager`
**Purpose**: Manages Salesforce Record Types.
**Key Features**:
- **Caching**: Record Type definitions (IDs, DeveloperNames) are cached to avoid frequent round-trips.
- **Picklist Filtering**: Can retrieve the specific list of picklist values allowed for a given Record Type (using the UI API).
- **Defaults**: Retrieves default values for fields based on the selected Record Type.
- **Page Layouts**: Can identify which Page Layout is assigned to a specific Record Type.

### `ISchemaService` (implied dependency)
**Purpose**: The low-level provider for "Describe" calls. It fetches raw metadata about SObjects (fields, relationships) which other components (like `RecordTypeManager`) consume.

### `SchemaDiff` (inferred)
**Purpose**: Likely compares two schemas (e.g., local C# model vs remote Salesforce object) to identify drift or missing fields.

## 3. Design Decisions
- **UI API Integration**: The `RecordTypeManager` explicitly uses the Salesforce UI API (`/ui-api/`) instead of just the standard REST API. This is because the UI API provides pre-processed metadata (like "effective" picklist values per record type) that is otherwise difficult to calculate.
