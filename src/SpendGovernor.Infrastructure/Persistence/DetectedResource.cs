namespace SpendGovernor.Infrastructure.Persistence;

public sealed class DetectedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PullRequestScanId { get; set; }
    public PullRequestScan? PullRequestScan { get; set; }
    public string SourceFile { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string? Sku { get; set; }
    public string? Region { get; set; }
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string RawJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
