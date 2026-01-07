# Metadata Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Metadata` directory contains `ISchemaService` and its implementation. This is the **Brain** of the library, responsible for introspecting the Salesforce data model.

## 2. Key Components

### `ISchemaService`
**Purpose**: Provides detailed metadata about SObjects.
**Key Capabilities**:
- **Describe Caching**: Calls `/services/data/vXX.0/sobjects/{Object}/describe` and caches the result. This is critical for performance as these payloads are large.
- **Smart Field Resolution**: `GetNameFieldAsync` figures out what the "Name" field is for an object (e.g., "CaseNumber" for Cases, "Subject" for Tasks), allowing generic UI components to display readable titles.
- **Picklist Logic**: `GetDependentPicklistValuesAsync` handles the complex logic of dependent picklists (e.g., if Country="USA", State can only be "NY", "CA", etc.).
- **Relationship Discovery**: `GetChildRelationshipsAsync` finds all objects that link *to* the current object, enabling "Related Lists" in the UI.

## 3. Design Decisions
- **Cache Invalidation**: Includes `InvalidateCacheAsync` to allow the application to refresh metadata without restarting if an Admin adds a field in Salesforce.
- **FLS Enforcement**: Provides `SanitizeFieldListWithFlsAsync` to ensure queries never request forbidden fields, preventing API errors.
