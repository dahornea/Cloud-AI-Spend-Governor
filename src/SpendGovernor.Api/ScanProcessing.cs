using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;
using SpendGovernor.Infrastructure.Services;
using PersistenceRepository = SpendGovernor.Infrastructure.Persistence.Repository;

public sealed record QueuedScanJob(
    Guid ScanId,
    Guid ProjectId,
    Guid RepositoryId,
    AnalysisRequest Request,
    string? CorrelationId);

public interface IScanJobQueue
{
    ValueTask QueueAsync(QueuedScanJob job, CancellationToken cancellationToken = default);

    ValueTask<QueuedScanJob> DequeueAsync(CancellationToken cancellationToken = default);
}

public sealed class ChannelScanJobQueue : IScanJobQueue
{
    private readonly Channel<QueuedScanJob> channel = Channel.CreateUnbounded<QueuedScanJob>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask QueueAsync(QueuedScanJob job, CancellationToken cancellationToken = default)
    {
        if (job.ScanId == Guid.Empty)
        {
            throw new ArgumentException("Queued scan jobs must include a scan id.", nameof(job));
        }

        return channel.Writer.WriteAsync(job, cancellationToken);
    }

    public ValueTask<QueuedScanJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return channel.Reader.ReadAsync(cancellationToken);
    }
}

public sealed class QueuedScanWorker : BackgroundService
{
    private readonly IScanJobQueue queue;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<QueuedScanWorker> logger;

    public QueuedScanWorker(
        IScanJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<QueuedScanWorker> logger)
    {
        this.queue = queue;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Queued scan worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            QueuedScanJob job;
            try
            {
                job = await queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            using var scope = BeginScanScope(logger, job.CorrelationId, job.ScanId, job.ProjectId, job.RepositoryId, job.Request.PullRequestNumber);
            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Queued scan {ScanId} failed before completion.", job.ScanId);
                await MarkFailedAsync(job.ScanId, SafeFailureMessage(ex), stoppingToken);
            }
        }

        logger.LogInformation("Queued scan worker stopped.");
    }

    private async Task ProcessJobAsync(QueuedScanJob job, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<SpendGovernorStore>();
        var dbContext = scope.ServiceProvider.GetRequiredService<SpendGovernorDbContext>();
        var scanExecution = scope.ServiceProvider.GetRequiredService<ScanExecutionService>();

        var project = store.GetProject(job.ProjectId);
        var repository = await dbContext.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == job.RepositoryId, cancellationToken);

        if (project is null || repository is null)
        {
            var reason = project is null
                ? "Queued scan could not run because the project no longer exists."
                : "Queued scan could not run because the repository no longer exists.";
            await MarkFailedAsync(job.ScanId, reason, cancellationToken);
            logger.LogWarning("Queued scan {ScanId} was abandoned. {Reason}", job.ScanId, reason);
            return;
        }

        var publishing = await scanExecution.ExecuteAsync(project, repository, job.ScanId, job.Request, cancellationToken);
        var auditType = publishing.IsSimulated
            ? "GitHub PR comment simulated"
            : publishing.Succeeded ? "GitHub PR report published" : "GitHub PR report publishing failed";
        var auditMessage = publishing.Succeeded
            ? $"Report publishing status: {publishing.PublishingStatus}."
            : $"Report publishing failed after scan persistence: {publishing.ErrorMessage}";
        store.AddAudit(project.WorkspaceId, project.Id, job.ScanId, auditType, auditMessage);
    }

    private async Task MarkFailedAsync(Guid scanId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var scanStore = scope.ServiceProvider.GetRequiredService<IScanStore>();
            await scanStore.MarkFailedAsync(scanId, reason, null, cancellationToken);
            await scanStore.SaveGitHubPublishingResultAsync(scanId, null, null, null, "Failed", reason, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Could not persist failure state for queued scan {ScanId}.", scanId);
        }
    }

    private static IDisposable? BeginScanScope(ILogger logger, string? correlationId, Guid scanId, Guid projectId, Guid repositoryId, int pullRequestNumber)
    {
        return logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["ScanId"] = scanId,
            ["ProjectId"] = projectId,
            ["RepositoryId"] = repositoryId,
            ["PullRequestNumber"] = pullRequestNumber
        });
    }

    private static string SafeFailureMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}

public sealed class ScanExecutionService
{
    private readonly AnalysisEngine engine;
    private readonly IScanStore scanStore;
    private readonly IGitHubPullRequestReporter gitHubReporter;
    private readonly ILogger<ScanExecutionService> logger;

