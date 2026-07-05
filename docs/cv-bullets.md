# CV Bullets - Cloud & AI Spend Governor

## Short Project Description

Cloud & AI Spend Governor is a .NET SaaS-style developer tool that analyzes infrastructure and AI workflow changes before merge/deploy to estimate monthly Azure and LLM spend impact.

## CV Bullets

- Built a full-stack ASP.NET Core cost governance tool that analyzes pull request changes and estimates monthly cloud/AI spend impact before deployment.
- Implemented EF Core + SQL Server persistence for users, workspaces, projects, repositories, scan history, detected resources, cost breakdowns, assumptions, policy evaluations, and GitHub publishing metadata.
- Added Terraform plan JSON and Bicep compiled ARM JSON analysis to improve Azure infrastructure cost estimation accuracy, with raw Terraform/Bicep fallbacks.
- Implemented versioned local Azure/AI pricing catalogs plus optional Azure Retail Prices API lookup and explainable pricing confidence metadata.
- Built GitHub webhook/reporting flow with HMAC verification, simulated local mode, real GitHub App PR comments/check runs, and idempotent report updates.
- Added background scan processing with queued/running/completed/failed lifecycle states.
- Added dashboard demo flows, local seed/reset scenarios, CSV exports, health checks, request correlation IDs, Docker setup, and GitHub Actions CI.
- Created an automated console scenario suite covering analyzers, pricing, policy evaluation, persistence, GitHub signatures/reporting, and queue behavior.

## LinkedIn/GitHub Summary

A SaaS-style .NET project that acts as a CI/CD cost firewall for cloud and AI spend. It analyzes Terraform/Bicep/ARM and AI workflow changes, estimates monthly cost impact, evaluates environment budgets, persists scan history, and reports actionable recommendations in GitHub Pull Requests and a dashboard.
