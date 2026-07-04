using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpendGovernor.Core;

public sealed class PolicyEngine
{
    public IReadOnlyList<PolicyFinding> Evaluate(AnalysisResult result, PolicyConfig config)
    {
        var findings = new List<PolicyFinding>();
        var analysis = result.Analysis;

        foreach (var rule in config.Rules)
        {
            switch (rule.Type)
            {
                case "monthly_delta":
                    AddIfExceeded(findings, analysis.Id, rule, analysis.MonthlyDelta, "Monthly cost delta");
                    break;
                case "proposed_monthly_cost":
                    AddIfExceeded(findings, analysis.Id, rule, analysis.ProposedMonthlyCost, "Proposed monthly cost");
                    break;
                case "unknown_resource_count":
                    AddIfExceeded(findings, analysis.Id, rule, analysis.UnknownResourceCount, "Unknown resource count");
                    break;
                case "ai_monthly_cost":
                    var aiTotal = result.ProposedResources
                        .Where(resource => resource.Category == CostCategory.Ai)
                        .Sum(resource => resource.MonthlyCost ?? 0);
                    AddIfExceeded(findings, analysis.Id, rule, aiTotal, "AI monthly cost");
                    break;
                case "ai_workflow_cost":
                    foreach (var workflow in result.ProposedResources.Where(resource => resource.Category == CostCategory.Ai))
                    {
                        AddIfExceeded(findings, analysis.Id, rule, workflow.MonthlyCost, $"AI workflow {workflow.ResourceName}");
                    }
                    break;
            }
        }

        foreach (var environment in config.Environments)
        {
            var total = result.ProposedResources
                .Where(resource => resource.Environment?.Equals(environment.Key, StringComparison.OrdinalIgnoreCase) == true)
                .Sum(resource => resource.MonthlyCost ?? 0);
            if (total > environment.Value.MonthlyBudget)
            {
                findings.Add(new PolicyFinding
                {
                    AnalysisId = analysis.Id,
                    RuleId = $"environment-budget-{environment.Key}",
                    Action = environment.Value.Action,
                    ActualValue = total,
                    ThresholdValue = environment.Value.MonthlyBudget,
                    Message = $"{environment.Key} monthly estimate {FormatMoney(total, analysis.Currency)} exceeds budget {FormatMoney(environment.Value.MonthlyBudget, analysis.Currency)}."
                });
            }
        }

        if (config.Ai.Enabled)
        {
            var aiTotal = result.ProposedResources
                .Where(resource => resource.Category == CostCategory.Ai)
                .Sum(resource => resource.MonthlyCost ?? 0);
            if (aiTotal > config.Ai.MonthlyBudget)
            {
                findings.Add(new PolicyFinding
                {
                    AnalysisId = analysis.Id,
                    RuleId = "ai-monthly-budget",
                    Action = config.Ai.Action,
                    ActualValue = aiTotal,
                    ThresholdValue = config.Ai.MonthlyBudget,
                    Message = $"AI monthly estimate {FormatMoney(aiTotal, analysis.Currency)} exceeds budget {FormatMoney(config.Ai.MonthlyBudget, analysis.Currency)}."
                });
            }

            foreach (var workflow in result.ProposedResources.Where(resource => resource.Category == CostCategory.Ai && (resource.MonthlyCost ?? 0) > config.Ai.MaxCostPerWorkflowMonthly))
            {
                findings.Add(new PolicyFinding
                {
                    AnalysisId = analysis.Id,
                    RuleId = "ai-workflow-budget",
                    Action = config.Ai.Action,
                    ActualValue = workflow.MonthlyCost,
                    ThresholdValue = config.Ai.MaxCostPerWorkflowMonthly,
                    Message = $"AI workflow {workflow.ResourceName} estimate {FormatMoney(workflow.MonthlyCost ?? 0, analysis.Currency)} exceeds workflow budget {FormatMoney(config.Ai.MaxCostPerWorkflowMonthly, analysis.Currency)}."
                });
            }
        }

        return findings;
    }

