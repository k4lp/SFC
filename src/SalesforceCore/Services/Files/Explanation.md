# Files Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Files` directory abstracts the complexity of the Salesforce **ContentVersion** and **ContentDocument** data model. Working with files in Salesforce involves multiple steps (create version, link to record), which this service consolidates into single method calls.

## 2. Key Components

### `IFileService`
**Purpose**: High-level API for file management.
**Key Methods**:
- **`UploadAsync`**: Creates a `ContentVersion` record (the file blob) and automatically creates a `ContentDocumentLink` to attach it to a parent record (Account, Case, etc.).
- **`DownloadAsync`**: Retrieves the binary blob of a file.
- **`GetFilesAsync`**: Lists all files attached to a specific record.

## 3. Salesforce Data Model
This service hides the complexity of the underlying objects:
- **`ContentVersion`**: Represents a specific version of a file. This is where the binary data lives.
- **`ContentDocument`**: Represents the file "container" across all versions.
- **`ContentDocumentLink`**: The junction object that shares a `ContentDocument` with a User, Group, or Record.

## 4. Design Decisions
- **Stream Support**: `UploadAsync` and `DownloadToStreamAsync` accept/return .NET `Stream` objects. This is critical for web applications where users might upload large files (e.g., 50MB PDFs) that shouldn't be fully buffered in RAM.
