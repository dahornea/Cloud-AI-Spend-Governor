using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;
using SpendGovernor.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<IPricingAdapter, SeededAzurePricingAdapter>();
builder.Services.AddSingleton<AiModelPriceCatalog>();
builder.Services.AddSingleton<MonthlyCostEstimator>();
builder.Services.AddSingleton<AnalysisEngine>();
builder.Services.AddSingleton<SpendGovernorStore>();
builder.Services.AddDbContext<SpendGovernorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SpendGovernorDb")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"));
builder.Services.AddScoped<IRepositoryStore, RepositoryStore>();
builder.Services.AddScoped<IScanResultWriter, ScanResultWriter>();
builder.Services.AddScoped<IScanStore, ScanStore>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/me", (HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return Results.Ok(user);
});

app.MapGet("/api/workspaces", (HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return Results.Ok(store.GetWorkspaces(user.Id));
});

app.MapPost("/api/workspaces", (CreateWorkspaceRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var workspace = store.CreateWorkspace(user, request.Name);
    return Results.Created($"/api/workspaces/{workspace.Id}", workspace);
});

app.MapGet("/api/workspaces/{workspaceId:guid}", (Guid workspaceId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return store.CanAccessWorkspace(user.Id, workspaceId)
        ? Results.Ok(store.GetWorkspace(workspaceId))
        : Results.NotFound();
});

app.MapPost("/api/projects", async (CreateProjectRequest request, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore) =>
{
    var user = CurrentUser(context, store);
    if (!store.CanEditWorkspace(user.Id, request.WorkspaceId))
    {
        return Results.Forbid();
    }

    var project = store.CreateProject(request);
    await repositoryStore.FindOrCreateAsync("github", project.RepositoryOwner, project.RepositoryName, "main", null, project.GitHubInstallationId, context.RequestAborted);
    return Results.Created($"/api/projects/{project.Id}", project);
});

app.MapGet("/api/workspaces/{workspaceId:guid}/projects", (Guid workspaceId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return store.CanAccessWorkspace(user.Id, workspaceId)
        ? Results.Ok(store.GetProjects(workspaceId))
        : Results.NotFound();
});

app.MapGet("/api/projects/{projectId:guid}", async (Guid projectId, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var repository = await repositoryStore.FindByProviderAndFullNameAsync("github", $"{project.RepositoryOwner}/{project.RepositoryName}", context.RequestAborted);
    var metrics = repository is null
        ? ProjectMetrics.Empty
        : ProjectMetrics.FromScans(await scanStore.GetLatestScansForRepositoryAsync(repository.Id, 5, context.RequestAborted), project.Currency);
    return Results.Ok(new ProjectDetailResponse(project, metrics));
});

app.MapPatch("/api/projects/{projectId:guid}/settings", (Guid projectId, PatchProjectSettingsRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (!store.CanEditWorkspace(user.Id, project.WorkspaceId))
    {
        return Results.Forbid();
    }

    store.UpdateProject(projectId, request);
    return Results.Ok(project);
});

app.MapGet("/api/projects/{projectId:guid}/analyses", async (Guid projectId, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var repository = await repositoryStore.FindByProviderAndFullNameAsync("github", $"{project.RepositoryOwner}/{project.RepositoryName}", context.RequestAborted);
    if (repository is null)
    {
        return Results.Ok(Array.Empty<AnalysisListItem>());
    }

    var scans = await scanStore.GetLatestScansForRepositoryAsync(repository.Id, 50, context.RequestAborted);
    return Results.Ok(scans.Select(AnalysisListItem.FromScan));
});

app.MapPost("/api/projects/{projectId:guid}/analyses", async (Guid projectId, RunAnalysisRequest request, HttpContext context, SpendGovernorStore store, AnalysisEngine engine, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var analysisRequest = request.ToAnalysisRequest(project, DashboardBaseUrl(context));
    var repository = await EnsureRepositoryAsync(project, repositoryStore, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repository, analysisRequest, context.RequestAborted);
    await scanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow, context.RequestAborted);
    var result = engine.Analyze(analysisRequest);
    await PersistScanResultAsync(project, repository, scan.Id, result, analysisRequest, store, scanStore, context.RequestAborted);
    var persisted = await scanStore.GetScanDetailsAsync(scan.Id, context.RequestAborted);
    return Results.Created($"/api/analyses/{scan.Id}", AnalysisDetailResponse.FromScan(persisted!));
});

