using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;
using SpendGovernor.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.Configure<AzureRetailPricesOptions>(builder.Configuration.GetSection("Pricing:AzureRetailPrices"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(_ => JsonPricingCatalogService.LoadDefault(validate: true));
builder.Services.AddSingleton<IAzureRetailPricesClient>(services => new AzureRetailPricesClient(
    services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(AzureRetailPricesClient)),
    services.GetRequiredService<IOptions<AzureRetailPricesOptions>>()));
builder.Services.AddSingleton<IAzureLivePricingProvider, AzureLivePricingProvider>();
builder.Services.AddSingleton<IPricingCatalogService, HybridPricingCatalogService>();
builder.Services.AddSingleton<MonthlyCostEstimator>();
builder.Services.AddSingleton<AnalysisEngine>();
builder.Services.AddScoped<SpendGovernorStore>();
builder.Services.Configure<GitHubIntegrationOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddSingleton<IGitHubApiClient, GitHubApiClient>();
builder.Services.AddScoped<IGitHubPullRequestReporter>(services => GitHubReporterFactory.Create(services));
builder.Services.AddDbContext<SpendGovernorDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SpendGovernorDb")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=Spend-Governor;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"));
builder.Services.AddScoped<IRepositoryStore, RepositoryStore>();
builder.Services.AddScoped<IScanResultWriter, ScanResultWriter>();
builder.Services.AddScoped<IScanStore, ScanStore>();
builder.Services.AddScoped<ScanExecutionService>();
builder.Services.AddSingleton<IScanJobQueue, ChannelScanJobQueue>();
builder.Services.AddHostedService<QueuedScanWorker>();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API process is running."))
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

app.Use(async (context, next) =>
{
    var correlationId = ResolveCorrelationId(context);
    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-ID"] = correlationId;

    using (app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["CorrelationId"] = correlationId
    }))
    {
        await next();
    }
});

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
    {
        app.Logger.LogError(ex, "Unhandled request failure for {Method} {Path}.", context.Request.Method, context.Request.Path);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await Results.Problem(
                title: "Unexpected server error",
                detail: "The request failed. Use the correlation id when checking logs.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = ResolveCorrelationId(context)
                }).ExecuteAsync(context);
        }
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/health");
app.MapGet("/dashboard", (IWebHostEnvironment environment) =>
    Results.Content(File.ReadAllText(Path.Combine(environment.WebRootPath, "dashboard.html")), "text/html", Encoding.UTF8)).WithOrder(-10);
app.MapGet("/app", (IWebHostEnvironment environment) =>
    Results.Content(File.ReadAllText(Path.Combine(environment.WebRootPath, "dashboard.html")), "text/html", Encoding.UTF8)).WithOrder(-10);
app.MapGet("/workspaces", (IWebHostEnvironment environment) =>
    Results.Content(File.ReadAllText(Path.Combine(environment.WebRootPath, "dashboard.html")), "text/html", Encoding.UTF8)).WithOrder(-10);

app.MapPost("/api/auth/register", (AuthRegisterRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var result = store.RegisterUser(request.Email, request.Password, request.DisplayName);
    if (result.User is null)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    SetAuthCookie(context, result.User.Id);
    return Results.Ok(new AuthResponse(result.User, store.GetWorkspaces(result.User.Id)));
});

app.MapPost("/api/auth/login", (AuthLoginRequest request, HttpContext context, SpendGovernorStore store) =>
{
    var result = store.LoginUser(request.Email, request.Password);
    if (result.User is null)
    {
        return Results.BadRequest(new { error = result.Error });
    }

    SetAuthCookie(context, result.User.Id);
    return Results.Ok(new AuthResponse(result.User, store.GetWorkspaces(result.User.Id)));
});

