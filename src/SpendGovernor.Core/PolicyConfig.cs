using System.Globalization;

namespace SpendGovernor.Core;

public sealed class PolicyConfig
{
    public int Version { get; set; } = 1;
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int HoursPerMonth { get; set; } = 730;
    public string OnInternalError { get; set; } = "fail_open";
    public PullRequestBehavior PullRequests { get; set; } = new();
    public List<PolicyRule> Rules { get; set; } = [];
    public Dictionary<string, EnvironmentBudget> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AiPolicyConfig Ai { get; set; } = new();

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

public sealed class EnvironmentBudget
{
    public decimal MonthlyBudget { get; set; }
    public PolicyAction Action { get; set; } = PolicyAction.Warn;
}

public sealed class AiPolicyConfig
{
    public bool Enabled { get; set; } = true;
    public decimal MonthlyBudget { get; set; } = 300;
    public decimal MaxCostPerWorkflowMonthly { get; set; } = 100;
    public PolicyAction Action { get; set; } = PolicyAction.Warn;
}

public sealed class SpendGovConfigParseResult
{
    public PolicyConfig Config { get; init; } = new();
    public List<string> Errors { get; init; } = [];
}

public static class SpendGovConfigParser
{
    private static readonly HashSet<string> SupportedCurrencies = new(StringComparer.OrdinalIgnoreCase) { "EUR", "USD" };
    private static readonly HashSet<string> SupportedSections = new(StringComparer.OrdinalIgnoreCase) { "pullRequests", "rules", "environments", "ai", "aiWorkflows" };
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
                }
                else
                {
                    errors.Add("Environment monthlyBudget must be numeric.");
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
            errors.Add($"Unsupported top-level section '{section}'. Supported sections: pullRequests, rules, environments, ai, aiWorkflows.");
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

        if (config.Environments.Count == 0)
        {
            errors.Add("No environments configured. Add at least one environments.<name>.monthlyBudget entry so environment budgets can be evaluated.");
        }

        foreach (var environment in config.Environments)
        {
            if (string.IsNullOrWhiteSpace(environment.Key))
            {
                errors.Add("Environment name is required.");
            }

            if (environment.Value.MonthlyBudget <= 0)
            {
                errors.Add($"Environment '{environment.Key}' is missing monthlyBudget or has a value less than or equal to 0.");
            }
        }

        if (config.Ai.MonthlyBudget <= 0)
        {
            errors.Add("ai.monthlyBudget must be greater than 0.");
        }

        if (config.Ai.MaxCostPerWorkflowMonthly <= 0)
        {
            errors.Add("ai.maxCostPerWorkflowMonthly must be greater than 0.");
        }
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
