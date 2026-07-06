# Screenshot Guide

This project uses a screenshot-based demo package for portfolio review. Do not add fabricated screenshots. Capture real screenshots after running the local demo seed and save them under `docs/assets/`.

Before publishing screenshots, make sure they do not reveal real secrets, private keys, webhook secrets, personal access tokens, production connection strings, private repository names, or personal email addresses.

## 1. Landing Page

File:

`docs/assets/landing-page.png`

Route:

`/`

Required demo data:

None.

Should show:

- Hero section.
- Core value proposition.
- Product preview cards.
- CTA buttons.

Purpose:

Shows the product as a polished SaaS-style portfolio project before the reviewer opens the app.

## 2. Dashboard Overview

File:

`docs/assets/dashboard-overview.png`

Route:

`/dashboard`

Required demo data:

Run the development-only demo seed from the dashboard.

Should show:

- Summary metrics.
- Latest persisted scans.
- PASS, WARN, and FAIL decisions if visible.
- Project or environment context.

Purpose:

Shows that scan results are persisted and visible in a product dashboard.

## 3. Scan History

File:

`docs/assets/scan-history.png`

Route:

`/dashboard`, then open the scan history or analyses view.

Required demo data:

Run the development-only demo seed from the dashboard.

Should show:

- Repository name.
- PR number.
- Environment.
- Status.
- Decision.
- Estimated monthly delta.
- Confidence.
- Created/completed timestamps.

Purpose:

Shows the reviewer that the MVP tracks multiple scans, not just a single mocked result.

## 4. Expensive Cloud Scan Detail

File:

`docs/assets/scan-detail-expensive-cloud.png`

Route:

`/dashboard`, then open the expensive cloud change scan from scan history.

Required demo data:

Run the development-only demo seed and select the expensive cloud change scenario.

Should show:

- FAIL decision.
- Estimated monthly delta.
- Azure Redis Premium P1.
- App Service plan cost driver.
- Log Analytics ingestion assumption.
- Cost breakdown.
- Policy evaluation.
- Recommendation to use cheaper dev or environment-specific SKUs.

Purpose:

Demonstrates the core "cost firewall" value: risky infrastructure changes are blocked before merge/deploy.

## 5. AI Workflow Scan Detail

File:

`docs/assets/scan-detail-ai-workflow.png`

Route:

`/dashboard`, then open the expensive AI workflow scan from scan history.

Required demo data:

Run the development-only demo seed and select the expensive AI workflow scenario.

Should show:

- WARN or FAIL decision.
- Model: `gpt-4.1`.
- Monthly runs: `10,000`.
- Average input tokens: `8,000`.
- Average output tokens: `2,000`.
- Estimated monthly AI cost.
- Recommendation to use a cheaper model or reduce token usage/runs.

Purpose:

Shows that the product covers LLM spend as well as Azure infrastructure spend.

## 6. Pricing Metadata

File:

`docs/assets/pricing-metadata.png`

Route:

`/dashboard`, then open any seeded scan detail with priced resources.

Required demo data:

Run the development-only demo seed.

Should show:

- Pricing source.
- Catalog version or Azure Retail Prices API metadata.
- Match type.
- Fallback reason, if any.
- Confidence level.
- Assumptions used for the estimate.

Purpose:

Shows explainability: reviewers can see why the estimate has a given confidence level.

## 7. GitHub PR Report Preview

File:

`docs/assets/github-pr-report-preview.png`

Route:

`/dashboard`, then open a seeded scan detail and capture the PR report preview section.

Required demo data:

Run the development-only demo seed. Simulated GitHub reporting is enough for this screenshot.

Should show:

- PASS/WARN/FAIL status.
- Estimated monthly impact.
- Main cost drivers.
- Policy evaluation.
- Pricing metadata.
- Recommendation.

Purpose:

Shows how the same analysis would appear inside a GitHub Pull Request.

## 8. Environment Budgets

File:

`docs/assets/environment-budgets.png`

Route:

`/dashboard`, then open the project/workspace budget area.

Required demo data:

A local project should exist. The development demo seed creates one if needed.

Should show:

- Environment names.
- Monthly budget or delta thresholds.
- Block/approval behavior.
- Project budget context.

Purpose:

Shows that PASS/WARN/FAIL decisions are policy-driven, not arbitrary labels.

## 9. Architecture Diagram

File:

`docs/assets/architecture-diagram.png`

Route:

Capture the Mermaid diagram from `README.md` or `docs/architecture.md` using GitHub's rendered Markdown view.

Required demo data:

None.

Should show:

- GitHub Pull Request or local demo input.
- ASP.NET Core API.
- Scan queue/background worker.
- IaC and AI analyzers.
- Pricing engine.
- Policy engine.
- SQL Server persistence.
- Dashboard and GitHub PR report outputs.

Purpose:

Helps a reviewer understand the system architecture quickly from the repository page.

## Capture Flow

1. Start the app locally.
2. Open `http://localhost:5102`.
3. Capture `landing-page.png`.
4. Open `http://localhost:5102/dashboard`.
5. Seed demo data from the dashboard.
6. Capture the dashboard overview and scan history.
7. Open each seeded scan detail and capture the expensive cloud, AI workflow, pricing metadata, and PR report preview screenshots.
8. Capture the environment budgets page.
9. Capture the rendered architecture diagram from GitHub or local Markdown preview.
