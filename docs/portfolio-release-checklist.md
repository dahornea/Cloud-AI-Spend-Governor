# Portfolio Release Checklist

- [x] README explains the problem and solution clearly.
- [ ] Landing page screenshot added.
- [ ] Dashboard screenshot added.
- [ ] Scan detail screenshots added.
- [ ] GitHub PR report preview added.
- [ ] Architecture diagram screenshot included.
- [x] Sample PR report exists.
- [x] Demo walkthrough exists.
- [x] CLI and local GitHub Action usage are documented.
- [x] CV bullets are documented.
- [x] Local setup instructions work.
- [x] Database migration instructions are documented.
- [x] Tests pass locally.
- [x] CI workflow exists.
- [x] No real secrets are committed in example configuration.
- [x] Known limitations are honest.
- [x] Repository has a strong GitHub description suggestion.
- [x] Repository topics are suggested.
- [ ] Repository metadata is updated on GitHub.
- [ ] Repository is ready to pin on GitHub.

## Manual Verification Before Pinning

1. Run `dotnet build CloudAiSpendGovernor.slnx`.
2. Run `dotnet run --project tests\SpendGovernor.Tests\SpendGovernor.Tests.csproj`.
3. Apply database migrations.
4. Start the app locally.
5. Confirm `/health` is healthy.
6. Seed demo data from the dashboard.
7. Capture real screenshots into `docs/assets/`.
8. Confirm README screenshot links are still placeholders unless image files exist.
9. Review `docs/sample-pr-report.md`.
10. Re-run a secret scan before committing.

## Screenshot Assets Still Needed

- [ ] `docs/assets/landing-page.png`
- [ ] `docs/assets/dashboard-overview.png`
- [ ] `docs/assets/scan-history.png`
- [ ] `docs/assets/scan-detail-expensive-cloud.png`
- [ ] `docs/assets/scan-detail-ai-workflow.png`
- [ ] `docs/assets/pricing-metadata.png`
- [ ] `docs/assets/github-pr-report-preview.png`
- [ ] `docs/assets/environment-budgets.png`
- [ ] `docs/assets/architecture-diagram.png`
