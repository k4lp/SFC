# 20 — Salesforce Data Type Mapping

How each Salesforce field type maps to .NET, and the exact text SFC writes for it in REST/composite JSON, SOQL
literals and Bulk API 2.0 CSV. All formatting lives in one place: `SalesforceCore.Mapping.SalesforceWireFormat`
(public, so you can use it in your own code). Tests: `tests/SalesforceCore.Tests/V26/DataTypeMatrixTests.cs` and
`tests/SalesforceCore.E2E/WireComplianceE2ETests.cs`.

> **Verification scope.** These formats are checked against the Salesforce documentation (Object Reference "Field
> Types", REST API Developer Guide, SOQL and SOSL Reference, Bulk API 2.0 Developer Guide) using doc-shaped sample
> JSON and a fake server. They have **not** been run against a live org.

| Salesforce type | .NET type | REST JSON (write) | SOQL literal | Bulk CSV cell | Notes |
|---|---|---|---|---|---|
| `id` | `string` | `"001D000000IRFmaIAH"` | `'001D000000IRFmaIAH'` | as is | 15-char ids are case-sensitive. `SalesforceWireFormat.To18CharId` / `IdEquals` convert and compare. |
| `reference` (lookup, polymorphic) | `string` | id | quoted id | id | Polymorphic type: `IDataService.ResolvePolymorphicTypeAsync`. |
| `boolean` | `bool` | `true` | `TRUE` / `FALSE` | `true` / `false` | |
| `string`, `textarea`, long/rich text, `email`, `phone`, `url`, `combobox` | `string` | JSON string | `'…'` escaped `\\ \' \" \n \r \t \b \f` | CSV-quoted when it holds `,` `"` or a newline | Rich text is HTML; SFC does not sanitise it on write. Unicode quotes (’) are sent as is. |
| `encryptedstring` | `string` | plain value | not filterable | plain value | Reads come back masked (`****1234`) unless the user has *View Encrypted Data*. |
| `picklist` | `string` or enum | `[SalesforceValue]` or member name | `'Very Warm'` | same | Unknown values throw on read (2.5.0). |
| `multipicklist` | `string`, `string[]`, `List<string>`, `[Flags]` enum | `"A;B"` | use `INCLUDES`/`EXCLUDES` | `A;B` | 2.6.0: arrays/lists/flags are joined with `;`; before, an array was sent as `System.String[]` and flags as `A, B`. Reads split on `;`. |
| `currency`, `percent`, `double` (number with scale) | `decimal` (recommended) or `double` | JSON number, all digits | positional digits, no exponent | invariant, no exponent | Use `decimal` for money: Salesforce numbers allow 18 digits; `double` holds ~15–17. SOQL `double` literals keep full round-trip precision (2.6.0; earlier went through `decimal` and kept 15 digits). NaN/∞ throw. |
| `int` | `int` | number | `42` | `42` | |
| `long` (Number(18,0)) | `long` | number | `9007199254740993` | same | |
| `date` | `DateOnly` (or `DateTime`) | `"2024-02-29"` | `2024-02-29` (unquoted) | `2024-02-29` | |
| `datetime` | `DateTime`, `DateTimeOffset` | `"2024-01-02T03:04:05.006Z"` | `2024-01-02T03:04:05Z` (unquoted, whole seconds) | `2024-01-02T03:04:05.006Z` | Converted to UTC; `DateTimeKind.Unspecified` is treated as UTC. Reads accept `+0000`, `Z`, `+05:30`; typed reads return `DateTimeKind.Utc`. |
| `time` | `TimeOnly` | `"09:30:00.000Z"` | `09:30:00.000Z` | same | |
| `base64` | `byte[]` | base64 string | not filterable | **not supported** (throws) | Bulk API 2.0 cannot load binary; use `UploadFileAsync` (ContentVersion). |
| `address` (compound) | `SalesforceAddress` | **omitted** (read-only) | filter the components | n/a | Write `BillingStreet`, `BillingCity`, … |
| `location` (compound) | `SalesforceLocation` | **omitted** (read-only) | `DISTANCE(...)` | n/a | Write `Loc__Latitude__s` / `Loc__Longitude__s`. |
| `anyType` | `object` / `JsonNode` | as given | depends on value | depends on value | |
| null | any nullable | `null` (or omitted, per operation) | `NULL` | empty cell; `#N/A` to clear a field | |

## SOQL literals
- Strings: quoted, backslash escapes only (SOQL has no `''` doubling). Control characters other than `\n \r \t \b \f`
  are removed.
- Collections (`IEnumerable`, except `string`) become an IN list: `('a', 'b')`, `(1, NULL, TRUE)`.
- Numbers and dates never depend on the thread culture.
- Date literals such as `TODAY` or `LAST_N_DAYS:30` are written by the LINQ/builder date-literal helpers (see
  [19 — LINQ to SOQL](19-LINQ-to-SOQL.md)), never quoted.

## Bulk CSV
- Header row of field API names, `,` delimiter, `"` quoting, **LF line endings** and `lineEnding: "LF"` on the job
  (2.6.0; earlier CRLF data was sent to a job that defaults to LF). Caller-supplied CSV with CRLF is declared `CRLF`.
- Empty cell = leave unchanged on update; `#N/A` = set to null.
