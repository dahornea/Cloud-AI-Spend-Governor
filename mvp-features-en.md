# Cloud & AI Spend Governor — MVP Feature Backlog

## MVP goal

The MVP must validate the product as a **cost firewall inside pull requests**: when a developer changes infrastructure or an AI workflow, the product estimates the monthly cost, compares the change against budgets/policies, and posts the result directly in the GitHub PR.

The MVP should not be a full FinOps dashboard. It should quickly prove the core value:

> “We know how much this deploy will cost before we merge it.”

---

## Product principles for the MVP

1. **Developer-first** — the result appears in the PR, not only in a dashboard.
2. **Preventive, not reactive** — the product must warn before deploy.
3. **Explainable estimates** — each cost must show the resource, SKU, region, quantity, and assumptions used.
4. **Simple policies** — warn, approval required, or block.
5. **Narrow scope** — GitHub + Azure + Terraform/Bicep + simple AI cost config.
6. **Transparent fallback** — when a resource cannot be estimated, mark it as `unknown`; do not invent costs.
7. **Configurable from the repository** — budgets and rules can be defined in a `.spendgov.yml` file.

---

## Recommended technical assumptions

These are recommendations for the MVP, not hard requirements if the project already has a different stack.

- Backend: `.NET 9 / ASP.NET Core`
- Worker jobs: `.NET Worker Service` or `Azure Functions`
- Frontend dashboard: `React / Next.js`
- Database: `PostgreSQL`
- Cache: `Redis`, optional for pricing cache
- Queue: `Azure Service Bus`, `Hangfire`, or a simple DB-backed queue for the MVP
- Initial cloud: `Azure`
- Initial Git provider: `GitHub App`
- Default currency: `EUR`
- Default monthly estimate: `730 hours/month`

---

## Priorities

- **P0** — required for the first MVP demo / pilot.
- **P1** — important, but can be implemented after the first working version.
- **P2** — explicitly post-MVP; do not implement now.

---

# P0 — Required MVP features

## FEAT-001 — Workspaces, projects, and users

### Description

The product must allow users to create a workspace for a company or team, with projects connected to GitHub repositories.

### User story

As a founder/CTO, I want to create a workspace and connect a repository so that I can see the cost impact of PR changes.

### Functional requirements

- A user can create a workspace.
- A workspace can have multiple projects.
- A project is linked to a GitHub repository.
- A project has default settings:
  - cloud provider: `Azure`
  - currency: `EUR`
  - default region: `westeurope`
  - monthly hours: `730`
- Minimum roles:
  - `Owner`
  - `Member`
  - `Viewer`

### Acceptance criteria

- A new workspace can be created.
- A project can be created inside a workspace.
- The project's default settings can be saved.
- A user without workspace access cannot see that workspace's projects.

---

## FEAT-002 — GitHub App integration

### Description

The product must connect to GitHub through a GitHub App, receive webhooks for pull requests, and post comments/checks in the PR.

### User story

As a DevOps engineer, I want to install the GitHub App on a repository so that the product analyzes every PR automatically.

### Functional requirements

- GitHub App install flow.
- Store `installation_id` for each workspace/project.
- Select repositories accessible to the app.
- Supported webhook events:
  - `pull_request.opened`
  - `pull_request.synchronize`
  - `pull_request.reopened`
  - `pull_request.closed`
- Verify the GitHub webhook signature.
- Fetch changed files from the PR.
- Fetch the contents of relevant files.
- Post/update a PR comment.
- Create a GitHub Check Run with status:
  - `success`
  - `neutral`
  - `failure`

### Acceptance criteria

- When a PR is opened, the product receives the webhook.
- The product identifies the associated repository and project.
- The product reads the modified files.
- The product posts a comment in the PR.
- When a new commit is pushed to the same PR, the existing comment is updated instead of creating a duplicate.
- If the webhook signature is invalid, the request is rejected.

---

## FEAT-003 — PR analysis job pipeline

### Description

Each relevant PR must be converted into an asynchronous analysis job so that processing does not block the GitHub webhook.

### User story

As a platform engineer, I want PR analysis to be robust and repeatable so that GitHub events are not lost.

### Functional requirements

- The webhook creates a `PullRequestAnalysisJob`.
- The job has a status:
  - `Queued`
  - `Running`
  - `Succeeded`
  - `Failed`
  - `Skipped`
- The job stores input metadata:
  - workspace id
  - project id
  - repository
  - pull request number
  - base branch
  - head branch
  - commit SHA
  - changed files
