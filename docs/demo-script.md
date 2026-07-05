# Demo Script

Use this flow for a 3-5 minute walkthrough.

## Setup

```powershell
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Open http://localhost:5102. Register a local user or use the development default `demo@spendgov.local`.

## Walkthrough

1. Open the dashboard and show the workspace/project selector.
2. Click `Seed Demo Data` and explain that the app writes one repository and three persisted scan results into SQL Server.
3. Show `Latest Analyses`: repository, PR number, environment, status, decision, monthly delta, confidence, created time, and completed time.
4. Open the cheap cloud change scan and explain PASS: small storage/App Service delta, high confidence, no blocking action.
5. Open the expensive cloud change scan and explain FAIL: premium Redis/larger capacity exceeds policy and the recommendation points to cheaper dev or environment-specific SKUs.
6. Open the expensive AI workflow scan and explain token math: model, monthly runs, average input tokens, average output tokens, catalog pricing, and final monthly estimate.
7. Show the cost breakdown, detected resources, assumptions, and policy evaluations on the scan detail page.
8. Explain that real usage posts the same report back to the GitHub Pull Request as one idempotent comment/check run.

## Reset

```powershell
Invoke-RestMethod -Method Delete http://localhost:5102/api/dev/demo/reset
```

## Optional Bicep/ARM Moment

Run one of the Bicep ARM demo scenarios from the dashboard and point out that compiled ARM JSON is preferred over raw Bicep because it exposes structured Azure resource metadata.
