# Model Generator CLI (`sf-gen`)

Generate strongly-typed C# models from Salesforce metadata.

## Requirements
- **Required**: .NET 10; authenticated Salesforce access token and instance URL.
- **Recommended**: Run against a sandbox first; commit generated models to source control.
- **Optional**: Custom namespaces/output paths.

## Install
```bash
dotnet tool install -g SalesforceCore.ModelGenerator
```

## Environment Variables
| Variable | Required | Description |
|----------|----------|-------------|
| `SF_INSTANCE_URL` | Yes | e.g., https://myorg.my.salesforce.com |
| `SF_ACCESS_TOKEN` | Yes | OAuth/JWT/Client Credentials token with metadata scope. |

## Common Commands
- Generate specific objects:
```bash
sf-gen generate Account Contact Lead -o ./Models -n MyApp.Models
```

- Generate all standard objects (include custom):
```bash
sf-gen generate "*" -o ./Models --include-custom
```

- List available objects:
```bash
sf-gen list --filter "Account*"
```

- Describe object fields:
```bash
sf-gen describe Account --fields-only
```

## Output
- Creates C# classes with `[SalesforceObject]`, `[SalesforceField]`, and related attributes.
- Namespaces use `-n` parameter (default: `SalesforceModels`).
- API version defaults to `v62.0` (override with `--api-version`).
- Respects field types, required flags, and lengths.

## Tips
- Regenerate models when API version changes or metadata evolves.
- Pair generated models with `ITypedDataService` for LINQ queries.
- Keep generated files in a dedicated project to avoid runtime coupling.

## Next Steps
- Typed usage: [05-Typed-Data-Service.md](05-Typed-Data-Service.md).
- Authentication to obtain tokens: [02-Authentication.md](02-Authentication.md).