    public static PolicyAction FinalAction(IEnumerable<PolicyFinding> findings)
    {
        return findings.Select(finding => finding.Action).DefaultIfEmpty(PolicyAction.Pass).MaxBy(Severity);
    }

    public static int Severity(PolicyAction action) => action switch
    {
        PolicyAction.Pass => 0,
        PolicyAction.Warn => 1,
        PolicyAction.ApprovalRequired => 2,
        PolicyAction.Block => 3,
        _ => 0
    };

    private static void AddIfExceeded(List<PolicyFinding> findings, Guid analysisId, PolicyRule rule, decimal? actual, string label)
    {
        if (actual is null || actual <= rule.Threshold)
        {
            return;
        }

        findings.Add(new PolicyFinding
        {
            AnalysisId = analysisId,
            RuleId = rule.Id,
            Action = rule.Action,
            ActualValue = actual,
            ThresholdValue = rule.Threshold,
            Message = $"{label} {actual.Value.ToString("0.##", CultureInfo.InvariantCulture)} exceeds threshold {rule.Threshold.ToString("0.##", CultureInfo.InvariantCulture)}."
        });
    }

    private static string FormatMoney(decimal amount, string currency)
    {
        return $"{CurrencySymbol(currency)}{amount:0.00}";
    }

    private static string CurrencySymbol(string currency) => currency.ToUpperInvariant() switch
    {
        "EUR" => "EUR ",
        "USD" => "USD ",
        _ => currency + " "
    };
}

