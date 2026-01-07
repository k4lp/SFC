# Attributes Directory Explanation

## 1. Overview
The `src/SalesforceCore/Attributes` directory contains custom C# attributes used to decorate POCO (Plain Old CLR Object) models. These attributes act as the "glue" between C# classes/properties and Salesforce SObjects/Fields. They are read at runtime via **Reflection** by the `SalesforceMapper` and `SalesforceQueryable` engine.

## 2. Key Attributes

### `[SalesforceObject]`
**Purpose**: Maps a C# class to a specific Salesforce SObject API name.
**Usage**:
```csharp
[SalesforceObject("Account")]
public class CustomerAccount { ... }
```
**Why**: Allows the C# class name to differ from the Salesforce object name (e.g., to follow C# naming conventions or handle conflicts).

### `[SalesforceField]`
**Purpose**: Maps a C# property to a specific Salesforce Field API name.
**Usage**:
```csharp
[SalesforceField("Account_Number__c")]
public string AccountNumber { get; set; }
```
**Why**: Handles naming convention differences (PascalCase vs snake_case) and custom fields (`__c`). It also stores metadata like `ReadOnly`, `Createable`, `MaxLength` used for validation.

### `[SalesforceIgnore]`
**Purpose**: Excludes a property from serialization/deserialization.
**Why**: Useful for computed properties or runtime-only data that shouldn't be sent to Salesforce.

### `[SalesforceLookup]` & `[SalesforcePolymorphicLookup]`
**Purpose**: Defines relationship metadata.
**Why**: Used by `SoqlExpressionVisitor` to properly construct relationship queries (e.g., `Owner.Name`) and by `LookupService` to resolve IDs to names.

## 3. C# Concepts
- **`AttributeUsage`**: Defines where an attribute can be placed (Class, Property, etc.).
- **Metadata Storage**: These attributes effectively turn C# types into self-describing schemas, avoiding the need for separate XML or JSON mapping files.