- The job can be rerun manually from the dashboard.
- Errors are stored with a clear message and an internal stack trace.

### Acceptance criteria

- The webhook responds quickly after creating the job.
- The job runs independently from the webhook request.
- If analysis fails, the status becomes `Failed` and appears in the dashboard.
- If the PR does not contain relevant files, the status becomes `Skipped`.

---

## FEAT-004 — IaC file discovery

### Description

The product must detect files that are relevant to infrastructure and cost analysis.

### User story

As a developer, I want the product to analyze only cost-relevant files so that PRs without cloud impact do not create noise.

### File types supported in the MVP

- Terraform:
  - `*.tf`
  - `*.tfvars`
- Azure Bicep:
  - `*.bicep`
  - `*.bicepparam`
- AI spend config:
  - `.spendgov.yml`
  - `ai-spend.yml`
  - `ai-spend.yaml`

### Functional requirements

- Detect modified files in a PR.
- Group files by type:
  - Terraform
  - Bicep
  - SpendGov config
  - AI spend config
- PRs without relevant files are marked `Skipped`.
- For each relevant file, save its path and type.

### Acceptance criteria

- A PR with `main.tf` triggers analysis.
- A PR with `infra/main.bicep` triggers analysis.
- A PR that only modifies `.cs` or `.md` files is skipped.
- A PR that modifies `.spendgov.yml` triggers policy validation.

---

## FEAT-005 — Terraform parser v0 for Azure

### Description

The product must parse Terraform files and extract Azure resources that can be estimated.

### User story

As a developer, I want to see the estimated cost of changed Terraform resources so that I know the financial impact before merge.

### Terraform MVP scope

The parser can be simple and pragmatic. It does not need to support every Terraform case in the world.

Recommended Azure resources for the MVP:

- `azurerm_linux_virtual_machine`
- `azurerm_windows_virtual_machine`
- `azurerm_virtual_machine_scale_set`
- `azurerm_service_plan`
- `azurerm_linux_web_app`
- `azurerm_windows_web_app`
- `azurerm_storage_account`
- `azurerm_mssql_database`
- `azurerm_postgresql_flexible_server`
- `azurerm_kubernetes_cluster`
- `azurerm_kubernetes_cluster_node_pool`
- `azurerm_container_app`
- `azurerm_redis_cache`

### Functional requirements

- The parser extracts:
  - resource type
  - resource name
  - location/region
  - SKU/size/tier
  - capacity/count, if present
  - tags, if present
  - environment, if it can be inferred from tags/path/branch
- Minimum support for simple variables:
  - string literals
  - numeric literals
  - booleans
  - `var.name` if the variable is defined in the same repository
  - `.tfvars` for explicit values
- Resources that cannot be estimated are marked `unsupported`.

### Acceptance criteria

- The parser extracts an Azure VM with `size = "Standard_B2s"`.
- The parser extracts an App Service Plan with `sku_name`.
- The parser extracts a Storage Account with `account_tier` and `replication_type`.
- The parser marks unknown resources as `unsupported` without stopping the analysis.
- The parser returns a normalized list of `CloudResourceEstimateInput`.

---

## FEAT-006 — Bicep parser v0 for Azure

### Description

The product must parse Bicep files and extract Azure resources that can be estimated.

### User story

As an Azure team, I want the product to understand Bicep because we use it for our infrastructure.

### Bicep MVP scope

Recommended Bicep resources for the MVP:

- `Microsoft.Compute/virtualMachines`
- `Microsoft.Web/serverfarms`
- `Microsoft.Web/sites`
- `Microsoft.Storage/storageAccounts`
- `Microsoft.Sql/servers/databases`
- `Microsoft.DBforPostgreSQL/flexibleServers`
- `Microsoft.ContainerService/managedClusters`
- `Microsoft.App/containerApps`
- `Microsoft.Cache/Redis`

### Functional requirements

- The parser extracts:
  - resource type
  - resource name
  - location
  - SKU/tier/name
  - capacity/count
  - tags
- Minimum support for:
  - `param`
  - `var`
  - default values
  - simple string interpolation
- Resources that cannot be estimated are marked `unsupported`.

### Acceptance criteria

- The parser extracts a Bicep App Service Plan with `sku.name`.
- The parser extracts a Bicep Storage Account with `sku.name`.
- The parser extracts an Azure SQL Database with tier/SKU when present.
- The parser marks unknown resources as `unsupported`.

---

