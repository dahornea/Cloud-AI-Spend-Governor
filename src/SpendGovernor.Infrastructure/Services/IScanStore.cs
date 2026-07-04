using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public interface IScanStore
{
    Task<PullRequestScan> CreateScanAsync(Repository repository, AnalysisRequest request, CancellationToken cancellationToken = default);

    Task MarkRunningAsync(Guid scanId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(Guid scanId, AnalysisResult result, string? gitHubCommentId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid scanId, string failureReason, string? gitHubCommentId = null, CancellationToken cancellationToken = default);

    Task<PullRequestScan?> FindLatestByRepositoryAndPrAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default);

    Task<string?> FindExistingGitHubCommentIdAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default);

    Task<string?> FindExistingGitHubCheckRunIdAsync(Guid repositoryId, int pullRequestNumber, CancellationToken cancellationToken = default);

    Task SaveGitHubPublishingResultAsync(
        Guid scanId,
        string? gitHubCommentId,
        string? gitHubCheckRunId,
        string? gitHubReportUrl,
        string reportPublishingStatus,
        string? reportPublishingError,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PullRequestScan>> GetLatestScansForRepositoryAsync(Guid repositoryId, int take = 50, CancellationToken cancellationToken = default);

    Task<PullRequestScan?> GetScanDetailsAsync(Guid scanId, CancellationToken cancellationToken = default);
}
