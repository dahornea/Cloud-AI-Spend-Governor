using Microsoft.EntityFrameworkCore;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public sealed class RepositoryStore : IRepositoryStore
{
    private readonly SpendGovernorDbContext dbContext;

    public RepositoryStore(SpendGovernorDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<Repository?> FindByProviderAndFullNameAsync(string provider, string fullName, CancellationToken cancellationToken = default)
    {
        return dbContext.Repositories
            .Include(repository => repository.Project)
            .FirstOrDefaultAsync(repository =>
            repository.Provider == provider && repository.FullName == fullName, cancellationToken);
    }

    public Task<Repository?> FindByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return dbContext.Repositories
            .Include(repository => repository.Project)
            .FirstOrDefaultAsync(repository => repository.ProjectId == projectId, cancellationToken);
    }

    public Task<Repository?> FindByProjectAndProviderAndFullNameAsync(Guid projectId, string provider, string fullName, CancellationToken cancellationToken = default)
    {
        return dbContext.Repositories
            .Include(repository => repository.Project)
            .FirstOrDefaultAsync(repository =>
                repository.ProjectId == projectId
                && repository.Provider == provider
                && repository.FullName == fullName,
                cancellationToken);
    }

    public async Task<Repository> FindOrCreateAsync(
        string provider,
        string owner,
        string name,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default)
    {
        var project = await EnsureDefaultProjectAsync(cancellationToken);
        return await FindOrCreateAsync(project.Id, provider, owner, name, defaultBranch, externalRepositoryId, installationId, cancellationToken);
    }

    public async Task<Repository> FindOrCreateAsync(
        Guid projectId,
        string provider,
        string owner,
        string name,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default)
    {
        var fullName = $"{owner}/{name}";
        var repository = await FindByProjectAndProviderAndFullNameAsync(projectId, provider, fullName, cancellationToken);
        if (repository is null)
        {
            repository = new Repository
            {
                ProjectId = projectId,
                Provider = provider,
                Owner = owner,
                Name = name,
                FullName = fullName,
                DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch,
                ExternalRepositoryId = externalRepositoryId,
                InstallationId = installationId
            };
            dbContext.Repositories.Add(repository);
        }
        else
        {
            repository.Owner = owner;
            repository.Name = name;
            repository.DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? repository.DefaultBranch : defaultBranch;
            repository.ExternalRepositoryId = externalRepositoryId ?? repository.ExternalRepositoryId;
            repository.InstallationId = installationId ?? repository.InstallationId;
            repository.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task UpdateMetadataAsync(
        Guid repositoryId,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default)
    {
        var repository = await dbContext.Repositories.FindAsync([repositoryId], cancellationToken);
        if (repository is null)
        {
            return;
        }

        repository.DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? repository.DefaultBranch : defaultBranch;
        repository.ExternalRepositoryId = externalRepositoryId ?? repository.ExternalRepositoryId;
        repository.InstallationId = installationId ?? repository.InstallationId;
        repository.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ProjectEntity> EnsureDefaultProjectAsync(CancellationToken cancellationToken)
    {
        var project = await dbContext.Projects
            .FirstOrDefaultAsync(item => item.Slug == "default-project", cancellationToken);
        if (project is not null)
        {
            return project;
        }

        var user = await dbContext.ApplicationUsers
            .FirstOrDefaultAsync(item => item.Email == "demo@spendgov.local", cancellationToken);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Email = "demo@spendgov.local",
                UserName = "demo@spendgov.local",
                DisplayName = "Demo Owner"
            };
            dbContext.ApplicationUsers.Add(user);
        }

        var workspace = await dbContext.Workspaces
            .FirstOrDefaultAsync(item => item.Slug == "default-workspace", cancellationToken);
        if (workspace is null)
        {
            workspace = new WorkspaceEntity
            {
                Name = "Default Workspace",
                Slug = "default-workspace",
                CreatedByUserId = user.Id
            };
            dbContext.Workspaces.Add(workspace);
            dbContext.WorkspaceMembers.Add(new WorkspaceMemberEntity
            {
                WorkspaceId = workspace.Id,
                UserId = user.Id,
                Role = WorkspaceRole.Owner
            });
        }

        project = new ProjectEntity
        {
            WorkspaceId = workspace.Id,
            Name = "Default Project",
            Slug = "default-project",
            Provider = "azure",
            Currency = "EUR",
            DefaultRegion = "westeurope",
            HoursPerMonth = 730,
            PolicyYaml = PolicyConfig.DefaultYaml
        };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return project;
    }
}
