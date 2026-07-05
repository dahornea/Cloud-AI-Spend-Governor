# Demo Script

## Goal

Show how Cloud & AI Spend Governor detects risky cloud/AI cost changes before merge/deploy.

## Setup

```powershell
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Open http://localhost:5102. Register a local user or use the development default `demo@spendgov.local`.

## Demo Flow

1. Open the dashboard and show the workspace/project selector.
2. Click `Seed Demo Data` and explain that the app writes one repository and three persisted scan results into SQL Server.
3. Show `Latest Analyses`: repository, PR number, environment, status, decision, monthly delta, confidence, created time, and completed time.
4. Open the cheap cloud change scan and explain PASS: small storage/App Service delta, high confidence, no blocking action.
5. Open the expensive cloud change scan and explain FAIL: premium Redis/larger capacity exceeds policy and the recommendation points to cheaper dev or environment-specific SKUs.
6. Open the expensive AI workflow scan and explain token math: model, monthly runs, average input tokens, average output tokens, catalog pricing, and final monthly estimate.
7. Show the cost breakdown, detected resources, assumptions, and policy evaluations on the scan detail page.
8. Show `docs/sample-pr-report.md` or a simulated GitHub report and explain that real mode can post the same report to a GitHub Pull Request.
9. Explain how this helps a developer fix the PR before merge.

## Key Talking Points

- Cost visibility before deploy, not after invoice.
- IaC analysis from Terraform plan JSON, Bicep compiled ARM JSON, and pragmatic fallbacks.
- AI workflow cost estimation from model, tokens, and monthly runs.
- Budget-based PASS/WARN/FAIL policy.
- Persisted scan history for demos and auditability.
- Developer-friendly GitHub PR reports.
- Simulated GitHub mode is honest local demo mode; real GitHub App publishing is available when configured.

## Reset

```powershell
Invoke-RestMethod -Method Delete http://localhost:5102/api/dev/demo/reset
```

## Optional Bicep/ARM Moment

Run one of the Bicep ARM demo scenarios from the dashboard and point out that compiled ARM JSON is preferred over raw Bicep because it exposes structured Azure resource metadata.