## FEAT-007 — Resource normalization layer

### Description

Terraform and Bicep must be converted into a common resource model so that the cost estimation engine does not depend directly on IaC syntax.

### User story

As a product developer, I want a common resource model so that I can add new parsers without rewriting the cost engine.

### Recommended model

```ts
CloudResourceEstimateInput {
  id: string
  sourceType: "terraform" | "bicep" | "ai-config"
  sourceFile: string
  provider: "azure"
  resourceType: string
  resourceName: string
  region: string | null
  sku: string | null
  tier: string | null
  capacity: number | null
  quantity: number
  hoursPerMonth: number
  environment: string | null
  tags: Record<string, string>
  raw: object
}
```

### Functional requirements

- Every parser produces the same output type.
- Missing fields are `null`, not empty strings.
- `quantity` defaults to `1`.
- `hoursPerMonth` defaults to `730`.
- `region` defaults to the project setting if it is missing in code.

### Acceptance criteria

- Terraform and Bicep return the same internal model.
- The cost estimation engine receives only normalized resources.
- Resources without a SKU remain in the result with a warning.

---

## FEAT-008 — Azure pricing adapter v0

### Description

The product must retrieve Azure prices for supported resources and cache them.

### User story

As a user, I want estimates based on real Azure prices, not arbitrary hardcoded values.

### Functional requirements

- Integrate with the Azure Retail Prices API or an equivalent internal provider.
- Query by:
  - service name
  - product name
  - SKU
  - region
  - meter name
- Local cache for pricing results.
- Fallback for resources with no price found:
  - status `price_not_found`
  - cost `unknown`
  - warning in the PR report
- Convert to EUR if the returned price is in another currency.
- For the MVP, a simple manually configured conversion rate is acceptable, for example `usdToEurRate`.

### Acceptance criteria

- For a `Standard_B2s` VM in `westeurope`, the adapter returns an estimated monthly price.
- For an unknown SKU, the adapter returns `price_not_found`, not a fatal error.
- Prices are cached so that the API is not queried for every identical PR.
- Every cost includes the price source and the last updated date.

---

## FEAT-009 — Monthly cloud cost estimation engine

### Description

The engine calculates the estimated monthly cost for each resource and the PR total.

### User story

As a developer, I want to see the estimated monthly cost of a change so that I can decide whether the PR is financially acceptable.

### Functional requirements

- Calculate monthly cost per resource.
- Calculate total cost per PR.
- Calculate cost by category:
  - compute
  - storage
  - database
  - networking
  - container
  - ai
  - unknown
- Support estimation based on:
  - hourly price × hours/month × quantity
  - monthly price × quantity
  - storage price × GB/month, if size is available
- Add a confidence level:
  - `high`
  - `medium`
  - `low`
  - `unknown`
- Store the assumptions used.

### Acceptance criteria

- A VM with an hourly price produces a monthly cost.
- An App Service Plan produces a monthly cost.
- A storage account without explicit capacity is marked with confidence `low` or `unknown`.
- The PR total excludes `unknown` costs but displays them separately.

---

## FEAT-010 — Baseline and PR cost delta

### Description

The product must compare the estimated cost of the PR branch with the baseline of the base branch.

### User story

As a reviewer, I want to see the cost difference introduced by the PR, not only the total infrastructure cost.

### Functional requirements

- For each PR, calculate:
  - `baselineMonthlyCost`
  - `proposedMonthlyCost`
  - `monthlyDelta`
  - `unknownResourceCount`
- The baseline can be calculated from the base branch or from the latest successful analysis.
- If the baseline does not exist, the product displays only the proposed cost and marks the delta as `not_available`.
- Save the result in the DB.

### Acceptance criteria

- A PR that changes VM size from `B2s` to `D4s_v5` shows a positive cost delta.
- A PR that deletes a resource shows a negative cost delta.
- If the baseline cannot be calculated, the PR comment explains this clearly.

---

## FEAT-011 — PR cost comment

### Description

The product posts a Markdown comment in the GitHub PR with the analysis result.

### User story

As a developer, I want to see the cost directly in the PR without opening a separate tool.

### Recommended template