app.MapPost("/api/demo/projects/{projectId:guid}/analyze", async (Guid projectId, DemoRunRequest request, HttpContext context, SpendGovernorStore store, AnalysisEngine engine, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var analysisRequest = DemoScenarios.Create(project, request.Scenario, DashboardBaseUrl(context));
    var repository = await EnsureRepositoryAsync(project, repositoryStore, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repository, analysisRequest, context.RequestAborted);
    await scanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow, context.RequestAborted);
    var result = engine.Analyze(analysisRequest);
    await PersistScanResultAsync(project, repository, scan.Id, result, analysisRequest, store, scanStore, context.RequestAborted);
    var persisted = await scanStore.GetScanDetailsAsync(scan.Id, context.RequestAborted);
    return Results.Created($"/api/analyses/{scan.Id}", AnalysisDetailResponse.FromScan(persisted!));
});

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/dev/demo/status", () =>
        Results.Ok(new DemoStatusResponse(true, "acme/spend-governor-demo", DemoScenarios.SeedScenarioIds)));

    app.MapPost("/api/dev/demo/seed", async (
        HttpContext context,
        SpendGovernorStore store,
        AnalysisEngine engine,
        IRepositoryStore repositoryStore,
        IScanStore scanStore,
        SpendGovernorDbContext dbContext) =>
    {
        var user = CurrentUser(context, store);
        var project = store.EnsureDemoProject(user);
        var reset = await ResetDemoDataAsync(dbContext, context.RequestAborted);
        var repository = await EnsureRepositoryAsync(project, repositoryStore, context.RequestAborted);
        var seededScans = new List<AnalysisListItem>();

        foreach (var scenario in DemoScenarios.SeedScenarioIds)
        {
            var request = DemoScenarios.Create(project, scenario, DashboardBaseUrl(context));
            var scan = await scanStore.CreateScanAsync(repository, request, context.RequestAborted);
            await scanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow, context.RequestAborted);
            var result = engine.Analyze(request);
            await PersistScanResultAsync(project, repository, scan.Id, result, request, store, scanStore, context.RequestAborted);
            var persisted = await scanStore.GetScanDetailsAsync(scan.Id, context.RequestAborted);
            if (persisted is not null)
            {
                seededScans.Add(AnalysisListItem.FromScan(persisted));
            }
        }

        store.AddAudit(project.WorkspaceId, project.Id, null, "Demo data seeded", "Seeded the three local demo scans into SQL Server LocalDB.");
        return Results.Ok(new DemoSeedResponse(project.Id, repository.FullName, reset.DeletedScans, seededScans.ToArray()));
    });

    app.MapDelete("/api/dev/demo/reset", async (HttpContext context, SpendGovernorDbContext dbContext) =>
    {
        var reset = await ResetDemoDataAsync(dbContext, context.RequestAborted);
        return Results.Ok(reset);
    });
}

app.MapGet("/api/analyses/{analysisId:guid}", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForRepositoryForUser(scan.Repository.Owner, scan.Repository.Name, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(AnalysisDetailResponse.FromScan(scan));
});

app.MapPost("/api/analyses/{analysisId:guid}/rerun", async (Guid analysisId, HttpContext context, SpendGovernorStore store, AnalysisEngine engine, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var original = store.GetAnalysisForUser(analysisId, user.Id);
    if (original is null)
    {
        return Results.NotFound();
    }

    var request = store.GetRequest(analysisId);
    if (request is null)
    {
        return Results.BadRequest(new { error = "The original analysis request was not stored." });
    }

    var project = store.GetProjectForUser(request.ProjectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var rerun = engine.Analyze(request with { CommitSha = request.CommitSha + "-rerun" });
    var repository = await EnsureRepositoryAsync(project, repositoryStore, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repository, request, context.RequestAborted);
    await scanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow, context.RequestAborted);
    await PersistScanResultAsync(project, repository, scan.Id, rerun, request, store, scanStore, context.RequestAborted);
    var persisted = await scanStore.GetScanDetailsAsync(scan.Id, context.RequestAborted);
    return Results.Created($"/api/analyses/{scan.Id}", AnalysisDetailResponse.FromScan(persisted!));
});

app.MapGet("/api/github/comments/{owner}/{repo}/{pullRequestNumber:int}", (string owner, string repo, int pullRequestNumber, SpendGovernorStore store) =>
{
    var comment = store.GetGitHubComment(owner, repo, pullRequestNumber);
    return comment is null ? Results.NotFound() : Results.Ok(comment);
});

app.MapGet("/api/analyses/{analysisId:guid}/export/resources.csv", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForRepositoryForUser(scan.Repository.Owner, scan.Repository.Name, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Text(ResourcesCsv(scan), "text/csv");
});

app.MapGet("/api/analyses/{analysisId:guid}/export/policy-findings.csv", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForRepositoryForUser(scan.Repository.Owner, scan.Repository.Name, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Text(PolicyEvaluationsCsv(scan), "text/csv");
});

app.MapGet("/api/analyses/{analysisId:guid}/export/recommendations.csv", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForRepositoryForUser(scan.Repository.Owner, scan.Repository.Name, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Text("analysisId,severity,title,description,estimatedMonthlySavings\r\n", "text/csv");
});

