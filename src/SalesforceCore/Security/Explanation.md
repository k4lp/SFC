# Security

`src/SalesforceCore/Security` holds transport-level protections. Since 2.0 the library performs **no access gating**
(no object/field visibility rules, no FLS filtering): Salesforce enforces CRUD/FLS/sharing and its errors surface as
typed exceptions (`SalesforceFieldSecurityException`, `SalesforceForbiddenException`, ...).

| Type | Purpose |
|------|---------|
| `SalesforceUrlValidator` | Instance-host allowlist: the bearer token is only ever sent to trusted Salesforce hosts. |
| `SensitiveDataRedactor` | Redacts tokens/secrets from logs and exception messages. |
| `MissingConfigurationTokenProvider` | Placeholder provider that fails with a descriptive error when no auth is configured. |

Related protections elsewhere: SOQL/SOSL escaping (`Query/`, `Utilities/SecurityUtils`), path-segment validation for
REST/Apex/Bulk/Composite URLs, token encryption in distributed caches, CSRF and XSS-safe rendering in `SalesforceCore.AspNetCore`.