public sealed class RecommendationEngine
{
    public IReadOnlyList<Recommendation> Generate(AnalysisResult result)
    {
        var recommendations = new List<Recommendation>();

        foreach (var resource in result.ProposedResources)
        {
            if (resource.Category is CostCategory.Compute or CostCategory.Container && !IsProduction(resource.Environment))
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "medium",
                    Title = "Add a shutdown schedule",
                    Description = $"Run {resource.ResourceName} only during working hours in non-production.",
                    EstimatedMonthlySavings = resource.MonthlyCost is null ? null : decimal.Round(resource.MonthlyCost.Value * 0.45m, 2)
                });
            }

            if (resource.Sku is not null
                && !IsProduction(resource.Environment)
                && (resource.Sku.Contains("D4", StringComparison.OrdinalIgnoreCase)
                    || resource.Sku.Contains("P1v3", StringComparison.OrdinalIgnoreCase)
                    || resource.Sku.Contains("Premium", StringComparison.OrdinalIgnoreCase)
                    || resource.Sku.Contains("Standard_C_", StringComparison.OrdinalIgnoreCase)))
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "high",
                    Title = "Use a smaller non-production SKU",
                    Description = $"{resource.ResourceName} uses {resource.Sku} in {resource.Environment ?? "a non-production environment"}; use B-series, Basic, or a lower Standard SKU before merge.",
                    EstimatedMonthlySavings = resource.MonthlyCost is null ? null : decimal.Round(resource.MonthlyCost.Value * 0.5m, 2)
                });
            }

            if (resource.Category == CostCategory.Container && resource.Quantity > 3)
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "medium",
                    Title = "Review Kubernetes capacity",
                    Description = $"{resource.ResourceName} is configured with {resource.Quantity} nodes or replicas; use autoscaling and lower minimum capacity for non-peak environments.",
                    EstimatedMonthlySavings = resource.MonthlyCost is null ? null : decimal.Round(resource.MonthlyCost.Value * 0.3m, 2)
                });
            }

            if (resource.Category == CostCategory.Storage && resource.Status != EstimateStatus.Estimated)
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "medium",
                    Title = "Add storage capacity and lifecycle assumptions",
                    Description = $"Add estimated GB/month and lifecycle policy details for {resource.ResourceName}."
                });
            }

            if (string.IsNullOrWhiteSpace(resource.Environment))
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "low",
                    Title = "Tag the environment",
                    Description = $"Add an environment tag to {resource.ResourceName} so budgets can be applied accurately."
                });
            }

            if (resource.Status is EstimateStatus.Unsupported or EstimateStatus.PriceNotFound or EstimateStatus.Unknown)
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "medium",
                    Title = "Complete pricing inputs",
                    Description = $"Add SKU, region, capacity, or catalog support for {resource.ResourceName}."
                });
            }

            if (resource.Category == CostCategory.Ai && resource.MonthlyCost is > 100)
            {
                recommendations.Add(new Recommendation
                {
                    AnalysisId = result.Analysis.Id,
                    ResourceEstimateId = resource.Id,
                    Severity = "high",
                    Title = "Reduce AI workflow unit cost",
                    Description = $"{resource.ResourceName} uses {resource.Sku}; use a cheaper model, reduce monthly requests, lower token limits, or cache repeated prompts before merge.",
                    EstimatedMonthlySavings = decimal.Round(resource.MonthlyCost.Value * 0.25m, 2)
                });
            }
        }

        if (result.Analysis.MonthlyDelta is > 100)
        {
            recommendations.Add(new Recommendation
            {
                AnalysisId = result.Analysis.Id,
                Severity = "medium",
                Title = "Reduce the PR monthly delta",
                Description = "The PR exceeds a review threshold; reduce SKU size, lower always-on capacity, or get an explicit approval before merge."
            });
        }

        foreach (var finding in result.PolicyFindings.Where(finding => finding.Action is PolicyAction.ApprovalRequired or PolicyAction.Block))
        {
            recommendations.Add(new Recommendation
            {
                AnalysisId = result.Analysis.Id,
                Severity = "high",
                Title = "Resolve the blocking budget finding",
                Description = $"{finding.RuleId} triggered: lower the changed resources until the estimate is below {finding.ThresholdValue:0.##}, or route the PR through approval if the increase is intentional."
            });
        }

        return recommendations
            .GroupBy(rec => (rec.Title, rec.ResourceEstimateId))
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsProduction(string? environment)
    {
        return environment?.Equals("production", StringComparison.OrdinalIgnoreCase) == true
            || environment?.Equals("prod", StringComparison.OrdinalIgnoreCase) == true;
    }
}

public static class PrCommentRenderer
{
    public const string Marker = "<!-- cloud-ai-spend-governor-report -->";

