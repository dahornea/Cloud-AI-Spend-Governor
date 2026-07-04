# Demo Scenarios

These folders contain small repository examples for a 3-5 minute Cloud & AI Spend Governor demo.

- `scenario-cheap-change`: a small storage account plus small App Service plan that should pass budget rules.
- `scenario-expensive-cloud-change`: premium Redis and larger always-on dev cloud capacity that should fail.
- `scenario-expensive-ai-workflow`: a `gpt-4.1` workflow with 10,000 monthly runs that should warn or fail depending on policy.

The dashboard has matching demo buttons plus Development-only `Seed Demo Data` and `Reset Demo Data` controls. These files are also useful as copy/paste inputs for `POST /api/projects/{projectId}/analyses`.
