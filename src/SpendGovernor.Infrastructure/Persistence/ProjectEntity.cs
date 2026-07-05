namespace SpendGovernor.Infrastructure.Persistence;

public sealed class ProjectEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public WorkspaceEntity? Workspace { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string Provider { get; set; } = "azure";
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int HoursPerMonth { get; set; } = 730;
    public string PolicyYaml { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Repository> Repositories { get; set; } = [];
    public List<EnvironmentBudget> EnvironmentBudgets { get; set; } = [];
}