    public static string Render(AnalysisResult result)
    {
        var analysis = result.Analysis;
        if (analysis.Status == AnalysisStatus.Skipped)
        {
            return $"""
                   {Marker}
                   ## Cloud & AI Spend Governor Report

                   Status: PASS

                   No Terraform, Bicep, SpendGov policy, or AI spend files changed in this PR.

                   Confidence: High
                   """;
        }

        if (analysis.Status == AnalysisStatus.Failed)
        {
            var failedBuilder = new StringBuilder();
            failedBuilder.AppendLine(Marker);
            failedBuilder.AppendLine("## Cloud & AI Spend Governor Report");
            failedBuilder.AppendLine();
            failedBuilder.AppendLine("Status: WARN");
            failedBuilder.AppendLine();
            failedBuilder.AppendLine("Cloud & AI Spend Governor could not complete this scan. The MVP is configured to fail open for internal errors.");
            failedBuilder.AppendLine();
            failedBuilder.AppendLine("Detected issue:");
            failedBuilder.AppendLine($"- {Escape(analysis.ErrorMessage ?? "Unknown scan failure.")}");
            failedBuilder.AppendLine();
            failedBuilder.AppendLine("Recommended fix:");
            failedBuilder.AppendLine("- Check the scan input, repository files, and server logs, then rerun the analysis.");
            AppendDashboardLink(failedBuilder, result);
            return failedBuilder.ToString();
        }

        var builder = new StringBuilder();
        builder.AppendLine(Marker);
        builder.AppendLine("## Cloud & AI Spend Governor Report");
        builder.AppendLine();
        builder.AppendLine($"Status: {FormatDecision(analysis.PolicyStatus)}");
        builder.AppendLine();
        builder.AppendLine($"Estimated monthly impact: {FormatDelta(analysis.MonthlyDelta, analysis.Currency)}/month  ");
        builder.AppendLine($"Budget limit{FormatEnvironmentSuffix(analysis.Environment)}: {FormatMoney(analysis.BudgetLimitMonthly, analysis.Currency)}/month  ");
        builder.AppendLine($"Confidence: {FormatConfidence(analysis.OverallConfidence)}");
        builder.AppendLine();
        builder.AppendLine("| Metric | Value |");
        builder.AppendLine("|---|---:|");
        builder.AppendLine($"| Baseline monthly cost | {FormatMoney(analysis.BaselineMonthlyCost, analysis.Currency)} |");
        builder.AppendLine($"| Proposed monthly cost | {FormatMoney(analysis.ProposedMonthlyCost, analysis.Currency)} |");
        builder.AppendLine($"| Monthly delta | {FormatDelta(analysis.MonthlyDelta, analysis.Currency)} |");
        builder.AppendLine($"| Environment | {Escape(analysis.Environment ?? "not detected")} |");
        builder.AppendLine($"| Unknown resources | {analysis.UnknownResourceCount} |");
        builder.AppendLine($"| Final decision | {FormatDecision(analysis.PolicyStatus)} |");
        builder.AppendLine();

        if (result.CostChanges.Count > 0)
        {
            builder.AppendLine("### Main cost drivers");
            builder.AppendLine();
            builder.AppendLine("| Resource | Change | Estimated monthly impact |");
            builder.AppendLine("|---|---:|---:|");
            foreach (var change in result.CostChanges.OrderByDescending(change => Math.Abs(change.MonthlyDelta)).Take(10))
            {
                var skuChange = change.ChangeKind switch
                {
                    "added" => $"New {change.AfterSku ?? "resource"}",
                    "removed" => $"Removed {change.BeforeSku ?? "resource"}",
                    _ => $"{change.BeforeSku ?? "-"} -> {change.AfterSku ?? "-"}"
                };
                builder.AppendLine($"| {Escape(change.ResourceName)} | {Escape(skuChange)} | {FormatDelta(change.MonthlyDelta, analysis.Currency)} |");
            }

            builder.AppendLine();
        }

        if (result.ProposedResources.Count > 0)
        {
            builder.AppendLine("### Detected resources and workflows");
            builder.AppendLine();
            builder.AppendLine("| Resource/workflow | Type | SKU/model | Region | Monthly cost | Confidence |");
            builder.AppendLine("|---|---|---|---|---:|---|");
            foreach (var resource in result.ProposedResources.Take(12))
            {
                builder.AppendLine($"| {Escape(resource.ResourceName)} | {Escape(resource.ResourceType)} | {Escape(resource.Sku ?? "-")} | {Escape(resource.Region ?? "-")} | {FormatMoney(resource.MonthlyCost, analysis.Currency)} | {FormatConfidence(resource.Confidence)} |");
            }

            builder.AppendLine();
        }

        if (result.PolicyFindings.Count > 0)
        {
            builder.AppendLine("### Policy findings");
            builder.AppendLine();
            foreach (var finding in result.PolicyFindings.Take(10))
            {
                builder.AppendLine($"- `{Escape(finding.RuleId)}`: {Escape(finding.Message)} ({FormatDecision(finding.Action)})");
            }

            builder.AppendLine();
        }

        builder.AppendLine("### Assumptions");
        builder.AppendLine();
        foreach (var assumption in BuildAssumptions(result).Take(10))
        {
            builder.AppendLine($"- {Escape(assumption)}");
        }

        builder.AppendLine();

        builder.AppendLine("### Recommendation");
        builder.AppendLine();
        if (result.Recommendations.Count > 0)
        {
            foreach (var recommendation in result.Recommendations.Take(10))
            {
                var savings = recommendation.EstimatedMonthlySavings is null
                    ? ""
                    : $" Estimated impact: {FormatMoney(recommendation.EstimatedMonthlySavings, analysis.Currency)}.";
                builder.AppendLine($"- {Escape(recommendation.Title)}: {Escape(recommendation.Description)}{savings}");
            }
        }
        else
        {
            builder.AppendLine("- No blocking action recommended. Continue with normal review.");
        }

        var resourcesWithWarnings = result.ProposedResources.Where(resource => resource.Warnings.Count > 0).Take(8).ToArray();
        if (resourcesWithWarnings.Length > 0 || result.ConfigErrors.Count > 0)
        {
            builder.AppendLine("### Notes");
            builder.AppendLine();
            foreach (var error in result.ConfigErrors)
            {
                builder.AppendLine($"- Config: {Escape(error)}");
            }

            foreach (var resource in resourcesWithWarnings)
            {
                foreach (var warning in resource.Warnings)
                {
                    builder.AppendLine($"- {Escape(resource.ResourceName)}: {Escape(warning)}");
                }
            }
        }

        AppendDashboardLink(builder, result);

        return builder.ToString();
    }

