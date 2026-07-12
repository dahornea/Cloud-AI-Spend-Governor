namespace SpendGovernor.Core;

public sealed class SpendPolicyEvaluator
{
    public IReadOnlyList<SpendPolicyEvaluation> Evaluate(
        AnalysisResult result,
        PolicyConfig config,
        IReadOnlyList<PolicyFinding> budgetFindings)
    {
        if (config.Policies.Count == 0)
        {
            return [];
        }

        return config.Policies
            .Where(policy => policy.Enabled)
            .Select(policy => EvaluatePolicy(policy, result, config, budgetFindings))
            .ToArray();
    }

    private static SpendPolicyEvaluation EvaluatePolicy(
        SpendPolicy policy,
        AnalysisResult result,
        PolicyConfig config,
        IReadOnlyList<PolicyFinding> budgetFindings)
    {
        var candidates = result.ProposedResources
            .Where(resource => MatchesPolicyEnvironment(policy, resource, result.Analysis, config))
            .Where(resource => !policy.Match.HasAny || MatchesResource(policy.Match, resource))
            .Where(resource => MatchesCondition(policy.Condition, resource, result, budgetFindings))
            .ToArray();

        var matched = candidates.Length > 0;
        if (!policy.Match.HasAny && policy.Condition.HasAny)
        {
            matched = MatchesScanCondition(policy.Condition, result, budgetFindings)
                && MatchesScanEnvironment(policy, result.Analysis, config);
        }

        return new SpendPolicyEvaluation
        {
            PolicyId = policy.Id,
            Title = policy.Title,
            Description = policy.Description,
            Severity = policy.Severity,
            Matched = matched,
            Result = matched ? ToResult(policy.Severity) : SpendPolicyEvaluationStatus.NotMatched,
            MatchedResource = matched && candidates.Length > 0
                ? string.Join(", ", candidates.Select(resource => resource.ResourceName).Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
                : null,
            MatchedResourceType = matched && candidates.Length > 0
                ? string.Join(", ", candidates.Select(resource => resource.ResourceType).Distinct(StringComparer.OrdinalIgnoreCase).Take(5))
                : null,
            Message = policy.Message,
            Recommendation = policy.Recommendation
        };
    }

    private static bool MatchesResource(PolicyMatch match, ResourceEstimate resource)
    {
        return MatchesType(match.Type, resource)
            && MatchesText(match.Provider, resource.Provider, normalizeAzure: true)
            && MatchesResourceType(match.ResourceType, resource)
            && MatchesText(match.ResourceName, resource.ResourceName)
            && ContainsText(resource.ResourceName, match.ResourceNameContains)
            && MatchesText(match.Sku, resource.Sku)
            && ContainsText(resource.Sku, match.SkuContains)
            && ContainsAny(resource.Sku, match.SkuContainsAny)
            && MatchesText(match.Region, resource.Region)
            && MatchesText(match.Environment, resource.Environment)
            && MatchesText(match.AnalysisSource, resource.AnalysisSource)
            && MatchesText(match.Model, resource.Sku)
            && ContainsExact(resource.Sku, match.ModelIn);
    }

    private static bool MatchesCondition(
        PolicyCondition condition,
        ResourceEstimate resource,
        AnalysisResult result,
        IReadOnlyList<PolicyFinding> budgetFindings)
    {
        if (!condition.HasAny)
        {
            return true;
        }

        return GreaterThan(resource.MonthlyCost, condition.EstimatedMonthlyCostGreaterThan)
            && GreaterThan(resource.MonthlyDelta ?? result.Analysis.MonthlyDelta, condition.EstimatedMonthlyDeltaGreaterThan)
            && ConfidenceBelow(resource.Confidence, condition.ConfidenceBelow)
            && BoolMatches(HasBudgetExceeded(result, budgetFindings), condition.BudgetExceeded)
            && BoolMatches(resource.PricingFallbackUsed, condition.PricingFallbackUsed)
            && BoolMatches(string.IsNullOrWhiteSpace(resource.Region), condition.RegionMissing)
            && BoolMatches(string.IsNullOrWhiteSpace(resource.Sku), condition.SkuMissing)
            && BoolMatches(IsFallbackAnalysisSource(resource), condition.AnalysisSourceIsFallback);
    }

    private static bool MatchesScanCondition(
        PolicyCondition condition,
        AnalysisResult result,
        IReadOnlyList<PolicyFinding> budgetFindings)
    {
        return GreaterThan(result.Analysis.ProposedMonthlyCost, condition.EstimatedMonthlyCostGreaterThan)
            && GreaterThan(result.Analysis.MonthlyDelta, condition.EstimatedMonthlyDeltaGreaterThan)
            && ConfidenceBelow(result.Analysis.OverallConfidence, condition.ConfidenceBelow)
            && BoolMatches(HasBudgetExceeded(result, budgetFindings), condition.BudgetExceeded)
            && BoolMatches(result.ProposedResources.Any(resource => resource.PricingFallbackUsed), condition.PricingFallbackUsed)
            && BoolMatches(result.ProposedResources.Any(resource => string.IsNullOrWhiteSpace(resource.Region)), condition.RegionMissing)
            && BoolMatches(result.ProposedResources.Any(resource => string.IsNullOrWhiteSpace(resource.Sku)), condition.SkuMissing)
            && BoolMatches(result.ProposedResources.Any(IsFallbackAnalysisSource), condition.AnalysisSourceIsFallback);
    }

    private static bool MatchesPolicyEnvironment(SpendPolicy policy, ResourceEstimate resource, PullRequestAnalysis analysis, PolicyConfig config)
    {
        if (policy.Environments.Count == 0)
        {
            return true;
        }

        var environment = resource.Environment ?? analysis.Environment ?? config.Environment;
        return !string.IsNullOrWhiteSpace(environment)
            && policy.Environments.Any(candidate => candidate.Equals(environment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesScanEnvironment(SpendPolicy policy, PullRequestAnalysis analysis, PolicyConfig config)
    {
        if (policy.Environments.Count == 0)
        {
            return true;
        }

        var environment = analysis.Environment ?? config.Environment;
        return !string.IsNullOrWhiteSpace(environment)
            && policy.Environments.Any(candidate => candidate.Equals(environment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesType(string? expected, ResourceEstimate resource)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (expected.Equals("aiWorkflow", StringComparison.OrdinalIgnoreCase)
            || expected.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase))
        {
            return resource.ResourceType.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase)
                || resource.Category == CostCategory.Ai;
        }

        return resource.ResourceType.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || (resource.SourceType.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesResourceType(string? expected, ResourceEstimate resource)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return resource.ResourceType.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || (resource.MappedResourceType?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true)
            || (resource.ArmResourceType?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool MatchesText(string? expected, string? actual, bool normalizeAzure = false)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        if (normalizeAzure && expected.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            return actual.Equals("azure", StringComparison.OrdinalIgnoreCase)
                || actual.Equals("Azure", StringComparison.OrdinalIgnoreCase);
        }

        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(string? actual, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected)
            || (!string.IsNullOrWhiteSpace(actual) && actual.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(string? actual, IReadOnlyList<string> expected)
    {
        return expected.Count == 0
            || (!string.IsNullOrWhiteSpace(actual) && expected.Any(value => actual.Contains(value, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsExact(string? actual, IReadOnlyList<string> expected)
    {
        return expected.Count == 0
            || (!string.IsNullOrWhiteSpace(actual) && expected.Any(value => actual.Equals(value, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool GreaterThan(decimal? actual, decimal? threshold)
    {
        return threshold is null || actual > threshold;
    }

    private static bool BoolMatches(bool actual, bool? expected)
    {
        return expected is null || actual == expected.Value;
    }

    private static bool ConfidenceBelow(ConfidenceLevel actual, ConfidenceLevel? threshold)
    {
        return threshold is null || Rank(actual) < Rank(threshold.Value);
    }

    private static int Rank(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Unknown => 0,
        ConfidenceLevel.Low => 1,
        ConfidenceLevel.Medium => 2,
        ConfidenceLevel.High => 3,
        _ => 0
    };

    private static bool HasBudgetExceeded(AnalysisResult result, IReadOnlyList<PolicyFinding> budgetFindings)
    {
        return budgetFindings.Any(finding => finding.Action is PolicyAction.Warn or PolicyAction.ApprovalRequired or PolicyAction.Block)
            || result.Analysis.BudgetLimitMonthly is not null && result.Analysis.MonthlyDelta > result.Analysis.BudgetLimitMonthly;
    }

    private static bool IsFallbackAnalysisSource(ResourceEstimate resource)
    {
        return string.IsNullOrWhiteSpace(resource.AnalysisSource)
            && resource.SourceType is ResourceSourceType.Terraform or ResourceSourceType.Bicep;
    }

    private static SpendPolicyEvaluationStatus ToResult(SpendPolicySeverity severity) => severity switch
    {
        SpendPolicySeverity.Fail => SpendPolicyEvaluationStatus.Fail,
        SpendPolicySeverity.Warn => SpendPolicyEvaluationStatus.Warn,
        _ => SpendPolicyEvaluationStatus.Info
    };
}
