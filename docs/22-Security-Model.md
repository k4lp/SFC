# Security model (2.7.0)

This page is the threat model for every SalesforceCore package, the protections the library applies by default, the
opt-outs, and what the host application still has to do. It was written by attacking the library (STRIDE per package)
and is backed by `tests/SalesforceCore.Tests/SecurityAttackTests.cs` and `tests/SalesforceCore.E2E/SecurityAttackE2ETests.cs`.

**Design rule:** the library never decides *who may do what*. Salesforce enforces sharing, FLS and permissions; the
library only makes sure that a request goes to the right host, to the route the caller asked for, with values that
cannot change the meaning of the request, and that secrets do not leak.

## Assets and trust boundaries

| Asset | Where it lives | Main threat |
|---|---|---|
| Access/refresh tokens, client secret, JWT private key | token providers, caches, HTTP `Authorization` header | theft (logs, exceptions, hostile hosts, cross-user caches) |
| Record data | Salesforce, responses, caches | injection, cross-user cache leaks, XSS when rendered |
| Host app process | memory, CPU, disk | DoS through hostile responses, path traversal through generated files |

Untrusted inputs: end-user values (filters, IDs, file names, form posts), Salesforce/proxy responses (a compromised
proxy or a hostile `instance_url` is in scope), describe metadata fed to `sf-gen`, and configuration in shared hosting.

## STRIDE summary

| Package | Spoofing | Tampering | Repudiation | Information disclosure | DoS | Elevation |
|---|---|---|---|---|---|---|
| SalesforceCore | `instance_url` / `Domain` allow-list, HTTPS only, redirects never followed (unreleased), cross-host 3xx refused | SOQL/SOSL escaping, identifier validation, path guard, header guard | `Sforce-Call-Options client=` (`ClientName`), request IDs on exceptions | token redaction in logs and exception messages; token fingerprints | response buffer cap, gzip cap, JSON depth 64, bounded retries/paging, SOQL length cap, regex timeouts | path traversal to other endpoints (Apex/Tooling) blocked |
| AspNetCore | OAuth state + PKCE (ASP.NET Core OIDC handler), `instance_url` validated at sign-in | anti-forgery on every POST | ‑ | generic error messages for non-Salesforce exceptions, file headers (`nosniff`, CSP sandbox) | upload size limits | open redirect blocked (`IsLocalUrl`) |
| SqlServer | ‑ | quoted identifiers, parameterised values | ‑ | AES-GCM encrypted entries, per-user keys | bounded batch sizes | ‑ |
| ModelGenerator | ‑ | identifiers sanitised, strings/comments escaped | ‑ | ‑ | ‑ | output path confined to the output directory |

## Protections, by threat

### Injection
- **SOQL/SOSL values**: `SoqlBuilder`, LINQ and `SecurityUtils.FormatSoqlValue` escape `\ ' " \n \r \t \b \f` (backslash
  first) and drop other control characters. `LIKE` adds wildcard escaping. Verified with a payload corpus that checks
  the value stays inside exactly one literal.
- **Identifiers** (objects, fields, ORDER BY, GROUP BY): validated with anchored patterns. 2.7.0 replaced `$` with `\z`
  everywhere, because `$` also matched before a trailing newline (`"Account\n"` used to pass).
- **URL paths**: every instance-relative path goes through `SalesforcePathGuard` (in `SalesforceClient.BuildPath` and
  for every composite sub-request URL). `.`/`..` segments, also as `%2e%2e` or double-encoded, backslashes, control
  characters and absolute/protocol-relative URLs are rejected before anything is sent (the scheme check uses the raw
  path, so a percent-encoded `https%3A%2F%2F…` external ID value stays legal; unreleased). Values placed in a single
  segment (external IDs) use `SalesforcePathGuard.Segment`. Record, report, instance, ApexLog and record type IDs must be
  15/18-character IDs.
- **Headers**: names must be RFC 7230 tokens; values with CR, LF or NUL are rejected (`ClientName` at startup).
- **SQL (SqlServer cache)**: values are parameters; table/schema names are bracket-quoted with `]` doubled.
- **Generated C# (sf-gen)**: identifiers are reduced to `[A-Za-z0-9_]`, keywords prefixed, strings and XML comments
  escaped, files written only inside the output directory.

### Secrets
- Tokens never appear in log templates; response bodies in logs are redacted (`SensitiveDataRedactor`).
- 2.7.0: error bodies are redacted **before** they become exception messages (`Bearer …`, `access_token=…`, session
  IDs). A hostile proxy that echoed the `Authorization` header used to put the live token into `ex.Message`.
