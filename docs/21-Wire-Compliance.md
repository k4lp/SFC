# 21 — Wire Compliance

What SFC sends to and expects from Salesforce, checked against the official documentation. **No live org was used:
compliance is against the specs**, with fake-server contract tests (`tests/SalesforceCore.E2E/WireComplianceE2ETests.cs`
and the other E2E suites).

| Area | SFC behaviour | Spec reference |
|---|---|---|
| API version | `Salesforce:ApiVersion` (e.g. `v62.0`) validated at startup; minimum enforced by `SalesforceApiVersion`. | REST API Developer Guide, "Versions" |
| Auth header | `Authorization: Bearer <token>`; `Authorization`/`Host` cannot be overridden per request. | REST API, "Using cURL / Authorization" |
| Instance URL | Taken from the token response `instance_url` and re-read on every refresh, so an org migration is followed. Validated against Salesforce hosts unless `ValidateInstanceUrl=false`. | OAuth 2.0 token response fields |
| OAuth token response | `access_token`, `instance_url`, `token_type`, `issued_at`, `id`, `signature`, `scope`. OAuth errors `{"error","error_description"}` map to `SalesforceAuthException`. | Salesforce Help, "OAuth 2.0 JWT Bearer Flow", "Web Server Flow" |
| 401 | One token refresh and retry. | REST API, "Status Codes and Error Responses" |
| Error bodies | `[{"message","errorCode","fields"}]`, single object, or OAuth shape; mapped by `SalesforceErrorMapper`. | REST API, "Status Codes and Error Responses" |
| Rate limits | `REQUEST_LIMIT_EXCEEDED` (403) / 429 → `SalesforceRateLimitException` with `RetryAfter` from `Retry-After` (seconds or date; a past date is 0 since 2.6.0). Retries honour `Retry-After`. | REST API, "API Request Limits and Allocations" |
| API usage | `Sforce-Limit-Info: api-usage=used/max` recorded in `ISalesforceApiUsage`. | REST API, "Limit Info Header" |
| Query page size | Optional `Sforce-Query-Options: batchSize=200..2000` via `QueryBatchSize` (2.6.0); default 2000. | REST API, "Query Options Header" |
| Client id | Optional `Sforce-Call-Options: client=NAME` via `ClientName` (2.6.0). | REST API, "Call Options Header" |
| Compression | Opt-in `Accept-Encoding: gzip` (`EnableResponseCompression`). | REST API, "Compression Headers" |
| Query paging | `nextRecordsUrl` followed (validated to stay on `/services/data`); query locators expire (~15 min idle) and raise `INVALID_QUERY_LOCATOR` as a query exception — restart the query. | SOQL and SOSL Reference, "query" / REST API "Execute a SOQL Query" |
| SOQL length | Statements over 100,000 characters are rejected before sending (2.6.0). Long IN lists: split them. | SOQL and SOSL Reference, "SOQL and SOSL Limits" |
| sObject Collections | ≤ 200 records per call (chunked), `allOrNone` honoured. | REST API, "sObject Collections" |
| Composite | ≤ 25 subrequests per call (batched); record bodies use the same field formatting as single-record calls (2.6.0). | REST API, "Composite" |
| Bulk API 2.0 ingest | CSV, `,` delimiter, `lineEnding: LF` declared and used; results streamed (no 10 MB cap); cancel aborts the job. | Bulk API 2.0 Developer Guide, "Create a Job", "Upload Job Data" |
| Bulk API 2.0 query | `Sforce-Locator` paging until `null`; `maxRecords` respected. | Bulk API 2.0, "Get Results for a Query Job" |
| Apex REST | `/services/apexrest/...`; enums sent as text; case-insensitive typed reads. | Apex Developer Guide, "Exposing Apex Classes as REST Web Services" |
| Describe | `/sobjects/{obj}/describe`, errors propagate; cached per user by default. | REST API, "sObject Describe" |
| Files | `ContentVersion` insert with `VersionData` base64 in a JSON body; downloads of `/VersionData` streamed. Multipart upload (needed for files above the JSON base64 limit, about 37.5 MB) is not implemented. | REST API, "Insert or Update Blob Data" |
| Data types | See [20 — Data Type Mapping](20-Data-Type-Mapping.md). | Object Reference, "Field Types" |

## Not verified without a real org
- Behaviour of real limits (query locator expiry timing, concurrent-request limits, actual `Retry-After` values).
- Org-specific field behaviour: encrypted fields, State/Country picklists, rich text sanitisation, geolocation precision.
- Real JWT / web-server login round trips and `instance_url` changes during an org migration.
- `If-Modified-Since` on describe (not sent by SFC today).
- Multipart ContentVersion uploads for large files (not implemented).