```md
## Cloud & AI Spend Governor

Status: ⚠️ Warn

| Metric | Value |
|---|---:|
| Baseline monthly cost | €420.00 |
| Proposed monthly cost | €548.00 |
| Monthly delta | +€128.00 |
| Unknown resources | 2 |
| Policy result | Warn |

### Top cost changes

| Resource | Type | Region | Change | Monthly delta |
|---|---|---|---:|---:|
| app-plan-prod | Microsoft.Web/serverfarms | westeurope | S1 → P1v3 | +€96.00 |
| worker-vm | Microsoft.Compute/virtualMachines | westeurope | B2s → D4s_v5 | +€32.00 |

### Policy findings

- ⚠️ `max-pr-delta`: Monthly increase exceeds €100.

### Recommendations

- Consider autoscaling instead of a fixed larger SKU for `app-plan-prod`.
- Add a shutdown schedule for non-production resources.

### Notes

- Estimates use 730 hours/month.
- Some usage-based services may be underestimated without runtime metrics.
```

### Functional requirements

- The comment is created only once per PR.
- On reanalysis, the existing comment is updated.
- The comment includes:
  - overall status
  - baseline cost
  - proposed cost
  - monthly delta
  - top cost changes
  - policy findings
  - recommendations
  - unknown resources
  - assumptions
- The comment includes a link to the dashboard analysis detail page.

### Acceptance criteria

- The PR receives a comment after analysis.
- A new commit updates the existing comment.
- The comment is readable in GitHub Markdown.
- The comment does not exceed reasonable limits; long lists are truncated with a link to the dashboard.

---

## FEAT-012 — GitHub Check Run gating

### Description

The product must create a GitHub Check Run that can pass, warn, or block the PR based on policies.

### User story

As a CTO, I want to automatically block PRs that exceed the budget so that accidental costs are prevented.

### Functional requirements

- Check Run name: `Spend Governor`
- Final status:
  - `success` for pass
  - `neutral` for warn
  - `failure` for block
- Check summary includes:
  - monthly delta
  - policy result
  - link to report
- Branch protection can use this check as a required status.

### Acceptance criteria

- If the policy is `warn`, the check is `neutral` or `success` with an explicit warning, depending on the project setting.
- If the policy is `block`, the check is `failure`.
- If analysis fails because of an internal error, the behavior is configurable: `fail_open` or `fail_closed`.

---

## FEAT-013 — `.spendgov.yml` policy-as-code

### Description

The repository can contain a `.spendgov.yml` file with budget rules and behavior settings.

### User story

As a platform engineer, I want to define cost policies in code so that they are versioned and reviewed in PRs.

### Recommended minimum config

```yaml
version: 1
currency: EUR
defaultRegion: westeurope
hoursPerMonth: 730
onInternalError: fail_open

pullRequests:
  comment: true
  checkRun: true

rules:
  - id: max-pr-delta
    description: Block PRs that add more than 250 EUR/month
    type: monthly_delta
    threshold: 250
    action: block

  - id: warn-pr-delta
    description: Warn when PRs add more than 100 EUR/month
    type: monthly_delta
    threshold: 100
    action: warn

  - id: max-unknown-resources
    description: Warn when too many resources cannot be estimated
    type: unknown_resource_count
    threshold: 3
    action: warn

environments:
  dev:
    monthlyBudget: 200
    action: warn
  staging:
    monthlyBudget: 500
    action: approval_required
  production:
    monthlyBudget: 3000
    action: block

ai:
  enabled: true
  monthlyBudget: 300
  maxCostPerWorkflowMonthly: 100
  action: warn
```

### Functional requirements

- The product looks for `.spendgov.yml` in the repository root.
- If the file is missing, it uses the project settings from the dashboard.
- The config is validated on every PR.
- Config errors are shown in the PR comment.
- Policies in the file take priority over the project's default settings.

### Acceptance criteria

- A valid config is read and applied.
- An invalid config produces a clear message in the PR.
- Changing the threshold in `.spendgov.yml` changes the analysis result.
- Missing config does not stop analysis.

---

## FEAT-014 — Budget and policy engine v0

### Description

The policy engine decides whether the PR passes, receives a warning, requires approval, or is blocked.

### User story

As a CTO, I want simple budget rules so that I can control costs without manually reviewing every PR.

### MVP rule types

- `monthly_delta`
- `proposed_monthly_cost`
- `environment_budget`
- `unknown_resource_count`
- `ai_monthly_cost`
- `ai_workflow_cost`

### MVP actions

- `pass`
- `warn`
- `approval_required`
- `block`

### Functional requirements

- The engine receives an `AnalysisResult` and a `PolicyConfig`.
- The engine returns:
  - overall status
  - list of findings
  - final action
  - explanation for each triggered rule
