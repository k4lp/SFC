# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 1.3.x   | ✅ security fixes |
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

In scope: `SalesforceCore`, `SalesforceCore.AspNetCore`, `SalesforceCore.ModelGenerator` as published from this repository.
Out of scope: Salesforce platform vulnerabilities (report to Salesforce), misconfiguration in consuming apps, and the unrelated `SalesforceCore` package on nuget.org that is not published from this repository.

## Hardening guidance

See the **Security** section of the [README](README.md#security).
