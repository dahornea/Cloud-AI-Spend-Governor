using System.Text.RegularExpressions;

namespace SpendGovernor.Core;

public sealed class SpendFindingGenerator
{
    private const decimal DefaultHighMonthlyImpactThreshold = 100m;

    public IReadOnlyList<SpendFinding> Generate(AnalysisResult result)
    {
        if (result.Analysis.Status is AnalysisStatus.Skipped)
        {
            return [];
        }

        var findings = new List<SpendFinding>();
        var policyAsCodeIds = result.PolicyAsCodeEvaluations
            .Select(evaluation => evaluation.PolicyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var error in result.ConfigErrors)
        {
            findings.Add(new SpendFinding
            {
                Id = StableId("spendgov.config.validation", ".spendgov.yml", error),
                RuleId = "spendgov.config.validation",
                Title = "SpendGov configuration warning",
                Message = Sanitize(error),
                Recommendation = "Fix the .spendgov.yml validation issue and rerun the scan.",
                Severity = SpendFindingSeverity.Warning,
                Category = "config",
                SourceFile = ".spendgov.yml",
                StartLine = 1,
                StartColumn = 1,
                Currency = result.Analysis.Currency,
                Environment = result.Analysis.Environment,
                ConfidenceLevel = result.Analysis.OverallConfidence
            });
        }

        foreach (var evaluation in result.PolicyAsCodeEvaluations.Where(evaluation => evaluation.Matched))
        {
            var resource = MatchPolicyResource(evaluation, result.ProposedResources) ?? MainResource(result);
            findings.Add(FromPolicyEvaluation(result, evaluation, resource));
        }

        foreach (var finding in result.PolicyFindings.Where(finding => !policyAsCodeIds.Contains(finding.RuleId)))
        {
            var resource = MainResource(result);
            findings.Add(FromPolicyFinding(result, finding, resource));
        }

        var highImpactThreshold = result.Analysis.BudgetLimitMonthly is > 0
            ? Math.Min(DefaultHighMonthlyImpactThreshold, result.Analysis.BudgetLimitMonthly.Value)
            : DefaultHighMonthlyImpactThreshold;

        foreach (var resource in result.ProposedResources)
        {
            var impact = Math.Abs(resource.MonthlyDelta ?? resource.MonthlyCost ?? 0);
            if (impact >= highImpactThreshold)
            {
                findings.Add(new SpendFinding
                {
                    Id = StableId("spendgov.cost.highMonthlyImpact", resource.SourceFile, resource.ResourceName),
                    RuleId = "spendgov.cost.highMonthlyImpact",
                    Title = "High monthly spend impact",
                    Message = $"{DisplayName(resource)} may add {FormatDelta(resource.MonthlyDelta ?? resource.MonthlyCost, result.Analysis.Currency)} per month.",
                    Recommendation = "Review whether this capacity or model choice is intentional before merging.",
                    Severity = result.Analysis.PolicyStatus is PolicyAction.Block or PolicyAction.ApprovalRequired
                        ? SpendFindingSeverity.Error
                        : SpendFindingSeverity.Warning,
                    Category = resource.Category == CostCategory.Ai ? "ai-workflow" : "resource",
                    SourceFile = NullIfEmpty(resource.SourceFile),
                    StartLine = StartLine(resource),
                    StartColumn = StartColumn(resource),
                    ResourceName = resource.ResourceName,
                    ResourceType = resource.ResourceType,
                    Provider = resource.Provider,
                    Environment = resource.Environment ?? result.Analysis.Environment,
                    EstimatedMonthlyCost = resource.MonthlyCost,
                    EstimatedMonthlyDelta = resource.MonthlyDelta,
                    Currency = resource.Currency,
                    ConfidenceLevel = resource.Confidence,
                    PricingSource = resource.PricingSource ?? resource.PriceSource,
                    PricingMatchType = resource.PricingMatchType
                });
            }

            if (resource.Confidence is ConfidenceLevel.Low or ConfidenceLevel.Unknown)
            {
                findings.Add(new SpendFinding
                {
                    Id = StableId("spendgov.confidence.low", resource.SourceFile, resource.ResourceName),
                    RuleId = "spendgov.confidence.low",
                    Title = "Low confidence cost estimate",
                    Message = $"{DisplayName(resource)} has {resource.Confidence} confidence; source metadata or pricing fields are incomplete.",
                    Recommendation = "Use Terraform Plan JSON, compiled ARM JSON, or explicit SKU/region metadata for a higher-confidence estimate.",
                    Severity = SpendFindingSeverity.Warning,
                    Category = "confidence",
                    SourceFile = NullIfEmpty(resource.SourceFile),
                    StartLine = StartLine(resource),
                    StartColumn = StartColumn(resource),
                    ResourceName = resource.ResourceName,
                    ResourceType = resource.ResourceType,
                    Provider = resource.Provider,
                    Environment = resource.Environment ?? result.Analysis.Environment,
                    EstimatedMonthlyCost = resource.MonthlyCost,
                    EstimatedMonthlyDelta = resource.MonthlyDelta,
                    Currency = resource.Currency,
                    ConfidenceLevel = resource.Confidence,
                    PricingSource = resource.PricingSource ?? resource.PriceSource,
                    PricingMatchType = resource.PricingMatchType
                });
            }

            if (resource.PricingFallbackUsed)
            {
                findings.Add(new SpendFinding
                {
                    Id = StableId("spendgov.pricing.fallback", resource.SourceFile, resource.ResourceName),
                    RuleId = "spendgov.pricing.fallback",
                    Title = "Pricing fallback used",
                    Message = $"{DisplayName(resource)} used pricing fallback{FormatReason(resource.PricingFallbackReason)}.",
                    Recommendation = "Add a more specific SKU, region, or pricing catalog entry.",
                    Severity = resource.Confidence == ConfidenceLevel.Low ? SpendFindingSeverity.Warning : SpendFindingSeverity.Note,
                    Category = "pricing",
                    SourceFile = NullIfEmpty(resource.SourceFile),
                    StartLine = StartLine(resource),
                    StartColumn = StartColumn(resource),
                    ResourceName = resource.ResourceName,
                    ResourceType = resource.ResourceType,
                    Provider = resource.Provider,
                    Environment = resource.Environment ?? result.Analysis.Environment,
                    EstimatedMonthlyCost = resource.MonthlyCost,
                    EstimatedMonthlyDelta = resource.MonthlyDelta,
                    Currency = resource.Currency,
                    ConfidenceLevel = resource.Confidence,
                    PricingSource = resource.PricingSource ?? resource.PriceSource,
                    PricingMatchType = resource.PricingMatchType
                });
            }

            if (resource.Status is EstimateStatus.PriceNotFound or EstimateStatus.Unknown or EstimateStatus.Unsupported || resource.MonthlyCost is null)
            {
                findings.Add(new SpendFinding
                {
                    Id = StableId("spendgov.pricing.unknown", resource.SourceFile, resource.ResourceName),
                    RuleId = "spendgov.pricing.unknown",
                    Title = "Unknown pricing",
                    Message = $"{DisplayName(resource)} could not be priced reliably.",
                    Recommendation = "Confirm the resource type, SKU, region, and catalog coverage.",
                    Severity = SpendFindingSeverity.Warning,
                    Category = "pricing",
                    SourceFile = NullIfEmpty(resource.SourceFile),
                    StartLine = StartLine(resource),
                    StartColumn = StartColumn(resource),
                    ResourceName = resource.ResourceName,
                    ResourceType = resource.ResourceType,
                    Provider = resource.Provider,
                    Environment = resource.Environment ?? result.Analysis.Environment,
                    EstimatedMonthlyCost = resource.MonthlyCost,
                    EstimatedMonthlyDelta = resource.MonthlyDelta,
                    Currency = resource.Currency,
                    ConfidenceLevel = resource.Confidence,
                    PricingSource = resource.PricingSource ?? resource.PriceSource,
                    PricingMatchType = resource.PricingMatchType
                });
            }
        }

        return findings
            .GroupBy(finding => $"{finding.RuleId}|{finding.SourceFile}|{finding.StartLine}|{finding.ResourceName}|{finding.Message}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(finding => Rank(finding.Severity))
            .ThenBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SpendFinding FromPolicyEvaluation(AnalysisResult result, SpendPolicyEvaluation evaluation, ResourceEstimate? resource)
    {
        return new SpendFinding
        {
            Id = StableId($"spendgov.policy.{NormalizeSegment(evaluation.PolicyId)}", resource?.SourceFile, resource?.ResourceName ?? evaluation.PolicyId),
            RuleId = $"spendgov.policy.{NormalizeSegment(evaluation.PolicyId)}",
            Title = string.IsNullOrWhiteSpace(evaluation.Title) ? $"Policy {evaluation.PolicyId}" : evaluation.Title,
            Message = Sanitize(evaluation.Message),
            Recommendation = Sanitize(evaluation.Recommendation),
            Severity = evaluation.Severity switch
            {
                SpendPolicySeverity.Fail => SpendFindingSeverity.Error,
                SpendPolicySeverity.Warn => SpendFindingSeverity.Warning,
                _ => SpendFindingSeverity.Note
            },
            Category = "policy",
            SourceFile = NullIfEmpty(resource?.SourceFile),
            StartLine = StartLine(resource),
            StartColumn = StartColumn(resource),
            ResourceName = resource?.ResourceName ?? evaluation.MatchedResource,
            ResourceType = resource?.ResourceType ?? evaluation.MatchedResourceType,
            Provider = resource?.Provider,
            Environment = resource?.Environment ?? result.Analysis.Environment,
            EstimatedMonthlyCost = resource?.MonthlyCost,
            EstimatedMonthlyDelta = resource?.MonthlyDelta,
            Currency = resource?.Currency ?? result.Analysis.Currency,
            ConfidenceLevel = resource?.Confidence ?? result.Analysis.OverallConfidence,
            PolicyId = evaluation.PolicyId,
            PricingSource = resource?.PricingSource ?? resource?.PriceSource,
            PricingMatchType = resource?.PricingMatchType
        };
    }

    private static SpendFinding FromPolicyFinding(AnalysisResult result, PolicyFinding finding, ResourceEstimate? resource)
    {
        var category = IsBudgetFinding(finding) ? "budget" : "policy";
        return new SpendFinding
        {
            Id = StableId(NormalizePolicyFindingRule(finding), resource?.SourceFile, finding.RuleId),
            RuleId = NormalizePolicyFindingRule(finding),
            Title = category == "budget" ? "Budget threshold exceeded" : "Policy finding",
            Message = Sanitize(finding.Message),
            Recommendation = category == "budget"
                ? "Reduce the monthly delta or request approval before merging."
                : "Review the policy finding before merging.",
            Severity = finding.Action switch
            {
                PolicyAction.Block or PolicyAction.ApprovalRequired => SpendFindingSeverity.Error,
                PolicyAction.Warn => SpendFindingSeverity.Warning,
                _ => SpendFindingSeverity.Note
            },
            Category = category,
            SourceFile = NullIfEmpty(resource?.SourceFile),
            StartLine = StartLine(resource),
            StartColumn = StartColumn(resource),
            ResourceName = resource?.ResourceName,
            ResourceType = resource?.ResourceType,
            Provider = resource?.Provider,
            Environment = resource?.Environment ?? result.Analysis.Environment,
            EstimatedMonthlyCost = resource?.MonthlyCost ?? result.Analysis.ProposedMonthlyCost,
            EstimatedMonthlyDelta = resource?.MonthlyDelta ?? finding.ActualValue ?? result.Analysis.MonthlyDelta,
            Currency = resource?.Currency ?? result.Analysis.Currency,
            ConfidenceLevel = resource?.Confidence ?? result.Analysis.OverallConfidence,
            PricingSource = resource?.PricingSource ?? resource?.PriceSource,
            PricingMatchType = resource?.PricingMatchType
        };
    }

    private static ResourceEstimate? MatchPolicyResource(SpendPolicyEvaluation evaluation, IReadOnlyList<ResourceEstimate> resources)
    {
        var matched = evaluation.MatchedResource?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(matched))
        {
            return null;
        }

        return resources.FirstOrDefault(resource => resource.ResourceName.Equals(matched, StringComparison.OrdinalIgnoreCase))
            ?? resources.FirstOrDefault(resource => resource.ResourceType.Equals(evaluation.MatchedResourceType ?? "", StringComparison.OrdinalIgnoreCase));
    }

    private static ResourceEstimate? MainResource(AnalysisResult result)
    {
        return result.ProposedResources
            .OrderByDescending(resource => Math.Abs(resource.MonthlyDelta ?? resource.MonthlyCost ?? 0))
            .FirstOrDefault();
    }

    private static bool IsBudgetFinding(PolicyFinding finding)
    {
        return finding.RuleId.Contains("budget", StringComparison.OrdinalIgnoreCase)
            || finding.RuleId.Contains("delta", StringComparison.OrdinalIgnoreCase)
            || finding.RuleId.Contains("approval", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePolicyFindingRule(PolicyFinding finding)
    {
        if (finding.RuleId.Contains("cost", StringComparison.OrdinalIgnoreCase)
            || finding.RuleId.Contains("budget", StringComparison.OrdinalIgnoreCase))
        {
            return "spendgov.budget.maxMonthlyCost";
        }

        if (finding.RuleId.Contains("delta", StringComparison.OrdinalIgnoreCase)
            || finding.RuleId.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return "spendgov.budget.maxMonthlyDelta";
        }

        return $"spendgov.policyFinding.{NormalizeSegment(finding.RuleId)}";
    }

    private static string NormalizeSegment(string value)
    {
        var normalized = Regex.Replace(value.Trim(), "[^A-Za-z0-9_.-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string StableId(string ruleId, string? sourceFile, string discriminator)
    {
        return $"{ruleId}:{sourceFile ?? "scan"}:{NormalizeSegment(discriminator)}";
    }

    private static int? StartLine(ResourceEstimate? resource)
    {
        return string.IsNullOrWhiteSpace(resource?.SourceFile) ? null : 1;
    }

    private static int? StartColumn(ResourceEstimate? resource)
    {
        return string.IsNullOrWhiteSpace(resource?.SourceFile) ? null : 1;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string DisplayName(ResourceEstimate resource)
    {
        return string.IsNullOrWhiteSpace(resource.ResourceName) ? resource.ResourceType : resource.ResourceName;
    }

    private static string FormatReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? "" : $": {Sanitize(reason)}";
    }

    private static string FormatDelta(decimal? amount, string currency)
    {
        return amount is null ? "an unknown amount" : $"{amount.Value:+0.##;-0.##;0} {currency}";
    }

    private static int Rank(SpendFindingSeverity severity) => severity switch
    {
        SpendFindingSeverity.Error => 3,
        SpendFindingSeverity.Warning => 2,
        _ => 1
    };

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return Regex.Replace(
            value,
            "(password|secret|token|key|connectionstring)\\s*[:=]\\s*[^\\s,;]+",
            "$1=***",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
