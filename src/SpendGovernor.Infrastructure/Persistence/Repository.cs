namespace SpendGovernor.Infrastructure.Persistence;

public sealed class Repository
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "github";
    public string Owner { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public string DefaultBranch { get; set; } = "main";
    public string? ExternalRepositoryId { get; set; }
    public string? InstallationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<PullRequestScan> PullRequestScans { get; set; } = [];
}

