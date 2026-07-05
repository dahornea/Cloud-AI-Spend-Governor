# Testing

The repository uses a console scenario test project rather than xUnit/NUnit.

Run all scenario tests:

```powershell
dotnet run --project tests\SpendGovernor.Tests\SpendGovernor.Tests.csproj
```

Build all projects:

```powershell
dotnet build CloudAiSpendGovernor.slnx
```

## What The Tests Cover

The scenario suite covers:

- Basic PR analysis outcomes.
- Terraform plan JSON detection, parsing, deltas, removals, and invalid input.
- Bicep compiled ARM JSON detection, parsing, expression handling, persistence, and report metadata.
- Raw Terraform/Bicep fallback behavior.
- AI workflow token cost estimation.
- Pricing Catalog v2 loading, validation, matches, fallback, and confidence impact.
- Mocked Azure Retail Prices API client/provider behavior.
- Policy evaluation and recommendations.
- PR report formatting.
- GitHub webhook HMAC verification.
- Simulated and real GitHub reporter behavior with fake clients.
- EF Core persistence using SQLite in-memory fixtures.
- Dashboard scan detail mapping.
- Workspace/project/repository scoping.
- Background scan queue round-trip behavior.

## What Is Mocked

- Azure Retail Prices API calls are tested with fake HTTP/client responses.
- GitHub API publishing is tested with fake GitHub clients.
- EF persistence tests use SQLite in-memory fixtures.

## What Does Not Require Secrets

The test suite does not require cloud credentials, GitHub credentials, Azure CLI, Terraform, Bicep CLI, or internet access.

## CI

The GitHub Actions workflow runs:

```bash
dotnet restore CloudAiSpendGovernor.slnx --configfile NuGet.Config
dotnet build CloudAiSpendGovernor.slnx --configuration Release --no-restore
dotnet run --configuration Release --no-build --project tests/SpendGovernor.Tests/SpendGovernor.Tests.csproj
```
