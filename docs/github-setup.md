# GitHub Setup

The MVP supports simulated and real GitHub publishing.

## Simulated Mode

Simulated mode is the default local/demo path.

```powershell
$env:GitHub__Mode = "Simulated"
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

In simulated mode, the app still verifies signed webhook payloads when a webhook secret is configured, persists the scan, and stores simulated PR comment/check metadata. This is useful for demos without installing a real GitHub App.

Dashboard seed/manual demo flows do not require a webhook secret.

## Real GitHub App Mode

Real mode is implemented for GitHub App PR comments and optional check runs.

Required GitHub App permissions:

- Metadata: read-only.
- Contents: read-only.
- Pull requests: read-only.
- Issues: read and write, because PR comments use the Issues comments API.
- Checks: read and write, if check runs are enabled.

Subscribe to the `Pull request` event.

## Local Tunnel

Expose the local API:

```powershell
ngrok http 5102
```

Use the HTTPS forwarding URL plus:

```txt
/api/github/webhooks
```

Example:

```txt
https://example.ngrok-free.app/api/github/webhooks
```

## Environment Variables

```powershell
$env:GitHub__Mode = "Real"
$env:GitHub__AppId = "<your-github-app-id>"
$env:GitHub__PrivateKeyPath = "C:\path\outside\repo\github-app.pem"
$env:GitHub__WebhookSecret = "<same-secret-configured-in-github>"
$env:GitHub__EnableCheckRuns = "true"
dotnet run --project src\SpendGovernor.Api\SpendGovernor.Api.csproj --urls http://localhost:5102
```

Alternative private key configuration:

```powershell
$env:GitHub__PrivateKey = "<pem-contents>"
```

Do not commit private keys, webhook secrets, tokens, or `.env`.

## Webhook Payload Notes

The webhook receiver supports pull request actions:

- `opened`
- `synchronize`
- `reopened`
- `ready_for_review`

For local MVP payload testing, the webhook can include:

- `spendgov_changed_files`
- `spendgov_baseline_files`
- `spendgov_proposed_files`

Real production-grade file fetching from GitHub is not the focus of this MVP.