- If multiple rules are triggered, the most severe action applies.
- Severity: `pass < warn < approval_required < block`.

### Acceptance criteria

- If the delta is below all thresholds, the status is `pass`.
- If the delta exceeds a warn threshold, the status is `warn`.
- If the delta exceeds a block threshold, the status is `block`.
- If both warn and block are triggered, the final status is `block`.

---

## FEAT-015 — Approval required flow v0

### Description

For some policies, the product does not permanently block the PR, but requires explicit approval.

### User story

As an engineering manager, I want to manually approve a justified cost increase so that legitimate delivery is not blocked.

### Functional requirements

- `approval_required` status in the analysis.
- Dashboard page for approval.
- Only `Owner` and `Member` roles can approve.
- Approval stores:
  - user id
  - timestamp
  - reason/comment
  - policy id
  - PR number
  - commit SHA
- After approval, the GitHub check is updated to `success`.
- If the PR receives a new commit, the old approval is no longer valid.

### Acceptance criteria

- A PR that requires approval appears in the dashboard.
- An authorized user can approve it with a reason.
- After approval, the GitHub check is updated.
- A new commit requires approval again.

---

## FEAT-016 — Minimal dashboard

### Description

The product has a simple dashboard for projects, PR analyses, budgets, and results.

### User story

As a CTO, I want to see analyzed PRs and cost risks in one place so that I can track the product's impact.

### MVP pages

1. **Login / Auth**
2. **Workspace selector**
3. **Projects list**
4. **Project detail**
5. **PR analyses list**
6. **PR analysis detail**
7. **Budgets & policies settings**
8. **Approvals page**

### Project detail must show

- connected repository
- default region
- currency
- total PRs analyzed
- total monthly cost delta detected
- total blocked/warned PRs
- latest analyses

### PR analysis detail must show

- PR metadata
- status
- cost summary
- resource breakdown
- policy findings
- recommendations
- unknown resources
- raw assumptions
- audit events

### Acceptance criteria

- The user sees the list of projects in the workspace.
- The user sees the latest PR analyses for a project.
- The user can open an analysis detail page.
- The user can modify the project's default budgets.

---

## FEAT-017 — Basic recommendations engine

### Description

The product must provide simple, explainable recommendations based on detected resources.

### User story

As a developer, I do not only want to see that a PR costs more; I want clear suggestions for reducing cost.

### MVP recommendations

- If an environment is not production and has always-on compute resources, recommend a shutdown schedule.
- If VM size is large for `dev` or `staging`, recommend a smaller SKU.
- If an App Service Plan is premium in non-production, recommend a lower tier.
- If storage has no lifecycle policy, recommend a lifecycle policy.
- If resources have no `environment` tag, recommend tagging.
- If the cost delta exceeds a threshold, recommend approval/review.
- If there are unknown resources, recommend completing SKU/region/capacity.

### Functional requirements

- Recommendations are generated deterministically using simple rules.
- Each recommendation has:
  - title
  - explanation
  - estimated impact, if it can be calculated
  - affected resource
  - severity: `low`, `medium`, `high`
- Recommendations appear in the PR comment and dashboard.

### Acceptance criteria

- A premium App Service Plan in staging generates a downgrade recommendation.
- A resource without an environment tag generates a tagging recommendation.
- Recommendations do not block a PR by themselves.

---

## FEAT-018 — AI spend config v0

### Description

The MVP includes a simple AI spend governance variant based on a configuration file, not complex runtime monitoring.

### User story

As a team using LLMs, I want to estimate the monthly cost of AI workflows in PRs so that I can avoid overly expensive prompts or agents before deploy.

### Recommended config

File: `ai-spend.yml` or an `ai` section in `.spendgov.yml`.

```yaml
aiWorkflows:
  - id: support-ticket-classifier
    provider: openai
    model: gpt-4.1-mini
    estimatedMonthlyRequests: 50000
    averageInputTokens: 1200
    averageOutputTokens: 250
    maxOutputTokens: 500
    environment: production

  - id: sales-agent
    provider: azure-openai
    model: gpt-4.1
    estimatedMonthlyRequests: 10000
    averageInputTokens: 4000
    averageOutputTokens: 1200
    maxAgentSteps: 8
    environment: production
```

### Functional requirements

- YAML parser for AI workflows.
- Required fields:
  - `id`
  - `provider`
  - `model`
  - `estimatedMonthlyRequests`
  - `averageInputTokens`
  - `averageOutputTokens`