app.MapGet("/api/projects/{projectId:guid}/export/summary.csv", async (Guid projectId, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var repository = await repositoryStore.FindByProviderAndFullNameAsync("github", $"{project.RepositoryOwner}/{project.RepositoryName}", context.RequestAborted);
    if (repository is null)
    {
        return Results.Text("analysisId,repository,pr,branch,environment,status,decision,monthlyDelta,currency,confidence,createdAt,completedAt,failureReason\r\n", "text/csv");
    }

    var scans = await scanStore.GetLatestScansForRepositoryAsync(repository.Id, 100, context.RequestAborted);
    return Results.Text(ProjectSummaryCsv(scans), "text/csv");
});

app.MapGet("/api/projects/{projectId:guid}/policies", (Guid projectId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    return project is null
        ? Results.NotFound()
        : Results.Ok(new PolicyResponse(project.PolicyYaml, SpendGovConfigParser.Parse(project.PolicyYaml, ProjectSettings.FromProject(project))));
});

app.MapPut("/api/projects/{projectId:guid}/policies", (Guid projectId, PolicyUpdateRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (!store.CanEditWorkspace(user.Id, project.WorkspaceId))
    {
        return Results.Forbid();
    }

    project.PolicyYaml = request.Yaml;
    store.AddAudit(project.WorkspaceId, project.Id, null, "Config changed", ".spendgov.yml policy settings were updated from the dashboard.");
    return Results.Ok(new PolicyResponse(project.PolicyYaml, SpendGovConfigParser.Parse(project.PolicyYaml, ProjectSettings.FromProject(project))));
});

app.MapPost("/api/analyses/{analysisId:guid}/approve", (Guid analysisId, ApprovalRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var result = store.GetAnalysisForUser(analysisId, user.Id);
    if (result is null)
    {
        return Results.NotFound();
    }

    var project = store.GetProject(result.Analysis.ProjectId);
    if (project is null || !store.CanEditWorkspace(user.Id, project.WorkspaceId))
    {
        return Results.Forbid();
    }

    var approval = store.Approve(result, user, request.Reason);
    return Results.Ok(approval);
});

app.MapGet("/api/projects/{projectId:guid}/approvals", (Guid projectId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return store.GetProjectForUser(projectId, user.Id) is null
        ? Results.NotFound()
        : Results.Ok(store.GetApprovals(projectId));
});

app.MapGet("/api/projects/{projectId:guid}/audit-events", (Guid projectId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    return store.GetProjectForUser(projectId, user.Id) is null
        ? Results.NotFound()
        : Results.Ok(store.GetAudit(projectId));
});

app.MapGet("/api/github/installations/callback", (HttpContext context, SpendGovernorStore store) =>
{
    var installationId = context.Request.Query["installation_id"].ToString();
    var projectIdRaw = context.Request.Query["projectId"].ToString();
    if (string.IsNullOrWhiteSpace(installationId) || !Guid.TryParse(projectIdRaw, out var projectId))
    {
        return Results.BadRequest(new { error = "installation_id and projectId are required." });
    }

    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    if (!store.CanEditWorkspace(user.Id, project.WorkspaceId))
    {
        return Results.Forbid();
    }

    project.GitHubInstallationId = installationId;
    store.AddAudit(project.WorkspaceId, project.Id, null, "GitHub App installed", $"Installation {installationId} linked to {project.RepositoryOwner}/{project.RepositoryName}.");
    return Results.Ok(new { project.Id, project.GitHubInstallationId });
});

