using SpendGovernor.Core;

namespace SpendGovernor.Infrastructure.Persistence;

public sealed class WorkspaceMemberEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public WorkspaceEntity? Workspace { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
