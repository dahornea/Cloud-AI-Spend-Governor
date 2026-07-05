# Local Development

## Prerequisites

- .NET SDK 10.x.
- SQL Server LocalDB, SQL Server Express, or Docker.
- Optional: `dotnet-ef` global tool.
- Optional for real GitHub webhook testing: ngrok or cloudflared.

## Restore And Build

```powershell
dotnet restore CloudAiSpendGovernor.slnx --configfile NuGet.Config
dotnet build CloudAiSpendGovernor.slnx
```

## Database

Default LocalDB connection string:

```txt
Server=(localdb)\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

Apply migrations:

```powershell
dotnet ef database update --project src\SpendGovernor.Infrastructure\SpendGovernor.Infrastructure.csproj --startup-project src\SpendGovernor.Api\SpendGovernor.Api.csproj --context SpendGovernorDbContext
```

## Run The App

```powershell
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Open http://localhost:5102.

## Seed Demo Data

Development only:

```powershell
Invoke-RestMethod -Method Post http://localhost:5102/api/dev/demo/seed
```

Reset:

```powershell
Invoke-RestMethod -Method Delete http://localhost:5102/api/dev/demo/reset
```

Seeded scenarios:

- Cheap cloud change: PASS.
- Expensive cloud change: FAIL.
- Expensive AI workflow: WARN.

## Run Tests

```powershell
dotnet run --project tests\SpendGovernor.Tests\SpendGovernor.Tests.csproj
```

## GitHub Simulated Mode

Local config defaults to simulated GitHub reporting. Dashboard/demo scans do not require GitHub secrets.

For signed webhook testing, set a local secret:

```powershell
$env:GitHub__WebhookSecret = "<local-webhook-secret>"
```

Do not commit secrets. Use environment variables, user secrets, or a local ignored file.

## Docker

```powershell
Copy-Item .env.example .env
notepad .env
docker compose up --build
```

Set `SA_PASSWORD` in `.env` before running Compose.
