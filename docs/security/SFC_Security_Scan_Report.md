# SFC (SalesforceCore) Security Scan Report: v1.2.0 → v1.3.0

**Repo:** k4lp/SFC · **Baseline:** `master` @ `c264774` (v1.2.0) · **Fix branch:** `security/scan-fixes-v1.3.0` · **Date:** 25 Oct 2026 (IST)
**Scope:** every tracked file under `src/` (SalesforceCore, SalesforceCore.AspNetCore, SalesforceCore.ModelGenerator) plus build/config files and the test suite. About 48k LOC of C# in 174 files.

## 1. Executive summary

This was a full review, not a spot check. 33 security-relevant files got a line-by-line read. The other 141 files (mostly models, DTOs, layout descriptors and validation helpers) got a pattern-assisted skim plus every analyzer and Semgrep. I traced all string-built SOQL/SOSL/SQL, URL building, HTML generation, crypto, token handling and file I/O back to their sources.

**What v1.2.0 got right:** the URL allow-list, the SOQL/SOSL escaping and the record-ID path validation hold up. I tested the allow-list against subdomain tricks, punycode, `userinfo@`, backslash, `#`/`?`/`/` smuggling, trailing dots, ports and http downgrade, and none of them got through. I found no committed secrets in the git history (gitleaks, 15 commits), no vulnerable packages (direct or transitive), and no security-category CA3xxx/CA5xxx analyzer hits.

**What it missed:** mostly the ASP.NET Core layer, which the earlier pass barely covered (there were zero controller or web token-provider tests):
- **One High:** stored XSS through `FileController.Preview` and `GetImage`. The attacker picks the `type`, and a `.xml` upload (allowed by default) is served inline as `application/xml`. That runs XHTML script on the app origin under a CSP that allows `'unsafe-inline'`.
- **Eight Medium:**
  - `DistributedCacheTokenProvider` stores access and refresh tokens in plaintext.
  - Its cache key is the session ID alone, which allows session fixation.
  - Logout doesn't revoke the refresh token.
  - One process-wide refresh lock blocks token refresh for every user.
  - The describe cache is shared across users, even though describe flags are evaluated per user.
  - The Details action skips the visibility policy.
  - An exponential-backtracking **ReDoS** in the URL validation regex (I confirmed the runtime doubles with each added character).
  - Deferred by design: the generic CRUD controller exposes every object.

**Counts (after the self-review in §6):** High 1 · Medium 8 · Low 11 · Info 5 · **Total 25**.
**Fixed in v1.3.0:** 19. **Deferred:** 5, plus 1 accepted as designed, each with a reason in §4 and §7. Tests went from **638 to 698**, all passing.

## 2. Method and tooling

| Tool | Result | Raw output |
|---|---|---|
| `dotnet build -p:AnalysisMode=All -p:AnalysisLevel=latest-all` (before) | 0 errors. Security categories CA3xxx/CA5xxx: **0**. Reliability: CA2000 (undisposed `FormUrlEncodedContent` in 6 src places), CA2016 (5 dropped CancellationTokens), plus CA2007/CA2227 noise | `raw/dotnet_build_analyzers.txt` |
| Same build (after fixes) | CA3xxx/CA5xxx 0. CA2000 and CA2016 in `src/` are now **0**. The remaining CA2000 hits are in tests only | `raw/dotnet_build_analyzers_after_fixes.txt` |
| `dotnet list package --vulnerable/--deprecated/--outdated --include-transitive` | No vulnerable packages, before or after. Deprecated and outdated lists are attached for information | `raw/packages_*.txt` |
| Semgrep 1.x `p/csharp` + `p/secrets` (68 rules, 207 files) | 1 finding, a false positive (FP-1) | `raw/semgrep*.txt/json` |
| gitleaks 8.21.2 over the full history | No leaks | `raw/gitleaks.*` |
| DevSkim | Not run: there's no offline Linux build available. Semgrep and the analyzers cover the same rule families | — |
| Manual review | Checked against OWASP ASVS 4.0 (V1–V14), RFC 9700 (OAuth 2.0 Security BCP), Salesforce-specific risks and .NET-specific risks | this report |

