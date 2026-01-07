# Mapping Directory Explanation

## 1. Overview
The `src/SalesforceCore/Mapping` directory contains the logic for converting between C# objects and Salesforce JSON representations. It is the bridge between the strongly-typed C# world and the loosely-typed API world.

## 2. Key Components

### `SalesforceMapper`
**Purpose**: A static utility class that performs the actual mapping.
**Key Features**:
- **Reflection Caching**: uses a `ConcurrentDictionary` to cache reflection results (attributes, property info) to ensure high performance after the first use.
- **`ToSalesforceDictionary`**: Converts a C# object to a `Dictionary<string, object>` for serialization. It respects `[SalesforceIgnore]`, `ReadOnly` flags, and filters fields based on the operation context (Create vs Update).
- **`FromSalesforce`**: Converts `JsonNode` responses from Salesforce back into C# objects. It handles nested dot-notation (e.g., mapping `Parent.Name` in JSON to `ParentName` property).
- **Type Conversion**: Handles the nuances of converting JSON types to C# types (e.g., `DateOnly`, `TimeOnly`, `DateTimeOffset`, and Enums).

## 3. C# Concepts
- **Reflection**: Used to inspect properties and attributes at runtime.
- **ConcurrentDictionary**: Thread-safe caching mechanism.
- **JsonNode API**: Uses `System.Text.Json.Nodes` for flexible, DOM-based JSON manipulation.
