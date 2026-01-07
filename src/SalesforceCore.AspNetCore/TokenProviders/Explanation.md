# TokenProviders Directory Explanation

## 1. Overview
The `src/SalesforceCore.AspNetCore/TokenProviders` directory contains the logic for retrieving and refreshing OAuth tokens in a web environment.

## 2. Key Components

### `AspNetCoreTokenProvider`
**Purpose**: The default provider for web apps.
**Logic**:
- **Get**: Retrieves `access_token` from `HttpContext.GetTokenAsync()`.
- **Refresh**:
    1.  Uses a **Semaphore** (lock) to ensure only *one* thread attempts to refresh the token if multiple parallel requests fail with 401.
    2.  Uses `IDistributedLockProvider` (if available) to coordinate refresh across *multiple servers*.
    3.  Calls the Salesforce `/token` endpoint with the `refresh_token`.
    4.  Updates the `AuthenticationProperties` (cookie/ticket) with the new access token and refresh token.
    5.  Signs the user in again (updates the cookie) so subsequent requests use the new token.

### `SessionTokenProvider` / `DistributedCacheTokenProvider`
**Purpose**: Alternative strategies that store tokens directly in Session or Cache, rather than in the Auth Cookie. This allows for decoupling the token lifecycle from the login session lifecycle.

## 3. Design Decisions
- **Concurrency Handling**: The "Thundering Herd" problem is common in OAuth apps (100 requests hit 401 at once). The locking logic here is critical to prevent spamming Salesforce with 100 refresh requests, which would lead to API bans.
