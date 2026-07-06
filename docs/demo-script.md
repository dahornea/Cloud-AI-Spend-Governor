# Demo Script

For the portfolio-facing walkthrough, use [demo-walkthrough.md](demo-walkthrough.md). This file remains as a compact local operator script.

## Goal

Show how Cloud & AI Spend Governor detects risky cloud/AI cost changes before merge/deploy.

## Setup

```powershell
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Open http://localhost:5102 for the public landing page. Open http://localhost:5102/dashboard for the app dashboard. Register a local user or use the development default `demo@spendgov.local`.

## Demo Flow

1. Open the public landing page and explain the positioning: pre-merge cloud and AI cost checks, not after-the-invoice FinOps.
2. Click `Open demo dashboard` and show the dashboard overview, workspace/project selector, monthly cost-at-risk metric, failed-check count, and confidence summary.
3. Click `Seed Demo Data` and explain that the app writes one repository and three persisted scan results into SQL Server.
4. Open `Analyses` and show the searchable/filterable scan list: repository, PR number, environment, status, decision, monthly delta, confidence, created time, and completed time.
5. Open the cheap cloud change scan and explain PASS: small storage/App Service delta, high confidence, and "No blocking action needed."
6. Open the expensive cloud change scan and explain FAIL: premium Redis, larger App Service, and Log Analytics ingestion exceed policy; the recommendation points to cheaper dev or environment-specific SKUs.
7. Open the expensive AI workflow scan and explain token math: `gpt-4.1`, 10,000 monthly runs, 8,000 input tokens, 2,000 output tokens, catalog pricing, and final monthly estimate.
8. On a scan detail page, show recommendations first, then main cost breakdown, detected resources, pricing metadata, assumptions, and policy evaluations.
9. Scroll to `GitHub PR Report Preview` and explain that simulated mode shows the same report shape locally while real mode can post it to a GitHub Pull Request.
10. Close by explaining that the developer can fix the PR before merge instead of discovering the spend increase on the next invoice.

## Key Talking Points

- Cost visibility before deploy, not after invoice.
- IaC analysis from Terraform plan JSON, Bicep compiled ARM JSON, and pragmatic fallbacks.
- AI workflow cost estimation from model, tokens, and monthly runs.
- Budget-based PASS/WARN/FAIL policy.
- Persisted scan history for demos and auditability.
- Developer-friendly GitHub PR reports.
- Simulated GitHub mode is honest local demo mode; real GitHub App publishing is available when configured.
- Compiled ARM JSON from Bicep is preferred over raw Bicep fallback because it exposes structured resource type, SKU, region, and API metadata.

## Reset

```powershell
Invoke-RestMethod -Method Delete http://localhost:5102/api/dev/demo/reset
```

## Optional Bicep/ARM Moment

Run one of the Bicep ARM demo scenarios from the dashboard and point out that compiled ARM JSON is preferred over raw Bicep because it exposes structured Azure resource metadata.

Recommended line:

> In CI, run `az bicep build --file infra/main.bicep --outfile infra/main.json`; Spend Governor analyzes the generated ARM JSON without running Azure CLI inside the web app.