## 3. Coverage table
| File | LOC | Review depth | Checklist areas |
|---|---|---|---|
| `Directory.Build.props` | 12 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `SalesforceCore.sln` | 40 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `docs/ci/ci.yml` | 51 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Attributes/SalesforceAuthorizeAttribute.cs` | 381 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore.AspNetCore/Authentication/DistributedCacheTicketStore.cs` | 123 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, DataProtection, Races |
| `src/SalesforceCore.AspNetCore/Controllers/DynamicUiController.cs` | 291 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Controllers/FileController.cs` | 145 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Controllers/LookupController.cs` | 149 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Controllers/SalesforceController.cs` | 613 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Controllers/SalesforceControllerBase.cs` | 283 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Extensions/AspNetCoreTokenProvider.cs` | 523 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore.AspNetCore/Extensions/AspNetCoreUserContextProvider.cs` | 23 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Extensions/ServiceCollectionExtensions.cs` | 579 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore.AspNetCore/Filters/SalesforceValidateAttribute.cs` | 179 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Middleware/SalesforceExceptionMiddleware.cs` | 189 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/Middleware/SecurityHeadersMiddleware.cs` | 69 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/SalesforceCore.AspNetCore.csproj` | 61 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforceFieldGroupTagHelper.cs` | 466 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforceFieldTagHelper.cs` | 176 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforceLookupTagHelper.cs` | 343 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforceModelFormTagHelper.cs` | 702 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforcePermissionTagHelper.cs` | 155 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/SalesforcePicklistTagHelper.cs` | 306 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TagHelpers/VisibilityTagHelper.cs` | 40 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/TokenProviders/DistributedCacheTokenProvider.cs` | 399 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, DataProtection, Races |
| `src/SalesforceCore.AspNetCore/TokenProviders/SessionTokenProvider.cs` | 180 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore.AspNetCore/ViewComponents/SalesforceRelatedListViewComponent.cs` | 337 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/ViewComponents/SalesforceToastViewComponent.cs` | 228 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, AccessCtrl, OutputEnc/XSS, CSRF |
| `src/SalesforceCore.AspNetCore/ViewModels/ViewModels.cs` | 408 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Lookup/_LookupResults.cshtml` | 47 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Salesforce/Details.cshtml` | 314 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Salesforce/Form.cshtml` | 180 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Salesforce/Index.cshtml` | 203 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Shared/Components/SalesforceRelatedList/Default.cshtml` | 120 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Shared/Components/SalesforceToast/Default.cshtml` | 77 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Shared/_FieldInput.cshtml` | 252 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/Shared/_LookupField.cshtml` | 65 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/Views/_ViewImports.cshtml` | 8 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/wwwroot/css/salesforce-core.css` | 887 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/wwwroot/js/salesforce-core.js` | 787 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/wwwroot/js/salesforce-forms.js` | 341 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.AspNetCore/wwwroot/js/salesforce-lookup.js` | 355 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore.ModelGenerator/ModelGenerator.cs` | 638 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, Path traversal, Codegen inj |
| `src/SalesforceCore.ModelGenerator/Program.cs` | 249 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, Path traversal, Codegen inj |
| `src/SalesforceCore.ModelGenerator/SalesforceCore.ModelGenerator.csproj` | 43 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, Path traversal, Codegen inj |
| `src/SalesforceCore.ModelGenerator/StaticTokenProvider.cs` | 44 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, Path traversal, Codegen inj |
| `src/SalesforceCore/Attributes/SalesforceAttributes.cs` | 378 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Extensions/PermissionServiceExtensions.cs` | 27 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Extensions/ServiceCollectionExtensions.cs` | 429 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Infrastructure/Locking/IDistributedLockHandle.cs` | 13 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Locking/IDistributedLockProvider.cs` | 22 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Locking/SqlServer/SqlAppLockProvider.cs` | 175 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Processing/ChannelBatchProcessor.cs` | 163 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Processing/ChannelBatchProcessorOptions.cs` | 25 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Processing/IBatchProcessor.cs` | 16 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Infrastructure/Processing/IChannelBatchHandler.cs` | 14 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Mapping/SalesforceMapper.cs` | 824 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Authorization/PermissionAuditResult.cs` | 111 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Models/Authorization/PermissionGuardResult.cs` | 113 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Models/Authorization/PermissionManifest.cs` | 173 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Models/Authorization/PermissionModels.cs` | 436 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Models/Configuration/DynamicUiOptions.cs` | 760 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Configuration/ModuleConfig.cs` | 545 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Configuration/SalesforceOptions.cs` | 570 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Configuration/SalesforceOptionsValidator.cs` | 106 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Configuration/SqlCacheWriteBehindOptions.cs` | 46 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Models/Configuration/VisibilityOptions.cs` | 59 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Data/BulkModels.cs` | 323 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/CompositeModels.cs` | 531 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/LookupResult.cs` | 173 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/QueryResult.cs` | 212 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/ReplicationModels.cs` | 257 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/ReportModels.cs` | 483 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/SearchModels.cs` | 348 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Data/ToolingModels.cs` | 443 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Errors/SalesforceException.cs` | 457 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Layout/LayoutDescriptors.cs` | 971 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Models/Metadata/GlobalDescribe.cs` | 167 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Metadata/SObjectDescribe.cs` | 221 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Metadata/SObjectField.cs` | 334 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Security/AccessMode.cs` | 36 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Models/Security/CachedToken.cs` | 52 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, SOQL/SOSL inj, DataProtection, Races |
| `src/SalesforceCore/Properties/AssemblyInfo.cs` | 3 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Query/SalesforceQueryable.cs` | 2637 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Query/SoqlExpressionVisitor.cs` | 720 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/SalesforceConstants.cs` | 467 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/SalesforceCore.csproj` | 65 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Schema/FieldDependencyGraph.cs` | 458 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Schema/RecordTypeManager.cs` | 309 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Schema/SchemaDiff.cs` | 594 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Schema/TypeDescriptor.cs` | 482 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Security/FieldLevelSecurity.cs` | 525 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Security/MissingConfigurationTokenProvider.cs` | 218 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, SOQL/SOSL inj |
| `src/SalesforceCore/Security/SalesforceUrlValidator.cs` | 98 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Security/SensitiveDataRedactor.cs` | 49 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Apex/ApexService.cs` | 142 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Apex/IApexService.cs` | 131 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Authorization/DefaultUserContextProvider.cs` | 21 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/Handlers/RoleHandler.cs` | 26 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/Handlers/SalesforcePermissionHandler.cs` | 54 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IPermissionGuard.cs` | 54 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IPermissionGuardBuilder.cs` | 58 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IPermissionService.cs` | 138 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IUserContextProvider.cs` | 16 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IVisibilityRequirementHandler.cs` | 27 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/IVisibilityService.cs` | 18 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/PermissionGuard.cs` | 290 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/PermissionService.cs` | 525 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Authorization/VisibilityService.cs` | 108 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Caching/CacheProviderType.cs` | 31 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/DistributedCacheProvider.cs` | 298 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/ICacheProvider.cs` | 50 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/MemoryCacheProvider.cs` | 192 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/CacheAccessEvent.cs` | 14 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/EncryptedCacheEntry.cs` | 88 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/EncryptedSqlServerCache.cs` | 729 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/EncryptedSqlServerCacheCleanupService.cs` | 263 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/EncryptedSqlServerCacheDbContext.cs` | 122 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/SqlCacheAccessBatchHandler.cs` | 169 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/SqlServerCacheExtensions.cs` | 308 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Caching/SqlServer/SqlServerCacheOptions.cs` | 133 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, DataProtection, Races |
| `src/SalesforceCore/Services/Configuration/ConfigurationService.cs` | 245 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Configuration/IConfigurationService.cs` | 91 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/BulkService.cs` | 654 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/ClientCredentialsTokenProvider.cs` | 232 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets, Comms/TLS/URL trust, HttpClient lifetime |
| `src/SalesforceCore/Services/Core/CompositeService.cs` | 514 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/IBulkService.cs` | 264 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/ICompositeService.cs` | 207 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/ILimitsService.cs` | 198 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/ISalesforceClient.cs` | 182 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, Comms/TLS/URL trust, HttpClient lifetime |
| `src/SalesforceCore/Services/Core/ISalesforceUrlBuilder.cs` | 96 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/JwtTokenProvider.cs` | 319 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Core/LimitsService.cs` | 126 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/SalesforceClient.cs` | 689 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, Comms/TLS/URL trust, HttpClient lifetime |
| `src/SalesforceCore/Services/Core/SalesforceUrlBuilder.cs` | 161 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/SynchronizationService.cs` | 64 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Core/TokenRefreshBackgroundService.cs` | 72 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, OAuth/Session, Crypto/Secrets |
| `src/SalesforceCore/Services/Data/DataService.cs` | 1511 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/IDataService.cs` | 559 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/ILookupService.cs` | 72 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/IReplicationService.cs` | 129 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/ISearchService.cs` | 96 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/LookupService.cs` | 371 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/ReplicationService.cs` | 297 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/SearchService.cs` | 127 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Data/TypedDataService.cs` | 371 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Files/FileService.cs` | 241 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Files/IFileService.cs` | 169 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Layout/DynamicUiConfigProvider.cs` | 262 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Layout/IDynamicUiConfigProvider.cs` | 21 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Layout/ILayoutDescriptorService.cs` | 144 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Layout/LayoutDescriptorService.cs` | 1546 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Metadata/ISchemaService.cs` | 249 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Metadata/SchemaService.cs` | 614 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Query/SoqlAggregateCondition.cs` | 242 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Query/SoqlBuilder.cs` | 1416 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Query/SoqlCondition.cs` | 593 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Services/Reports/IReportService.cs` | 216 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Reports/ReportService.cs` | 497 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Tooling/IToolingService.cs` | 220 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Services/Tooling/ToolingService.cs` | 299 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Tracking/ChangeTracker.cs` | 501 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Tracking/FieldHistoryService.cs` | 480 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/BitmaskUtils.cs` | 49 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/FieldTypeConverter.cs` | 474 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/JsonNodeExtensions.cs` | 210 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/SalesforceConventions.cs` | 345 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/SalesforceDateTimeConverter.cs` | 168 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Utilities/SecurityUtils.cs` | 888 | Deep manual (line-by-line) | InputVal, ErrHandling/Logging, SOQL/SOSL inj |
| `src/SalesforceCore/Utilities/UrlUtils.cs` | 18 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Validation/FieldValidator.cs` | 497 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Validation/IFieldValidator.cs` | 160 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Validation/ValidationRuleEngine.cs` | 365 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |
| `src/SalesforceCore/Validation/ValidationRules.cs` | 490 | Pattern-assisted manual skim + analyzers/Semgrep | InputVal, ErrHandling/Logging |

Total files: 174; deep-reviewed: 33

> **What "review depth" means:** "Deep manual" files were read line by line against the full checklist. "Pattern-assisted" files (models, DTOs, layout and validation helpers, attributes) were skimmed manually. For those I grepped every string-built query, HTML, URL, path, regex, crypto and process API, and ran the analyzers and Semgrep over them. I don't claim a line-by-line read of those 141 files. Regex DoS: I checked all 12 `Regex` call sites in `src/`. One is catastrophic (SFC-25, `FieldValidator.UrlRegex`). The rest are anchored and linear or bounded: ID and identifier checks, `StripHtml` `<[^>]*>`, the source-generated redactor patterns, and ModelGenerator's wildcard filter, which escapes its input and uses only `.*`/`.`.

## 4. Findings

Line numbers refer to the v1.2.0 baseline (`c264774`).

**Severity scale:** High = exploitable by an ordinary authenticated user with real confidentiality or integrity impact. Medium = needs an extra precondition or deployment mode. Low = defence in depth or limited impact. Info = correctness or hygiene. The CVSS-ish notes give the reasoning, not formal scores.

| ID | Sev | Conf | File:line (v1.2.0) | Description | Exploit scenario | Fix (v1.3.0) | Effort | Breaking? | Status |
|---|---|---|---|---|---|---|---|---|---|
| SFC-01 | **High** (AV:N/PR:L/UI:R/S:C/C:H/I:H ≈ 7.3) | High | `AspNetCore/Controllers/FileController.cs:56-136` | `Preview` and `GetImage` take the MIME type from the `type` query string, and `Preview` adds `Content-Disposition: inline`. `.xml`, `.json`, `.txt` and `.csv` are allowed uploads by default. | A user uploads `x.xml` containing an XHTML `<script>`, then sends a colleague `/File/Preview/{versionId}?type=xml`. The script runs on the app origin (the default CSP allows `'unsafe-inline'`) and can act as the victim, including CSRF-protected writes. The same works with any file the victim can read in Salesforce. | Inline only for an allow-list (jpeg/png/gif/webp/bmp/pdf; `GetImage` is images only). Everything else becomes `application/octet-stream` as an attachment. All file responses get `nosniff` and `CSP: default-src 'none'; sandbox`. | S | Behaviour change: txt/csv/xml/json previews now download | Fixed + 13 tests |
| SFC-02 | Medium (PR:L, C:L-H depending on policy) | High | `SalesforceController.cs:158-214` | `Details` doesn't evaluate `ModuleConfig.VisibilityPolicy`, but Index, Create, Edit, Delete and Upload all do. | A user blocked from `Opportunity` by policy reads any record directly at `/Salesforce/Opportunity/Details/{id}`. With integration-user auth, Salesforce sharing doesn't help. | Added the same policy check. | XS | No | Fixed + test |
| SFC-03 | Medium (needs cache read access; C:H) | High | `TokenProviders/DistributedCacheTokenProvider.cs:134-160, 357-366` | Access and refresh tokens are written to `IDistributedCache` (Redis or SQL) as plaintext JSON. SECURITY.md claimed "plaintext tokens in cache" was fixed, but that only covered the refresh coordinator. | Anyone who can read Redis or the cache table (ops staff, a backup, a misconfigured Redis) gets long-lived refresh tokens for every user. | Encrypt with Data Protection (`dp1:` prefix). Refuse plaintext unless `AllowUnprotectedTokenStorage=true`. Ignore legacy plaintext entries. `AddSalesforceDistributedCacheTokenStorage` now registers Data Protection. | S | Yes: needs Data Protection (registered automatically; web farms must persist keys). Old entries are dropped, so users log in once more. | Fixed + 4 tests |
| SFC-04 | Medium (session fixation; AC:H) | Medium | `DistributedCacheTokenProvider.cs:70-91` | The cache key is `Session.Id` when a session exists. ASP.NET Core doesn't rotate session IDs on sign-in, and the key isn't bound to the user. | An attacker plants a known session cookie (sibling subdomain, or a shared machine before the victim logs in). After the victim's login stores tokens under that session ID, the attacker's requests with the same cookie resolve to the victim's Salesforce tokens. | The key is always `SHA-256(NameIdentifier/sub)` plus the session ID. With no user, it throws unless `AllowAnonymousSessionCacheKey=true`. | S | Yes: cache key format changed (one re-login) | Fixed + 2 tests |
| SFC-05 | Medium (RFC 9700 §2.2.2 / RFC 7009) | High | `Extensions/AspNetCoreTokenProvider.cs:130-170` | `RevokeTokenAsync` revokes only the access token. The refresh token (stored in the auth ticket, or in the cache under SFC-03) stays valid after logout. | A stolen ticket or cache entry still mints access tokens after the user logs out. | Revoke the refresh token when there is one (Salesforce then invalidates its access tokens), otherwise the access token. Also disposes the `FormUrlEncodedContent`. | XS | No | Fixed + 2 tests |
| SFC-06 | Medium (availability; A:M) | High | `AspNetCoreTokenProvider.cs:40, 183` | `private static readonly SemaphoreSlim _refreshLock` serialises all users' refreshes process-wide, with a network call held under the lock and a 30 s wait. README claimed "serialized per session". | One slow or hung Salesforce token call stalls token refresh for every user. Under a 401 storm, waiters time out and fall through to stale tokens. | 64 striped locks keyed by the stable `sf_session_id` (falling back to the user id). | S | No | Fixed + test |
| SFC-07 | Medium (C:L / I:L) | Medium | `Services/Metadata/SchemaService.cs:60, 225` | Describe and global-describe results are cached by object name only. Salesforce evaluates `accessible/createable/updateable` for the **calling user**, so in user-context (OIDC) auth the first caller's view is served to everyone. | An admin warms the cache, then a low-privilege user's forms show and submit fields they can't edit. FLS-aware field lists (`GetAccessibleFieldsAsync`) come from the admin's describe. The Salesforce API still enforces FLS for the user token, so the impact is metadata disclosure plus wrong UI and authorization decisions, not record data. | Per-user partition of describe cache keys (hashed user id) when a user is authenticated. Opt-out: `PartitionSchemaCacheByUser=false` for single integration-user apps. | S | More describe calls (API usage); opt-out available | Fixed + 2 tests |
| SFC-08 | Low (needs attacker-influenced instance_url) | Medium | `Security/SalesforceUrlValidator.cs:12-25` | The allow-list included `.salesforce-sites.com` (public Sites, whose content and public Apex REST are controlled by any Salesforce customer). It's never an API instance host. | Defence in depth: if an `instance_url` is ever attacker-influenced (a tampered cache under SFC-03/04), the bearer token could go to another tenant's public Apex endpoint, which still passes "is Salesforce". | Removed from the defaults. Re-enable with `AdditionalAllowedHosts`. Added parser-trick regression tests (`\@`, `#`, `?`, `%2F`, bare apex, trailing dot, port, punycode). The residual risk for `.force.com` (legacy Sites) is documented. | XS | Only for anyone using a Sites host as API host (unusual) | Fixed + 13 tests |
| SFC-09 | Low (CSRF; A:L) | High | `Controllers/DynamicUiController.cs:259` | `POST api/dynamic-ui/refresh` is cookie-authenticated with no antiforgery check. Any authenticated user can also flush all layout caches. | A cross-site form auto-posts to flush caches repeatedly, driving up API calls and latency. | `[ValidateAntiForgeryToken]` (send the `RequestVerificationToken` header). Restricting the endpoint to admins is still an app decision (SFC-18). | XS | Yes for clients that don't send the token | Fixed + test |
| SFC-10 | Low (info disclosure) | High | `SalesforceController.cs:300-310, 437-443, 500-506, 570` | `ex.Message` from any exception is shown in TempData and in the HTMX response body. | DB, network or internal messages (hosts, SQL, file paths) leak to end users. | Only `SalesforceException` messages (user-facing validation) are shown; anything else gets a generic message. The full exception is still logged. | XS | No | Fixed + test |
| SFC-11 | Low | High | `SalesforceController.cs:106` | The search box uses `SoqlCondition.Like(name, $"%{q}%")`. `%` and `_` in `q` aren't escaped. This isn't injection, since quotes are escaped. | `q=_` or `%%%` gives unbounded, expensive matches and enumeration within the object. | New `SoqlCondition.Contains` and `StartsWith` escape wildcards with `SanitizeSoqlLike`. The controller uses `Contains`. | XS | No (search now literal) | Fixed + 3 tests |
| SFC-12 | Low (DoS / API limits) | High | `LookupController.cs:44-60, 104`; `LookupService.cs:90` | The `limit` query parameter is unbounded for Search. `Recent` throws a 500 when it's out of range. | `?limit=50000` queries pull large result sets and burn API limits. | Clamped to 1–200 in the controller and the service. | XS | No | Fixed + 3 tests |
| SFC-13 | Low | Medium | `SalesforceController.cs:515-560` | Upload checks the visibility policy of `{sObject}` but attaches the file to any `{id}`. | A user allowed on `Case` attaches files to records of a policy-hidden object by routing through `/Salesforce/Case/Upload/{otherId}`. | The record ID must start with the routed object's `KeyPrefix`. | XS | No | Fixed + test |
| SFC-14 | Low (robustness) | High | `DataService.cs:157-159`; `SalesforceController.cs` Index | `(page-1)*pageSize` overflows int for huge `page`, then the Offset guard throws, giving an unhandled 500. SOQL's maximum OFFSET is 2000. | `?page=2147483647` gives a 500. | 64-bit maths plus an explicit 2000 cap (`ArgumentOutOfRangeException`). The controller returns 400. | XS | No | Fixed + 5 tests |
| SFC-15 | Info (correctness) | High | `JwtTokenProvider.cs:226-245` | `iss` and `aud` are added both as claims and as constructor arguments, so the assertion carries duplicate claims (`aud` as an array). | Some IdP validators reject this, and it's non-standard per RFC 7523. | Only `sub` goes in the claim list. The test asserts a single iss/aud and RS256. | XS | No | Fixed + test |
| SFC-16 | Low (needs DB write) | High | `EncryptedSqlServerCache.cs:339-385` | AES-256-GCM without associated data, so a ciphertext isn't bound to its row key. | Someone with SQL write access copies user A's encrypted token row onto user B's key, and B's session then runs as A. | The cache key is bound as AES-GCM AAD. Old entries fail authentication and are treated as cache misses. | XS | Old encrypted entries are invalidated (cache) | Fixed + test |
| SFC-17 | Low (defence in depth) | Medium | `TagHelpers/SalesforceLookupTagHelper.cs:276-310` | `SearchFields`, `inputId`, `fieldName` and target-object names are interpolated into attributes without encoding. The values come from developers and metadata. | Metadata or dev-supplied values containing `"` break out of the attribute. | `HtmlAttributeEncode` on all of them. | XS | No | Fixed + test |
| SFC-25 | Medium (AV:N/PR:L/A:H ≈ 6.5) | High | `Validation/FieldValidator.cs:21-23` | `UrlRegex` = `^(https?://)?([\da-z.-]+)\.([a-z.]{2,6})([/\w .-]*)*/?$` has a nested quantifier `( … * )*`, which gives exponential backtracking. No match timeout is set. I measured with an equivalent engine: +2 characters means about 4× the time, and 24 characters already takes 2.8 s. | A user submits a URL field value like `http://a.bc/aaaa…a!` (40+ characters). A request thread pins a CPU core indefinitely, and a few requests take the app down. | Removed the outer quantifier, added a 250 ms match timeout to all validator regexes, and treat a timeout as "invalid". | XS | No | Fixed + 4 tests |
| SFC-18 | Medium (design) | High | `SalesforceController.cs` (whole controller), `FileController`, `LookupController` | Any authenticated app user can list, read, create, edit and delete **any** sObject and ContentVersion. There's no object allow-list unless a `VisibilityPolicy` is configured per module. With JWT or client-credentials (integration user) auth, every app user effectively has the integration user's Salesforce privileges (IDOR across the org). | An app user browses `/Salesforce/User`, `/Salesforce/PermissionSetAssignment` or `/File/Download/{anyVersionId}`. | **Deferred.** It needs a product decision (deny-by-default object allow-list, `RequireConfiguredModule` option). Fixing it silently would break every existing app. Recommendation for 1.4: add `SalesforceMvcOptions.AllowedObjects` and default-deny when auth isn't user-context. | M | Yes | **Deferred** |
| SFC-19 | Info | Medium | `AspNetCoreTokenProvider.cs` refresh; `DistributedCacheTokenProvider.cs` refresh | Salesforce's token-response `signature` (HMAC-SHA256 of `id`+`issued_at` with the client secret) isn't verified. The response comes over TLS from an allow-listed host, so this is low value. | — | Deferred: only meaningful with a client secret, and TLS plus the allow-list already authenticate the endpoint. | S | No | Deferred |
| SFC-20 | Info (config) | High | `AspNetCore/Extensions/ServiceCollectionExtensions.cs:178-205, 214` | `AddDataProtection()` without key persistence, so keys are per-process or ephemeral in containers and cookies break across instances. `bool.Parse` and `TimeSpan.Parse` on config throw raw `FormatException` at startup. | Operational, not exploitable. | Deferred: documented in the README (persist the key ring). The Parse behaviour fails closed. | XS | — | Deferred |
| SFC-21 | Info (reliability) | High | Analyzer CA2000 ×6, CA2016 ×5 (`JwtTokenProvider`, `ClientCredentialsTokenProvider`, `AspNetCoreTokenProvider`, `LayoutDescriptorService`) | Undisposed `FormUrlEncodedContent`, and CancellationTokens not forwarded to `EvaluatePolicyAsync`. | Resource churn; requests can't be cancelled. | `using var`, and the tokens are forwarded. | XS | No | Fixed |
| SFC-22 | Low (defence in depth) | High | `Middleware/SecurityHeadersMiddleware.cs:50-55` | The default CSP is `script-src 'self' 'unsafe-inline'`, which removes CSP as a backstop for any XSS (it amplified SFC-01). | — | **Deferred:** removing `'unsafe-inline'` needs nonces in the TagHelpers and views, which is a larger change. It can already be overridden with `Salesforce:Security:ContentSecurityPolicy`. | M | Yes | Deferred |
| SFC-23 | Info (API design) | High | `Models/Data/SearchModels.cs:140-160` (`ReturningClause.WhereClause/OrderBy`); `SalesforceQueryable` raw `whereClause` overloads | These public members accept raw SOQL fragments by design. They're injection sinks if a consumer passes user input. | A consumer builds `WhereClause = "Name = '" + input + "'"`. | Documented here as unsafe-by-design. Typed alternatives exist (`SoqlCondition`). | — | — | Accepted / documented |
| SFC-24 | Low | Medium | `Services/Authorization/PermissionService.cs:509-524` | The per-user permission cache suffix falls back to `Identity.Name` (not unique) when there's no NameIdentifier/sub claim. | Two users with the same display name in a non-OIDC setup share permission snapshots. | **Deferred** (to be paired with SFC-18 in 1.4): the OIDC handler always maps `sub`, so this is reachable only with custom auth. | XS | Possibly | Deferred |

