namespace SpendGovernor.Infrastructure.Persistence;

public enum ScanStatus
{
    Queued,
    Running,
    Completed,
    Failed
}

public enum PolicyDecision
{
    Pass,
    Warn,
    Fail,
    Unknown
}

public enum ScanConfidenceLevel
{
    Low,
    Medium,
    High,
    Unknown
}

public sealed class PullRequestScan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RepositoryId { get; set; }
    public Repository? Repository { get; set; }
    public int PullRequestNumber { get; set; }
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public string? Environment { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Queued;
    public PolicyDecision Decision { get; set; } = PolicyDecision.Unknown;
    public decimal? EstimatedMonthlyDelta { get; set; }
    public string Currency { get; set; } = "EUR";
    public ScanConfidenceLevel ConfidenceLevel { get; set; } = ScanConfidenceLevel.Unknown;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public string? DashboardReportUrl { get; set; }
    public string? GitHubCommentId { get; set; }
    public string? GitHubCheckRunId { get; set; }
    public string? GitHubReportUrl { get; set; }
    public string? GitHubPullRequestUrl { get; set; }
    public string ReportPublishingStatus { get; set; } = "Pending";
    public string? ReportPublishingError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CostBreakdownItem> CostBreakdownItems { get; set; } = [];
    public List<DetectedResource> DetectedResources { get; set; } = [];
    public List<ScanAssumption> ScanAssumptions { get; set; } = [];
    public List<PolicyEvaluation> PolicyEvaluations { get; set; } = [];
}
