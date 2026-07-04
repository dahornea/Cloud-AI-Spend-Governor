using Microsoft.EntityFrameworkCore;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public sealed class ScanStore : IScanStore
{
    private readonly SpendGovernorDbContext dbContext;
    private readonly IScanResultWriter resultWriter;

    public ScanStore(SpendGovernorDbContext dbContext, IScanResultWriter resultWriter)
    {
        this.dbContext = dbContext;
        this.resultWriter = resultWriter;
    }

    public async Task<PullRequestScan> CreateScanAsync(Repository repository, AnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var scan = new PullRequestScan
        {
            RepositoryId = repository.Id,
            PullRequestNumber = request.PullRequestNumber,
            SourceBranch = request.HeadBranch,
            TargetBranch = request.BaseBranch,
            Environment = InferEnvironment(request.HeadBranch),
            Status = ScanStatus.Queued,
            Decision = PolicyDecision.Unknown,
            Currency = request.Settings.Currency,
            ConfidenceLevel = ScanConfidenceLevel.Unknown,
            DashboardReportUrl = request.DashboardBaseUrl,
            GitHubPullRequestUrl = $"https://github.com/{repository.FullName}/pull/{request.PullRequestNumber}",
            ReportPublishingStatus = "Pending"
        };
        dbContext.PullRequestScans.Add(scan);
        await dbContext.SaveChangesAsync(cancellationToken);
        return scan;
    }

    public async Task MarkRunningAsync(Guid scanId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        var scan = await dbContext.PullRequestScans.FindAsync([scanId], cancellationToken);
        if (scan is null)
        {
            return;
        }

        scan.Status = ScanStatus.Running;
        scan.StartedAt = startedAt;
        scan.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCompletedAsync(Guid scanId, AnalysisResult result, string? gitHubCommentId, CancellationToken cancellationToken = default)
    {
        var scan = await dbContext.PullRequestScans.FindAsync([scanId], cancellationToken);
        if (scan is null)
        {
            return;
        }

        await resultWriter.PersistCompletedResultAsync(scanId, result, cancellationToken);

        scan.SourceBranch = result.Analysis.HeadBranch;
        scan.TargetBranch = result.Analysis.BaseBranch;
        scan.Environment = result.Analysis.Environment;
        scan.Status = result.Analysis.Status == AnalysisStatus.Failed ? ScanStatus.Failed : ScanStatus.Completed;
        scan.Decision = ToPolicyDecision(result.Analysis.PolicyStatus);
        scan.EstimatedMonthlyDelta = result.Analysis.MonthlyDelta;
        scan.Currency = result.Analysis.Currency;
        scan.ConfidenceLevel = ToScanConfidence(result.Analysis.OverallConfidence);
        scan.StartedAt = result.Analysis.StartedAt ?? scan.StartedAt;
        scan.CompletedAt = result.Analysis.CompletedAt ?? DateTimeOffset.UtcNow;
        scan.FailureReason = result.Analysis.ErrorMessage;
        scan.DashboardReportUrl = result.Analysis.DashboardUrl ?? result.DashboardUrl;
        scan.GitHubCommentId = gitHubCommentId ?? scan.GitHubCommentId;
        scan.GitHubPullRequestUrl = result.Analysis.GitHubPullRequestUrl;
        scan.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid scanId, string failureReason, string? gitHubCommentId = null, CancellationToken cancellationToken = default)
    {
        var scan = await dbContext.PullRequestScans.FindAsync([scanId], cancellationToken);
        if (scan is null)
        {
            return;
        }

        scan.Status = ScanStatus.Failed;
        scan.Decision = PolicyDecision.Unknown;
        scan.FailureReason = failureReason;
        scan.GitHubCommentId = gitHubCommentId ?? scan.GitHubCommentId;
        scan.CompletedAt = DateTimeOffset.UtcNow;
        scan.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<PullRequestScan?> FindLatestByRepositoryAndPrAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default)
    {
        return dbContext.PullRequestScans
            .Where(scan => scan.RepositoryId == repositoryId && scan.PullRequestNumber == pullRequestNumber)
            .OrderByDescending(scan => scan.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> FindExistingGitHubCommentIdAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default)
    {
        var scans = await dbContext.PullRequestScans
            .Where(scan => scan.RepositoryId == repositoryId && scan.PullRequestNumber == pullRequestNumber && scan.GitHubCommentId != null)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return scans
            .OrderByDescending(scan => scan.UpdatedAt)
            .Select(scan => scan.GitHubCommentId)
            .FirstOrDefault();
    }

    public async Task<string?> FindExistingGitHubCheckRunIdAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default)
    {
        var scans = await dbContext.PullRequestScans
            .Where(scan => scan.RepositoryId == repositoryId && scan.PullRequestNumber == pullRequestNumber && scan.GitHubCheckRunId != null)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return scans
            .OrderByDescending(scan => scan.UpdatedAt)
            .Select(scan => scan.GitHubCheckRunId)
            .FirstOrDefault();
    }

    public async Task SaveGitHubPublishingResultAsync(
        Guid scanId,
        string? gitHubCommentId,
        string? gitHubCheckRunId,
        string? gitHubReportUrl,
        string reportPublishingStatus,
        string? reportPublishingError,
        CancellationToken cancellationToken = default)
    {
        var scan = await dbContext.PullRequestScans.FindAsync([scanId], cancellationToken);
        if (scan is null)
        {
            return;
        }

        scan.GitHubCommentId = gitHubCommentId ?? scan.GitHubCommentId;
        scan.GitHubCheckRunId = gitHubCheckRunId ?? scan.GitHubCheckRunId;
        scan.GitHubReportUrl = gitHubReportUrl ?? scan.GitHubReportUrl;
        scan.ReportPublishingStatus = string.IsNullOrWhiteSpace(reportPublishingStatus) ? "Unknown" : reportPublishingStatus;
        scan.ReportPublishingError = reportPublishingError;
        scan.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PullRequestScan>> GetLatestScansForRepositoryAsync(Guid repositoryId, int take = 50, CancellationToken cancellationToken = default)
    {
        var scans = await dbContext.PullRequestScans
            .Where(scan => scan.RepositoryId == repositoryId)
            .Include(scan => scan.Repository)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return scans
            .OrderByDescending(scan => scan.CreatedAt)
            .Take(take)
            .ToArray();
    }

    public Task<PullRequestScan?> GetScanDetailsAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        return dbContext.PullRequestScans
            .Include(scan => scan.Repository)
            .Include(scan => scan.CostBreakdownItems)
            .Include(scan => scan.DetectedResources)
            .Include(scan => scan.ScanAssumptions)
            .Include(scan => scan.PolicyEvaluations)
            .AsNoTracking()
            .FirstOrDefaultAsync(scan => scan.Id == scanId, cancellationToken);
    }

    private static PolicyDecision ToPolicyDecision(PolicyAction action) => action switch
    {
        PolicyAction.Pass => PolicyDecision.Pass,
        PolicyAction.Warn => PolicyDecision.Warn,
        PolicyAction.ApprovalRequired => PolicyDecision.Fail,
        PolicyAction.Block => PolicyDecision.Fail,
        _ => PolicyDecision.Unknown
    };

    private static ScanConfidenceLevel ToScanConfidence(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.High => ScanConfidenceLevel.High,
        ConfidenceLevel.Medium => ScanConfidenceLevel.Medium,
        ConfidenceLevel.Low => ScanConfidenceLevel.Low,
        ConfidenceLevel.Unknown => ScanConfidenceLevel.Unknown,
        _ => ScanConfidenceLevel.Unknown
    };

    private static string? InferEnvironment(string branch)
    {
        var lowered = branch.ToLowerInvariant();
        if (lowered.Contains("prod", StringComparison.Ordinal))
        {
            return "production";
        }

        if (lowered.Contains("staging", StringComparison.Ordinal) || lowered.Contains("stage", StringComparison.Ordinal))
        {
            return "staging";
        }

        if (lowered.Contains("dev", StringComparison.Ordinal))
        {
            return "dev";
        }

        return null;
    }
}