- Optional fields:
  - `maxOutputTokens`
  - `maxAgentSteps`
  - `environment`
  - `tenant`
  - `feature`
- Calculate estimated monthly cost:
  - input token cost
  - output token cost
  - total workflow monthly cost
- Use a configurable model price catalog.
- If the model does not exist in the catalog, mark the cost as `unknown`.

### Acceptance criteria

- A valid AI workflow produces an estimated monthly cost.
- An unknown model produces a warning, not a fatal error.
- AI cost appears separately in the PR comment.
- The `ai_monthly_cost` and `ai_workflow_cost` policies can produce warn/block results.

---

## FEAT-019 — AI model price catalog v0

### Description

The product must have a simple price catalog for AI models that is configurable and easy to update.

### User story

As an admin, I want to define prices for the AI models we use so that estimates reflect my contracts or providers.

### Functional requirements

- Internal `AiModelPrices` table.
- Recommended fields:
  - provider
  - model
  - inputPricePerMillionTokens
  - outputPricePerMillionTokens
  - currency
  - validFrom
  - source
- Simple UI or seed config for prices.
- For the MVP, manually entered/configured prices are acceptable.
- Support workspace-level overrides.

### Acceptance criteria

- The cost of an AI workflow is calculated from the catalog.
- A workspace can have a custom price for a model.
- If the price is missing, analysis displays `unknown AI cost`.

---

## FEAT-020 — Reports export v0

### Description

The user can export the results of an analysis or project to CSV.

### User story

As a founder/CTO, I want to export results so that I can send them to finance or a client.

### MVP scope

- CSV export is required.
- PDF export is optional P1, not required for the first MVP.

### MVP CSV exports

1. PR analysis resource breakdown
2. Policy findings
3. Recommendations
4. Project summary for the last 30 days

### Acceptance criteria

- From analysis detail, the user can export a CSV with resources and costs.
- From project detail, the user can export a CSV with recent analyses.
- The CSV includes currency and timestamp.

---

## FEAT-021 — Audit log

### Description

The product must keep a minimal audit trail for important actions.

### User story

As a CTO, I want to see who approved a budget overrun and which rules were applied.

### MVP events

- GitHub App installed
- Project created
- PR analysis queued
- PR analysis completed
- PR analysis failed
- Policy triggered
- Approval requested
- Approval granted
- Approval invalidated by new commit
- Config changed

### Acceptance criteria

- Each analysis has associated audit events.
- Approvals are visible in the audit log.
- Analysis errors are visible in the audit log.

---

## FEAT-022 — Observability and error handling

### Description

The MVP must be observable enough to be used in a pilot with real customers.

### Functional requirements

- Structured logging.
- Correlation id per webhook/job/analysis.
- Minimum metrics:
  - jobs queued
  - jobs succeeded
  - jobs failed
  - average analysis duration
  - GitHub API failures
  - pricing API failures
- Error boundary in the dashboard.
- Retry for temporary GitHub/pricing API errors.
- Rate-limit handling for the GitHub API.

### Acceptance criteria

- A failed analysis can be diagnosed from logs.
- Retries do not create duplicate comments.
- External errors are marked clearly in the analysis result.

---

# P1 — Useful features after the first working MVP

## FEAT-023 — Azure read-only cloud connection

### Description

Read-only connection to an Azure Subscription for validating existing resources, real costs, and better idle detection.

### Functional requirements

- Azure OAuth / read-only service principal.
- Read resource inventory.
- Associate cloud resources with projects using tags.
- Read actual cost from Azure Cost Management, if available.
- Read simple metrics for supported resources.

### Acceptance criteria

- The user can connect an Azure Subscription in read-only mode.
- The dashboard displays real resources associated with the project.
- PR analysis can compare the estimate with existing costs where data is available.

---

## FEAT-024 — Idle resource detection v0

### Description

Simple detection for resources that may be idle or abandoned.

### MVP/P1 heuristics

- Non-production resources with no traffic/relevant metrics.
- Unattached disks.
- Unassociated public IPs.
- Load balancers with no healthy backend.
- Temporary environments older than N days.
- Resources without an owner tag.

### Acceptance criteria

- The dashboard displays a list of idle candidates.
- Each candidate has a reason and confidence score.
- The product does not delete resources automatically.

---

## FEAT-025 — Slack / Microsoft Teams alerts

### Description

Send alerts to the team channel for PRs with high cost impact.

### Acceptance criteria

- The user configures a Slack or Teams webhook.
- PRs with `warn`, `approval_required`, or `block` send an alert.
- The message includes links to the PR and analysis detail page.