    public ScanExecutionService(
        AnalysisEngine engine,
        IScanStore scanStore,
        IGitHubPullRequestReporter gitHubReporter,
        ILogger<ScanExecutionService> logger)
    {
        this.engine = engine;
        this.scanStore = scanStore;
        this.gitHubReporter = gitHubReporter;
        this.logger = logger;
    }

    public async Task<GitHubReportPublishResult> ExecuteAsync(
        Project project,
        PersistenceRepository repository,
        Guid scanId,
        AnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["ScanId"] = scanId,
            ["ProjectId"] = project.Id,
            ["WorkspaceId"] = project.WorkspaceId,
            ["RepositoryFullName"] = repository.FullName,
            ["PullRequestNumber"] = request.PullRequestNumber
        });

        logger.LogInformation("Scan {ScanId} started for {RepositoryFullName} PR #{PullRequestNumber}.", scanId, repository.FullName, request.PullRequestNumber);

        try
        {
            await scanStore.MarkRunningAsync(scanId, DateTimeOffset.UtcNow, cancellationToken);
            var result = engine.Analyze(request);
            var publishing = await PersistScanResultAsync(project, repository, scanId, result, request, cancellationToken);

            logger.LogInformation(
                "Scan {ScanId} completed with decision {Decision}, monthly delta {MonthlyDelta} {Currency}, confidence {Confidence}, publish status {PublishStatus}.",
                scanId,
                result.Analysis.PolicyStatus,
                result.Analysis.MonthlyDelta,
                result.Analysis.Currency,
                result.Analysis.OverallConfidence,
                publishing.PublishingStatus);
            return publishing;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var reason = SafeFailureMessage(ex);
            logger.LogError(ex, "Scan {ScanId} failed.", scanId);

            var existingCommentId = await scanStore.FindExistingGitHubCommentIdAsync(repository.Id, request.PullRequestNumber, cancellationToken);
            var existingCheckRunId = await scanStore.FindExistingGitHubCheckRunIdAsync(repository.Id, request.PullRequestNumber, cancellationToken);
            await scanStore.MarkFailedAsync(scanId, reason, existingCommentId, cancellationToken);

            var failed = GitHubReportPublishResult.Failed(existingCommentId, existingCheckRunId, null, reason);
            await scanStore.SaveGitHubPublishingResultAsync(
                scanId,
                failed.CommentId,
                failed.CheckRunId,
                failed.ReportUrl,
                failed.PublishingStatus,
                failed.ErrorMessage,
                cancellationToken);
            return failed;
        }
    }

    private async Task<GitHubReportPublishResult> PersistScanResultAsync(
        Project project,
        PersistenceRepository repository,
        Guid scanId,
        AnalysisResult result,
        AnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.DashboardBaseUrl))
        {
            result.DashboardUrl = $"{request.DashboardBaseUrl.TrimEnd('/')}/?analysisId={scanId}";
            result.Analysis.DashboardUrl = result.DashboardUrl;
            result.CommentMarkdown = PrCommentRenderer.Render(result);
        }

        var existingCommentId = await scanStore.FindExistingGitHubCommentIdAsync(repository.Id, result.Analysis.PullRequestNumber, cancellationToken);
        var existingCheckRunId = await scanStore.FindExistingGitHubCheckRunIdAsync(repository.Id, result.Analysis.PullRequestNumber, cancellationToken);
        if (result.Analysis.Status == AnalysisStatus.Failed)
        {
            await scanStore.MarkFailedAsync(scanId, result.Analysis.ErrorMessage ?? "Scan failed.", existingCommentId, cancellationToken);
        }
        else
        {
            await scanStore.MarkCompletedAsync(scanId, result, existingCommentId, cancellationToken);
        }

        GitHubReportPublishResult publishing;
        try
        {
            publishing = await gitHubReporter.PublishAsync(new GitHubReportPublishRequest(
                project,
                repository,
                scanId,
                result,
                request,
                existingCommentId,
                existingCheckRunId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            publishing = GitHubReportPublishResult.Failed(
                existingCommentId,
                existingCheckRunId,
                result.DashboardUrl ?? result.Analysis.DashboardUrl,
                SafeFailureMessage(ex));
        }

        await scanStore.SaveGitHubPublishingResultAsync(
            scanId,
            publishing.CommentId,
            publishing.CheckRunId,
            publishing.ReportUrl,
            publishing.PublishingStatus,
            publishing.ErrorMessage,
            cancellationToken);

        return publishing;
    }

    private static string SafeFailureMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = exception.GetType().Name;
        }

        return message.Length <= 1800 ? message : message[..1800];
    }
}

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        this.scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<SpendGovernorDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection is available.")
                : HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Database health check failed.", ex);
        }
    }
}
