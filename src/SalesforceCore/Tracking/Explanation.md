# Tracking Directory Explanation

## 1. Overview
The `src/SalesforceCore/Tracking` directory implements a **Change Tracking** system, similar to Entity Framework's `ChangeTracker`. This allows the application to work with C# objects, modify their properties, and then automatically calculate the minimal "delta" needed to update Salesforce.

## 2. Key Components

### `ChangeTracker`
**Purpose**: Monitors entities for modifications.
**Mechanism**:
1. **`Track<T>`**: Takes a snapshot of an object's current state (Original Values).
2. **`DetectChanges`**: Compares the current object state against the snapshot.
3. **`GetChanges`**: Returns a list of `FieldChange` objects (OldValue, NewValue).
4. **`GetModifiedFields`**: Returns a dictionary suitable for a `PATCH` request, containing *only* the fields that changed.

### `EntityState`
**Enum**: `Detached`, `Unchanged`, `Added`, `Modified`, `Deleted`.
**Usage**: Tracks the lifecycle of an object.

### `IChangeTracker`
**Purpose**: Interface to allow DI and mocking.

## 3. C# Concepts
- **Snapshotting**: The tracker likely uses deep cloning or serialization to store the "Original Values" so that subsequent changes to the object don't mutate the baseline.
- **Reference Equality vs Value Equality**: The tracker handles comparing simple values (int, string) and complex values (JSON objects) to determine if a "real" change occurred.
