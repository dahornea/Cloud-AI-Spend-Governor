# CV Bullets - Cloud & AI Spend Governor

## Short Project Description

Cloud & AI Spend Governor is a .NET SaaS-style developer tool that analyzes infrastructure and AI workflow changes before merge/deploy to estimate monthly Azure and LLM spend impact.

## CV Bullets

- Built a full-stack ASP.NET Core SaaS-style tool that analyzes Pull Request changes and estimates monthly cloud and AI spend impact before merge/deploy.
- Implemented EF Core + SQL Server persistence for users, workspaces, projects, repositories, scan history, detected resources, cost breakdowns, assumptions, policy evaluations, and GitHub publishing metadata.
- Added Terraform Plan JSON and Bicep compiled ARM JSON analysis to detect Azure infrastructure changes and extract pricing-relevant metadata, with raw Terraform/Bicep fallbacks.
- Designed a budget policy engine that classifies changes as PASS, WARN, or FAIL based on estimated monthly cost deltas and environment budgets.
- Implemented versioned Azure and AI pricing catalogs, optional Azure Retail Prices API lookup, and confidence-aware pricing metadata.
- Built GitHub webhook/reporting support with HMAC verification, simulated local mode, real GitHub App PR comments/check runs, and idempotent report updates.
- Added a .NET CLI and local GitHub Action wrapper so the same cost checks can run in CI without deploying the web app.
- Added Policy-as-Code support through `.spendgov.yml`, enabling custom spend guardrails for cloud resources and AI workflows across the web app, CLI, and GitHub Action.
- Added SARIF output and GitHub Actions annotations for cloud/AI spend findings, allowing cost policy violations to appear directly in CI workflows with source-aware diagnostics.
- Added development-only demo seed/reset flows that create realistic persisted scans for cheap cloud, expensive cloud, and expensive AI workflow scenarios.
- Created portfolio-ready documentation, screenshot walkthroughs, sample PR reports, local setup guidance, and honest MVP limitations.
- Added a console scenario suite covering analyzers, pricing, policy evaluation, persistence, GitHub signatures/reporting, queue behavior, and dashboard mapping.

## LinkedIn/GitHub Summary

A SaaS-style .NET project that acts as a CI/CD cost firewall for cloud and AI spend. It analyzes Terraform/Bicep/ARM and AI workflow changes, estimates monthly cost impact, evaluates environment budgets, persists scan history, and reports actionable recommendations in GitHub Pull Requests and a dashboard.
