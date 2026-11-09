# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 2.8.x   | ✅ security fixes |
| 2.7.x   | ⚠️ upgrade to 2.8.0 — HTTP redirects could resend record data, SOQL or refresh tokens; browser-login token refresh failed on Salesforce's documented string `issued_at` (see CHANGELOG 2.8.0) |
| 2.0–2.6 | ⚠️ upgrade to 2.8.0 — also includes 2.7.0 path traversal, tokens in exception messages, gzip decompression bomb, SQL identifier quoting, Linux file-name sanitisation |
| 1.3.x   | ❌ |
| 1.2.x   | ⚠️ upgrade to 1.3.0 — stored XSS via file preview, plaintext tokens in DistributedCacheTokenProvider, session-fixation token cache key (see docs/security/SFC_Security_Scan_Report.md) |
| 1.1.x   | ⚠️ upgrade to 1.2.0 — contains a SOQL escaping injection bug and no instance_url validation |
| < 1.1   | ❌ |

## Reporting a vulnerability

**Do not open a public issue for security problems.**

Report privately through GitHub: **Security → Advisories → "Report a vulnerability"** on
<https://github.com/k4lp/SFC/security/advisories/new>.

Please include: affected package and version, a minimal reproduction, impact, and any suggested fix.

What to expect:

- Acknowledgement within **3 business days**.
- An initial assessment (severity, affected versions) within **10 business days**.
- A fix or mitigation targeted within **30 days** for High/Critical issues, followed by a GitHub Security Advisory and release notes crediting the reporter (unless you prefer to stay anonymous).

Please give us a reasonable window to release a fix before public disclosure. Good-faith research that avoids privacy violations, data destruction and service disruption is welcome; only test against Salesforce orgs you own (e.g. a free Developer Edition org).

## Scope

In scope: `SalesforceCore`, `SalesforceCore.Abstractions`, `SalesforceCore.AspNetCore`, `SalesforceCore.SqlServer` and `SalesforceCore.ModelGenerator` (`sf-gen`) as published on this repository's GitHub Releases.
Out of scope: Salesforce platform vulnerabilities (report to Salesforce), misconfiguration in consuming apps, and the unrelated `SalesforceCore` package on nuget.org that is not published from this repository.

## Hardening guidance

- Threat model, default protections, opt-outs and host-app responsibilities: [docs/22-Security-Model.md](docs/22-Security-Model.md).
- Detailed guidance: [docs/09-Security.md](docs/09-Security.md) and the **Security** section of the [README](README.md#security).

## Supply chain

- Packages are distributed **only** as GitHub Release assets of this repository; the `SalesforceCore` ID on nuget.org
  belongs to an unrelated publisher. Pin a `nuget.config` package source mapping so `SalesforceCore*` resolves only
  from your local feed (see docs/00-Setup-Guide.md).
- Every restore audits direct and transitive packages (`NuGetAuditMode=all`) and fails on known vulnerabilities.
- Builds are deterministic with SourceLink (`PublishRepositoryUrl`, `EmbedUntrackedSources`); symbol packages are
  attached to each release. .NET security analyzers run with `AnalysisModeSecurity=All`.
- Packages are not Authenticode/NuGet-signed. Compare each download with the SHA-256 checksums in the release notes.