app.MapPost("/api/github/webhooks", async (HttpContext context, SpendGovernorStore store, AnalysisEngine engine, IConfiguration configuration, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var payload = await reader.ReadToEndAsync();
    var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
    var secret = configuration["GitHub:WebhookSecret"] ?? "dev-secret";
    if (!GitHubSignatureVerifier.VerifySha256(payload, signature, secret))
    {
        return Results.Unauthorized();
    }

    using var document = JsonDocument.Parse(payload);
    var root = document.RootElement;
    var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : "";
    if (action is not ("opened" or "synchronize" or "reopened" or "closed"))
    {
        return Results.Accepted(value: new { skipped = true, reason = "Unsupported pull_request action." });
    }

    var repository = root.GetProperty("repository");
    var owner = repository.GetProperty("owner").GetProperty("login").GetString() ?? "";
    var repoName = repository.GetProperty("name").GetString() ?? "";
    var installationId = root.TryGetProperty("installation", out var installation)
        ? installation.GetProperty("id").GetRawText().Trim('"')
        : null;
    var project = store.FindProjectByRepository(owner, repoName, installationId);
    if (project is null)
    {
        return Results.Accepted(value: new { queued = false, reason = "No linked project found for repository." });
    }

    var pullRequest = root.GetProperty("pull_request");
    var changedFiles = ExtractWebhookChangedFiles(root);
    var proposedFiles = ExtractWebhookRepositoryFiles(root, "spendgov_proposed_files");
    var baselineFiles = ExtractWebhookRepositoryFiles(root, "spendgov_baseline_files");
    var request = new AnalysisRequest
    {
        ProjectId = project.Id,
        RepositoryOwner = owner,
        RepositoryName = repoName,
        PullRequestNumber = pullRequest.GetProperty("number").GetInt32(),
        BaseBranch = pullRequest.GetProperty("base").GetProperty("ref").GetString() ?? "main",
        HeadBranch = pullRequest.GetProperty("head").GetProperty("ref").GetString() ?? "feature",
        CommitSha = pullRequest.GetProperty("head").GetProperty("sha").GetString() ?? "webhook",
        ChangedFiles = changedFiles,
        BaselineFiles = baselineFiles,
        ProposedFiles = proposedFiles,
        Settings = ProjectSettings.FromProject(project),
        DashboardBaseUrl = DashboardBaseUrl(context)
    };
    var repositoryRecord = await repositoryStore.FindOrCreateAsync("github", owner, repoName, request.BaseBranch, repository.TryGetProperty("id", out var repoId) ? repoId.GetRawText().Trim('"') : null, installationId, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repositoryRecord, request, context.RequestAborted);
    await scanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow, context.RequestAborted);
    var result = engine.Analyze(request);
    var comment = await PersistScanResultAsync(project, repositoryRecord, scan.Id, result, request, store, scanStore, context.RequestAborted);
    store.AddAudit(project.WorkspaceId, project.Id, result.Analysis.Id, "GitHub PR comment simulated", "Analysis completed locally. Configure GitHub App credentials to post comments/checks to GitHub.");
    return Results.Accepted(value: new
    {
        analysisId = scan.Id,
        result.CheckConclusion,
        simulatedGitHubComment = true,
        comment.CommentId,
        comment.WasCreatedOnLastWrite,
        comment.UpdateCount
    });
});

app.MapFallbackToFile("index.html");

app.Run();

static User CurrentUser(HttpContext context, SpendGovernorStore store)
{
    var email = context.Request.Headers["X-User-Email"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(email))
    {
        email = "demo@spendgov.local";
    }

    return store.GetOrCreateUser(email);
}

static string DashboardBaseUrl(HttpContext context)
{
    return $"{context.Request.Scheme}://{context.Request.Host}";
}

static IReadOnlyList<string> ExtractWebhookChangedFiles(JsonElement root)
{
    if (!root.TryGetProperty("spendgov_changed_files", out var files) || files.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    return files.EnumerateArray()
        .Select(item => item.GetString())
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path!)
        .ToArray();
}

static IReadOnlyList<RepositoryFile> ExtractWebhookRepositoryFiles(JsonElement root, string propertyName)
{
    if (!root.TryGetProperty(propertyName, out var files) || files.ValueKind != JsonValueKind.Array)
    {
        return [];
    }

    var result = new List<RepositoryFile>();
    foreach (var item in files.EnumerateArray())
    {
        if (!item.TryGetProperty("path", out var pathElement) || !item.TryGetProperty("content", out var contentElement))
        {
            continue;
        }

        var path = pathElement.GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            continue;
        }

        result.Add(new RepositoryFile(path, contentElement.GetString() ?? ""));
    }

    return result;
}

static async Task<DemoResetResponse> ResetDemoDataAsync(SpendGovernorDbContext dbContext, CancellationToken cancellationToken)
{
    var repository = await dbContext.Repositories
        .FirstOrDefaultAsync(item => item.Provider == "github" && item.FullName == "acme/spend-governor-demo", cancellationToken);
    if (repository is null)
    {
        return new DemoResetResponse(0, 0, false);
    }

    var scanIds = await dbContext.PullRequestScans
        .Where(scan => scan.RepositoryId == repository.Id)
        .Select(scan => scan.Id)
        .ToArrayAsync(cancellationToken);
    var childRows = scanIds.Length == 0
        ? 0
        : await dbContext.CostBreakdownItems.CountAsync(item => scanIds.Contains(item.PullRequestScanId), cancellationToken)
            + await dbContext.DetectedResources.CountAsync(resource => scanIds.Contains(resource.PullRequestScanId), cancellationToken)
            + await dbContext.ScanAssumptions.CountAsync(assumption => scanIds.Contains(assumption.PullRequestScanId), cancellationToken)
            + await dbContext.PolicyEvaluations.CountAsync(evaluation => scanIds.Contains(evaluation.PullRequestScanId), cancellationToken);

    var scans = await dbContext.PullRequestScans
        .Where(scan => scan.RepositoryId == repository.Id)
        .ToArrayAsync(cancellationToken);
    dbContext.PullRequestScans.RemoveRange(scans);
    await dbContext.SaveChangesAsync(cancellationToken);

    dbContext.Repositories.Remove(repository);
    await dbContext.SaveChangesAsync(cancellationToken);
    dbContext.ChangeTracker.Clear();

    return new DemoResetResponse(scanIds.Length, childRows, true);
}

