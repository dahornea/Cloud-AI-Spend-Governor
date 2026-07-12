# GitHub Action

The repository includes local composite Actions for running Spend Governor in CI without deploying the web app:

```txt
.github/actions/spendgov/action.yml
.github/actions/spendgov-scan/action.yml
```

Both run the same `SpendGovernor.Cli` project and support Markdown, JSON, SARIF, and GitHub Actions annotations.

## Example

```yaml
name: Spend Governor

on:
  pull_request:

jobs:
  spendgov:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore
        run: dotnet restore CloudAiSpendGovernor.slnx --configfile NuGet.Config

      - name: Run Spend Governor
        uses: ./.github/actions/spendgov-scan
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

      - name: Add report to Step Summary
        if: always()
        run: cat artifacts/spendgov-report.md >> "$GITHUB_STEP_SUMMARY"

      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: spendgov-reports
          path: artifacts/
```

## Inputs

```txt
path
baseline-path
markdown-report
json-report
output-sarif
github-annotations
annotations-min-severity
upload-sarif
fail-on
repository
pr-number
base-branch
head-branch
commit-sha
```

`upload-sarif` defaults to `false`. Enable it only when the repository supports GitHub Code Scanning and the workflow has the required permissions.

## Demo Workflow

See `.github/workflows/spendgov-scan-demo.yml`.

The demo workflow uses `fail-on: never` so portfolio/demo runs do not fail permanently when using intentionally expensive sample data. Change it to `fail` for real pull request gates.
