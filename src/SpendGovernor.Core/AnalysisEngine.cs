using System.Text.Json;

namespace SpendGovernor.Core;

public sealed class AnalysisEngine
{
    private readonly TerraformParser terraformParser = new();
    private readonly BicepParser bicepParser = new();
    private readonly AiSpendParser aiSpendParser = new();
    private readonly MonthlyCostEstimator estimator;
    private readonly PolicyEngine policyEngine = new();
    private readonly RecommendationEngine recommendationEngine = new();

    public AnalysisEngine(MonthlyCostEstimator estimator)
    {
        this.estimator = estimator;
    }

    public AnalysisResult Analyze(AnalysisRequest request)
    {
        var analysis = new PullRequestAnalysis
        {
            ProjectId = request.ProjectId,
            RepositoryOwner = request.RepositoryOwner,
            RepositoryName = request.RepositoryName,
            PullRequestNumber = request.PullRequestNumber,
            BaseBranch = request.BaseBranch,
            HeadBranch = request.HeadBranch,
            CommitSha = request.CommitSha,
            Status = AnalysisStatus.Running,
            Currency = request.Settings.Currency,
            StartedAt = DateTimeOffset.UtcNow,
            GitHubPullRequestUrl = BuildGitHubPullRequestUrl(request.RepositoryOwner, request.RepositoryName, request.PullRequestNumber)
        };

        var detectedChangedFiles = FileDiscovery.DetectMany(request.ChangedFiles);
        var result = new AnalysisResult
        {
            Analysis = analysis,
            AuditEvents =
            [
                Audit(analysis, "PR analysis queued", "Pull request analysis was queued."),
                Audit(analysis, "PR analysis started", "Pull request analysis started.")
            ]
        };

        if (detectedChangedFiles.Count == 0)
        {
            analysis.Status = AnalysisStatus.Skipped;
            analysis.PolicyStatus = PolicyAction.Pass;
            analysis.OverallConfidence = ConfidenceLevel.High;
            analysis.CompletedAt = DateTimeOffset.UtcNow;
            result.CommentMarkdown = PrCommentRenderer.Render(result);
            result.CheckConclusion = "success";
            result.AuditEvents = result.AuditEvents
                .Append(Audit(analysis, "PR analysis skipped", "No cost-relevant files changed."))
                .ToArray();
            return result;
        }

        try
        {
            var proposedConfigYaml = FindSpendGovYaml(request.ProposedFiles) ?? request.Settings.PolicyYaml;
            var configResult = SpendGovConfigParser.Parse(proposedConfigYaml, request.Settings);
            var config = configResult.Config;
            analysis.Currency = config.Currency;

            var effectiveSettings = new ProjectSettings
            {
                Provider = request.Settings.Provider,
                Currency = config.Currency,
                DefaultRegion = config.DefaultRegion,
                HoursPerMonth = config.HoursPerMonth,
                PolicyYaml = proposedConfigYaml ?? PolicyConfig.DefaultYaml
            };

            var baselineResources = AnalyzeRepositoryFiles(request.BaselineFiles, analysis.Id, effectiveSettings, request.BaseBranch);
            var proposedResources = AnalyzeRepositoryFiles(request.ProposedFiles, analysis.Id, effectiveSettings, request.HeadBranch);
            var changes = CalculateChanges(baselineResources, proposedResources);
            var validationErrors = configResult.Errors
                .Concat(aiSpendParser.Validate(aiSpendParser.Parse(request.ProposedFiles)))
                .ToArray();

            var baselineCost = baselineResources.Sum(resource => resource.MonthlyCost ?? 0);
            var proposedCost = proposedResources.Sum(resource => resource.MonthlyCost ?? 0);
            analysis.BaselineMonthlyCost = decimal.Round(baselineCost, 2);
            analysis.ProposedMonthlyCost = decimal.Round(proposedCost, 2);
            analysis.MonthlyDelta = decimal.Round(proposedCost - baselineCost, 2);
            analysis.UnknownResourceCount = proposedResources.Count(resource => resource.Status != EstimateStatus.Estimated || resource.MonthlyCost is null);
            analysis.Environment = DetermineEnvironment(proposedResources, request.HeadBranch);
            analysis.OverallConfidence = DetermineOverallConfidence(proposedResources);
            analysis.BudgetLimitMonthly = DetermineBudgetLimit(config, analysis.Environment);
            analysis.Status = AnalysisStatus.Completed;
            analysis.CompletedAt = DateTimeOffset.UtcNow;
            analysis.DashboardUrl = request.DashboardBaseUrl is null ? null : $"{request.DashboardBaseUrl.TrimEnd('/')}/?analysisId={analysis.Id}";

            result = new AnalysisResult
            {
                Analysis = analysis,
                BaselineResources = baselineResources,
                ProposedResources = proposedResources,
                CostChanges = changes,
                ConfigErrors = validationErrors,
                DashboardUrl = analysis.DashboardUrl,
                AuditEvents =
                [
                    Audit(analysis, "PR analysis queued", "Pull request analysis was queued."),
                    Audit(analysis, "PR analysis completed", "Pull request analysis completed.", new
                    {
                        analysis.BaselineMonthlyCost,
                        analysis.ProposedMonthlyCost,
                        analysis.MonthlyDelta,
                        analysis.UnknownResourceCount
                    })
                ]
            };

            var findings = policyEngine.Evaluate(result, config);
            if (validationErrors.Length > 0)
            {
                findings = findings
                    .Append(new PolicyFinding
                    {
                        AnalysisId = analysis.Id,
                        RuleId = "spendgov-config-validation",
                        Action = PolicyAction.Warn,
                        Message = "The .spendgov.yml file has validation warnings; see notes in the PR comment."
                    })
                    .ToArray();
            }

            analysis.PolicyStatus = PolicyEngine.FinalAction(findings);
            analysis.BudgetLimitMonthly = DetermineBudgetLimit(config, analysis.Environment, findings);
            result.PolicyFindings = findings;
            var recommendations = recommendationEngine.Generate(result);
            foreach (var change in changes)
            {
                var resource = proposedResources.FirstOrDefault(item => ResourceKey(item) == change.ResourceKey);
                if (resource is not null)
                {
                    resource.MonthlyDelta = change.MonthlyDelta;
                }
            }

            result.Recommendations = recommendations;
            result.CommentMarkdown = PrCommentRenderer.Render(result);
            result.CheckConclusion = analysis.PolicyStatus switch
            {
                PolicyAction.Block => "failure",
                PolicyAction.ApprovalRequired => "failure",
                PolicyAction.Warn => "neutral",
                _ => "success"
            };
            result.AuditEvents = result.AuditEvents
                .Concat(findings.Select(finding => Audit(analysis, "Policy triggered", finding.Message, new { finding.RuleId, finding.Action })))
                .ToArray();

            return result;
        }
        catch (Exception ex)
        {
            analysis.Status = AnalysisStatus.Failed;
            analysis.ErrorMessage = ex.Message;
            analysis.InternalStackTrace = ex.ToString();
            analysis.CompletedAt = DateTimeOffset.UtcNow;
            analysis.OverallConfidence = ConfidenceLevel.Unknown;
            analysis.PolicyStatus = PolicyAction.Pass;
            analysis.DashboardUrl = request.DashboardBaseUrl is null ? null : $"{request.DashboardBaseUrl.TrimEnd('/')}/?analysisId={analysis.Id}";

            result.Analysis = analysis;
            result.CheckConclusion = "success";
            result.DashboardUrl = analysis.DashboardUrl;
            result.CommentMarkdown = PrCommentRenderer.Render(result);
            result.AuditEvents = result.AuditEvents
                .Append(Audit(analysis, "PR analysis failed", ex.Message))
                .ToArray();
            return result;
        }
    }

