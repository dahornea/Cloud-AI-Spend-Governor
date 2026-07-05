namespace SpendGovernor.Infrastructure.Persistence;

public enum CostChangeType
{
    Added,
    Modified,
    Removed,
    Unknown
}

public sealed class CostBreakdownItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PullRequestScanId { get; set; }
    public PullRequestScan? PullRequestScan { get; set; }
    public string ResourceName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public CostChangeType ChangeType { get; set; } = CostChangeType.Unknown;
    public decimal? EstimatedMonthlyCost { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string? PricingCatalogVersion { get; set; }
    public string? PricingSource { get; set; }
    public string? PricingMatchType { get; set; }
    public string? PricingFallbackReason { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
