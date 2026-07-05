# Demo Scenarios

These folders contain small repository examples for a 3-5 minute Cloud & AI Spend Governor demo.

- `scenario-cheap-change`: a small storage account plus small App Service plan that should pass budget rules.
- `scenario-expensive-cloud-change`: premium Redis and larger always-on dev cloud capacity that should fail.
- `scenario-expensive-ai-workflow`: a `gpt-4.1` workflow with 10,000 monthly runs that should warn or fail depending on policy.
- `terraform-plan-json/cheap-change`: Terraform plan JSON with a small storage account and B1 App Service plan.
- `terraform-plan-json/expensive-cloud-change`: Terraform plan JSON with Redis Premium, P1v3 App Service, and a Kubernetes replica increase.
- `terraform-plan-json/sku-upgrade`: Terraform plan JSON showing a B1 to P1v3 App Service plan upgrade with before/after values.
- `bicep-arm-json/cheap-change`: compiled ARM JSON from Bicep with Standard_LRS storage and a B1 App Service plan.
- `bicep-arm-json/expensive-cloud-change`: compiled ARM JSON from Bicep with Redis Premium P1, P1v3 App Service, and Log Analytics ingestion assumptions.
- `bicep-arm-json/parameterized-template`: compiled ARM JSON from Bicep with parameter and variable defaults plus one unresolved expression.

The dashboard has scenario buttons, including three Bicep ARM JSON demo PRs, plus Development-only `Seed Demo Data` and `Reset Demo Data` controls. Seed still creates the original three product-demo scans. These files are also useful as copy/paste inputs for `POST /api/projects/{projectId}/analyses`.