- `CachedToken.ToString()` and debugger display show fingerprints only. Distributed/SQL token caches are encrypted
  (Data Protection / AES-GCM) and keyed per user.

### Transport and hosts
- `instance_url` and `Domain` must be HTTPS Salesforce hosts (`*.salesforce.com`, `*.force.com`, gov, China, …).
  Add your own with `AdditionalAllowedHosts`. `ValidateInstanceUrl=false` turns this off (not recommended).
- 2.7.0: `SalesforceTransportSecurityHandler` refuses to send an `Authorization` header over `http://` and discards a
  response that was redirected to another host. `AllowInsecureHttp` is the opt-out for local test proxies.
- unreleased: `SalesforceRedirectPolicy` turns off automatic redirects on every Salesforce client (API, JWT, client
  credentials, token refresh/revoke). .NET strips `Authorization` on redirect but still re-sends SOQL query strings and,
  for 307/308, record bodies; `RedirectTwoListenerE2ETests` asserts that a second real HTTPS listener receives nothing.
- TLS: the library never sets `ServerCertificateCustomValidationCallback` or `SslProtocols`; the OS default (TLS 1.2+)
  applies. Salesforce requires TLS 1.2+.

### Resource abuse
- JSON responses are buffered up to `MaxResponseContentBufferSize` (10 MB). Streamed reads (bulk results, files) are
  not buffered. 2.7.0: gzip responses are capped after decompression by `MaxDecompressedResponseBytes` (1 GiB; a 64 KB
  bomb that expands to 64 MB is cut off at the configured limit in the E2E test).
- System.Text.Json's default max depth (64) stops deeply nested hostile JSON with a `JsonException`.
- Retries, query paging and bulk polling are bounded; regexes run with timeouts or have no nested quantifiers.

### AspNetCore
- Tag helpers HTML-encode every value in every context (text, attributes, URLs).
- Every state-changing endpoint has `[ValidateAntiForgeryToken]`.
- Non-Salesforce exceptions are shown as a generic message; stack traces and SOQL are never rendered.
- File endpoints: only passive types render inline; everything else is `application/octet-stream` attachments
  (2.7.0 also for `Download`), with `X-Content-Type-Options: nosniff` and a sandbox CSP. File names are sanitised with a
  portable rule set (2.7.0: `\ : * ? " < > |`, control characters, leading dots and Windows device names are removed
  on every OS; before, Linux hosts only removed `/`).

## Host-app responsibilities
- Use HTTPS, HSTS, and security headers (CSP, `X-Frame-Options`/`frame-ancestors`, `Referrer-Policy`) in your app.
- Persist ASP.NET Core Data Protection keys securely (they protect token cookies and the distributed token cache).
- Keep the client secret and JWT private key in a secret store, never in `appsettings.json` in source control.
- Do not log `SalesforceOptions` or token provider options objects.
- Raw SOQL (`QueryAsync(string)`) is your responsibility: build it with `SoqlBuilder`/LINQ or escape values with
  `SecurityUtils.SanitizeSoql`.
- Authorisation of your own endpoints (who may open which record page) is yours; Salesforce enforces data access for
  the signed-in user's token.

## Safe configuration checklist
```json
{
  "Salesforce": {
    "Domain": "https://yourorg.my.salesforce.com",
    "ValidateInstanceUrl": true,
    "AdditionalAllowedHosts": [],
    "AllowInsecureHttp": false,
    "MaxResponseContentBufferSize": 10485760,
    "MaxDecompressedResponseBytes": 1073741824
  }
}
```

## Opt-outs (all off by default)
| Setting | Effect |
|---|---|
| `ValidateInstanceUrl=false` | trust any `instance_url` (tokens can then be sent anywhere) |
| `AdditionalAllowedHosts` | extend the host allow-list (exact host or `.suffix`) |
| `AllowInsecureHttp=true` | allow `http://` login and API URLs |
| `MaxDecompressedResponseBytes=0` | no gzip expansion cap |
| `AllowInsecureSqlCacheKeyDerivation=true` | deterministic SQL cache key (dev only) |

## Residual risks
- Not tested against a live org; real rate limits and real JWT/browser logins are verified only against the fake server.
- `SecurityCodeScan` does not support .NET 10; only the built-in .NET security analyzers were run.
- Packages are not signed (GitHub Release assets only). Verify downloads against the SHA-256 checksums listed in the release notes.
- Bulk CSV is sent to Salesforce, not to spreadsheets, so spreadsheet formula prefixes (`=`, `+`, `-`, `@`) are not
  altered. If you export Salesforce data to CSV for people to open in Excel, neutralise formulas in your export.
