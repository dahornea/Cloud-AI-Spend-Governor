namespace SpendGovernor.Infrastructure.Persistence;

public sealed class WorkspaceEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public ApplicationUser? CreatedByUser { get; set; }

    public List<WorkspaceMemberEntity> WorkspaceMembers { get; set; } = [];
    public List<ProjectEntity> Projects { get; set; } = [];
}
