using SpendGovernor.Core;

namespace SpendGovernor.Infrastructure.Services;

public interface IScanResultWriter
{
    Task PersistCompletedResultAsync(Guid scanId, AnalysisResult result, CancellationToken cancellationToken = default);
}

