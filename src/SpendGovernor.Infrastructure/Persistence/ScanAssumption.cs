namespace SpendGovernor.Infrastructure.Persistence;

public sealed class ScanAssumption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PullRequestScanId { get; set; }
    public PullRequestScan? PullRequestScan { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

