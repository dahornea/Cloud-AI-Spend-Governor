# spendgov CLI

The `spendgov` CLI runs Cloud & AI Spend Governor checks from the command line or GitHub Actions without deploying the ASP.NET Core web app.

It reuses the same core analyzer, pricing, policy, confidence, recommendation, and Markdown report code as the dashboard/GitHub integration.

## Run Locally

```powershell
dotnet run --project src\SpendGovernor.Cli\SpendGovernor.Cli.csproj -- scan `
  --path demo\scenario-expensive-cloud-change `
  --markdown artifacts\spendgov-report.md `
  --json artifacts\spendgov-report.json `
  --output-sarif artifacts\spendgov.sarif `
  --github-annotations `
  --fail-on fail `
  --repository acme/shop-api `
  --pr-number 42 `
  --head-branch feature/dev-premium-redis
```

Use `--fail-on never` when you want to generate reports without failing the shell command.

## Inputs

The CLI scans the proposed directory passed with `--path` and detects:

- `.spendgov.yml` or `.spendgov.yaml`
- Terraform Plan JSON such as `tfplan.json`
- Bicep compiled ARM JSON such as `main.json` or `azuredeploy.json`
- raw Terraform `.tf`
- raw Bicep `.bicep`
- AI workflow config such as `ai-spend.yml`

Use `--baseline-path` when you have a before/after directory to compare against. Terraform Plan JSON remains the preferred diff source when available.

## Outputs

The CLI writes:

- a Markdown report, default `spendgov-report.md`;
- a JSON report, default `spendgov-report.json`;
- an optional SARIF 2.1.0 report when `--output-sarif` is provided.

Use `--markdown -` or `--json -` to write either report to stdout.

The Markdown report is the same developer-friendly report shape used for GitHub PR comments. The JSON report includes the decision, cost summary, resources, cost changes, Policy-as-Code evaluations, CI findings, policy findings, recommendations, config warnings, confidence, and pricing metadata.

## SARIF and Annotations

```powershell
dotnet run --project src\SpendGovernor.Cli\SpendGovernor.Cli.csproj -- scan `
  --path . `
  --markdown artifacts\spendgov-report.md `
  --json artifacts\spendgov-report.json `
  --output-sarif artifacts\spendgov.sarif `
  --github-annotations `
  --annotations-min-severity warning `
  --fail-on fail
```

`--output-sarif` writes a SARIF 2.1.0 report that can be uploaded as an artifact or optionally sent to GitHub Code Scanning.

`--github-annotations` emits GitHub workflow commands such as `::error` and `::warning`, so findings can appear inline in Actions logs. `--annotations-min-severity` accepts `note`, `warning`, or `error`; the default is `warning`.

Findings are generated for budget failures, Policy-as-Code matches, high monthly impact resources/workflows, low-confidence estimates, pricing fallback, unknown pricing, and config/analyzer warnings.

## Exit Codes

```txt
0 success
1 CLI usage or file I/O error
2 invalid config or policy threshold failed for the configured --fail-on level
3 scan engine failed unexpectedly
```

`--fail-on fail` is the default and exits `2` for invalid config or `FAIL` decisions caused by block or approval-required policy findings.

`--fail-on warn` exits `2` for `WARN` or `FAIL`.

`--fail-on never` always returns `0` unless the CLI itself cannot run.

## Local GitHub Action Wrapper

The repository includes a composite Action at:

```txt
.github/actions/spendgov/action.yml
```

Example usage from a workflow in this repository:

```yaml
name: Spend Governor

on:
  pull_request:
    paths:
      - "infra/**"
      - "bicep/**"
      - "ai-spend.yml"
      - ".spendgov.yml"

jobs:
  spendgov:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Run Spend Governor
        uses: ./.github/actions/spendgov
        with:
          path: "."
          markdown-report: "artifacts/spendgov-report.md"
          json-report: "artifacts/spendgov-report.json"
          output-sarif: "artifacts/spendgov.sarif"
          github-annotations: "true"
          annotations-min-severity: "warning"
          fail-on: "fail"
          repository: ${{ github.repository }}
          pr-number: ${{ github.event.pull_request.number }}
          base-branch: ${{ github.base_ref }}
          head-branch: ${{ github.head_ref }}
          commit-sha: ${{ github.sha }}

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: spendgov-reports
          path: artifacts/
```

The repository also includes `.github/actions/spendgov-scan/action.yml` as an alias with the same SARIF and annotation inputs.

## Notes

- The CLI does not require SQL Server, LocalDB, the dashboard, GitHub webhooks, or cloud credentials.
- The CLI uses the local versioned pricing catalogs by default.
- The CLI does not run Terraform, Azure CLI, or Bicep. Generate Terraform Plan JSON or Bicep compiled ARM JSON before running it when you want the most accurate analysis.