    private static IEnumerable<string> BuildAssumptions(AnalysisResult result)
    {
        var analysis = result.Analysis;
        var region = result.ProposedResources.Select(resource => resource.Region).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        yield return $"Region: {region ?? "not detected"}";
        yield return "Pricing source: local MVP static pricing catalog";
        yield return $"Usage estimate: {result.ProposedResources.FirstOrDefault(resource => resource.HoursPerMonth > 0)?.HoursPerMonth ?? 730} hours/month for always-on resources";
        yield return "Catalog version: local MVP catalog";
        yield return $"Currency: {analysis.Currency}";
        yield return $"Confidence rule: {FormatConfidence(analysis.OverallConfidence)} based on detected resource type, SKU, region, and catalog price availability";
    }

    private static void AppendDashboardLink(StringBuilder builder, AnalysisResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.DashboardUrl))
        {
            builder.AppendLine();
            builder.AppendLine($"Dashboard report: {result.DashboardUrl}");
        }
    }

    private static string FormatAction(PolicyAction action) => action switch
    {
        PolicyAction.Pass => "Pass",
        PolicyAction.Warn => "Warn",
        PolicyAction.ApprovalRequired => "Approval required",
        PolicyAction.Block => "Block",
        _ => "Pass"
    };

    private static string FormatDecision(PolicyAction action) => action switch
    {
        PolicyAction.Pass => "PASS",
        PolicyAction.Warn => "WARN",
        PolicyAction.ApprovalRequired => "FAIL",
        PolicyAction.Block => "FAIL",
        _ => "PASS"
    };

    private static string FormatConfidence(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.High => "High",
        ConfidenceLevel.Medium => "Medium",
        ConfidenceLevel.Low => "Low",
        ConfidenceLevel.Unknown => "Low",
        _ => "Low"
    };

    private static string FormatMoney(decimal? amount, string currency)
    {
        return amount is null ? "not available" : $"{CurrencyPrefix(currency)}{amount.Value:0.00}";
    }

    private static string FormatDelta(decimal? amount, string currency)
    {
        if (amount is null)
        {
            return "not available";
        }

        var prefix = amount.Value >= 0 ? "+" : "-";
        return $"{prefix}{CurrencyPrefix(currency)}{Math.Abs(amount.Value):0.00}";
    }

    private static string CurrencyPrefix(string currency) => currency.ToUpperInvariant() switch
    {
        "EUR" => "EUR ",
        "USD" => "USD ",
        _ => currency + " "
    };

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string FormatEnvironmentSuffix(string? environment)
    {
        return string.IsNullOrWhiteSpace(environment) ? "" : $" for {environment}";
    }
}

