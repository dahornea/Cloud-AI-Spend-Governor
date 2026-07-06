# Demo Walkthrough

## Goal

Show how Cloud & AI Spend Governor catches risky cloud and AI cost changes before merge/deploy. This walkthrough is designed for a 3-5 minute portfolio review using screenshots and a local demo.

## Preparation

1. Start the app locally.
2. Open `http://localhost:5102/dashboard`.
3. Use `Seed Demo Data` in Development mode.
4. Confirm the dashboard shows the cheap cloud change, expensive cloud change, and expensive AI workflow scans.

## Walkthrough

### 1. Open the landing page

Open `http://localhost:5102`.

Explain the core idea: traditional FinOps tools often explain spend after the invoice arrives. Cloud & AI Spend Governor moves cost feedback into the pull request workflow so developers can fix risky changes before merge/deploy.

Recommended screenshot:

`docs/assets/landing-page.png`

### 2. Open the dashboard

Open `http://localhost:5102/dashboard`.

Show that the app has persisted scan history. Point out the repository, PR number, environment, status, decision, estimated monthly delta, confidence level, and timestamps.

Recommended screenshots:

- `docs/assets/dashboard-overview.png`
- `docs/assets/scan-history.png`

### 3. Open the expensive cloud change scan

Open the expensive cloud change scan from the scan history.

Explain:

- The decision is `FAIL`.
- The monthly cost delta exceeds the dev budget.
- Detected resources include Azure Redis Premium P1, a larger App Service plan, and Log Analytics ingestion.
- The cost breakdown shows which resources drive the estimate.
- Pricing metadata explains the source and confidence of the estimate.
- The policy evaluation explains why the scan failed.
- The recommendation is to use cheaper dev SKUs or environment-specific configuration.

Recommended screenshots:

- `docs/assets/scan-detail-expensive-cloud.png`
- `docs/assets/pricing-metadata.png`

### 4. Open the expensive AI workflow scan

Open the expensive AI workflow scan from the scan history.

Explain:

- The workflow uses `gpt-4.1`.
- The estimate is based on 10,000 monthly runs.
- Average input tokens are 8,000 per run.
- Average output tokens are 2,000 per run.
- The estimated monthly AI spend is calculated from model token pricing.
- The recommendation is to use a cheaper model, reduce prompt/output size, or lower run frequency.

Recommended screenshot:

`docs/assets/scan-detail-ai-workflow.png`

### 5. Open the GitHub PR report preview

Open the PR report preview from a scan detail page.

Explain that the same decision can be posted directly to a GitHub Pull Request. In local demos, simulated mode stores the report without requiring a real GitHub webhook. In real GitHub App mode, the app can create or update a PR comment and optional check run.

Recommended screenshot:

`docs/assets/github-pr-report-preview.png`

### 6. Show environment budgets

Open the project/workspace budget area.

Explain that decisions are based on environment-specific budgets and policy rules. A small dev change can pass, while an expensive dev change can fail even if the same resource might be acceptable elsewhere.

Recommended screenshot:

`docs/assets/environment-budgets.png`

### 7. Conclusion

Cloud & AI Spend Governor helps developers review cloud and AI spend before merge/deploy. The MVP demonstrates analyzers, pricing metadata, policy evaluation, persistence, dashboard reporting, and GitHub PR report output without requiring production infrastructure.

## Screenshot Checklist

- `docs/assets/landing-page.png`
- `docs/assets/dashboard-overview.png`
- `docs/assets/scan-history.png`
- `docs/assets/scan-detail-expensive-cloud.png`
- `docs/assets/scan-detail-ai-workflow.png`
- `docs/assets/pricing-metadata.png`
- `docs/assets/github-pr-report-preview.png`
- `docs/assets/environment-budgets.png`
- `docs/assets/architecture-diagram.png`
