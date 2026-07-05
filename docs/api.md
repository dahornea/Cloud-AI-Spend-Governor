# API Notes

The MVP API is a .NET minimal API. It intentionally avoids generated OpenAPI/Swagger dependencies for now, but the local endpoints below are stable enough for demo and testing.

## Health

```http
GET /health
```

Checks the API process and database connectivity.

## Auth And User Context

```http
POST /api/auth/register
POST /api/auth/login
POST /api/auth/logout
GET /api/me
```

Development requests without a cookie fall back to `demo@spendgov.local`. You can also send `X-User-Email` for local user switching.

## Projects And Analyses

```http
GET /api/workspaces
POST /api/workspaces
GET /api/workspaces/{workspaceId}/projects
POST /api/projects
GET /api/projects/{projectId}
GET /api/projects/{projectId}/analyses
POST /api/projects/{projectId}/analyses
GET /api/analyses/{analysisId}
POST /api/analyses/{analysisId}/rerun
```

Manual analysis requests accept changed files plus baseline/proposed file contents. Results are persisted as `PullRequestScans` and related child rows.

## Demo

Development only:

```http
GET /api/dev/demo/status
POST /api/dev/demo/seed
DELETE /api/dev/demo/reset
POST /api/demo/projects/{projectId}/analyze
```

## GitHub Webhook

```http
POST /api/github/webhooks
```

Required for signed deliveries:

```txt
X-Hub-Signature-256: sha256=<hmac>
GitHub__WebhookSecret=<same secret configured in GitHub>
```

Webhook scans are persisted as queued rows and processed by the background scan worker. The accepted response returns the scan id immediately.

## Exports

```http
GET /api/analyses/{analysisId}/export/resources.csv
GET /api/analyses/{analysisId}/export/policy-findings.csv
GET /api/analyses/{analysisId}/export/recommendations.csv
GET /api/projects/{projectId}/export/summary.csv
```
