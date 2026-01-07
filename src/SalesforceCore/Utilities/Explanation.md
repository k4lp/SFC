# Utilities Directory Explanation

## 1. Overview
The `src/SalesforceCore/Utilities` directory contains static helper classes and constants that standardize how the library interacts with Salesforce.

## 2. Key Components

### `SalesforceConventions`
**Purpose**: A knowledge base of Salesforce "rules" and standard behaviors.
**Content**:
- **`IdPrefixes`**: A dictionary mapping "001" to "Account", "003" to "Contact", etc. Used for polymorphic lookup resolution.
- **`NonQueryableFieldTypes`**: Lists field types like `address` or `location` that require special handling in SOQL.
- **`ObjectSearchFields`**: Default search fields for common objects.
- **`NameFieldCandidates`**: Candidate fields used to infer display names.

### `SecurityUtils`
**Purpose**: Helper methods for sanitizing inputs to prevent injection.
**Key Methods**: `SanitizeSoql`, `SanitizeSoqlLike`, `TryValidateSoqlQuery`, `IsValidObjectName`, `IsValidFieldName`.

### `UrlUtils`
**Purpose**: Helpers for constructing and escaping API URLs.

### `FieldTypeConverter`
**Purpose**: Logic for converting between C# types and Salesforce API field types (e.g., handling the various date formats).

### `BitmaskUtils`
**Purpose**: Likely used for handling bitmask fields if any exist (less common in modern Salesforce, but useful for generic programming).

## 3. Design Decisions
- **Static Knowledge**: By hardcoding things like `IdPrefixes`, the library can perform local "smarts" (like knowing "001..." is an Account) without needing an API call, improving performance.
