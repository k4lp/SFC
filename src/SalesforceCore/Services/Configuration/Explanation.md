# Configuration Service Directory Explanation

## 1. Overview
The `src/SalesforceCore/Services/Configuration` directory manages the settings that drive the application's behavior and UI structure. It allows for "Configuration-Driven" development, where menus and field behaviors can be adjusted without code changes.

## 2. Key Components

### `IConfigurationService`
**Purpose**: Retrieves and manages `SalesforceConfig`.
**Capabilities**:
- **Module Discovery**: `AutoDiscoverModulesAsync` can scan Salesforce tabs/objects to automatically build a navigation menu.
- **Field Overrides**: `GetFieldOverrideAsync` allows specifying custom labels, help text, or visibility rules for fields that override the defaults from Salesforce metadata.
- **Persistance**: `SaveConfigurationAsync` allows writing changes back to a file (e.g., `salesforce-config.json`), enabling a "No-Code" admin experience within the app.

### Configuration Models (Implied)
- **`ModuleConfig`**: Defines a menu item (Label, Icon, linked SObject).
- **`FieldOverride`**: Custom metadata for a specific field.

## 3. Use Cases
- **Dynamic Menus**: The application sidebar is likely rendered by calling `GetVisibleModulesAsync`.
- **Customization**: Renaming "Account" to "Client" in the UI without changing the actual Salesforce API name.
