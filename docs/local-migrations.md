# Local Migrations And Troubleshooting

## LocalDB

Apply migrations:

```powershell
dotnet ef database update --project src\SpendGovernor.Infrastructure\SpendGovernor.Infrastructure.csproj --startup-project src\SpendGovernor.Api\SpendGovernor.Api.csproj --context SpendGovernorDbContext
```

Default database:

```txt
Server=(localdb)\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

## Docker SQL Server

1. Copy `.env.example` to `.env`.
2. Set a local `SA_PASSWORD`.
3. Start SQL Server and the app:

```powershell
docker compose up --build
```

For migration commands from the host, set `ConnectionStrings__SpendGovernorDb` to the exposed Docker SQL Server port:

```powershell
$env:ConnectionStrings__SpendGovernorDb = "Server=localhost,14333;Database=Spend-Governor;User Id=sa;Password=<your-local-password>;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=true"
dotnet ef database update --project src\SpendGovernor.Infrastructure\SpendGovernor.Infrastructure.csproj --startup-project src\SpendGovernor.Api\SpendGovernor.Api.csproj --context SpendGovernorDbContext
```

Clear the temporary environment variable after use:

```powershell
Remove-Item Env:\ConnectionStrings__SpendGovernorDb
```

## Common Issues

- Build fails because DLLs are locked: stop any running `SpendGovernor.Api` process or close the Visual Studio debug session.
- `dotnet ef` is unavailable: install or update the EF tool with `dotnet tool install --global dotnet-ef` or `dotnet tool update --global dotnet-ef`.
- SQL Server LocalDB is missing: install the SQL Server Express LocalDB component or use Docker Compose.
- Login fails in Docker SQL Server: confirm the `.env` `SA_PASSWORD` matches the connection string used by migration commands.
- App starts but `/health` is unhealthy: the API process is running, but the database connection failed; check connection string, SQL Server startup time, and password.
- Demo seed creates no rows: run the app with `ASPNETCORE_ENVIRONMENT=Development`; the seed/reset endpoints are development-only.

## Verification In SQL Server Object Explorer

Open database `Spend-Governor` and confirm these tables exist:

- `ApplicationUsers`
- `Workspaces`
- `WorkspaceMembers`
- `Projects`
- `EnvironmentBudgets`
- `Repositories`
- `PullRequestScans`
- `CostBreakdownItems`
- `DetectedResources`
- `ScanAssumptions`
- `PolicyEvaluations`
- `__EFMigrationsHistory`

After seeding demo data, `PullRequestScans` should contain three completed demo scans, and the child tables should contain rows for each scan.
