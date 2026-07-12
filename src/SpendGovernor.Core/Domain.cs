using System.Globalization;

namespace SpendGovernor.Core;

public enum WorkspaceRole
{
    Owner,
    Member
}

public enum AnalysisStatus
{
    Queued,
    Running,
    Completed,
    Succeeded,
    Failed,
    Skipped
}

public enum PolicyAction
{
    Pass,
    Warn,
    ApprovalRequired,
    Block
}

public enum SpendPolicySeverity
{
    Info,
    Warn,
    Fail
}

public enum SpendPolicyEvaluationStatus
{
    NotMatched,
    Info,
    Warn,
    Fail
}

public enum SpendFindingSeverity
{
    Note,
    Warning,
    Error
}

public enum ResourceSourceType
{
    Terraform,
    Bicep,
    AiConfig
}

public enum CostCategory
{
    Compute,
    Storage,
    Database,
    Networking,
    Container,
    Ai,
    Unknown
}

public enum EstimateStatus
{
    Estimated,
    Unsupported,
    PriceNotFound,
    Unknown
}

public enum ConfidenceLevel
{
    High,
    Medium,
    Low,
    Unknown
}

public enum RelevantFileKind
{
    Terraform,
    TerraformPlanJson,
    TerraformVars,
    ArmTemplateJson,
    Bicep,
    BicepParam,
    SpendGovConfig,
    AiSpendConfig,
    Other
}

public sealed record Workspace(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record User(Guid Id, string Email, string Name, DateTimeOffset CreatedAt);

public sealed record WorkspaceMember(Guid WorkspaceId, Guid UserId, WorkspaceRole Role);

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; init; }
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "azure";
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int HoursPerMonth { get; set; } = 730;
    public string RepositoryProvider { get; set; } = "github";
    public string RepositoryOwner { get; set; } = "";
    public string RepositoryName { get; set; } = "";
    public string? GitHubInstallationId { get; set; }
    public string PolicyYaml { get; set; } = PolicyConfig.DefaultYaml;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ProjectSettings
{
    public string Provider { get; set; } = "azure";
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int HoursPerMonth { get; set; } = 730;
    public string PolicyYaml { get; set; } = PolicyConfig.DefaultYaml;

    public static ProjectSettings FromProject(Project project) => new()
    {
        Provider = project.Provider,
        Currency = project.Currency,
        DefaultRegion = project.DefaultRegion,
        HoursPerMonth = project.HoursPerMonth,
        PolicyYaml = project.PolicyYaml
    };
}

public sealed class PullRequestAnalysis
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string RepositoryOwner { get; init; } = "";
    public string RepositoryName { get; init; } = "";
    public int PullRequestNumber { get; init; }
    public string BaseBranch { get; init; } = "";
    public string HeadBranch { get; init; } = "";
    public string CommitSha { get; init; } = "";
    public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;
    public PolicyAction PolicyStatus { get; set; } = PolicyAction.Pass;
    public string? Environment { get; set; }
    public ConfidenceLevel OverallConfidence { get; set; } = ConfidenceLevel.Unknown;
    public decimal? BaselineMonthlyCost { get; set; }
    public decimal? ProposedMonthlyCost { get; set; }
    public decimal? MonthlyDelta { get; set; }
    public decimal? BudgetLimitMonthly { get; set; }
    public string? BudgetSource { get; set; }
    public string Currency { get; set; } = "EUR";
    public int UnknownResourceCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InternalStackTrace { get; set; }
    public string? GitHubPullRequestUrl { get; set; }
    public string? DashboardUrl { get; set; }
}

public sealed record RepositoryFile(string Path, string Content);

public sealed record DetectedFile(string Path, RelevantFileKind Kind)
{
    public bool IsRelevant => Kind != RelevantFileKind.Other;
}

public sealed record AnalysisRequest
{
    public Guid ProjectId { get; init; }
    public string RepositoryOwner { get; init; } = "";
    public string RepositoryName { get; init; } = "";
    public int PullRequestNumber { get; init; }
    public string BaseBranch { get; init; } = "main";
    public string HeadBranch { get; init; } = "feature/spend-change";
    public string CommitSha { get; init; } = "local";
    public IReadOnlyList<string> ChangedFiles { get; init; } = [];
    public IReadOnlyList<RepositoryFile> BaselineFiles { get; init; } = [];
    public IReadOnlyList<RepositoryFile> ProposedFiles { get; init; } = [];
    public ProjectSettings Settings { get; init; } = new();
    public string? DashboardBaseUrl { get; init; }
}