    private static string? DetermineEnvironment(IReadOnlyList<ResourceEstimate> resources, string branch)
    {
        var environment = resources
            .Select(resource => resource.Environment)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return environment;
        }

        var lowered = branch.ToLowerInvariant();
        if (lowered.Contains("prod", StringComparison.Ordinal))
        {
            return "production";
        }

        if (lowered.Contains("staging", StringComparison.Ordinal) || lowered.Contains("stage", StringComparison.Ordinal))
        {
            return "staging";
        }

        if (lowered.Contains("dev", StringComparison.Ordinal))
        {
            return "dev";
        }

        return null;
    }

    private static ConfidenceLevel DetermineOverallConfidence(IReadOnlyList<ResourceEstimate> resources)
    {
        if (resources.Count == 0)
        {
            return ConfidenceLevel.High;
        }

        return resources.Select(resource => resource.Confidence).MinBy(ConfidenceRank);
    }

    private static int ConfidenceRank(ConfidenceLevel confidence) => confidence switch
    {
        ConfidenceLevel.Unknown => 0,
        ConfidenceLevel.Low => 1,
        ConfidenceLevel.Medium => 2,
        ConfidenceLevel.High => 3,
        _ => 0
    };

    private static decimal? DetermineBudgetLimit(PolicyConfig config, string? environment, IReadOnlyList<PolicyFinding>? findings = null)
    {
        if (findings is not null)
        {
            var triggered = findings
                .Where(finding => finding.ThresholdValue is not null)
                .OrderByDescending(finding => PolicyEngine.Severity(finding.Action))
                .ThenBy(finding => finding.ThresholdValue)
                .FirstOrDefault();
            if (triggered?.ThresholdValue is not null)
            {
                return triggered.ThresholdValue;
            }
        }

        if (!string.IsNullOrWhiteSpace(environment)
            && config.Environments.TryGetValue(environment, out var environmentBudget)
            && environmentBudget.MonthlyBudget > 0)
        {
            return environmentBudget.MonthlyBudget;
        }

        return config.Rules
            .Where(rule => rule.Type.Equals("monthly_delta", StringComparison.OrdinalIgnoreCase) && rule.Threshold > 0)
            .OrderBy(rule => rule.Threshold)
            .Select(rule => (decimal?)rule.Threshold)
            .FirstOrDefault();
    }

    private static string BuildGitHubPullRequestUrl(string owner, string repository, int pullRequestNumber)
    {
        return $"https://github.com/{owner}/{repository}/pull/{pullRequestNumber}";
    }

    private IReadOnlyList<ResourceEstimate> AnalyzeRepositoryFiles(
        IReadOnlyList<RepositoryFile> files,
        Guid analysisId,
        ProjectSettings settings,
        string branch)
    {
        var cloudInputs = terraformParser.Parse(files, settings.DefaultRegion, settings.HoursPerMonth, branch)
            .Concat(bicepParser.Parse(files, settings.DefaultRegion, settings.HoursPerMonth, branch))
            .ToArray();
        var cloudEstimates = estimator.EstimateCloudResources(cloudInputs, analysisId, settings.Currency);

        var aiWorkflows = aiSpendParser.Parse(files);
        var aiEstimates = estimator.EstimateAiWorkflows(aiWorkflows, analysisId, settings.Currency);

        return cloudEstimates.Concat(aiEstimates).ToArray();
    }

    private static IReadOnlyList<ResourceCostChange> CalculateChanges(
        IReadOnlyList<ResourceEstimate> baselineResources,
        IReadOnlyList<ResourceEstimate> proposedResources)
    {
        var keys = baselineResources.Select(ResourceKey)
            .Concat(proposedResources.Select(ResourceKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var changes = new List<ResourceCostChange>();

        foreach (var key in keys)
        {
            var before = baselineResources.FirstOrDefault(resource => ResourceKey(resource).Equals(key, StringComparison.OrdinalIgnoreCase));
            var after = proposedResources.FirstOrDefault(resource => ResourceKey(resource).Equals(key, StringComparison.OrdinalIgnoreCase));
            var beforeCost = before?.MonthlyCost ?? 0;
            var afterCost = after?.MonthlyCost ?? 0;
            var delta = decimal.Round(afterCost - beforeCost, 2);

            if (delta == 0 && string.Equals(before?.Sku, after?.Sku, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            changes.Add(new ResourceCostChange
            {
                ResourceKey = key,
                ResourceName = after?.ResourceName ?? before?.ResourceName ?? key,
                ResourceType = after?.ResourceType ?? before?.ResourceType ?? "",
                Region = after?.Region ?? before?.Region,
                BeforeSku = before?.Sku,
                AfterSku = after?.Sku,
                MonthlyDelta = delta,
                ChangeKind = before is null ? "added" : after is null ? "removed" : "changed"
            });
        }

        return changes.OrderByDescending(change => Math.Abs(change.MonthlyDelta)).ToArray();
    }

    private static string ResourceKey(ResourceEstimate resource)
    {
        return $"{resource.Provider}:{resource.ResourceType}:{resource.ResourceName}";
    }

    private static string? FindSpendGovYaml(IEnumerable<RepositoryFile> files)
    {
        return files.FirstOrDefault(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.SpendGovConfig)?.Content;
    }

    private static AuditEvent Audit(PullRequestAnalysis analysis, string eventType, string message, object? metadata = null)
    {
        return new AuditEvent
        {
            ProjectId = analysis.ProjectId,
            AnalysisId = analysis.Id,
            EventType = eventType,
            Message = message,
            MetadataJson = metadata is null ? "{}" : JsonSerializer.Serialize(metadata)
        };
    }
}