public static class CsvExporter
{
    public static string Resources(AnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("analysisId,resourceName,resourceType,sourceFile,provider,region,sku,environment,category,monthlyCost,currency,monthlyDelta,status,confidence,priceSource");
        foreach (var resource in result.ProposedResources)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(result.Analysis.Id.ToString()),
                Csv(resource.ResourceName),
                Csv(resource.ResourceType),
                Csv(resource.SourceFile),
                Csv(resource.Provider),
                Csv(resource.Region),
                Csv(resource.Sku),
                Csv(resource.Environment),
                Csv(resource.Category.ToString()),
                Csv(resource.MonthlyCost?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(resource.Currency),
                Csv(resource.MonthlyDelta?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(resource.Status.ToString()),
                Csv(resource.Confidence.ToString()),
                Csv(resource.PriceSource)
            }));
        }

        return builder.ToString();
    }

    public static string PolicyFindings(AnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("analysisId,ruleId,action,message,actualValue,thresholdValue");
        foreach (var finding in result.PolicyFindings)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(result.Analysis.Id.ToString()),
                Csv(finding.RuleId),
                Csv(SpendGovConfigParser.ToConfigString(finding.Action)),
                Csv(finding.Message),
                Csv(finding.ActualValue?.ToString("0.##", CultureInfo.InvariantCulture)),
                Csv(finding.ThresholdValue?.ToString("0.##", CultureInfo.InvariantCulture))
            }));
        }

        return builder.ToString();
    }

    public static string Recommendations(AnalysisResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("analysisId,severity,title,description,estimatedMonthlySavings");
        foreach (var recommendation in result.Recommendations)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(result.Analysis.Id.ToString()),
                Csv(recommendation.Severity),
                Csv(recommendation.Title),
                Csv(recommendation.Description),
                Csv(recommendation.EstimatedMonthlySavings?.ToString("0.00", CultureInfo.InvariantCulture))
            }));
        }

        return builder.ToString();
    }

    public static string ProjectSummary(IEnumerable<AnalysisResult> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("analysisId,repository,pr,branch,environment,commit,status,policyStatus,confidence,baselineMonthlyCost,proposedMonthlyCost,monthlyDelta,budgetLimit,currency,unknownResourceCount,startedAt,completedAt,failureReason");
        foreach (var result in results)
        {
            var analysis = result.Analysis;
            builder.AppendLine(string.Join(',', new[]
            {
                Csv(analysis.Id.ToString()),
                Csv($"{analysis.RepositoryOwner}/{analysis.RepositoryName}"),
                Csv(analysis.PullRequestNumber.ToString(CultureInfo.InvariantCulture)),
                Csv(analysis.HeadBranch),
                Csv(analysis.Environment),
                Csv(analysis.CommitSha),
                Csv(analysis.Status.ToString()),
                Csv(SpendGovConfigParser.ToConfigString(analysis.PolicyStatus)),
                Csv(analysis.OverallConfidence.ToString()),
                Csv(analysis.BaselineMonthlyCost?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(analysis.ProposedMonthlyCost?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(analysis.MonthlyDelta?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(analysis.BudgetLimitMonthly?.ToString("0.00", CultureInfo.InvariantCulture)),
                Csv(analysis.Currency),
                Csv(analysis.UnknownResourceCount.ToString(CultureInfo.InvariantCulture)),
                Csv(analysis.StartedAt?.ToString("O", CultureInfo.InvariantCulture)),
                Csv(analysis.CompletedAt?.ToString("O", CultureInfo.InvariantCulture)),
                Csv(analysis.ErrorMessage)
            }));
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

public static class GitHubSignatureVerifier
{
    public static bool VerifySha256(string payload, string signatureHeader, string secret)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(signatureHeader["sha256=".Length..]);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}