public sealed class CloudResourceEstimateInput
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n", CultureInfo.InvariantCulture);
    public ResourceSourceType SourceType { get; set; }
    public string SourceFile { get; set; } = "";
    public string Provider { get; set; } = "azure";
    public string ResourceType { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string? Region { get; set; }
    public string? Sku { get; set; }
    public string? Tier { get; set; }
    public string? AnalysisSource { get; set; }
    public string? ArmResourceType { get; set; }
    public string? ArmApiVersion { get; set; }
    public string? ArmKind { get; set; }
    public string? MappedResourceType { get; set; }
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string? TerraformChangeType { get; set; }
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string? PricingCatalogName { get; set; }
    public string? PricingCatalogVersion { get; set; }
    public string? PricingSource { get; set; }
    public string? PricingSourceType { get; set; }
    public string? PricingMatchType { get; set; }
    public string? PricingFallbackReason { get; set; }
    public string? PricingUnit { get; set; }
    public decimal? PricingUnitPrice { get; set; }
    public string? PricingMatchedKey { get; set; }
    public string? PricingConfidenceImpact { get; set; }
    public bool PricingLiveApiUsed { get; set; }
    public bool PricingFallbackUsed { get; set; }
    public bool PricingRegionDefaulted { get; set; }
    public bool PricingAmbiguousMatch { get; set; }
    public int? PricingMonthlyHours { get; set; }
    public string? PricingUnitOfMeasure { get; set; }
    public string? PricingMeterId { get; set; }
    public string? PricingMeterName { get; set; }
    public string? PricingProductName { get; set; }
    public string? PricingSkuName { get; set; }
    public string? PricingArmSkuName { get; set; }
    public string? PricingServiceName { get; set; }
    public string? PricingServiceFamily { get; set; }
    public string? PricingPriceType { get; set; }
    public DateTimeOffset? PricingEffectiveStartDate { get; set; }
    public decimal? Capacity { get; set; }
    public int Quantity { get; set; } = 1;
    public int HoursPerMonth { get; set; } = 730;
    public string? Environment { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Raw { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsSupported { get; set; } = true;
    public List<string> Warnings { get; } = [];
}

public sealed class ResourceEstimate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public ResourceSourceType SourceType { get; set; }
    public string SourceFile { get; set; } = "";
    public string Provider { get; set; } = "azure";
    public string ResourceType { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string? Region { get; set; }
    public string? Sku { get; set; }
    public string? Tier { get; set; }
    public string? AnalysisSource { get; set; }
    public string? ArmResourceType { get; set; }
    public string? ArmApiVersion { get; set; }
    public string? ArmKind { get; set; }
    public string? MappedResourceType { get; set; }
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string? TerraformChangeType { get; set; }
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string? PricingCatalogName { get; set; }
    public string? PricingCatalogVersion { get; set; }
    public string? PricingSource { get; set; }
    public string? PricingSourceType { get; set; }
    public string? PricingMatchType { get; set; }
    public string? PricingFallbackReason { get; set; }
    public string? PricingUnit { get; set; }
    public decimal? PricingUnitPrice { get; set; }
    public string? PricingMatchedKey { get; set; }
    public string? PricingConfidenceImpact { get; set; }
    public bool PricingLiveApiUsed { get; set; }
    public bool PricingFallbackUsed { get; set; }
    public bool PricingRegionDefaulted { get; set; }
    public bool PricingAmbiguousMatch { get; set; }
    public int? PricingMonthlyHours { get; set; }
    public string? PricingUnitOfMeasure { get; set; }
    public string? PricingMeterId { get; set; }
    public string? PricingMeterName { get; set; }
    public string? PricingProductName { get; set; }
    public string? PricingSkuName { get; set; }
    public string? PricingArmSkuName { get; set; }
    public string? PricingServiceName { get; set; }
    public string? PricingServiceFamily { get; set; }
    public string? PricingPriceType { get; set; }
    public DateTimeOffset? PricingEffectiveStartDate { get; set; }
    public string? Environment { get; set; }
    public CostCategory Category { get; set; } = CostCategory.Unknown;
    public decimal? MonthlyCost { get; set; }
    public decimal? MonthlyDelta { get; set; }
    public string Currency { get; set; } = "EUR";
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;
    public EstimateStatus Status { get; set; } = EstimateStatus.Unknown;
    public int Quantity { get; set; } = 1;
    public int HoursPerMonth { get; set; } = 730;
    public string AssumptionsJson { get; set; } = "{}";
    public string? PriceSource { get; set; }
    public DateOnly? PriceLastUpdated { get; set; }
    public List<string> Warnings { get; } = [];
}

public sealed class ResourceCostChange
{
    public string ResourceKey { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string? Region { get; set; }
    public string? BeforeSku { get; set; }
    public string? AfterSku { get; set; }
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string? Reason { get; set; }
    public string? PricingCatalogVersion { get; set; }
    public string? PricingSource { get; set; }
    public string? PricingMatchType { get; set; }
    public string? PricingFallbackReason { get; set; }
    public decimal MonthlyDelta { get; set; }
    public string ChangeKind { get; set; } = "changed";
}

public sealed class PolicyFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public string RuleId { get; set; } = "";
    public PolicyAction Action { get; set; }
    public string Message { get; set; } = "";
    public decimal? ActualValue { get; set; }
    public decimal? ThresholdValue { get; set; }
}

public sealed class SpendPolicyEvaluation
{
    public string PolicyId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public SpendPolicySeverity Severity { get; set; } = SpendPolicySeverity.Info;
    public bool Matched { get; set; }
    public SpendPolicyEvaluationStatus Result { get; set; } = SpendPolicyEvaluationStatus.NotMatched;
    public string? MatchedResource { get; set; }
    public string? MatchedResourceType { get; set; }
    public string Message { get; set; } = "";
    public string Recommendation { get; set; } = "";
}

public sealed class SpendFinding
{
    public string Id { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public SpendFindingSeverity Severity { get; set; } = SpendFindingSeverity.Warning;
    public string Category { get; set; } = "";
    public string? SourceFile { get; set; }
    public int? StartLine { get; set; }
    public int? StartColumn { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public string? ResourceName { get; set; }
    public string? ResourceType { get; set; }
    public string? Provider { get; set; }
    public string? Environment { get; set; }
    public decimal? EstimatedMonthlyCost { get; set; }
    public decimal? EstimatedMonthlyDelta { get; set; }
    public string Currency { get; set; } = "EUR";
    public ConfidenceLevel? ConfidenceLevel { get; set; }
    public string? PolicyId { get; set; }
    public string? PricingSource { get; set; }
    public string? PricingMatchType { get; set; }
}

public sealed class Recommendation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnalysisId { get; set; }
    public Guid? ResourceEstimateId { get; set; }
    public string Severity { get; set; } = "low";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? EstimatedMonthlySavings { get; set; }
}

public sealed class Approval
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AnalysisId { get; init; }
    public Guid ApprovedByUserId { get; init; }
    public string CommitSha { get; init; } = "";
    public string Reason { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? AnalysisId { get; set; }
    public string EventType { get; set; } = "";
    public string Message { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AnalysisResult
{
    public PullRequestAnalysis Analysis { get; set; } = new();
    public IReadOnlyList<ResourceEstimate> BaselineResources { get; set; } = [];
    public IReadOnlyList<ResourceEstimate> ProposedResources { get; set; } = [];
    public IReadOnlyList<ResourceCostChange> CostChanges { get; set; } = [];
    public IReadOnlyList<PolicyFinding> PolicyFindings { get; set; } = [];
    public IReadOnlyList<SpendPolicyEvaluation> PolicyAsCodeEvaluations { get; set; } = [];
    public IReadOnlyList<SpendFinding> Findings { get; set; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; set; } = [];
    public IReadOnlyList<AuditEvent> AuditEvents { get; set; } = [];
    public string CommentMarkdown { get; set; } = "";
    public string CheckConclusion { get; set; } = "success";
    public string? DashboardUrl { get; set; }
    public IReadOnlyList<string> ConfigErrors { get; set; } = [];
}

public sealed class GitHubPrCommentState
{
    public long CommentId { get; init; }
    public string RepositoryOwner { get; init; } = "";
    public string RepositoryName { get; init; } = "";
    public int PullRequestNumber { get; init; }
    public string Body { get; set; } = "";
    public string CheckConclusion { get; set; } = "success";
    public int UpdateCount { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool WasCreatedOnLastWrite { get; set; }
}

public sealed class GitHubPrCommentTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, GitHubPrCommentState> comments = new(StringComparer.OrdinalIgnoreCase);
    private long nextCommentId = 10_000;

    public GitHubPrCommentState Upsert(PullRequestAnalysis analysis, string body, string checkConclusion, string? existingCommentId = null)
    {
        var key = Key(analysis.RepositoryOwner, analysis.RepositoryName, analysis.PullRequestNumber);
        lock (gate)
        {
            if (!comments.TryGetValue(key, out var state))
            {
                var commentId = long.TryParse(existingCommentId, out var parsedCommentId)
                    ? parsedCommentId
                    : nextCommentId++;
                state = new GitHubPrCommentState
                {
                    CommentId = commentId,
                    RepositoryOwner = analysis.RepositoryOwner,
                    RepositoryName = analysis.RepositoryName,
                    PullRequestNumber = analysis.PullRequestNumber,
                    WasCreatedOnLastWrite = true
                };
                comments[key] = state;
            }
            else
            {
                state.WasCreatedOnLastWrite = false;
                state.UpdateCount++;
            }

            state.Body = body;
            state.CheckConclusion = checkConclusion;
            state.UpdatedAt = DateTimeOffset.UtcNow;
            return state;
        }
    }

    public GitHubPrCommentState? Get(string owner, string name, int pullRequestNumber)
    {
        lock (gate)
        {
            return comments.GetValueOrDefault(Key(owner, name, pullRequestNumber));
        }
    }

    private static string Key(string owner, string name, int pullRequestNumber)
    {
        return $"{owner}/{name}#{pullRequestNumber}";
    }
}
