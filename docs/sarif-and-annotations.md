# SARIF and GitHub Actions Annotations

Cloud & AI Spend Governor can produce CI-native diagnostics in addition to Markdown and JSON reports.

These outputs are useful when you want cloud/AI spend policy violations to behave like linter, security scanner, or code quality findings inside pull request workflows.

## Outputs

The CLI can emit:

- Markdown report: human-readable PR summary.
- JSON report: machine-readable scan data and findings.
- SARIF 2.1.0 report: uploadable as an artifact or optionally to GitHub Code Scanning.
- GitHub Actions annotations: inline `::error`, `::warning`, and `::notice` workflow commands.

## Local CLI Usage

```powershell
dotnet run --project src\SpendGovernor.Cli\SpendGovernor.Cli.csproj -- scan `
  --path demo\scenario-expensive-cloud-change `
  --markdown artifacts\spendgov-report.md `
  --json artifacts\spendgov-report.json `
  --output-sarif artifacts\spendgov.sarif `
  --github-annotations `
  --annotations-min-severity warning `
  --fail-on never
```

Use `--fail-on fail` when you want FAIL decisions to block CI.

## Generated Findings

Findings are generated from the existing scan result:

```txt
budget failures
Policy-as-Code matches
high monthly impact resources and AI workflows
low-confidence estimates
pricing fallback
unknown pricing
config and analyzer warnings
```

Each finding includes:

```txt
rule id
severity
category
message
recommendation
source file and best-effort line
resource/workflow metadata
monthly cost and delta
confidence
pricing source and match type
```

Severity maps to CI outputs as:

```txt
error   -> failed budget or fail policy
warning -> warn policy, low confidence, unknown pricing, high impact
note    -> informational pricing fallback or info policy
```

## SARIF

SARIF output uses stable rule IDs such as:

```txt
spendgov.budget.maxMonthlyDelta
spendgov.policy.dev-no-premium-skus
spendgov.cost.highMonthlyImpact
spendgov.confidence.low
spendgov.pricing.fallback
spendgov.pricing.unknown
```

SARIF includes tool metadata, rules, results, physical locations when available, and properties such as monthly cost, currency, confidence, environment, resource type, and pricing source.

## GitHub Actions Annotations

Enable annotations with:

```txt
--github-annotations
```

Filter annotation noise with:

```txt
--annotations-min-severity error
--annotations-min-severity warning
--annotations-min-severity note
```

Annotation text and properties are escaped for GitHub workflow commands. Findings are deduplicated and capped to avoid excessive repeated annotations.

## GitHub Code Scanning

SARIF upload is optional and depends on repository settings and permissions. The sample workflow keeps upload disabled by default:

```yaml
# Optional: upload SARIF to GitHub Code Scanning if enabled for the repository.
# - name: Upload SARIF
#   uses: github/codeql-action/upload-sarif@v3
#   with:
#     sarif_file: artifacts/spendgov.sarif
```

## Security

Spend Governor does not intentionally emit secrets. Findings are generated from resource metadata, policy messages, pricing metadata, and validation warnings. Obvious `password`, `secret`, `token`, `key`, and `connectionString` assignments are redacted in generated finding messages.

Do not put secrets in `.spendgov.yml` policy messages or recommendations.

## Limitations

- Terraform Plan JSON does not always include exact source line information.
- Bicep/ARM JSON may map to generated ARM template files, not original `.bicep` line numbers.
- Source locations default to line 1 when the analyzer knows the file but not the exact line.
- SARIF upload is optional and depends on GitHub Code Scanning availability.
- This does not require paid deployment, cloud credentials, Azure Cost Management, or a running web app.
