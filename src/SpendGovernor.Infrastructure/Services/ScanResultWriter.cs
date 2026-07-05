using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;

namespace SpendGovernor.Infrastructure.Services;

public sealed class ScanResultWriter : IScanResultWriter
{
    private const int MaxAssumptionValueLength = 2000;
    private const string AssumptionTruncationSuffix = "... (truncated; full metadata is stored on the detected resource raw JSON)";

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
            var resource = result.ProposedResources.FirstOrDefault(resource => ResourceKey(resource).Equals(change.ResourceKey, StringComparison.OrdinalIgnoreCase))
                ?? result.BaselineResources.FirstOrDefault(resource => ResourceKey(resource).Equals(change.ResourceKey, StringComparison.OrdinalIgnoreCase));
            yield return new CostBreakdownItem
            {
                PullRequestScanId = scanId,
                ResourceName = change.ResourceName,
                ResourceType = change.ResourceType,
                ChangeType = ToChangeType(change.ChangeKind),
                EstimatedMonthlyCost = change.MonthlyDelta,
                Currency = result.Analysis.Currency,
                BeforeSummary = change.BeforeSummary ?? change.BeforeSku,
                AfterSummary = change.AfterSummary ?? change.AfterSku,
                TerraformAddress = change.TerraformAddress,
                TerraformActions = change.TerraformActions,
                PricingCatalogVersion = change.PricingCatalogVersion ?? resource?.PricingCatalogVersion,
                PricingSource = change.PricingSource ?? resource?.PricingSource,
                PricingMatchType = change.PricingMatchType ?? resource?.PricingMatchType,
                PricingFallbackReason = change.PricingFallbackReason ?? resource?.PricingFallbackReason,
                Reason = BuildReason(change, resource)
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
                    PricingCatalogVersion = resource.PricingCatalogVersion,
                    PricingSource = resource.PricingSource,
                    PricingMatchType = resource.PricingMatchType,
                    PricingFallbackReason = resource.PricingFallbackReason,
                    Reason = BuildReason(new ResourceCostChange
                    {
                        ResourceName = resource.ResourceName,
                        ResourceType = resource.ResourceType,
                        ChangeKind = "estimated",
                        MonthlyDelta = resource.MonthlyCost ?? 0
                    }, resource)
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
                TerraformAddress = resource.TerraformAddress,
                TerraformActions = resource.TerraformActions,
                RawJson = JsonSerializer.Serialize(new
                {
                    resource.SourceType,
                    resource.AnalysisSource,
                    resource.ArmResourceType,
                    resource.ArmApiVersion,
                    resource.ArmKind,
                    resource.MappedResourceType,
                    resource.TerraformAddress,
                    resource.TerraformActions,
                    resource.TerraformChangeType,
                    resource.BeforeSummary,
                    resource.AfterSummary,
                    resource.PricingCatalogName,
                    resource.PricingCatalogVersion,
                    resource.PricingSource,
                    resource.PricingSourceType,
                    resource.PricingMatchType,
                    resource.PricingFallbackReason,
                    resource.PricingUnit,
                    resource.PricingUnitPrice,
                    resource.PricingMatchedKey,
                    resource.PricingConfidenceImpact,
                    resource.PricingLiveApiUsed,
                    resource.PricingFallbackUsed,
                    resource.PricingRegionDefaulted,
                    resource.PricingAmbiguousMatch,
                    resource.PricingMonthlyHours,
                    resource.PricingUnitOfMeasure,
                    resource.PricingMeterId,
                    resource.PricingMeterName,
                    resource.PricingProductName,
                    resource.PricingSkuName,
                    resource.PricingArmSkuName,
                    resource.PricingServiceName,
                    resource.PricingServiceFamily,
                    resource.PricingPriceType,
                    resource.PricingEffectiveStartDate,
                    resource.Category,
                    resource.Quantity,
                    resource.HoursPerMonth,
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
        var pricingSource = result.ProposedResources.Concat(result.BaselineResources).Any(resource => resource.PricingLiveApiUsed)
            ? "Azure Retail Prices API with local catalog fallback"
            : "local versioned MVP pricing catalog";
        yield return Assumption(scanId, "Region", region);
        yield return Assumption(scanId, "Pricing source", pricingSource);
        yield return Assumption(scanId, "Usage estimate", $"{result.ProposedResources.FirstOrDefault(resource => resource.HoursPerMonth > 0)?.HoursPerMonth ?? 730} hours/month for always-on resources");
        yield return Assumption(scanId, "Catalog version", "local MVP catalog");
        yield return Assumption(scanId, "Currency", result.Analysis.Currency);
        yield return Assumption(scanId, "Confidence", result.Analysis.OverallConfidence.ToString());
        if (!string.IsNullOrWhiteSpace(result.Analysis.BudgetSource))
        {
            yield return Assumption(scanId, "BudgetSource", result.Analysis.BudgetSource);
        }

        var pricedResources = result.ProposedResources
            .Concat(result.BaselineResources)
            .Where(resource => !string.IsNullOrWhiteSpace(resource.PricingCatalogVersion)
                || !string.IsNullOrWhiteSpace(resource.PricingSourceType)
                || !string.IsNullOrWhiteSpace(resource.PricingSource))
            .ToArray();
        var firstPricing = pricedResources.FirstOrDefault();
        if (firstPricing is not null)
        {
            yield return Assumption(scanId, "PricingCatalogName", firstPricing.PricingCatalogName ?? "unknown");
            yield return Assumption(scanId, "PricingCatalogVersion", firstPricing.PricingCatalogVersion ?? "unknown");
            yield return Assumption(scanId, "PricingSource", firstPricing.PricingSourceType ?? firstPricing.PricingSource ?? "unknown");
            yield return Assumption(scanId, "LiveApiUsed", firstPricing.PricingLiveApiUsed ? "true" : "false");
            yield return Assumption(scanId, "PricingFallbackUsed", firstPricing.PricingFallbackUsed ? "true" : "false");
            yield return Assumption(scanId, "PricingCurrency", result.Analysis.Currency);
            yield return Assumption(scanId, "PricingDefaultRegion", pricedResources.Select(resource => resource.Region).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? region);
            if (!string.IsNullOrWhiteSpace(firstPricing.PricingMeterName))
            {
                yield return Assumption(scanId, "PricingMeterName", firstPricing.PricingMeterName!);
            }

            if (!string.IsNullOrWhiteSpace(firstPricing.PricingProductName))
            {
                yield return Assumption(scanId, "PricingProductName", firstPricing.PricingProductName!);
            }

            if (!string.IsNullOrWhiteSpace(firstPricing.PricingUnitOfMeasure ?? firstPricing.PricingUnit))
            {
                yield return Assumption(scanId, "PricingUnitOfMeasure", firstPricing.PricingUnitOfMeasure ?? firstPricing.PricingUnit ?? "unknown");
            }

            foreach (var matchType in pricedResources.Select(resource => resource.PricingMatchType).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
            {
                yield return Assumption(scanId, "PricingMatchType", matchType!);
            }
        }

        foreach (var fallback in pricedResources.Where(resource => !string.IsNullOrWhiteSpace(resource.PricingFallbackReason)).Take(5))
        {
            yield return Assumption(scanId, $"Pricing fallback: {fallback.ResourceName}", fallback.PricingFallbackReason!);
        }

        foreach (var ai in result.ProposedResources.Where(resource => resource.ResourceType.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Assumption(scanId, "AIPricingCatalogName", ai.PricingCatalogName ?? "unknown");
            yield return Assumption(scanId, "AIPricingCatalogVersion", ai.PricingCatalogVersion ?? "unknown");
            yield return Assumption(scanId, "AIModel", ai.Sku ?? "unknown");
        }
        if (result.ProposedResources.Any(resource => resource.AnalysisSource == TerraformPlanJsonParser.AnalysisSource))
        {
            yield return Assumption(scanId, "AnalysisSource", TerraformPlanJsonParser.AnalysisSource);
            yield return Assumption(scanId, "TerraformPlanFormat", TerraformPlanJsonParser.TerraformPlanFormat);
            yield return Assumption(scanId, "MonthlyHours", $"{result.ProposedResources.FirstOrDefault(resource => resource.HoursPerMonth > 0)?.HoursPerMonth ?? 730}");
        }

        var armResources = result.ProposedResources
            .Where(resource => resource.AnalysisSource == ArmTemplateJsonParser.AnalysisSource)
            .ToArray();
        if (armResources.Length > 0)
        {
            yield return Assumption(scanId, "AnalysisSource", ArmTemplateJsonParser.AnalysisSource);
            yield return Assumption(scanId, "ArmTemplateFormat", ArmTemplateJsonParser.ArmTemplateFormat);
            yield return Assumption(scanId, "ArmTemplateDiffMode", ArmTemplateJsonParser.ArmTemplateDiffMode);
            yield return Assumption(scanId, "MonthlyHours", $"{armResources.FirstOrDefault(resource => resource.HoursPerMonth > 0)?.HoursPerMonth ?? 730}");
            foreach (var armAssumption in armResources.SelectMany(resource => BuildArmAnalysisAssumptions(scanId, resource)).Take(20))
            {
                yield return armAssumption;
            }
        }

        if (result.ProposedResources.Any(resource => resource.Warnings.Any(warning => warning.Contains("defaulted", StringComparison.OrdinalIgnoreCase))))
        {
            yield return Assumption(scanId, "RegionDefaulted", "true");
            yield return Assumption(scanId, "DefaultRegion", region);
        }

        if (result.ProposedResources.Any(resource => resource.Status is EstimateStatus.Unsupported or EstimateStatus.PriceNotFound or EstimateStatus.Unknown))
        {
            yield return Assumption(scanId, "UnknownResourceFallback", "true");
        }

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
        Value = TruncateAssumptionValue(value)
    };

    private static string TruncateAssumptionValue(string value)
    {
        if (value.Length <= MaxAssumptionValueLength)
        {
            return value;
        }

        var keep = Math.Max(0, MaxAssumptionValueLength - AssumptionTruncationSuffix.Length);
        return value[..keep] + AssumptionTruncationSuffix;
    }

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

    private static string BuildReason(ResourceCostChange change, ResourceEstimate? resource)
    {
        var baseReason = change.Reason ?? $"{change.BeforeSku ?? "none"} -> {change.AfterSku ?? "none"}";
        var pricing = resource?.PricingMatchType is null
            ? null
            : $" Pricing: {resource.PricingMatchType} via {resource.PricingCatalogVersion}.";
        var fallback = string.IsNullOrWhiteSpace(resource?.PricingFallbackReason)
            ? ""
            : $" Fallback: {resource.PricingFallbackReason}";
        return (baseReason + pricing + fallback).Trim();
    }

    private static IEnumerable<ScanAssumption> BuildArmAnalysisAssumptions(Guid scanId, ResourceEstimate resource)
    {
        if (string.IsNullOrWhiteSpace(resource.AssumptionsJson))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(resource.AssumptionsJson);
        if (!document.RootElement.TryGetProperty("Raw", out var raw) || raw.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var value in ReadStringArray(raw, "armParameterResolved").Take(5))
        {
            yield return Assumption(scanId, "ArmParameterResolved", value);
        }

        foreach (var value in ReadStringArray(raw, "armVariableResolved").Take(5))
        {
            yield return Assumption(scanId, "ArmVariableResolved", value);
        }

        if (raw.TryGetProperty("armExpressionUnresolved", out var unresolvedFlag)
            && unresolvedFlag.ValueKind == JsonValueKind.True)
        {
            yield return Assumption(scanId, "ArmExpressionUnresolved", "true");
        }

        if (raw.TryGetProperty("armUnresolvedExpressions", out var unresolved) && unresolved.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in unresolved.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Take(5))
            {
                var field = item.TryGetProperty("field", out var fieldValue) ? fieldValue.GetString() : "unknown";
                var expression = item.TryGetProperty("expression", out var expressionValue) ? expressionValue.GetString() : "unknown";
                yield return Assumption(scanId, "UnresolvedField", $"{resource.ResourceName}: {field}");
                yield return Assumption(scanId, "UnresolvedExpression", expression ?? "unknown");
            }
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonElement raw, string propertyName)
    {
        if (!raw.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                yield return value.GetString()!;
            }
        }
    }

    private static string ResourceKey(ResourceEstimate resource)
    {
        return $"{resource.Provider}:{resource.ResourceType}:{resource.ResourceName}";
    }
}
