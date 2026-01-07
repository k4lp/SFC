# Controllers Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Controllers` directory contains ready-to-use API controllers that expose `SalesforceCore` functionality to the frontend (SPA or JavaScript).

## 2. Key Controllers

### `DynamicUiController`
**Purpose**: Serves UI descriptors.
**Endpoints**:
- `GET /api/dynamic-ui/forms/{object}`: Returns the JSON configuration for a form (fields, layout, validation rules).
- `GET /api/dynamic-ui/navigation`: Returns the menu structure.
**Usage**: A React or Angular frontend calls these endpoints to render the UI dynamically.

### `FileController`
**Purpose**: Proxies file downloads.
**Why**: Salesforce file downloads require an Authorization header. You cannot simply put a `<a href="salesforce.com/..." />` link in the browser because the browser won't attach the Bearer token.
**Mechanism**: This controller accepts a request, uses the server-side `IFileService` (with the token) to stream the file from Salesforce, and pipes the stream back to the browser.

### `LookupController`
**Purpose**: Provides type-ahead search results for Lookup fields.
**Endpoint**: `GET /api/lookup/search?q=Acme&object=Account`.

## 3. Security
- All controllers are decorated with `[Authorize]`, ensuring only authenticated users can access Salesforce data.
