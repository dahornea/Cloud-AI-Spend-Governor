# Portfolio Release Checklist

- [x] README explains problem and solution clearly.
- [x] Architecture diagram is included.
- [x] Local setup instructions are documented.
- [x] Database migration instructions are documented.
- [x] Test command is documented.
- [x] CI workflow exists.
- [x] Demo seed flow is documented.
- [x] Sample PR report exists.
- [ ] Screenshots are captured from a real local demo.
- [x] Demo script exists.
- [x] CV bullets are documented.
- [x] Known limitations are honest.
- [x] No secrets are committed in example configuration.
- [x] Repository description suggestion is documented.
- [ ] Repository metadata is updated on GitHub.
- [ ] Repository is pinned on GitHub after screenshots are added.

## Manual Verification Before Pinning

1. Run `dotnet build CloudAiSpendGovernor.slnx`.
2. Run `dotnet run --project tests\SpendGovernor.Tests\SpendGovernor.Tests.csproj`.
3. Apply migrations.
4. Start the app.
5. Confirm `/health` is healthy.
6. Seed demo data.
7. Capture screenshots into `docs/assets/`.
8. Re-run a secret scan before committing.
