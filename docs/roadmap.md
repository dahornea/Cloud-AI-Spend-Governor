# Roadmap

## Completed / MVP

- ASP.NET Core API and static dashboard.
- SQL Server persistence with EF Core migrations.
- Local/private-beta user, workspace, project, repository, and budget model.
- Pull request scan history with queued/running/completed/failed states.
- Cost breakdowns, detected resources, assumptions, policy evaluations, and confidence levels.
- Development-only demo seed/reset flow.
- Terraform plan JSON analysis with raw Terraform fallback.
- Bicep compiled ARM JSON analysis with raw Bicep fallback.
- AI workflow token-cost estimation from local pricing catalog.
- Versioned local Azure and AI pricing catalogs.
- Optional Azure Retail Prices API lookup for supported Azure resources.
- GitHub webhook HMAC verification.
- Simulated GitHub PR report publishing for local demos.
- Real GitHub App PR comment/check-run publishing when configured.
- Health checks, request correlation IDs, structured console logging, Docker support, and CI.

## Next

1. Production hosting profile with managed SQL Server/Azure SQL.
2. Stronger authentication and invitation flow for private beta teams.
3. Actual vs estimated cost comparison using billing exports or Azure Cost Management.
4. Deployment what-if or Terraform drift-aware comparison.
5. More complete ARM expression evaluation and before/after ARM diffing.
6. More Azure service pricing coverage and catalog maintenance tooling.
7. Slack or Teams notifications.
8. Azure DevOps integration.
9. AWS/GCP support.
10. Stripe billing and subscription packaging.
11. Enterprise SSO and advanced RBAC.

## Not In Current MVP

The repository intentionally does not include Stripe, Slack, AWS/GCP, Azure DevOps, enterprise SSO, production cloud deployment automation, or Azure Cost Management ingestion.
