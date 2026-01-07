# Attributes Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Attributes` directory contains custom ASP.NET Core attributes.

## 2. Key Components

### `SalesforceAuthorizeAttribute`
**Purpose**: A specialized authorization attribute.
**Usage**: `[SalesforceAuthorize(Permission = "Delete_Account")]`
**Function**: It likely combines standard ASP.NET Core `[Authorize]` behavior with checks against Salesforce-specific permissions or Claims (e.g., checking if the user's token has the `api` scope or specific custom permissions).
