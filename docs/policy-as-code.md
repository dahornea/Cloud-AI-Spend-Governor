# Policy-as-Code

Cloud & AI Spend Governor supports declarative spend guardrails in `.spendgov.yml`. These policies run through the same scan engine in the web app, GitHub webhook scans, the `spendgov` CLI, and the local GitHub Action wrapper.

Policies do not execute scripts or arbitrary code. They are YAML-defined match and condition rules evaluated against detected cloud resources, AI workflows, pricing metadata, confidence, and budgets.

## Example

```yaml
version: 1
environment: dev
currency: EUR
defaultRegion: westeurope

budgets:
  dev:
    maxMonthlyDelta: 100
    maxMonthlyCost: 500
    action: block

policies:
  - id: dev-no-premium-skus
    title: "No premium SKUs in dev"
    severity: fail
    environments:
      - dev
    match:
      provider: Azure
      skuContainsAny:
        - Premium
        - P1v3
        - P2v3
    message: "Premium SKUs are not allowed in dev."
    recommendation: "Use Basic/Standard SKUs or environment-specific configuration."

  - id: ai-expensive-model-high-volume
    title: "Expensive AI model on high-volume workflow"
    severity: fail
    match:
      type: aiWorkflow
      modelIn:
        - gpt-4.1
        - gpt-4o
    condition:
      estimatedMonthlyCostGreaterThan: 250
    message: "High-volume workflow uses an expensive model."
    recommendation: "Use a smaller model or reduce token usage."
```

## Severity

```txt
info  -> records a matched policy but does not change PASS/WARN/FAIL
warn  -> final decision is at least WARN
fail  -> final decision is FAIL
```

Existing budget rules still run first. Policy-as-Code findings are then added to the final decision.

## Match Fields

Supported `match` fields:

```txt
type
provider
resourceType
resourceName
resourceNameContains
sku
skuContains
skuContainsAny
region
environment
analysisSource
model
modelIn
```

For AI workflows, use:

```yaml
match:
  type: aiWorkflow
  model: gpt-4.1
```

## Condition Fields

Supported `condition` fields:

```txt
estimatedMonthlyCostGreaterThan
estimatedMonthlyDeltaGreaterThan
confidenceBelow
budgetExceeded
pricingFallbackUsed
regionMissing
skuMissing
analysisSourceIsFallback
```

Example:

```yaml
condition:
  confidenceBelow: High
```

## Validation

Validation catches:

- missing policy id;
- duplicate policy id;
- invalid severity;
- unknown match fields;
- unknown condition fields;
- invalid numeric thresholds;
- empty message;
- invalid confidence value;
- empty model/SKU lists.

The CLI returns exit code `2` when policy configuration is invalid. Web and GitHub scans persist the validation warning in scan details and reports.

## CLI Usage

```powershell
dotnet run --project src\SpendGovernor.Cli\SpendGovernor.Cli.csproj -- scan --path . --markdown artifacts\spendgov-report.md --json artifacts\spendgov-report.json --fail-on fail
```

The Markdown and JSON reports include `Policy-as-Code` evaluations.

## GitHub Action Usage

```yaml
- uses: ./.github/actions/spendgov
  with:
    path: "."
    fail-on: "fail"
    markdown-report: "artifacts/spendgov-report.md"
    json-report: "artifacts/spendgov-report.json"
```

Policy decisions affect the Action through the CLI exit code:

```txt
fail policy + fail-on fail -> job fails
warn policy + fail-on warn -> job fails
warn policy + fail-on fail -> job passes with warning report
```

## Limitations

- No OPA/Rego engine yet.
- No custom scripts or dynamic code execution.
- No remote policy registry.
- Matching is based on detected scan metadata; missing fields fail safely unless a condition explicitly checks for missing data.