---

## FEAT-026 — PDF export

### Description

PDF export for non-technical stakeholders.

### Acceptance criteria

- The user can generate a PDF from the project summary.
- The PDF includes cost prevented, blocked PRs, top recommendations, and savings opportunities.

---

## FEAT-027 — Azure DevOps integration

### Description

GitHub-like integration for Azure Repos and Azure Pipelines.

### Acceptance criteria

- The product receives PR events from Azure DevOps.
- The product posts PR comments.
- The product can set a policy/check status in Azure DevOps.

---

# P2 — Explicitly post-MVP / do not implement now

These are valuable, but should not be included in the first MVP.

- AWS support
- GCP support
- Complete Kubernetes YAML / Helm cost estimation
- Multi-cloud discount modeling
- Advanced Reserved Instances / Savings Plans planning
- Real invoice anomaly detection
- Auto-remediation PRs
- Complete AI agent runtime monitoring
- Runtime telemetry-based cost allocation per tenant
- Rules marketplace
- Enterprise SSO/SAML
- Jira/Linear ticket creation
- 30/60/90-day forecasting
- Advanced traffic simulations
- Autoscaling recommendations based on complex historical metrics

---

# Recommended minimal data model

## Entities

```ts
Workspace {
  id: string
  name: string
  createdAt: DateTime
}

User {
  id: string
  email: string
  name: string
  createdAt: DateTime
}

WorkspaceMember {
  workspaceId: string
  userId: string
  role: "Owner" | "Member" | "Viewer"
}

Project {
  id: string
  workspaceId: string
  name: string
  provider: "azure"
  currency: "EUR" | "USD"
  defaultRegion: string
  hoursPerMonth: number
  repositoryProvider: "github"
  repositoryOwner: string
  repositoryName: string
  githubInstallationId: string
  createdAt: DateTime
}

PullRequestAnalysis {
  id: string
  projectId: string
  repositoryOwner: string
  repositoryName: string
  pullRequestNumber: number
  baseBranch: string
  headBranch: string
  commitSha: string
  status: "Queued" | "Running" | "Succeeded" | "Failed" | "Skipped"
  policyStatus: "pass" | "warn" | "approval_required" | "block"
  baselineMonthlyCost: decimal | null
  proposedMonthlyCost: decimal | null
  monthlyDelta: decimal | null
  currency: string
  unknownResourceCount: number
  createdAt: DateTime
  completedAt: DateTime | null
}

ResourceEstimate {
  id: string
  analysisId: string
  sourceType: "terraform" | "bicep" | "ai-config"
  sourceFile: string
  provider: "azure" | "openai" | "azure-openai" | "anthropic" | "unknown"
  resourceType: string
  resourceName: string
  region: string | null
  sku: string | null
  environment: string | null
  category: "compute" | "storage" | "database" | "networking" | "container" | "ai" | "unknown"
  monthlyCost: decimal | null
  currency: string
  confidence: "high" | "medium" | "low" | "unknown"
  status: "estimated" | "unsupported" | "price_not_found" | "unknown"
  assumptionsJson: string
}

PolicyFinding {
  id: string
  analysisId: string
  ruleId: string
  action: "warn" | "approval_required" | "block"
  message: string
  actualValue: decimal | null
  thresholdValue: decimal | null
}

Recommendation {
  id: string
  analysisId: string
  resourceEstimateId: string | null
  severity: "low" | "medium" | "high"
  title: string
  description: string
  estimatedMonthlySavings: decimal | null
}

Approval {
  id: string
  analysisId: string
  approvedByUserId: string
  commitSha: string
  reason: string
  createdAt: DateTime
}

AuditEvent {
  id: string
  workspaceId: string
  projectId: string | null
  analysisId: string | null
  eventType: string
  message: string
  metadataJson: string
  createdAt: DateTime
}
```

---

# Recommended minimum API endpoints

```txt
POST   /api/workspaces
GET    /api/workspaces
GET    /api/workspaces/{workspaceId}

POST   /api/projects
GET    /api/workspaces/{workspaceId}/projects
GET    /api/projects/{projectId}
PATCH  /api/projects/{projectId}/settings

POST   /api/github/webhooks
GET    /api/github/installations/callback

GET    /api/projects/{projectId}/analyses
GET    /api/analyses/{analysisId}
POST   /api/analyses/{analysisId}/rerun
GET    /api/analyses/{analysisId}/export/resources.csv

GET    /api/projects/{projectId}/policies
PUT    /api/projects/{projectId}/policies

POST   /api/analyses/{analysisId}/approve
GET    /api/projects/{projectId}/approvals

GET    /api/projects/{projectId}/audit-events
```