static async Task<SpendGovernor.Infrastructure.Persistence.Repository> EnsureRepositoryAsync(Project project, IRepositoryStore repositoryStore, CancellationToken cancellationToken)
{
    return await repositoryStore.FindOrCreateAsync(
        "github",
        project.RepositoryOwner,
        project.RepositoryName,
        "main",
        null,
        project.GitHubInstallationId,
        cancellationToken);
}

static async Task<GitHubPrCommentState> PersistScanResultAsync(
    Project project,
    SpendGovernor.Infrastructure.Persistence.Repository repository,
    Guid scanId,
    AnalysisResult result,
    AnalysisRequest request,
    SpendGovernorStore store,
    IScanStore scanStore,
    CancellationToken cancellationToken)
{
    if (!string.IsNullOrWhiteSpace(request.DashboardBaseUrl))
    {
        result.DashboardUrl = $"{request.DashboardBaseUrl.TrimEnd('/')}/?analysisId={scanId}";
        result.Analysis.DashboardUrl = result.DashboardUrl;
        result.CommentMarkdown = PrCommentRenderer.Render(result);
    }

    var existingCommentId = await scanStore.FindExistingGitHubCommentIdAsync(repository.Id, result.Analysis.PullRequestNumber, cancellationToken);
    var comment = store.SaveAnalysis(project, result, request, existingCommentId);
    var commentId = comment.CommentId.ToString(CultureInfo.InvariantCulture);
    if (result.Analysis.Status == AnalysisStatus.Failed)
    {
        await scanStore.MarkFailedAsync(scanId, result.Analysis.ErrorMessage ?? "Scan failed.", commentId, cancellationToken);
    }
    else
    {
        await scanStore.MarkCompletedAsync(scanId, result, commentId, cancellationToken);
    }

    return comment;
}

static string ResourcesCsv(PullRequestScan scan)
{
    var builder = new StringBuilder();
    builder.AppendLine("scanId,resourceName,resourceType,sourceFile,provider,region,sku,rawJson,createdAt");
    foreach (var resource in scan.DetectedResources)
    {
        builder.AppendLine(string.Join(',', new[]
        {
            Csv(scan.Id.ToString()),
            Csv(resource.ResourceName),
            Csv(resource.ResourceType),
            Csv(resource.SourceFile),
            Csv(resource.Provider),
            Csv(resource.Region),
            Csv(resource.Sku),
            Csv(resource.RawJson),
            Csv(resource.CreatedAt.ToString("O", CultureInfo.InvariantCulture))
        }));
    }

    return builder.ToString();
}

static string PolicyEvaluationsCsv(PullRequestScan scan)
{
    var builder = new StringBuilder();
    builder.AppendLine("scanId,ruleName,result,message,createdAt");
    foreach (var evaluation in scan.PolicyEvaluations)
    {
        builder.AppendLine(string.Join(',', new[]
        {
            Csv(scan.Id.ToString()),
            Csv(evaluation.RuleName),
            Csv(evaluation.Result.ToString()),
            Csv(evaluation.Message),
            Csv(evaluation.CreatedAt.ToString("O", CultureInfo.InvariantCulture))
        }));
    }

    return builder.ToString();
}

