using System.Globalization;

namespace SpendGovernor.Core;

public sealed class PolicyConfig
{
    public int Version { get; set; } = 1;
    public string? Environment { get; set; }
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int HoursPerMonth { get; set; } = 730;
    public string OnInternalError { get; set; } = "fail_open";
    public PullRequestBehavior PullRequests { get; set; } = new();
    public List<PolicyRule> Rules { get; set; } = [];
    public SpendGovernanceRules GovernanceRules { get; set; } = new();
    public Dictionary<string, EnvironmentBudget> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AiPolicyConfig Ai { get; set; } = new();
    public List<SpendPolicy> Policies { get; set; } = [];

    public static string DefaultYaml =>
        """
        version: 1
        currency: EUR
        defaultRegion: westeurope
        hoursPerMonth: 730
        onInternalError: fail_open

        pullRequests:
          comment: true
          checkRun: true

        rules:
          - id: max-pr-delta
            description: Block PRs that add more than 250 EUR/month
            type: monthly_delta
            threshold: 250
            action: block
          - id: warn-pr-delta
            description: Warn when PRs add more than 100 EUR/month
            type: monthly_delta
            threshold: 100
            action: warn
          - id: max-unknown-resources
            description: Warn when too many resources cannot be estimated
            type: unknown_resource_count
            threshold: 3
            action: warn

        environments:
          dev:
            monthlyBudget: 200
            action: warn
          staging:
            monthlyBudget: 500
            action: approval_required
          production:
            monthlyBudget: 3000
            action: block

        ai:
          enabled: true
          monthlyBudget: 300
          maxCostPerWorkflowMonthly: 100
          action: warn
        """;

    public static PolicyConfig CreateDefault(ProjectSettings settings)
    {
        var parsed = SpendGovConfigParser.Parse(settings.PolicyYaml, settings);
        return parsed.Config;
    }
}

public sealed class PullRequestBehavior
{
    public bool Comment { get; set; } = true;
    public bool CheckRun { get; set; } = true;
}

public sealed class PolicyRule
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal Threshold { get; set; }
    public PolicyAction Action { get; set; } = PolicyAction.Warn;
}

public sealed class SpendGovernanceRules
{
    public bool BlockOnBudgetExceeded { get; set; } = true;
    public bool WarnOnLowConfidence { get; set; }
    public decimal? RequireApprovalAbove { get; set; }
}

public sealed class EnvironmentBudget
{
    public decimal MonthlyBudget { get; set; }
    public decimal? MaxMonthlyDelta { get; set; }
    public decimal? MaxMonthlyCost { get; set; }
    public PolicyAction Action { get; set; } = PolicyAction.Warn;
}

public sealed class AiPolicyConfig
{
    public bool Enabled { get; set; } = true;
    public decimal MonthlyBudget { get; set; } = 300;
    public decimal MaxCostPerWorkflowMonthly { get; set; } = 100;
    public PolicyAction Action { get; set; } = PolicyAction.Warn;
}

