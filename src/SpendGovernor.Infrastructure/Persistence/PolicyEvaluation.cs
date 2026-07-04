namespace SpendGovernor.Infrastructure.Persistence;

public enum PolicyRuleResult
{
    Pass,
    Warn,
    Fail
}

public sealed class PolicyEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PullRequestScanId { get; set; }
    public PullRequestScan? PullRequestScan { get; set; }
    public string RuleName { get; set; } = "";
    public PolicyRuleResult Result { get; set; } = PolicyRuleResult.Pass;
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