app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    context.Response.Cookies.Delete(LocalAuth.CookieName);
    return Results.Ok(new { loggedOut = true });
});

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
    await repositoryStore.FindOrCreateAsync(project.Id, "github", project.RepositoryOwner, project.RepositoryName, "main", null, project.GitHubInstallationId, context.RequestAborted);
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

    var repository = await repositoryStore.FindByProjectIdAsync(project.Id, context.RequestAborted);
    var metrics = repository is null
        ? ProjectMetrics.Empty
        : ProjectMetrics.FromScans(await scanStore.GetLatestScansForRepositoryAsync(repository.Id, 5, context.RequestAborted), project.Currency);
    return Results.Ok(new ProjectDetailResponse(project, metrics, store.GetRepositories(project.Id)));
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
    return Results.Ok(store.GetProject(projectId));
});

app.MapGet("/api/projects/{projectId:guid}/analyses", async (Guid projectId, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    if (project is null)
    {
        return Results.NotFound();
    }

    var repository = await repositoryStore.FindByProjectIdAsync(project.Id, context.RequestAborted);
    if (repository is null)
    {
        return Results.Ok(Array.Empty<AnalysisListItem>());
    }

    var scans = await scanStore.GetLatestScansForRepositoryAsync(repository.Id, 50, context.RequestAborted);
    return Results.Ok(scans.Select(AnalysisListItem.FromScan));
});

app.MapPost("/api/projects/{projectId:guid}/analyses", async (Guid projectId, RunAnalysisRequest request, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore, ScanExecutionService scanExecution) =>
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
    await scanExecution.ExecuteAsync(project, repository, scan.Id, analysisRequest, context.RequestAborted);
    var persisted = await scanStore.GetScanDetailsAsync(scan.Id, context.RequestAborted);
    return Results.Created($"/api/analyses/{scan.Id}", AnalysisDetailResponse.FromScan(persisted!));
});

app.MapPost("/api/demo/projects/{projectId:guid}/analyze", async (Guid projectId, DemoRunRequest request, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore, ScanExecutionService scanExecution) =>
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
    await scanExecution.ExecuteAsync(project, repository, scan.Id, analysisRequest, context.RequestAborted);
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
        IRepositoryStore repositoryStore,
        IScanStore scanStore,
        ScanExecutionService scanExecution,
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
            await scanExecution.ExecuteAsync(project, repository, scan.Id, request, context.RequestAborted);
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
    if (scan?.Repository is null || store.GetProjectForUser(scan.Repository.ProjectId, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(AnalysisDetailResponse.FromScan(scan));
});

app.MapPost("/api/analyses/{analysisId:guid}/rerun", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IRepositoryStore repositoryStore, IScanStore scanStore, ScanExecutionService scanExecution) =>
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

    var rerunRequest = request with { CommitSha = request.CommitSha + "-rerun" };
    var repository = await EnsureRepositoryAsync(project, repositoryStore, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repository, rerunRequest, context.RequestAborted);
    await scanExecution.ExecuteAsync(project, repository, scan.Id, rerunRequest, context.RequestAborted);
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
    if (scan?.Repository is null || store.GetProjectForUser(scan.Repository.ProjectId, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Text(ResourcesCsv(scan), "text/csv");
});

app.MapGet("/api/analyses/{analysisId:guid}/export/policy-findings.csv", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForUser(scan.Repository.ProjectId, user.Id) is null)
    {
        return Results.NotFound();
    }

    return Results.Text(PolicyEvaluationsCsv(scan), "text/csv");
});

app.MapGet("/api/analyses/{analysisId:guid}/export/recommendations.csv", async (Guid analysisId, HttpContext context, SpendGovernorStore store, IScanStore scanStore) =>
{
    var user = CurrentUser(context, store);
    var scan = await scanStore.GetScanDetailsAsync(analysisId, context.RequestAborted);
    if (scan?.Repository is null || store.GetProjectForUser(scan.Repository.ProjectId, user.Id) is null)
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

    var repository = await repositoryStore.FindByProjectIdAsync(project.Id, context.RequestAborted);
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

    return Results.Ok(store.UpdateProjectPolicy(projectId, request.Yaml));
});