public sealed class SpendPolicy
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public SpendPolicySeverity Severity { get; set; } = SpendPolicySeverity.Warn;
    public List<string> Environments { get; set; } = [];
    public PolicyMatch Match { get; set; } = new();
    public PolicyCondition Condition { get; set; } = new();
    public string Message { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class PolicyMatch
{
    public string? Type { get; set; }
    public string? Provider { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceName { get; set; }
    public string? ResourceNameContains { get; set; }
    public string? Sku { get; set; }
    public string? SkuContains { get; set; }
    public List<string> SkuContainsAny { get; set; } = [];
    public string? Region { get; set; }
    public string? Environment { get; set; }
    public string? AnalysisSource { get; set; }
    public string? Model { get; set; }
    public List<string> ModelIn { get; set; } = [];

    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Type)
        || !string.IsNullOrWhiteSpace(Provider)
        || !string.IsNullOrWhiteSpace(ResourceType)
        || !string.IsNullOrWhiteSpace(ResourceName)
        || !string.IsNullOrWhiteSpace(ResourceNameContains)
        || !string.IsNullOrWhiteSpace(Sku)
        || !string.IsNullOrWhiteSpace(SkuContains)
        || SkuContainsAny.Count > 0
        || !string.IsNullOrWhiteSpace(Region)
        || !string.IsNullOrWhiteSpace(Environment)
        || !string.IsNullOrWhiteSpace(AnalysisSource)
        || !string.IsNullOrWhiteSpace(Model)
        || ModelIn.Count > 0;
}

public sealed class PolicyCondition
{
    public decimal? EstimatedMonthlyCostGreaterThan { get; set; }
    public decimal? EstimatedMonthlyDeltaGreaterThan { get; set; }
    public ConfidenceLevel? ConfidenceBelow { get; set; }
    public bool? BudgetExceeded { get; set; }
    public bool? PricingFallbackUsed { get; set; }
    public bool? RegionMissing { get; set; }
    public bool? SkuMissing { get; set; }
    public bool? AnalysisSourceIsFallback { get; set; }

    public bool HasAny =>
        EstimatedMonthlyCostGreaterThan is not null
        || EstimatedMonthlyDeltaGreaterThan is not null
        || ConfidenceBelow is not null
        || BudgetExceeded is not null
        || PricingFallbackUsed is not null
        || RegionMissing is not null
        || SkuMissing is not null
        || AnalysisSourceIsFallback is not null;
}

public sealed class SpendGovConfigParseResult
{
    public PolicyConfig Config { get; init; } = new();
    public List<string> Errors { get; init; } = [];
}

public static class SpendGovConfigParser
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "EUR", "USD" };
    private static readonly HashSet<string> SupportedSections = new(StringComparer.OrdinalIgnoreCase) { "pullRequests", "rules", "budgets", "environments", "ai", "aiWorkflows", "policies" };
    private static readonly HashSet<string> SupportedRuleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "monthly_delta",
        "proposed_monthly_cost",
        "environment_budget",
        "unknown_resource_count",
        "ai_monthly_cost",
        "ai_workflow_cost"
    };

    public static SpendGovConfigParseResult Parse(string? yaml, ProjectSettings? settings = null)
    {
        var config = new PolicyConfig
        {
            Currency = settings?.Currency ?? "EUR",
            DefaultRegion = settings?.DefaultRegion ?? "westeurope",
            HoursPerMonth = settings?.HoursPerMonth ?? 730
        };
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(yaml))
        {
            yaml = PolicyConfig.DefaultYaml;
        }

        string section = "";
        PolicyRule? currentRule = null;
        string? currentEnvironment = null;
        SpendPolicy? currentPolicy = null;
        string? currentPolicySection = null;
        string? currentPolicyList = null;
        var unknownSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var withoutComment = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(withoutComment))
            {
                continue;
            }

            var indent = withoutComment.TakeWhile(char.IsWhiteSpace).Count();
            var line = withoutComment.Trim();
            if (indent == 0)
            {
                currentRule = null;
                currentEnvironment = null;
                currentPolicy = null;
                currentPolicySection = null;
                currentPolicyList = null;
                if (line.EndsWith(':'))
                {
                    section = line[..^1].Trim();
                    if (!SupportedSections.Contains(section))
                    {
                        unknownSections.Add(section);
                    }
                    continue;
                }

                ApplyRoot(config, line, errors);
                section = "";
                continue;
            }

            switch (section)
            {
                case "pullRequests":
                    ApplyPullRequest(config.PullRequests, line, errors);
                    break;
                case "rules":
                    if (line.StartsWith("- ", StringComparison.Ordinal))
                    {
                        currentRule = new PolicyRule();
                        config.Rules.Add(currentRule);
                        var inline = line[2..].Trim();
                        if (!string.IsNullOrWhiteSpace(inline))
                        {
                            ApplyRule(currentRule, inline, errors);
                        }
                    }
                    else if (currentRule is not null)
                    {
                        ApplyRule(currentRule, line, errors);
                    }
                    else
                    {
                        ApplyGovernanceRules(config.GovernanceRules, line, errors);
                    }
                    break;
                case "budgets":
                    if (indent == 2 && line.EndsWith(':'))
                    {
                        currentEnvironment = line[..^1].Trim();
                        config.Environments[currentEnvironment] = new EnvironmentBudget();
                    }
                    else if (currentEnvironment is not null)
                    {
                        ApplyBudget(config.Environments[currentEnvironment], line, errors);
                    }
                    break;
                case "environments":
                    if (indent == 2 && line.EndsWith(':'))
                    {
                        currentEnvironment = line[..^1].Trim();
                        config.Environments[currentEnvironment] = new EnvironmentBudget();
                    }
                    else if (currentEnvironment is not null)
                    {
                        ApplyEnvironment(config.Environments[currentEnvironment], line, errors);
                    }
                    break;
                case "ai":
                    ApplyAi(config.Ai, line, errors);
                    break;
                case "policies":
                    ApplyPolicyLine(config, ref currentPolicy, ref currentPolicySection, ref currentPolicyList, indent, line, errors);
                    break;
            }
        }

        if (config.Rules.Count == 0)
        {
            config.Rules.AddRange(DefaultRules());
        }

        Validate(config, errors, unknownSections);
        return new SpendGovConfigParseResult { Config = config, Errors = errors };
    }

    private static IEnumerable<PolicyRule> DefaultRules()
    {
        yield return new PolicyRule
        {
            Id = "max-pr-delta",
            Description = "Block PRs that add more than 250 EUR/month",
            Type = "monthly_delta",
            Threshold = 250,
            Action = PolicyAction.Block
        };
        yield return new PolicyRule
        {
            Id = "warn-pr-delta",
            Description = "Warn when PRs add more than 100 EUR/month",
            Type = "monthly_delta",
            Threshold = 100,
            Action = PolicyAction.Warn
        };
        yield return new PolicyRule
        {
            Id = "max-unknown-resources",
            Description = "Warn when too many resources cannot be estimated",
            Type = "unknown_resource_count",
            Threshold = 3,
            Action = PolicyAction.Warn
        };
    }

    private static void ApplyRoot(PolicyConfig config, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "version":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
                {
                    config.Version = version;
                }
                else
                {
                    errors.Add("version must be an integer.");
                }
                break;
            case "environment":
                config.Environment = Unquote(value);
                break;
            case "currency":
                config.Currency = Unquote(value).ToUpperInvariant();
                break;
            case "defaultRegion":
                config.DefaultRegion = Unquote(value);
                break;
            case "hoursPerMonth":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours))
                {
                    config.HoursPerMonth = hours;
                }
                else
                {
                    errors.Add("hoursPerMonth must be an integer.");
                }
                break;
            case "onInternalError":
                config.OnInternalError = Unquote(value);
                break;
        }
    }

    private static void ApplyPullRequest(PullRequestBehavior behavior, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        var parsed = ParseBool(value);
        if (parsed is null)
        {
            errors.Add($"pullRequests.{key} must be true or false.");
            return;
        }

        if (key.Equals("comment", StringComparison.OrdinalIgnoreCase))
        {
            behavior.Comment = parsed.Value;
        }
        else if (key.Equals("checkRun", StringComparison.OrdinalIgnoreCase))
        {
            behavior.CheckRun = parsed.Value;
        }
    }

    private static void ApplyRule(PolicyRule rule, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "id":
                rule.Id = Unquote(value);
                break;
            case "description":
                rule.Description = Unquote(value);
                break;
            case "type":
                rule.Type = Unquote(value);
                break;
            case "threshold":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold))
                {
                    rule.Threshold = threshold;
                }
                else
                {
                    errors.Add($"Rule {rule.Id} has an invalid threshold.");
                }
                break;
            case "action":
                if (TryParseAction(value, out var action))
                {
                    rule.Action = action;
                }
                else
                {
                    errors.Add($"Rule {rule.Id} has an invalid action '{value}'.");
                }
                break;
        }
    }

    private static void ApplyGovernanceRules(SpendGovernanceRules rules, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "blockOnBudgetExceeded":
                ApplyBool(value, "rules.blockOnBudgetExceeded", parsed => rules.BlockOnBudgetExceeded = parsed, errors);
                break;
            case "warnOnLowConfidence":
                ApplyBool(value, "rules.warnOnLowConfidence", parsed => rules.WarnOnLowConfidence = parsed, errors);
                break;
            case "requireApprovalAbove":
                if (TryParseDecimal(value, out var approval))
                {
                    rules.RequireApprovalAbove = approval;
                }
                else
                {
                    errors.Add("rules.requireApprovalAbove must be numeric.");
                }
                break;
            default:
                errors.Add($"Unsupported rules field '{key}'.");
                break;
        }
    }

    private static void ApplyBudget(EnvironmentBudget environment, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "maxMonthlyDelta":
                if (TryParseDecimal(value, out var delta))
                {
                    environment.MaxMonthlyDelta = delta;
                }
                else
                {
                    errors.Add("Budget maxMonthlyDelta must be numeric.");
                }
                break;
            case "maxMonthlyCost":
                if (TryParseDecimal(value, out var cost))
                {
                    environment.MaxMonthlyCost = cost;
                    environment.MonthlyBudget = cost;
                }
                else
                {
                    errors.Add("Budget maxMonthlyCost must be numeric.");
                }
                break;
            case "action":
                if (TryParseAction(value, out var action))
                {
                    environment.Action = action;
                }
                else
                {
                    errors.Add($"Budget action '{value}' is invalid.");
                }
                break;
            default:
                errors.Add($"Unsupported budget field '{key}'.");
                break;
        }
    }

    private static void ApplyPolicyLine(
        PolicyConfig config,
        ref SpendPolicy? currentPolicy,
        ref string? currentPolicySection,
        ref string? currentPolicyList,
        int indent,
        string line,
        List<string> errors)
    {
        if (indent == 2 && line.StartsWith("- ", StringComparison.Ordinal))
        {
            currentPolicy = new SpendPolicy();
            config.Policies.Add(currentPolicy);
            currentPolicySection = null;
            currentPolicyList = null;
            var inline = line[2..].Trim();
            if (!string.IsNullOrWhiteSpace(inline))
            {
                ApplyPolicyRoot(currentPolicy, inline, errors);
            }

            return;
        }

        if (currentPolicy is null)
        {
            errors.Add("policies entries must start with '-'.");
            return;
        }

        if (indent == 4)
        {
            currentPolicySection = null;
            currentPolicyList = null;
            if (line.Equals("match:", StringComparison.OrdinalIgnoreCase))
            {
                currentPolicySection = "match";
                return;
            }

            if (line.Equals("condition:", StringComparison.OrdinalIgnoreCase))
            {
                currentPolicySection = "condition";
                return;
            }

            if (line.Equals("environments:", StringComparison.OrdinalIgnoreCase))
            {
                currentPolicyList = "environments";
                return;
            }

            ApplyPolicyRoot(currentPolicy, line, errors);
            return;
        }

        if (indent >= 6 && currentPolicyList == "environments")
        {
            AddListValue(currentPolicy.Environments, line, errors, "policy.environments");
            return;
        }

        if (indent == 6 && currentPolicySection == "match")
        {
            var pair = SplitPair(line);
            if (pair is null)
            {
                return;
            }

            var (key, value) = pair.Value;
            if (key is "skuContainsAny" or "modelIn" && string.IsNullOrWhiteSpace(value))
            {
                currentPolicyList = $"match.{key}";
                return;
            }

            ApplyPolicyMatch(currentPolicy.Match, key, value, errors);
            return;
        }

        if (indent >= 8 && currentPolicySection == "match" && currentPolicyList is "match.skuContainsAny" or "match.modelIn")
        {
            var values = currentPolicyList == "match.skuContainsAny"
                ? currentPolicy.Match.SkuContainsAny
                : currentPolicy.Match.ModelIn;
            AddListValue(values, line, errors, currentPolicyList);
            return;
        }

        if (indent == 6 && currentPolicySection == "condition")
        {
            var pair = SplitPair(line);
            if (pair is null)
            {
                return;
            }

            ApplyPolicyCondition(currentPolicy.Condition, pair.Value.Key, pair.Value.Value, errors);
            return;
        }
    }

    private static void ApplyPolicyRoot(SpendPolicy policy, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "id":
                policy.Id = Unquote(value);
                break;
            case "title":
                policy.Title = Unquote(value);
                break;
            case "description":
                policy.Description = Unquote(value);
                break;
            case "severity":
                if (TryParseSeverity(value, out var severity))
                {
                    policy.Severity = severity;
                }
                else
                {
                    errors.Add($"Policy {PolicyLabel(policy)} has invalid severity '{value}'. Supported values: info, warn, fail.");
                }
                break;
            case "environment":
                AddCsvOrInlineList(policy.Environments, value);
                break;
            case "environments":
                AddCsvOrInlineList(policy.Environments, value);
                break;
            case "message":
                policy.Message = Unquote(value);
                break;
            case "recommendation":
                policy.Recommendation = Unquote(value);
                break;
            case "enabled":
                ApplyBool(value, $"Policy {PolicyLabel(policy)} enabled", parsed => policy.Enabled = parsed, errors);
                break;
            default:
                errors.Add($"Policy {PolicyLabel(policy)} has unsupported field '{key}'.");
                break;
        }
    }

    private static void ApplyPolicyMatch(PolicyMatch match, string key, string value, List<string> errors)
    {
        switch (key)
        {
            case "type":
                match.Type = Unquote(value);
                break;
            case "provider":
                match.Provider = Unquote(value);
                break;
            case "resourceType":
                match.ResourceType = Unquote(value);
                break;
            case "resourceName":
                match.ResourceName = Unquote(value);
                break;
            case "resourceNameContains":
                match.ResourceNameContains = Unquote(value);
                break;
            case "sku":
                match.Sku = Unquote(value);
                break;
            case "skuContains":
                match.SkuContains = Unquote(value);
                break;
            case "skuContainsAny":
                AddCsvOrInlineList(match.SkuContainsAny, value);
                break;
            case "region":
                match.Region = Unquote(value);
                break;
            case "environment":
                match.Environment = Unquote(value);
                break;
            case "analysisSource":
                match.AnalysisSource = Unquote(value);
                break;
            case "model":
                match.Model = Unquote(value);
                break;
            case "modelIn":
                AddCsvOrInlineList(match.ModelIn, value);
                break;
            default:
                errors.Add($"Unsupported policy match field '{key}'.");
                break;
        }
    }

    private static void ApplyPolicyCondition(PolicyCondition condition, string key, string value, List<string> errors)
    {
        switch (key)
        {
            case "estimatedMonthlyCostGreaterThan":
                ApplyDecimal(value, "condition.estimatedMonthlyCostGreaterThan", parsed => condition.EstimatedMonthlyCostGreaterThan = parsed, errors);
                break;
            case "estimatedMonthlyDeltaGreaterThan":
                ApplyDecimal(value, "condition.estimatedMonthlyDeltaGreaterThan", parsed => condition.EstimatedMonthlyDeltaGreaterThan = parsed, errors);
                break;
            case "confidenceBelow":
                if (TryParseConfidence(value, out var confidence))
                {
                    condition.ConfidenceBelow = confidence;
                }
                else
                {
                    errors.Add($"Invalid confidence value '{value}'. Supported values: High, Medium, Low, Unknown.");
                }
                break;
            case "budgetExceeded":
                ApplyBool(value, "condition.budgetExceeded", parsed => condition.BudgetExceeded = parsed, errors);
                break;
            case "pricingFallbackUsed":
                ApplyBool(value, "condition.pricingFallbackUsed", parsed => condition.PricingFallbackUsed = parsed, errors);
                break;
            case "regionMissing":
                ApplyBool(value, "condition.regionMissing", parsed => condition.RegionMissing = parsed, errors);
                break;
            case "skuMissing":
                ApplyBool(value, "condition.skuMissing", parsed => condition.SkuMissing = parsed, errors);
                break;
            case "analysisSourceIsFallback":
                ApplyBool(value, "condition.analysisSourceIsFallback", parsed => condition.AnalysisSourceIsFallback = parsed, errors);
                break;
            default:
                errors.Add($"Unsupported policy condition field '{key}'.");
                break;
        }
    }

    private static void ApplyEnvironment(EnvironmentBudget environment, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "monthlyBudget":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var budget))
                {
                    environment.MonthlyBudget = budget;
                    environment.MaxMonthlyCost = budget;
                }
                else
                {
                    errors.Add("Environment monthlyBudget must be numeric.");
                }
                break;
            case "maxMonthlyCost":
                if (TryParseDecimal(value, out var maxCost))
                {
                    environment.MaxMonthlyCost = maxCost;
                    environment.MonthlyBudget = maxCost;
                }
                else
                {
                    errors.Add("Environment maxMonthlyCost must be numeric.");
                }
                break;
            case "maxMonthlyDelta":
                if (TryParseDecimal(value, out var maxDelta))
                {
                    environment.MaxMonthlyDelta = maxDelta;
                }
                else
                {
                    errors.Add("Environment maxMonthlyDelta must be numeric.");
                }
                break;
            case "action":
                if (TryParseAction(value, out var action))
                {
                    environment.Action = action;
                }
                else
                {
                    errors.Add($"Environment action '{value}' is invalid.");
                }
                break;
        }
    }

    private static void ApplyAi(AiPolicyConfig ai, string line, List<string> errors)
    {
        var pair = SplitPair(line);
        if (pair is null)
        {
            return;
        }

        var (key, value) = pair.Value;
        switch (key)
        {
            case "enabled":
                var enabled = ParseBool(value);
                if (enabled is null)
                {
                    errors.Add("ai.enabled must be true or false.");
                }
                else
                {
                    ai.Enabled = enabled.Value;
                }
                break;
            case "monthlyBudget":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var budget))
                {
                    ai.MonthlyBudget = budget;
                }
                else
                {
                    errors.Add("ai.monthlyBudget must be numeric.");
                }
                break;
            case "maxCostPerWorkflowMonthly":
                if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var workflowBudget))
                {
                    ai.MaxCostPerWorkflowMonthly = workflowBudget;
                }
                else
                {
                    errors.Add("ai.maxCostPerWorkflowMonthly must be numeric.");
                }
                break;
            case "action":
                if (TryParseAction(value, out var action))
                {
                    ai.Action = action;
                }
                else
                {
                    errors.Add($"ai.action '{value}' is invalid.");
                }
                break;
        }
    }

    private static void Validate(PolicyConfig config, List<string> errors, IReadOnlyCollection<string> unknownSections)
    {
        if (config.Version != 1)
        {
            errors.Add("Only .spendgov.yml version 1 is supported by this MVP.");
        }

        if (!SupportedCurrencies.Contains(config.Currency))
        {
            errors.Add($"Unsupported currency '{config.Currency}'. Supported currencies: EUR, USD.");
        }

        if (string.IsNullOrWhiteSpace(config.DefaultRegion))
        {
            errors.Add("defaultRegion is required.");
        }

        if (config.HoursPerMonth <= 0)
        {
            errors.Add("hoursPerMonth must be greater than 0.");
        }

        foreach (var section in unknownSections)
        {
            errors.Add($"Unsupported top-level section '{section}'. Supported sections: pullRequests, rules, budgets, environments, ai, aiWorkflows, policies.");
        }

        foreach (var rule in config.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                errors.Add("Every rule must have an id.");
            }

            if (string.IsNullOrWhiteSpace(rule.Type))
            {
                errors.Add($"Rule {rule.Id} must have a type.");
            }
            else if (!SupportedRuleTypes.Contains(rule.Type))
            {
                errors.Add($"Rule {rule.Id} has unsupported type '{rule.Type}'. Supported rule types: {string.Join(", ", SupportedRuleTypes)}.");
            }

            if (rule.Threshold < 0)
            {
                errors.Add($"Rule {rule.Id} threshold must be 0 or greater.");
            }
        }

        foreach (var environment in config.Environments)
        {
            if (string.IsNullOrWhiteSpace(environment.Key))
            {
                errors.Add("Environment name is required.");
            }

            if (environment.Value.MonthlyBudget <= 0 && environment.Value.MaxMonthlyDelta is null)
            {
                errors.Add($"Environment '{environment.Key}' is missing monthlyBudget/maxMonthlyCost or maxMonthlyDelta.");
            }
        }

        ValidatePolicies(config.Policies, errors);

        if (config.Ai.MonthlyBudget <= 0)
        {
            errors.Add("ai.monthlyBudget must be greater than 0.");
        }

        if (config.Ai.MaxCostPerWorkflowMonthly <= 0)
        {
            errors.Add("ai.maxCostPerWorkflowMonthly must be greater than 0.");
        }
    }

    private static void ValidatePolicies(IReadOnlyList<SpendPolicy> policies, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
        {
            if (string.IsNullOrWhiteSpace(policy.Id))
            {
                errors.Add("Every policy must have an id.");
            }
            else if (!ids.Add(policy.Id))
            {
                errors.Add($"Duplicate policy id '{policy.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(policy.Message))
            {
                errors.Add($"Policy {PolicyLabel(policy)} must have a non-empty message.");
            }

            if (policy.Match.SkuContainsAny.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Policy {PolicyLabel(policy)} has an invalid skuContainsAny list.");
            }

            if (policy.Match.ModelIn.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Policy {PolicyLabel(policy)} has an invalid modelIn list.");
            }

            if (!policy.Match.HasAny && !policy.Condition.HasAny)
            {
                errors.Add($"Policy {PolicyLabel(policy)} must define match or condition.");
            }
        }
    }

    private static void ApplyBool(string value, string label, Action<bool> apply, List<string> errors)
    {
        var parsed = ParseBool(value);
        if (parsed is null)
        {
            errors.Add($"{label} must be true or false.");
            return;
        }

        apply(parsed.Value);
    }

    private static void ApplyDecimal(string value, string label, Action<decimal> apply, List<string> errors)
    {
        if (TryParseDecimal(value, out var parsed))
        {
            apply(parsed);
            return;
        }

        errors.Add($"{label} must be numeric.");
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        return decimal.TryParse(Unquote(value), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool TryParseSeverity(string value, out SpendPolicySeverity severity)
    {
        severity = Unquote(value).Replace("-", "_", StringComparison.Ordinal).Trim().ToLowerInvariant() switch
        {
            "info" => SpendPolicySeverity.Info,
            "warn" or "warning" => SpendPolicySeverity.Warn,
            "fail" or "failure" or "block" => SpendPolicySeverity.Fail,
            _ => SpendPolicySeverity.Warn
        };

        var normalized = Unquote(value).Replace("-", "_", StringComparison.Ordinal).Trim();
        return normalized.Equals("info", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("warning", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("fail", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("failure", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("block", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseConfidence(string value, out ConfidenceLevel confidence)
    {
        confidence = Unquote(value).Trim().ToLowerInvariant() switch
        {
            "high" => ConfidenceLevel.High,
            "medium" => ConfidenceLevel.Medium,
            "low" => ConfidenceLevel.Low,
            "unknown" => ConfidenceLevel.Unknown,
            _ => ConfidenceLevel.Unknown
        };

        return Enum.TryParse<ConfidenceLevel>(Unquote(value), ignoreCase: true, out _);
    }

    private static void AddListValue(List<string> values, string line, List<string> errors, string label)
    {
        var value = line.Trim();
        if (value.StartsWith("- ", StringComparison.Ordinal))
        {
            value = value[2..].Trim();
        }

        value = Unquote(value);
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} contains an empty value.");
            return;
        }

        values.Add(value);
    }

    private static void AddCsvOrInlineList(List<string> values, string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            raw = raw[1..^1];
        }

        foreach (var value in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(Unquote(value));
        }
    }

    private static string PolicyLabel(SpendPolicy policy)
    {
        return string.IsNullOrWhiteSpace(policy.Id) ? "(missing id)" : policy.Id;
    }

    private static string StripComment(string line)
    {
        var inQuote = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] is '\'' or '"')
            {
                inQuote = !inQuote;
            }
            else if (line[i] == '#' && !inQuote)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static (string Key, string Value)? SplitPair(string line)
    {
        var index = line.IndexOf(':', StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        return (line[..index].Trim(), line[(index + 1)..].Trim());
    }

    private static bool? ParseBool(string value)
    {
        value = Unquote(value);
        return value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? true
            : value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    public static bool TryParseAction(string value, out PolicyAction action)
    {
        value = Unquote(value).Replace("-", "_", StringComparison.Ordinal).Trim();
        action = value.ToLowerInvariant() switch
        {
            "pass" => PolicyAction.Pass,
            "warn" => PolicyAction.Warn,
            "approval_required" => PolicyAction.ApprovalRequired,
            "approvalrequired" => PolicyAction.ApprovalRequired,
            "block" => PolicyAction.Block,
            _ => PolicyAction.Warn
        };

        return value.Equals("pass", StringComparison.OrdinalIgnoreCase)
            || value.Equals("warn", StringComparison.OrdinalIgnoreCase)
            || value.Equals("approval_required", StringComparison.OrdinalIgnoreCase)
            || value.Equals("approvalrequired", StringComparison.OrdinalIgnoreCase)
            || value.Equals("block", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToConfigString(PolicyAction action) => action switch
    {
        PolicyAction.Pass => "pass",
        PolicyAction.Warn => "warn",
        PolicyAction.ApprovalRequired => "approval_required",
        PolicyAction.Block => "block",
        _ => "warn"
    };

    public static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
