using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public interface IRepositoryStore
{
    Task<Repository?> FindByProviderAndFullNameAsync(string provider, string fullName, CancellationToken cancellationToken = default);

    Task<Repository?> FindByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Repository?> FindByProjectAndProviderAndFullNameAsync(Guid projectId, string provider, string fullName, CancellationToken cancellationToken = default);

    Task<Repository> FindOrCreateAsync(
        string provider,
        string owner,
        string name,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default);

    Task<Repository> FindOrCreateAsync(
        Guid projectId,
        string provider,
        string owner,
        string name,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default);

    Task UpdateMetadataAsync(
        Guid repositoryId,
        string defaultBranch,
        string? externalRepositoryId,
        string? installationId,
        CancellationToken cancellationToken = default);
}