---

# Required test scenarios

## Scenario 1 — PR with no cloud impact

### Input

The PR modifies only `README.md`.

### Expected

- Analysis status: `Skipped`
- No detailed cost report is posted.
- Check Run: `success` or `neutral`, depending on config.

---

## Scenario 2 — PR adds a small Azure VM

### Input

Terraform adds `azurerm_linux_virtual_machine` with size `Standard_B2s` in `westeurope`.

### Expected

- The resource is detected.
- Monthly cost is estimated.
- The PR comment shows a positive cost delta.
- Policy status is `pass` if the delta is below the threshold.

---

## Scenario 3 — PR changes SKU and exceeds threshold

### Input

The PR changes an App Service Plan from `S1` to `P1v3`.

### Expected

- Monthly delta is calculated.
- The `max-pr-delta` rule is triggered.
- The PR comment shows warning/block.
- The Check Run reflects the final action.

---

## Scenario 4 — Unknown resource

### Input

The PR adds an unsupported Terraform resource.

### Expected

- The resource appears as `unsupported`.
- The cost is not invented.
- Unknown resource count increases.
- If the unknown-resource threshold is exceeded, a policy finding appears.

---

## Scenario 5 — Expensive AI workflow

### Input

`ai-spend.yml` defines a workflow with an expensive model, many requests, and many tokens.

### Expected

- Monthly AI cost is calculated.
- The PR comment displays an AI Spend section.
- The `ai_workflow_cost` policy produces warn/block if the threshold is exceeded.

---

## Scenario 6 — Approval required

### Input

The PR exceeds the staging budget, and the rule has action `approval_required`.

### Expected

- The Check Run indicates approval required.
- The dashboard allows approval with a reason.
- After approval, the check becomes success.
- A new commit invalidates the approval.

---

# MVP Definition of Done

The MVP is ready for a pilot when all of the following are true:

- A user can create a workspace and project.
- The GitHub App can be installed on a repository.
- A PR with Terraform or Bicep automatically triggers analysis.
- The product estimates monthly cost for at least 5 Azure resource types.
- The product calculates the cost delta between baseline and proposed state.
- The product posts a Markdown comment in the PR.
- The product creates a GitHub Check Run.
- `.spendgov.yml` can control warn/block/approval required behavior.
- The dashboard displays projects, analyses, and details.
- AI spend config v0 can estimate the cost of a manually defined LLM workflow.
- CSV export works for PR analysis.
- The audit log stores important events.
- Errors are visible and diagnosable.
- At least 6 end-to-end test scenarios pass.

---

# Recommended MVP demo flow

1. User creates a workspace.
2. User installs the GitHub App.
3. User connects a demo repository.
4. The repository contains `.spendgov.yml`.
5. Developer opens a PR that modifies Terraform/Bicep.
6. Spend Governor analyzes the PR.
7. Spend Governor posts a comment with cost delta.
8. Spend Governor marks the GitHub Check as warn/block/pass.
9. User opens the dashboard and views analysis detail.
10. User exports CSV for stakeholders.

---

# Clear MVP exclusions

To avoid scope creep, do not implement the following in the MVP:

- Full multi-cloud support.
- Full support for all Azure resources.
- Perfect estimates for discounts, reserved instances, or enterprise agreements.
- Complete Kubernetes/Helm cost estimation.
- Complete runtime AI observability.
- Auto-remediation PRs.
- Automatic resource deletion.
- Advanced financial forecasting.
- ML anomaly detection.
- Enterprise SSO.
- Complete Stripe billing if the product is still in pilot.

---

# Recommended implementation order

1. Data model + auth/workspace/project basics.
2. GitHub App installation + webhook receiver.
3. PR analysis job pipeline.
4. IaC file discovery.
5. Terraform parser v0.
6. Bicep parser v0.
7. Resource normalization.
8. Azure pricing adapter + cache.
9. Cost estimation engine.
10. Baseline/proposed delta.
11. PR Markdown comment.
12. GitHub Check Run gating.
13. `.spendgov.yml` parser.
14. Policy engine.
15. Minimal dashboard.
16. Approval flow.
17. Recommendations engine.
18. AI spend config v0.
19. CSV export.
20. Audit log + observability.
