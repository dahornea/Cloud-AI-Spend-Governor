using Microsoft.EntityFrameworkCore;
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
        return dbContext.Repositories.FirstOrDefaultAsync(repository =>
            repository.Provider == provider && repository.FullName == fullName, cancellationToken);
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
        var fullName = $"{owner}/{name}";
        var repository = await FindByProviderAndFullNameAsync(provider, fullName, cancellationToken);
        if (repository is null)
        {
            repository = new Repository
            {
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
}

