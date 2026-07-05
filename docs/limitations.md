# Known MVP Limitations

This MVP proves the core workflow: estimate cloud and AI cost impact before merge/deploy, evaluate policy, persist the scan, and show the result in GitHub and the dashboard.

Current limitations:

- The app is optimized for local/private-beta usage, not full production hosting.
- SQL Server LocalDB or local/container SQL Server is used for development. Production deployment should use a managed database.
- Pricing is estimate-based. Real customer billing ingestion, negotiated discounts, reservations, savings plans, and Azure Cost Management data are not implemented.
- Azure Retail Prices API support is optional and only covers supported Azure resource mappings.
- AI pricing uses the local versioned catalog; live OpenAI/Azure OpenAI pricing fetch is not implemented.
- Terraform plan JSON support parses existing `terraform show -json` output; the app does not run Terraform.
- Bicep support analyzes compiled ARM JSON when available; raw Bicep parsing is a pragmatic fallback.
- Full ARM expression evaluation, nested deployment evaluation, and before/after ARM diffing are not implemented.
- GitHub integration can run in simulated or real mode. Local demos usually use simulated mode.
- The local auth model is suitable for demos, not production identity/security requirements.
- AWS, GCP, Azure DevOps, Stripe billing, Slack alerts, enterprise SSO, advanced RBAC, and full FinOps analytics are outside the MVP scope.
