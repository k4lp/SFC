# Extensions Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/Extensions` directory contains the setup logic for integrating the library into `Program.cs`.

## 2. Key Components

### `ServiceCollectionExtensions`
**Key Method**: `AddSalesforceCoreMvc` and `AddSalesforceAuthentication`.
**Logic**:
- **`AddSalesforceAuthentication`**: Configures the OpenID Connect (OIDC) handler.
    - Sets Authority to `https://login.salesforce.com`.
    - Enables **PKCE** (`options.UsePkce = true`).
    - Configures the scope (`api`, `refresh_token`, `web`).
    - Wires up the `OnTokenResponseReceived` event to extract the `instance_url` (which Salesforce returns in the body, not the token) and store it in the Auth Properties.

### `AspNetCoreTokenProvider` (Moved to TokenProviders namespace in newer structure, but registered here)
**Purpose**: Registers the implementation that knows how to read tokens from `HttpContext`.

## 3. Design Decisions
- **Secure Defaults**: The configuration enables PKCE and secure cookies by default, guiding developers toward a secure implementation.