app.MapGet("/api/projects/{projectId:guid}/budgets", (Guid projectId, HttpContext context, SpendGovernorStore store) =>
{
    var user = CurrentUser(context, store);
    var project = store.GetProjectForUser(projectId, user.Id);
    return project is null
        ? Results.NotFound()
        : Results.Ok(store.GetBudgets(projectId));
});

app.MapPut("/api/projects/{projectId:guid}/budgets/{environment}", (Guid projectId, string environment, EnvironmentBudgetUpdateRequest request, HttpContext context, SpendGovernorStore store) =>
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

    var saved = store.UpsertBudget(projectId, request with { Environment = environment });
    return Results.Ok(saved);
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
    store.UpdateGitHubInstallation(project.Id, installationId);
    store.AddAudit(project.WorkspaceId, project.Id, null, "GitHub App installed", $"Installation {installationId} linked to {project.RepositoryOwner}/{project.RepositoryName}.");
    return Results.Ok(new { project.Id, project.GitHubInstallationId });
});

app.MapPost("/api/github/webhooks", async (
    HttpContext context,
    SpendGovernorStore store,
    IRepositoryStore repositoryStore,
    IScanStore scanStore,
    IScanJobQueue scanQueue,
    IOptions<GitHubIntegrationOptions> gitHubOptions,
    IWebHostEnvironment environment) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var payload = await reader.ReadToEndAsync();
    var signature = context.Request.Headers["X-Hub-Signature-256"].ToString();
    var options = gitHubOptions.Value;
    var isUnsignedDevelopmentWebhook = environment.IsDevelopment()
        && options.AllowUnsignedWebhooksInDevelopment
        && string.IsNullOrWhiteSpace(signature);
    if (!isUnsignedDevelopmentWebhook && string.IsNullOrWhiteSpace(options.WebhookSecret))
    {
        return Results.Problem(
            title: "GitHub webhook secret is not configured",
            detail: "Set GitHub__WebhookSecret before accepting signed webhook deliveries.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    if (!isUnsignedDevelopmentWebhook && !GitHubSignatureVerifier.VerifySha256(payload, signature, options.WebhookSecret))
    {
        return Results.Unauthorized();
    }

    JsonDocument document;
    try
    {
        document = JsonDocument.Parse(payload);
    }
    catch (JsonException)
    {
        return Results.BadRequest(new { error = "Invalid GitHub webhook JSON payload." });
    }

    using (document)
    {
    var root = document.RootElement;
    var action = root.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : "";
    if (action is not ("opened" or "synchronize" or "reopened" or "ready_for_review"))
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
    var repositoryRecord = await repositoryStore.FindOrCreateAsync(project.Id, "github", owner, repoName, request.BaseBranch, repository.TryGetProperty("id", out var repoId) ? repoId.GetRawText().Trim('"') : null, installationId, context.RequestAborted);
    var scan = await scanStore.CreateScanAsync(repositoryRecord, request, context.RequestAborted);
    await scanQueue.QueueAsync(new QueuedScanJob(scan.Id, project.Id, repositoryRecord.Id, request, ResolveCorrelationId(context)), context.RequestAborted);
    store.AddAudit(project.WorkspaceId, project.Id, scan.Id, "GitHub webhook scan queued", $"Queued PR #{request.PullRequestNumber} scan for {repositoryRecord.FullName}.");
    return Results.Accepted(value: new
    {
        analysisId = scan.Id,
        status = "Queued",
        checkConclusion = "pending",
        mode = options.Mode.ToString(),
        reportPublishingStatus = scan.ReportPublishingStatus,
        simulatedGitHubComment = options.Mode == GitHubIntegrationMode.Simulated,
        reportUrl = $"{request.DashboardBaseUrl?.TrimEnd('/')}/?analysisId={scan.Id}"
    });
    }
});

app.MapFallbackToFile("index.html");

app.Run();

static User CurrentUser(HttpContext context, SpendGovernorStore store)
{
    if (context.Request.Cookies.TryGetValue(LocalAuth.CookieName, out var userIdRaw)
        && Guid.TryParse(userIdRaw, out var userId)
        && store.GetUserById(userId) is { } cookieUser)
    {
        return cookieUser;
    }

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

static string ResolveCorrelationId(HttpContext context)
{
    if (context.Items.TryGetValue("CorrelationId", out var existing) && existing is string existingId && !string.IsNullOrWhiteSpace(existingId))
    {
        return existingId;
    }

    var requested = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(requested))
    {
        return requested.Length <= 128 ? requested : requested[..128];
    }

    return Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture);
}

