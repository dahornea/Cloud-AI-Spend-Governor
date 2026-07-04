# Cloud-AI-Spend-Governor

Cloud & AI Spend Governor is an MVP SaaS prototype for PR-time cloud and AI spend guardrails.

## Run

```powershell
dotnet restore CloudAiSpendGovernor.slnx --configfile NuGet.Config
dotnet build CloudAiSpendGovernor.slnx
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Open http://localhost:5102 and use `demo@spendgov.local`.

## Local Demo Data

Demo seeding is available only when the API runs in `Development`.

From the dashboard:

1. Open http://localhost:5102.
2. Click `Seed Demo Data`.
3. Open `Analyses` and select each seeded PR scan.
4. Click `Reset Demo Data` when you want to clear the demo repository and scan rows.

From PowerShell:

```powershell
Invoke-RestMethod -Method Post http://localhost:5102/api/dev/demo/seed
Invoke-RestMethod -Method Delete http://localhost:5102/api/dev/demo/reset
```

The seed flow recreates repository `acme/spend-governor-demo` in SQL Server LocalDB and writes three completed scans:

- `Cheap change`: PASS, small storage account plus small App Service plan, small monthly delta, high confidence.
- `Expensive cloud change`: FAIL, premium Redis and larger always-on cloud capacity, high monthly delta.
- `Expensive AI workflow`: WARN, `gpt-4.1` at 10,000 monthly runs, 8,000 input tokens, and 2,000 output tokens.

## Environment Variables

The MVP runs without secrets for local demos.

- `GitHub:WebhookSecret` or `GitHub__WebhookSecret`: webhook HMAC secret. Defaults to `dev-secret`.
- `ASPNETCORE_URLS`: optional server URL override, for example `http://localhost:5102`.
- `ConnectionStrings:SpendGovernorDb` or `ConnectionStrings__SpendGovernorDb`: SQL Server LocalDB connection string.

Development defaults to:

```txt
Server=(localdb)\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Real GitHub comment/check posting is not enabled yet. The app verifies webhook signatures and stores an idempotent simulated PR comment/check result for beta demos.

## GitHub Webhook Setup

For local webhook testing, send `POST /api/github/webhooks` with:

- `X-Hub-Signature-256: sha256=<hmac>` using the configured webhook secret.
- Standard `repository`, `installation`, and `pull_request` objects.
- Optional MVP local fields:
  - `spendgov_changed_files`: array of changed paths.
  - `spendgov_baseline_files`: array of `{ "path": "...", "content": "..." }`.
  - `spendgov_proposed_files`: array of `{ "path": "...", "content": "..." }`.

Repeated deliveries for the same repository and PR update one stored Spend Governor comment instead of creating duplicates.

## .spendgov.yml Example

```yaml
version: 1
currency: EUR
defaultRegion: westeurope
hoursPerMonth: 730

rules:
  - id: dev-delta-limit
    description: Block dev PRs above 100 EUR/month
    type: monthly_delta
    threshold: 100
    action: block

environments:
  dev:
    monthlyBudget: 100
    action: block
  production:
    monthlyBudget: 3000
    action: block

ai:
  enabled: true
  monthlyBudget: 300
  maxCostPerWorkflowMonthly: 100
  action: warn
```

Supported rule types: `monthly_delta`, `proposed_monthly_cost`, `environment_budget`, `unknown_resource_count`, `ai_monthly_cost`, `ai_workflow_cost`.

## Demo Script

1. Open http://localhost:5102 and click `Seed Demo Data`.
2. Show `Latest Analyses`: repository, PR number, environment, status, decision, monthly delta, confidence, and created/completed timestamps are all loaded from persisted scan rows.
3. Open `Cheap change` and explain PASS: the estimated delta is small, confidence is High, and no blocking action is needed.
4. Open `Expensive cloud change` and explain FAIL: the dev budget policy catches premium Redis / larger cloud capacity and recommends a cheaper environment-specific SKU.
5. Open `Expensive AI workflow` and explain AI spend estimation: `gpt-4.1`, 10,000 monthly runs, 8,000 input tokens, and 2,000 output tokens produce the monthly AI estimate.
6. Explain that real usage posts the same report back to the GitHub Pull Request as an idempotent PR comment/check.

Demo files live in `demo/scenario-cheap-change`, `demo/scenario-expensive-cloud-change`, and `demo/scenario-expensive-ai-workflow`.

## Test

```powershell
dotnet run --project tests\SpendGovernor.Tests\SpendGovernor.Tests.csproj
```

The console test runner covers the MVP scenarios from `mvp-features-en.md`: no cloud impact, small VM, SKU threshold, unknown resource, expensive AI workflow, approval required, Azure resource coverage, config validation, confidence scoring, PR comment formatting, and idempotent PR comments.

## Database

The app uses EF Core with SQL Server LocalDB for private-beta persistence. The expected database is:

```txt
(localdb)\MSSQLLocalDB / Spend-Governor
```

Apply migrations:

```powershell
dotnet ef database update --project src\SpendGovernor.Infrastructure\SpendGovernor.Infrastructure.csproj --startup-project src\SpendGovernor.Api\SpendGovernor.Api.csproj --context SpendGovernorDbContext
```

The initial migration is `InitialSpendGovernorPersistence`. After applying it, SQL Server Object Explorer should show:

- `Repositories`
- `PullRequestScans`
- `CostBreakdownItems`
- `DetectedResources`
- `ScanAssumptions`
- `PolicyEvaluations`
- `__EFMigrationsHistory`

To verify the persistence flow, run the app, trigger a dashboard demo scan, then open the analysis detail. The scan list/detail endpoints read from SQL Server, not only in-memory state.

To verify seeded demo data in SQL Server Object Explorer:

1. Open `(localdb)\MSSQLLocalDB`.
2. Expand database `Spend-Governor`.
3. Open `Repositories` and confirm `acme/spend-governor-demo`.
4. Open `PullRequestScans` and confirm three `Completed` scans.
5. Open `CostBreakdownItems`, `DetectedResources`, `ScanAssumptions`, and `PolicyEvaluations` and confirm rows for each scan.

## Implemented MVP Slice

- Workspaces, projects, simple role-based access by `X-User-Email`.
- GitHub install callback and signed webhook receiver.
- Async-style PR analysis model with queued/running/completed/failed/skipped states.
- Terraform and Bicep discovery/parsing for the initial Azure resource set.
- Normalized resource model and seeded Azure pricing adapter.
- Monthly estimates, baseline/proposed delta, policy-as-code, approval flow, confidence scoring, recommendations, audit events.
- AI spend config v0 with a seed model price catalog.
- EF Core persistence for repositories, PR scans, cost breakdowns, detected resources, assumptions, policy evaluations, and GitHub comment IDs.
- Dashboard for projects, analyses, scan details, policies, approvals, audit, and CSV exports.

## Known MVP Limitations

- Workspaces/projects/users are still lightweight demo state; repository and scan history are persisted in SQL Server LocalDB.
- Azure prices and AI model prices use local seed catalogs, not live billing APIs.
- GitHub PR comments/checks are simulated after signature verification; real GitHub API posting needs app credentials and a GitHub client.
- Terraform and Bicep parsing is pragmatic v0 parsing, not full language evaluation.
- No AWS, GCP, Azure DevOps, Stripe, SSO, Slack, or real Azure billing ingestion in this MVP.
