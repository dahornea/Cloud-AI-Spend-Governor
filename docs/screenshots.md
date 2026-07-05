# Screenshots To Capture

No screenshots are committed yet. Do not add fabricated screenshots. Capture real screenshots after running the local demo.

Save images under:

```txt
docs/assets/
```

Recommended captures after clicking `Seed Demo Data`:

- `dashboard-overview.png`: overview hero, summary metrics, latest persisted scans, and policy snapshot.
- `dashboard-latest-scans.png`: analyses view with search/filter controls and the seeded cheap, expensive cloud, and AI scans visible.
- `scan-cheap-pass.png`: cheap cloud change detail with PASS decision, low monthly delta, resources, assumptions, and "No blocking action needed."
- `scan-expensive-cloud-fail.png`: expensive cloud change detail with FAIL decision, Redis/App Service/Log Analytics cost breakdown, and recommendation.
- `scan-ai-workflow.png`: expensive AI workflow detail with model, runs, token counts, estimated cost, and assumptions.
- `pricing-metadata.png`: pricing metadata/confidence section showing catalog or Azure Retail source, match type, and fallback status.
- `arm-bicep-detail.png`: ARM/Bicep detail section from a compiled ARM JSON scan, if demonstrating Bicep support.
- `project-budgets.png`: workspace/project budget configuration with enabled policy rows.
- `github-pr-report.png`: GitHub PR report preview or a real PR comment with the hidden marker excluded from the screenshot crop.
- `health-check.png`: `/health` returning healthy.

Good screenshot route:

1. Open `http://localhost:5102`.
2. Seed demo data from the dashboard.
3. Capture the overview first, then open `Analyses`.
4. Use the decision/environment filters to isolate each scenario before opening the detail page.
5. Capture the PR report preview from the bottom of the scan detail page, or use a real GitHub PR comment when configured.

Before publishing screenshots, check that they do not reveal:

- Real tokens or webhook secrets.
- Private keys.
- Personal emails.
- Private repository names.
- Production database connection strings.