static void SetAuthCookie(HttpContext context, Guid userId)
{
    context.Response.Cookies.Append(
        LocalAuth.CookieName,
        userId.ToString("D"),
        new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });
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
        project.Id,
        "github",
        project.RepositoryOwner,
        project.RepositoryName,
        "main",
        null,
        project.GitHubInstallationId,
        cancellationToken);
}

static string ResourcesCsv(PullRequestScan scan)
{
    var builder = new StringBuilder();
    builder.AppendLine("scanId,resourceName,resourceType,armResourceType,mappedResourceType,armApiVersion,armKind,sourceFile,provider,region,sku,terraformAddress,terraformActions,rawJson,createdAt");
    foreach (var resource in scan.DetectedResources)
    {
        var metadata = ResourceRawMetadata.Parse(resource.RawJson);
        builder.AppendLine(string.Join(',', new[]
        {
            Csv(scan.Id.ToString()),
            Csv(resource.ResourceName),
            Csv(resource.ResourceType),
            Csv(metadata.ArmResourceType),
            Csv(metadata.MappedResourceType),
            Csv(metadata.ArmApiVersion),
            Csv(metadata.ArmKind),
            Csv(resource.SourceFile),
            Csv(resource.Provider),
            Csv(resource.Region),
            Csv(resource.Sku),
            Csv(resource.TerraformAddress),
            Csv(resource.TerraformActions),
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

public sealed record AuthRegisterRequest(string Email, string Password, string? DisplayName);

public sealed record AuthLoginRequest(string Email, string Password);

public sealed record AuthResponse(User User, IReadOnlyList<Workspace> Workspaces);

public sealed record CreateProjectRequest(
    Guid WorkspaceId,
    string Name,
    string RepositoryOwner,
    string RepositoryName,
    string? DefaultRegion,
    string? Currency,
    int? HoursPerMonth);

public sealed record PatchProjectSettingsRequest(string? DefaultRegion, string? Currency, int? HoursPerMonth, string? PolicyYaml);

public sealed record EnvironmentBudgetItem(
    Guid Id,
    Guid ProjectId,
    string Environment,
    decimal? MaxMonthlyCost,
    decimal? MaxMonthlyDelta,
    string Currency,
    decimal? RequireApprovalAbove,
    bool BlockOnBudgetExceeded,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record EnvironmentBudgetUpdateRequest(
    string? Environment,
    decimal? MaxMonthlyCost,
    decimal? MaxMonthlyDelta,
    string? Currency,
    decimal? RequireApprovalAbove,
    bool? BlockOnBudgetExceeded);

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

public sealed record ProjectDetailResponse(Project Project, ProjectMetrics Metrics, IReadOnlyList<RepositoryListItem> Repositories);

public sealed record RepositoryListItem(
    Guid Id,
    Guid ProjectId,
    string Provider,
    string Owner,
    string Name,
    string FullName,
    string DefaultBranch,
    string? ExternalRepositoryId,
    string? InstallationId,
    DateTimeOffset? LastScanAt);

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
    IReadOnlyList<SpendFinding> Findings,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<AuditEvent> AuditEvents,
    string CommentMarkdown,
    string CheckConclusion,
    string AnalysisSource,
    IReadOnlyList<string> ConfigErrors,
    IReadOnlyList<ScanAssumptionItem> Assumptions,
    IReadOnlyList<PolicyEvaluationItem> PolicyEvaluations,
    string? GitHubCommentId,
    string? GitHubCheckRunId,
    string? GitHubReportUrl,
    string? ReportPublishingStatus,
    string? ReportPublishingError,
    string? GitHubPullRequestUrl)
{
    public static AnalysisDetailResponse FromResult(AnalysisResult result)
    {
        return new AnalysisDetailResponse(
            result.Analysis,
            result.ProposedResources,
            result.CostChanges,
            result.PolicyFindings,
            result.Findings,
            result.Recommendations,
            result.AuditEvents,
            result.CommentMarkdown,
            result.CheckConclusion,
            DetectAnalysisSource(result.ProposedResources),
            result.ConfigErrors,
            [],
            [],
            null,
            null,
            null,
            null,
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
            CreatedAt = scan.CreatedAt,
            StartedAt = scan.StartedAt,
            CompletedAt = scan.CompletedAt,
            ErrorMessage = scan.FailureReason,
            GitHubPullRequestUrl = scan.GitHubPullRequestUrl,
            DashboardUrl = scan.DashboardReportUrl
        };

        var resources = scan.DetectedResources.Select(resource =>
        {
            var metadata = ResourceRawMetadata.Parse(resource.RawJson);
            return new ResourceEstimate
            {
                AnalysisId = scan.Id,
                SourceFile = resource.SourceFile,
                Provider = resource.Provider,
                ResourceType = resource.ResourceType,
                ResourceName = resource.ResourceName,
                Sku = resource.Sku,
                Region = resource.Region,
                SourceType = metadata.SourceType ?? ResourceSourceType.Terraform,
                AnalysisSource = metadata.AnalysisSource,
                ArmResourceType = metadata.ArmResourceType,
                ArmApiVersion = metadata.ArmApiVersion,
                ArmKind = metadata.ArmKind,
                MappedResourceType = metadata.MappedResourceType,
                TerraformAddress = resource.TerraformAddress ?? metadata.TerraformAddress,
                TerraformActions = resource.TerraformActions ?? metadata.TerraformActions,
                TerraformChangeType = metadata.TerraformChangeType,
                BeforeSummary = metadata.BeforeSummary,
                AfterSummary = metadata.AfterSummary,
                PricingCatalogName = metadata.PricingCatalogName,
                PricingCatalogVersion = metadata.PricingCatalogVersion,
                PricingSource = metadata.PricingSource,
                PricingSourceType = metadata.PricingSourceType,
                PricingMatchType = metadata.PricingMatchType,
                PricingFallbackReason = metadata.PricingFallbackReason,
                PricingUnit = metadata.PricingUnit,
                PricingUnitPrice = metadata.PricingUnitPrice,
                PricingMatchedKey = metadata.PricingMatchedKey,
                PricingConfidenceImpact = metadata.PricingConfidenceImpact,
                PricingLiveApiUsed = metadata.PricingLiveApiUsed,
                PricingFallbackUsed = metadata.PricingFallbackUsed,
                PricingRegionDefaulted = metadata.PricingRegionDefaulted,
                PricingAmbiguousMatch = metadata.PricingAmbiguousMatch,
                PricingMonthlyHours = metadata.PricingMonthlyHours,
                PricingUnitOfMeasure = metadata.PricingUnitOfMeasure,
                PricingMeterId = metadata.PricingMeterId,
                PricingMeterName = metadata.PricingMeterName,
                PricingProductName = metadata.PricingProductName,
                PricingSkuName = metadata.PricingSkuName,
                PricingArmSkuName = metadata.PricingArmSkuName,
                PricingServiceName = metadata.PricingServiceName,
                PricingServiceFamily = metadata.PricingServiceFamily,
                PricingPriceType = metadata.PricingPriceType,
                PricingEffectiveStartDate = metadata.PricingEffectiveStartDate,
                Environment = scan.Environment,
                Category = metadata.Category ?? CostCategory.Unknown,
                Currency = scan.Currency,
                Status = metadata.Status ?? EstimateStatus.Estimated,
                Confidence = metadata.Confidence ?? AnalysisListItem.ToCoreConfidence(scan.ConfidenceLevel),
                Quantity = metadata.Quantity ?? 1,
                HoursPerMonth = metadata.HoursPerMonth ?? 730,
                MonthlyCost = metadata.MonthlyCost,
                MonthlyDelta = metadata.MonthlyDelta,
                AssumptionsJson = resource.RawJson
            };
        }).ToArray();

        var changes = scan.CostBreakdownItems.Select(item => new ResourceCostChange
        {
            ResourceKey = item.Id.ToString(),
            ResourceName = item.ResourceName,
            ResourceType = item.ResourceType,
            ChangeKind = item.ChangeType.ToString().ToLowerInvariant(),
            BeforeSku = item.BeforeSummary,
            AfterSku = item.AfterSummary,
            BeforeSummary = item.BeforeSummary,
            AfterSummary = item.AfterSummary,
            TerraformAddress = item.TerraformAddress,
            TerraformActions = item.TerraformActions,
            Reason = item.Reason,
            PricingCatalogVersion = item.PricingCatalogVersion,
            PricingSource = item.PricingSource,
            PricingMatchType = item.PricingMatchType,
            PricingFallbackReason = item.PricingFallbackReason,
            MonthlyDelta = item.EstimatedMonthlyCost ?? 0
        }).ToArray();

        var policyEvaluationItems = scan.PolicyEvaluations.Select(PolicyEvaluationItem.FromEntity).ToArray();
        var policyAsCodeEvaluations = policyEvaluationItems
            .Where(item => item.IsPolicyAsCode)
            .Select(item => new SpendPolicyEvaluation
            {
                PolicyId = item.PolicyId ?? item.RuleName,
                Title = item.Title ?? "",
                Severity = ParseSpendPolicySeverity(item.Severity),
                Matched = item.Matched,
                Result = ParseSpendPolicyResult(item.PolicyResult),
                MatchedResource = item.MatchedResource,
                Message = item.Message,
                Recommendation = item.Recommendation ?? ""
            })
            .ToArray();
        var policyFindings = policyEvaluationItems
            .Where(evaluation => evaluation.Result != PolicyRuleResult.Pass)
            .Select(evaluation => new PolicyFinding
            {
                AnalysisId = scan.Id,
                RuleId = evaluation.PolicyId ?? evaluation.RuleName,
                Action = evaluation.Result == PolicyRuleResult.Warn ? PolicyAction.Warn : PolicyAction.Block,
                Message = evaluation.Message
            })
            .ToArray();
        var recommendations = BuildPersistedRecommendations(scan);
        var checkConclusion = scan.Decision == PolicyDecision.Fail ? "failure" : scan.Decision == PolicyDecision.Warn ? "neutral" : "success";
        var persistedResult = new AnalysisResult
        {
            Analysis = analysis,
            ProposedResources = resources,
            CostChanges = changes,
            PolicyFindings = policyFindings,
            PolicyAsCodeEvaluations = policyAsCodeEvaluations,
            Recommendations = recommendations,
            CheckConclusion = checkConclusion,
            DashboardUrl = scan.DashboardReportUrl
        };
        persistedResult.Findings = new SpendFindingGenerator().Generate(persistedResult);
        var commentMarkdown = PrCommentRenderer.Render(persistedResult);

        return new AnalysisDetailResponse(
            analysis,
            resources,
            changes,
            policyFindings,
            persistedResult.Findings,
            recommendations,
            [],
            commentMarkdown,
            checkConclusion,
            DetectAnalysisSource(resources),
            [],
            scan.ScanAssumptions.Select(ScanAssumptionItem.FromEntity).ToArray(),
            policyEvaluationItems,
            scan.GitHubCommentId,
            scan.GitHubCheckRunId,
            scan.GitHubReportUrl,
            scan.ReportPublishingStatus,
            scan.ReportPublishingError,
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

    private static string DetectAnalysisSource(IReadOnlyList<ResourceEstimate> resources)
    {
        if (resources.Any(resource => resource.AnalysisSource == TerraformPlanJsonParser.AnalysisSource))
        {
            return TerraformPlanJsonParser.AnalysisSource;
        }

        if (resources.Any(resource => resource.AnalysisSource == ArmTemplateJsonParser.AnalysisSource))
        {
            return ArmTemplateJsonParser.AnalysisSource;
        }

        if (resources.Any(resource => resource.SourceType == ResourceSourceType.Terraform))
        {
            return "Terraform .tf parser fallback";
        }

        if (resources.Any(resource => resource.SourceType == ResourceSourceType.Bicep))
        {
            return "Raw Bicep parser fallback";
        }

        if (resources.All(resource => resource.SourceType == ResourceSourceType.AiConfig))
        {
            return "AI spend config";
        }

        return resources.Count == 0 ? "No cost-relevant files" : "Mixed analyzers";
    }

    private static SpendPolicySeverity ParseSpendPolicySeverity(string? severity)
    {
        return Enum.TryParse<SpendPolicySeverity>(severity, ignoreCase: true, out var parsed)
            ? parsed
            : SpendPolicySeverity.Info;
    }

    private static SpendPolicyEvaluationStatus ParseSpendPolicyResult(string? result)
    {
        return Enum.TryParse<SpendPolicyEvaluationStatus>(result, ignoreCase: true, out var parsed)
            ? parsed
            : SpendPolicyEvaluationStatus.NotMatched;
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

public sealed record PolicyEvaluationItem(
    string RuleName,
    PolicyRuleResult Result,
    string Message,
    bool IsPolicyAsCode,
    string? PolicyId,
    string? Title,
    string? Severity,
    bool Matched,
    string? PolicyResult,
    string? MatchedResource,
    string? Recommendation)
{
    public static PolicyEvaluationItem FromEntity(PolicyEvaluation evaluation)
    {
        if (!evaluation.RuleName.StartsWith("policy-as-code:", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyEvaluationItem(evaluation.RuleName, evaluation.Result, evaluation.Message, false, null, null, null, false, null, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(evaluation.Message);
            var root = document.RootElement;
            var policyId = GetString(root, "policyId") ?? evaluation.RuleName["policy-as-code:".Length..];
            var message = GetString(root, "message") ?? "";
            return new PolicyEvaluationItem(
                evaluation.RuleName,
                evaluation.Result,
                message,
                true,
                policyId,
                GetString(root, "title"),
                GetString(root, "severity"),
                GetBool(root, "matched"),
                GetString(root, "result"),
                GetString(root, "matchedResource"),
                GetString(root, "recommendation"));
        }
        catch (JsonException)
        {
            return new PolicyEvaluationItem(evaluation.RuleName, evaluation.Result, evaluation.Message, true, evaluation.RuleName["policy-as-code:".Length..], null, null, evaluation.Result != PolicyRuleResult.Pass, null, null, null);
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.GetRawText()
                : null;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}

public sealed record ResourceRawMetadata(
    ResourceSourceType? SourceType,
    string? AnalysisSource,
    string? ArmResourceType,
    string? ArmApiVersion,
    string? ArmKind,
    string? MappedResourceType,
    string? TerraformAddress,
    string? TerraformActions,
    string? TerraformChangeType,
    string? BeforeSummary,
    string? AfterSummary,
    string? PricingCatalogName,
    string? PricingCatalogVersion,
    string? PricingSource,
    string? PricingSourceType,
    string? PricingMatchType,
    string? PricingFallbackReason,
    string? PricingUnit,
    decimal? PricingUnitPrice,
    string? PricingMatchedKey,
    string? PricingConfidenceImpact,
    bool PricingLiveApiUsed,
    bool PricingFallbackUsed,
    bool PricingRegionDefaulted,
    bool PricingAmbiguousMatch,
    int? PricingMonthlyHours,
    string? PricingUnitOfMeasure,
    string? PricingMeterId,
    string? PricingMeterName,
    string? PricingProductName,
    string? PricingSkuName,
    string? PricingArmSkuName,
    string? PricingServiceName,
    string? PricingServiceFamily,
    string? PricingPriceType,
    DateTimeOffset? PricingEffectiveStartDate,
    CostCategory? Category,
    int? Quantity,
    int? HoursPerMonth,
    decimal? MonthlyCost,
    decimal? MonthlyDelta,
    ConfidenceLevel? Confidence,
    EstimateStatus? Status)
{
    public static ResourceRawMetadata Parse(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            return new ResourceRawMetadata(
                GetEnum<ResourceSourceType>(root, "SourceType", "sourceType"),
                GetString(root, "AnalysisSource", "analysisSource"),
                GetString(root, "ArmResourceType", "armResourceType"),
                GetString(root, "ArmApiVersion", "armApiVersion"),
                GetString(root, "ArmKind", "armKind"),
                GetString(root, "MappedResourceType", "mappedResourceType"),
                GetString(root, "TerraformAddress", "terraformAddress"),
                GetString(root, "TerraformActions", "terraformActions"),
                GetString(root, "TerraformChangeType", "terraformChangeType"),
                GetString(root, "BeforeSummary", "beforeSummary"),
                GetString(root, "AfterSummary", "afterSummary"),
                GetString(root, "PricingCatalogName", "pricingCatalogName"),
                GetString(root, "PricingCatalogVersion", "pricingCatalogVersion"),
                GetString(root, "PricingSource", "pricingSource"),
                GetString(root, "PricingSourceType", "pricingSourceType"),
                GetString(root, "PricingMatchType", "pricingMatchType"),
                GetString(root, "PricingFallbackReason", "pricingFallbackReason"),
                GetString(root, "PricingUnit", "pricingUnit"),
                GetDecimal(root, "PricingUnitPrice", "pricingUnitPrice"),
                GetString(root, "PricingMatchedKey", "pricingMatchedKey"),
                GetString(root, "PricingConfidenceImpact", "pricingConfidenceImpact"),
                GetBool(root, "PricingLiveApiUsed", "pricingLiveApiUsed"),
                GetBool(root, "PricingFallbackUsed", "pricingFallbackUsed"),
                GetBool(root, "PricingRegionDefaulted", "pricingRegionDefaulted"),
                GetBool(root, "PricingAmbiguousMatch", "pricingAmbiguousMatch"),
                GetInt(root, "PricingMonthlyHours", "pricingMonthlyHours"),
                GetString(root, "PricingUnitOfMeasure", "pricingUnitOfMeasure"),
                GetString(root, "PricingMeterId", "pricingMeterId"),
                GetString(root, "PricingMeterName", "pricingMeterName"),
                GetString(root, "PricingProductName", "pricingProductName"),
                GetString(root, "PricingSkuName", "pricingSkuName"),
                GetString(root, "PricingArmSkuName", "pricingArmSkuName"),
                GetString(root, "PricingServiceName", "pricingServiceName"),
                GetString(root, "PricingServiceFamily", "pricingServiceFamily"),
                GetString(root, "PricingPriceType", "pricingPriceType"),
                GetDateTimeOffset(root, "PricingEffectiveStartDate", "pricingEffectiveStartDate"),
                GetEnum<CostCategory>(root, "Category", "category"),
                GetInt(root, "Quantity", "quantity"),
                GetInt(root, "HoursPerMonth", "hoursPerMonth"),
                GetDecimal(root, "MonthlyCost", "monthlyCost"),
                GetDecimal(root, "MonthlyDelta", "monthlyDelta"),
                GetEnum<ConfidenceLevel>(root, "Confidence", "confidence"),
                GetEnum<EstimateStatus>(root, "Status", "status"));
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    private static ResourceRawMetadata Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, false, false, false, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        return null;
    }

    private static decimal? GetDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsedNumber))
            {
                return parsedNumber;
            }

            var text = GetString(root, name);
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
            {
                return parsedNumber;
            }

            var text = GetString(root, name);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool GetBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            var text = GetString(root, name);
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement root, params string[] names)
    {
        var text = GetString(root, names);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static TEnum? GetEnum<TEnum>(JsonElement root, params string[] names)
        where TEnum : struct
    {
        var text = GetString(root, names);
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) ? parsed : null;
    }
}

public static class LocalAuth
{
    public const string CookieName = "spendgov_user_id";
}