### Counts by severity

| Severity | Total | Fixed in 1.3.0 | Deferred | Accepted (by design) |
|---|---|---|---|---|
| High | 1 | 1 | 0 | 0 |
| Medium | 8 | 7 | 1 (SFC-18) | 0 |
| Low | 11 | 9 | 2 (SFC-22, SFC-24) | 0 |
| Info | 5 | 2 (SFC-15, SFC-21) | 2 (SFC-19, SFC-20) | 1 (SFC-23) |
| **Total** | **25** | **19** | **5** | **1** |

## 5. Verified-sound areas and false positives dismissed

| # | Candidate | Verdict | Why |
|---|---|---|---|
| FP-1 | Semgrep `missing-or-broken-authorization` on `SalesforceControllerBase<T>` | False positive | It's an abstract base with **no actions**. Authorization is the deriving controller's job, which the XML doc shows. |
| FP-2 | SQL string building with `qualifiedTable` (`EncryptedSqlServerCache.cs:660`, `SqlCacheAccessBatchHandler.cs:53`) | False positive | The table and schema come from the EF model (developer-controlled), not from requests. Values are parameterised. |
| FP-3 | ModelGenerator `File.WriteAllTextAsync` | Not exploitable | v1.2.0's `GetSafeOutputPath` checks `GetFileName` equality, invalid characters, `.`/`..` and a root-prefix check on the full path. No Process or shell invocation exists in ModelGenerator. |
| FP-4 | `SalesforceUrlValidator` bypasses | None found | `Uri.IdnHost` normalises punycode and IDN dots. The suffix match needs a leading dot and a non-empty label. `userinfo` is rejected, https is enforced, and backslash/`#`/`?`/`%2F` don't change the parsed host (new tests cover this). Ports are allowed but harmless (same Salesforce host). |
| FP-5 | `EscapeSoqlStringLiteral` | Sound | It escapes `\` first in a single pass, plus `'` `"` `\n` `\r` `\t`. There's no double escaping, and Unicode quote homoglyphs are normalised by `SanitizeSoql`. `SanitizeSoqlLike` adds `%`/`_` after the escaping (verified by the new `Contains` tests: `a\` becomes `a\\`). |
| FP-6 | SOSL escaping (`SearchModels.EscapeSoslTerm` leaves `*`/`?`) | Intentional | Wildcards are a documented feature. `}` and `{` are escaped, so you can't break out of `FIND {}`. |
| FP-7 | OIDC handler (PKCE, state, nonce, `RequireHttpsMetadata`, `__Host-` cookie, SameSite=Lax, `SaveTokens`) | Sound per RFC 9700 | Code flow plus PKCE S256 and the framework's state/nonce. Redirect URIs are registered server-side at Salesforce. |
| FP-8 | JWT bearer assertion | Sound | RS256 is hard-coded (no `alg` choice), `exp` is 3 min, `aud` is the configured login host, the key is loaded from PEM/PKCS#8 and disposed after signing. |
| FP-9 | HttpClient lifetime | Sound | `IHttpClientFactory` and typed clients everywhere. No `new HttpClient()` in `src/`. |
| FP-10 | Deserialization | Sound | Only `System.Text.Json`, no polymorphic type handling. No BinaryFormatter, Newtonsoft TypeNameHandling or XmlSerializer on untrusted input. |
| FP-11 | Bulk API CSV "formula injection" | Not applicable | Salesforce doesn't evaluate formulas on ingest. Results returned to consumers are raw CSV; spreadsheet escaping is the consumer's job when exporting. Noted in SFC-23. |
| FP-12 | Tag helpers other than Lookup | Sound | User and record data go through `HtmlEncode`/`HtmlAttributeEncode`. Remaining unencoded interpolations are constants (`$`, `%`, CSS classes). |

## 6. Self-review (performed after the initial scan, before implementation)

I re-read each finding against the code, re-rated the severities and stress-tested each proposed fix:
- **SFC-01, kept at High.** Re-verified the chain: `.xml` is in the default `AllowedFileExtensions`, `GetMimeType("xml")` gives `application/xml`, and `Preview` sets `inline`. Browsers render `application/xml` with the XHTML namespace as XHTML and run inline script under the default CSP. Fix bypass check: the type is taken only from the allow-list (no extension sniffing). PDF stays inline but sandboxed; I judged that acceptable because browser PDF viewers don't run on the app origin.
- **SFC-07, downgraded from "FLS bypass" to Medium "metadata disclosure / wrong UI decisions".** The REST API still enforces FLS for user tokens, so no record data leaks. The fix adds API calls, hence the opt-out.
- **SFC-08, downgraded to Low.** It needs an attacker-influenced `instance_url`, which already requires SFC-03/04 or a token-endpoint compromise.
- **SFC-04, confidence set to Medium.** Exploitation needs cookie injection, either from a sibling subdomain or by pre-seeding before login. The fix (user binding) closes it regardless.
- **SFC-03/04 fix sanity.** Keys and format change, so existing cache entries are ignored and users log in once. This is documented as breaking. The plaintext opt-out exists for dev, and legacy plaintext entries are never trusted when protection is on, so a cache writer can't inject a token.
- **SFC-06 fix sanity.** The stripe key is computed before the lock from the cookie ticket (a cheap local decode). The distributed lock path is unchanged.
- **SFC-18 deliberately not "fixed".** Any secure default (deny unknown objects) would break all current consumers. It's flagged as the top item for 1.4.
- **Added during review:** SFC-25. The ReDoS turned up when I re-checked my own "regex is fine" claim call site by call site, rather than trusting the first pass.
- **Dropped during review:** "SQL injection via table name" (FP-2), "path traversal in ModelGenerator" (FP-3), and "SOSL wildcard injection" (FP-6).

## 7. Prioritised fix plan (batches)

| Batch | Items | Status |
|---|---|---|
| 1. Exploitable now | SFC-01, SFC-02, SFC-03, SFC-04, SFC-05 | Done in v1.3.0 |
| 2. Robustness and authorization correctness | SFC-25, SFC-06, SFC-07, SFC-09, SFC-10, SFC-13 | Done in v1.3.0 |
| 3. Hardening and hygiene | SFC-08, SFC-11, SFC-12, SFC-14, SFC-15, SFC-16, SFC-17, SFC-21 | Done in v1.3.0 |
| 4. Design changes (1.4, needs your decision) | SFC-18 object allow-list / deny-by-default with integration-user auth; SFC-22 nonce-based CSP; SFC-24 | Deferred |
| 5. Optional | SFC-19 response signature check; SFC-20 key-ring persistence guidance and friendlier config parse errors; CA2007 `ConfigureAwait(false)` sweep (≈1,000 sites, library hygiene, not security) | Deferred |

**Analyzer suppressions (justified, not silenced in code):** CA2007 (missing ConfigureAwait) and CA2227 (settable collection properties on DTOs and options) are style and API-shape rules. They don't affect security, and changing CA2227 would break the public API. CA2234/CA2263/CA2249/CA2008/CA2208 are minor quality items.

## 8. Test-suite review

- **Before:** 638 tests, with **no** tests for `SalesforceController`, `FileController`, `LookupController`, `DynamicUiController`, `AspNetCoreTokenProvider` or `DistributedCacheTokenProvider`, and none for describe-cache partitioning or `EncryptedSqlServerCache` crypto.
- **After:** **698** tests (+60 in `ScanFixesV130Tests.cs`), all offline (Moq, in-memory caches, an ephemeral Data Protection provider and a capturing HTTP handler). Every fixed finding has at least one regression test.
- **Remaining gaps:** no full WebApplicationFactory or Razor integration tests (antiforgery is asserted by attribute presence, not over HTTP), and no concurrency or soak test for the striped refresh locks.

## 9. Public API: unsafe-by-default review

| Option | Default | Assessment |
|---|---|---|
| `ValidateInstanceUrl` | true | Good. Opt-out documented as test-only. |
| `ValidateSoqlInputs` | true | Good. Turning it off removes record-ID path validation. |
| `EnforceFieldLevelSecurity` | true | Good. |
| `ForceSecureCookie` | true | Good. |
| `AllowInsecureSqlCacheKeyDerivation` | false | Good. |
| `AllowedFileExtensions` | includes `.xml`, `.json`, `.txt`, `.csv` | Safe now that SFC-01 forces them to download. |
| `EnableFileUploads` | **true** | Consider defaulting to false in 1.4 (pairs with SFC-18). |
| `EnableDebugLogging` | false | Logs URLs that contain SOQL. Keep it off in production. |
| New in 1.3.0 | `PartitionSchemaCacheByUser=true`, `AllowUnprotectedTokenStorage=false`, `AllowAnonymousSessionCacheKey=false` | Secure defaults with documented opt-outs. |
| `SalesforceMvcOptions` object allow-list | **missing** | See SFC-18. |