static string ProjectSummaryCsv(IEnumerable<PullRequestScan> scans)
{
    var builder = new StringBuilder();
    builder.AppendLine("analysisId,repository,pr,branch,environment,status,decision,monthlyDelta,currency,confidence,createdAt,completedAt,failureReason");
    foreach (var scan in scans)
    {
        builder.AppendLine(string.Join(',', new[]
        {
            Csv(scan.Id.ToString()),
            Csv(scan.Repository?.FullName),
            Csv(scan.PullRequestNumber.ToString(CultureInfo.InvariantCulture)),
            Csv(scan.SourceBranch),
            Csv(scan.Environment),
            Csv(scan.Status.ToString()),
            Csv(scan.Decision.ToString()),
            Csv(scan.EstimatedMonthlyDelta?.ToString("0.00", CultureInfo.InvariantCulture)),
            Csv(scan.Currency),
            Csv(scan.ConfidenceLevel.ToString()),
            Csv(scan.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            Csv(scan.CompletedAt?.ToString("O", CultureInfo.InvariantCulture)),
            Csv(scan.FailureReason)
        }));
    }

    return builder.ToString();
}

static string Csv(string? value)
{
    value ??= "";
    return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}

public sealed record CreateWorkspaceRequest(string Name);

public sealed record CreateProjectRequest(
    Guid WorkspaceId,
    string Name,
    string RepositoryOwner,
    string RepositoryName,
    string? DefaultRegion,
    string? Currency,
    int? HoursPerMonth);

public sealed record PatchProjectSettingsRequest(string? DefaultRegion, string? Currency, int? HoursPerMonth, string? PolicyYaml);

public sealed record RunAnalysisRequest(
    int PullRequestNumber,
    string? BaseBranch,
    string? HeadBranch,
    string? CommitSha,
    IReadOnlyList<string>? ChangedFiles,
    IReadOnlyList<AnalysisFileDto>? BaselineFiles,
    IReadOnlyList<AnalysisFileDto>? ProposedFiles)
{
    public AnalysisRequest ToAnalysisRequest(Project project, string dashboardBaseUrl)
    {
        return new AnalysisRequest
        {
            ProjectId = project.Id,
            RepositoryOwner = project.RepositoryOwner,
            RepositoryName = project.RepositoryName,
            PullRequestNumber = PullRequestNumber,
            BaseBranch = BaseBranch ?? "main",
            HeadBranch = HeadBranch ?? "feature/spend-change",
            CommitSha = CommitSha ?? Guid.NewGuid().ToString("n")[..12],
            ChangedFiles = ChangedFiles ?? [],
            BaselineFiles = BaselineFiles?.Select(file => new RepositoryFile(file.Path, file.Content)).ToArray() ?? [],
            ProposedFiles = ProposedFiles?.Select(file => new RepositoryFile(file.Path, file.Content)).ToArray() ?? [],
            Settings = ProjectSettings.FromProject(project),
            DashboardBaseUrl = dashboardBaseUrl
        };
    }
}

public sealed record AnalysisFileDto(string Path, string Content);

public sealed record DemoRunRequest(string Scenario);

public sealed record DemoStatusResponse(bool Enabled, string Repository, IReadOnlyList<string> Scenarios);

public sealed record DemoSeedResponse(Guid ProjectId, string Repository, int DeletedExistingScans, AnalysisListItem[] SeededScans);

public sealed record DemoResetResponse(int DeletedScans, int DeletedChildRows, bool DeletedRepository);

public sealed record PolicyUpdateRequest(string Yaml);

public sealed record ApprovalRequest(string Reason);

public sealed record ProjectDetailResponse(Project Project, ProjectMetrics Metrics);

public sealed record ProjectMetrics(int TotalPrsAnalyzed, decimal TotalMonthlyDeltaDetected, int WarnedOrBlockedPrs, AnalysisListItem[] LatestAnalyses)
{
    public static ProjectMetrics Empty { get; } = new(0, 0, 0, []);

    public static ProjectMetrics FromScans(IReadOnlyList<PullRequestScan> scans, string currency)
    {
        var totalDelta = scans.Sum(scan => scan.EstimatedMonthlyDelta ?? 0);
        var warnedOrBlocked = scans.Count(scan => scan.Decision is PolicyDecision.Warn or PolicyDecision.Fail);
        return new ProjectMetrics(
            scans.Count,
            decimal.Round(totalDelta, 2),
            warnedOrBlocked,
            scans.Take(5).Select(AnalysisListItem.FromScan).ToArray());
    }
}

public sealed record AnalysisListItem(
    Guid Id,
    int PullRequestNumber,
    string Repository,
    string CommitSha,
    string HeadBranch,
    string? Environment,
    AnalysisStatus Status,
    PolicyAction PolicyStatus,
    decimal? MonthlyDelta,
    decimal? BudgetLimitMonthly,
    string Currency,
    ConfidenceLevel OverallConfidence,
    int UnknownResourceCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static AnalysisListItem FromResult(AnalysisResult result)
    {
        var analysis = result.Analysis;
        return new AnalysisListItem(
            analysis.Id,
            analysis.PullRequestNumber,
            $"{analysis.RepositoryOwner}/{analysis.RepositoryName}",
            analysis.CommitSha,
            analysis.HeadBranch,
            analysis.Environment,
            analysis.Status,
            analysis.PolicyStatus,
            analysis.MonthlyDelta,
            analysis.BudgetLimitMonthly,
            analysis.Currency,
            analysis.OverallConfidence,
            analysis.UnknownResourceCount,
            analysis.CreatedAt,
            analysis.StartedAt,
            analysis.CompletedAt);
    }

    public static AnalysisListItem FromScan(PullRequestScan scan)
    {
        return new AnalysisListItem(
            scan.Id,
            scan.PullRequestNumber,
            scan.Repository?.FullName ?? "",
            "",
            scan.SourceBranch,
            scan.Environment,
            ToCoreStatus(scan.Status),
            ToCorePolicy(scan.Decision),
            scan.EstimatedMonthlyDelta,
            null,
            scan.Currency,
            ToCoreConfidence(scan.ConfidenceLevel),
            0,
            scan.CreatedAt,
            scan.StartedAt,
            scan.CompletedAt);
    }

    public static AnalysisStatus ToCoreStatus(ScanStatus status) => status switch
    {
        ScanStatus.Queued => AnalysisStatus.Queued,
        ScanStatus.Running => AnalysisStatus.Running,
        ScanStatus.Completed => AnalysisStatus.Completed,
        ScanStatus.Failed => AnalysisStatus.Failed,
        _ => AnalysisStatus.Failed
    };

    public static PolicyAction ToCorePolicy(PolicyDecision decision) => decision switch
    {
        PolicyDecision.Pass => PolicyAction.Pass,
        PolicyDecision.Warn => PolicyAction.Warn,
        PolicyDecision.Fail => PolicyAction.Block,
        _ => PolicyAction.Pass
    };

    public static ConfidenceLevel ToCoreConfidence(ScanConfidenceLevel confidence) => confidence switch
    {
        ScanConfidenceLevel.High => ConfidenceLevel.High,
        ScanConfidenceLevel.Medium => ConfidenceLevel.Medium,
        ScanConfidenceLevel.Low => ConfidenceLevel.Low,
        _ => ConfidenceLevel.Unknown
    };
}

public sealed record AnalysisDetailResponse(
    PullRequestAnalysis Analysis,
    IReadOnlyList<ResourceEstimate> Resources,
    IReadOnlyList<ResourceCostChange> CostChanges,
    IReadOnlyList<PolicyFinding> PolicyFindings,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<AuditEvent> AuditEvents,
    string CommentMarkdown,
    string CheckConclusion,
    IReadOnlyList<string> ConfigErrors,
    IReadOnlyList<ScanAssumptionItem> Assumptions,
    IReadOnlyList<PolicyEvaluationItem> PolicyEvaluations,
    string? GitHubCommentId,
    string? GitHubPullRequestUrl)
{
    public static AnalysisDetailResponse FromResult(AnalysisResult result)
    {
        return new AnalysisDetailResponse(
            result.Analysis,
            result.ProposedResources,
            result.CostChanges,
            result.PolicyFindings,
            result.Recommendations,
            result.AuditEvents,
            result.CommentMarkdown,
            result.CheckConclusion,
            result.ConfigErrors,
            [],
            [],
            null,
            result.Analysis.GitHubPullRequestUrl);
    }

    public static AnalysisDetailResponse FromScan(PullRequestScan scan)
    {
        var owner = scan.Repository?.Owner ?? "";
        var name = scan.Repository?.Name ?? "";
        var analysis = new PullRequestAnalysis
        {
            Id = scan.Id,
            RepositoryOwner = owner,
            RepositoryName = name,
            PullRequestNumber = scan.PullRequestNumber,
            BaseBranch = scan.TargetBranch,
            HeadBranch = scan.SourceBranch,
            CommitSha = "",
            Status = AnalysisListItem.ToCoreStatus(scan.Status),
            PolicyStatus = AnalysisListItem.ToCorePolicy(scan.Decision),
            Environment = scan.Environment,
            OverallConfidence = AnalysisListItem.ToCoreConfidence(scan.ConfidenceLevel),
            BaselineMonthlyCost = scan.EstimatedMonthlyDelta is null ? null : 0,
            ProposedMonthlyCost = scan.EstimatedMonthlyDelta is null ? null : Math.Max(0, scan.EstimatedMonthlyDelta.Value),
            MonthlyDelta = scan.EstimatedMonthlyDelta,
            Currency = scan.Currency,
            StartedAt = scan.StartedAt,
            CompletedAt = scan.CompletedAt,
            ErrorMessage = scan.FailureReason,
            GitHubPullRequestUrl = scan.GitHubPullRequestUrl,
            DashboardUrl = scan.DashboardReportUrl
        };

        var resources = scan.DetectedResources.Select(resource => new ResourceEstimate
        {
            AnalysisId = scan.Id,
            SourceFile = resource.SourceFile,
            Provider = resource.Provider,
            ResourceType = resource.ResourceType,
            ResourceName = resource.ResourceName,
            Sku = resource.Sku,
            Region = resource.Region,
            Currency = scan.Currency,
            Status = EstimateStatus.Estimated,
            Confidence = AnalysisListItem.ToCoreConfidence(scan.ConfidenceLevel),
            AssumptionsJson = resource.RawJson
        }).ToArray();

        var changes = scan.CostBreakdownItems.Select(item => new ResourceCostChange
        {
            ResourceKey = item.Id.ToString(),
            ResourceName = item.ResourceName,
            ResourceType = item.ResourceType,
            ChangeKind = item.ChangeType.ToString().ToLowerInvariant(),
            MonthlyDelta = item.EstimatedMonthlyCost ?? 0
        }).ToArray();

        var policyFindings = scan.PolicyEvaluations
            .Where(evaluation => evaluation.Result != PolicyRuleResult.Pass)
            .Select(evaluation => new PolicyFinding
            {
                AnalysisId = scan.Id,
                RuleId = evaluation.RuleName,
                Action = evaluation.Result == PolicyRuleResult.Warn ? PolicyAction.Warn : PolicyAction.Block,
                Message = evaluation.Message
            })
            .ToArray();

        return new AnalysisDetailResponse(
            analysis,
            resources,
            changes,
            policyFindings,
            BuildPersistedRecommendations(scan),
            [],
            "",
            scan.Decision == PolicyDecision.Fail ? "failure" : scan.Decision == PolicyDecision.Warn ? "neutral" : "success",
            [],
            scan.ScanAssumptions.Select(ScanAssumptionItem.FromEntity).ToArray(),
            scan.PolicyEvaluations.Select(PolicyEvaluationItem.FromEntity).ToArray(),
            scan.GitHubCommentId,
            scan.GitHubPullRequestUrl);
    }

    private static IReadOnlyList<Recommendation> BuildPersistedRecommendations(PullRequestScan scan)
    {
        if (!string.IsNullOrWhiteSpace(scan.FailureReason))
        {
            return
            [
                Recommendation(scan, "high", "Fix failed scan", $"The scan failed before policy evaluation: {scan.FailureReason}", null)
            ];
        }

        var recommendations = new List<Recommendation>();
        var hasAiWorkflow = scan.DetectedResources.Any(resource =>
            resource.ResourceType.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase)
            || resource.Sku?.Contains("gpt-", StringComparison.OrdinalIgnoreCase) == true);
        if (hasAiWorkflow)
        {
            recommendations.Add(Recommendation(
                scan,
                "high",
                "Reduce AI workflow unit cost",
                "Use a cheaper model such as gpt-4.1-mini, reduce average token usage, or lower monthly runs before this workflow grows further.",
                scan.EstimatedMonthlyDelta is null ? null : decimal.Round(scan.EstimatedMonthlyDelta.Value * 0.25m, 2)));
        }

        var hasExpensiveCloudSku = scan.DetectedResources.Any(resource =>
            resource.Sku?.Contains("Premium", StringComparison.OrdinalIgnoreCase) == true
            || resource.Sku?.Contains("P1v3", StringComparison.OrdinalIgnoreCase) == true
            || resource.Sku?.Contains("Standard_C_", StringComparison.OrdinalIgnoreCase) == true)
            || scan.CostBreakdownItems.Any(item => item.EstimatedMonthlyCost is > 100 && item.ResourceType.Contains("azurerm_", StringComparison.OrdinalIgnoreCase));
        if (hasExpensiveCloudSku)
        {
            recommendations.Add(Recommendation(
                scan,
                "high",
                "Use a cheaper non-production SKU",
                "Switch dev resources to Basic, B-series, or an environment-specific SKU and keep premium capacity for production.",
                scan.EstimatedMonthlyDelta is null ? null : decimal.Round(scan.EstimatedMonthlyDelta.Value * 0.4m, 2)));
        }

        if (scan.Decision == PolicyDecision.Pass && recommendations.Count == 0)
        {
            recommendations.Add(Recommendation(
                scan,
                "low",
                "No blocking action needed",
                "The estimated monthly delta is small and no blocking policies were triggered.",
                null));
        }
        else if (scan.Decision is PolicyDecision.Warn or PolicyDecision.Fail && recommendations.Count == 0)
        {
            recommendations.Add(Recommendation(
                scan,
                "medium",
                "Review the budget finding",
                "Reduce the changed resources or document why the extra monthly spend is intentional before merge.",
                null));
        }

        return recommendations;
    }

    private static Recommendation Recommendation(PullRequestScan scan, string severity, string title, string description, decimal? savings) => new()
    {
        AnalysisId = scan.Id,
        Severity = severity,
        Title = title,
        Description = description,
        EstimatedMonthlySavings = savings
    };
}

public sealed record PolicyResponse(string Yaml, SpendGovConfigParseResult Parsed);

public sealed record ScanAssumptionItem(string Name, string Value)
{
    public static ScanAssumptionItem FromEntity(ScanAssumption assumption) => new(assumption.Name, assumption.Value);
}

public sealed record PolicyEvaluationItem(string RuleName, PolicyRuleResult Result, string Message)
{
    public static PolicyEvaluationItem FromEntity(PolicyEvaluation evaluation) => new(evaluation.RuleName, evaluation.Result, evaluation.Message);
}
