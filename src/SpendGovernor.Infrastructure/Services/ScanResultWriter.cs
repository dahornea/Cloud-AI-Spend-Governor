using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public sealed class ScanResultWriter : IScanResultWriter
{
    private readonly SpendGovernorDbContext dbContext;

    public ScanResultWriter(SpendGovernorDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    public async Task PersistCompletedResultAsync(Guid scanId, AnalysisResult result, CancellationToken cancellationToken = default)
    {
        var existingBreakdown = dbContext.CostBreakdownItems.Where(item => item.PullRequestScanId == scanId);
        var existingResources = dbContext.DetectedResources.Where(resource => resource.PullRequestScanId == scanId);
        var existingAssumptions = dbContext.ScanAssumptions.Where(assumption => assumption.PullRequestScanId == scanId);
        var existingPolicies = dbContext.PolicyEvaluations.Where(evaluation => evaluation.PullRequestScanId == scanId);
        dbContext.CostBreakdownItems.RemoveRange(existingBreakdown);
        dbContext.DetectedResources.RemoveRange(existingResources);
        dbContext.ScanAssumptions.RemoveRange(existingAssumptions);
        dbContext.PolicyEvaluations.RemoveRange(existingPolicies);

        dbContext.CostBreakdownItems.AddRange(BuildCostBreakdownItems(scanId, result));
        dbContext.DetectedResources.AddRange(BuildDetectedResources(scanId, result));
        dbContext.ScanAssumptions.AddRange(BuildAssumptions(scanId, result));
        dbContext.PolicyEvaluations.AddRange(BuildPolicyEvaluations(scanId, result));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IEnumerable<CostBreakdownItem> BuildCostBreakdownItems(Guid scanId, AnalysisResult result)
    {
        foreach (var change in result.CostChanges)
        {
            yield return new CostBreakdownItem
            {
                PullRequestScanId = scanId,
                ResourceName = change.ResourceName,
                ResourceType = change.ResourceType,
                ChangeType = ToChangeType(change.ChangeKind),
                EstimatedMonthlyCost = change.MonthlyDelta,
                Currency = result.Analysis.Currency,
                Reason = $"{change.BeforeSku ?? "none"} -> {change.AfterSku ?? "none"}"
            };
        }

        if (result.CostChanges.Count == 0)
        {
            foreach (var resource in result.ProposedResources.Where(resource => resource.MonthlyCost is not null))
            {
                yield return new CostBreakdownItem
                {
                    PullRequestScanId = scanId,
                    ResourceName = resource.ResourceName,
                    ResourceType = resource.ResourceType,
                    ChangeType = CostChangeType.Unknown,
                    EstimatedMonthlyCost = resource.MonthlyCost,
                    Currency = resource.Currency,
                    Reason = "Estimated proposed monthly cost."
                };
            }
        }
    }

    private static IEnumerable<DetectedResource> BuildDetectedResources(Guid scanId, AnalysisResult result)
    {
        foreach (var resource in result.ProposedResources)
        {
            yield return new DetectedResource
            {
                PullRequestScanId = scanId,
                SourceFile = resource.SourceFile,
                Provider = resource.Provider,
                ResourceType = resource.ResourceType,
                ResourceName = resource.ResourceName,
                Sku = resource.Sku,
                Region = resource.Region,
                RawJson = JsonSerializer.Serialize(new
                {
                    resource.SourceType,
                    resource.Category,
                    resource.MonthlyCost,
                    resource.MonthlyDelta,
                    resource.Confidence,
                    resource.Status,
                    resource.AssumptionsJson,
                    resource.Warnings
                })
            };
        }
    }

    private static IEnumerable<ScanAssumption> BuildAssumptions(Guid scanId, AnalysisResult result)
    {
        var region = result.ProposedResources.Select(resource => resource.Region).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "not detected";
        yield return Assumption(scanId, "Region", region);
        yield return Assumption(scanId, "Pricing source", "local MVP static pricing catalog");
        yield return Assumption(scanId, "Usage estimate", $"{result.ProposedResources.FirstOrDefault(resource => resource.HoursPerMonth > 0)?.HoursPerMonth ?? 730} hours/month for always-on resources");
        yield return Assumption(scanId, "Catalog version", "local MVP catalog");
        yield return Assumption(scanId, "Currency", result.Analysis.Currency);
        yield return Assumption(scanId, "Confidence", result.Analysis.OverallConfidence.ToString());

        foreach (var resource in result.ProposedResources.Where(resource => !string.IsNullOrWhiteSpace(resource.AssumptionsJson)))
        {
            yield return Assumption(scanId, $"Resource assumptions: {resource.ResourceName}", resource.AssumptionsJson);
        }
    }

    private static IEnumerable<PolicyEvaluation> BuildPolicyEvaluations(Guid scanId, AnalysisResult result)
    {
        if (result.PolicyFindings.Count == 0)
        {
            yield return new PolicyEvaluation
            {
                PullRequestScanId = scanId,
                RuleName = "all-policies",
                Result = PolicyRuleResult.Pass,
                Message = "No policy findings were triggered."
            };
            yield break;
        }

        foreach (var finding in result.PolicyFindings)
        {
            yield return new PolicyEvaluation
            {
                PullRequestScanId = scanId,
                RuleName = finding.RuleId,
                Result = ToPolicyRuleResult(finding.Action),
                Message = finding.Message
            };
        }
    }

    private static ScanAssumption Assumption(Guid scanId, string name, string value) => new()
    {
        PullRequestScanId = scanId,
        Name = name,
        Value = value
    };

    private static CostChangeType ToChangeType(string changeKind) => changeKind.ToLowerInvariant() switch
    {
        "added" => CostChangeType.Added,
        "changed" => CostChangeType.Modified,
        "removed" => CostChangeType.Removed,
        _ => CostChangeType.Unknown
    };

    private static PolicyRuleResult ToPolicyRuleResult(PolicyAction action) => action switch
    {
        PolicyAction.Pass => PolicyRuleResult.Pass,
        PolicyAction.Warn => PolicyRuleResult.Warn,
        PolicyAction.ApprovalRequired => PolicyRuleResult.Fail,
        PolicyAction.Block => PolicyRuleResult.Fail,
        _ => PolicyRuleResult.Warn
    };
}

