using System.Globalization;

namespace SpendGovernor.Core;

public sealed class AiWorkflow
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int EstimatedMonthlyRequests { get; set; }
    public int AverageInputTokens { get; set; }
    public int AverageOutputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? MaxAgentSteps { get; set; }
    public string? Environment { get; set; }
    public string? Tenant { get; set; }
    public string? Feature { get; set; }
    public string SourceFile { get; set; } = "";
}

public sealed record AiModelPrice(
    string Provider,
    string Model,
    decimal InputPricePerMillionTokens,
    decimal OutputPricePerMillionTokens,
    string Currency,
    DateOnly ValidFrom,
    string Source);

public sealed class AiSpendParser
{
    public IReadOnlyList<AiWorkflow> Parse(IEnumerable<RepositoryFile> files)
    {
        var workflows = new List<AiWorkflow>();
        foreach (var file in files.Where(file =>
                     FileDiscovery.Detect(file.Path).Kind is RelevantFileKind.AiSpendConfig or RelevantFileKind.SpendGovConfig))
        {
            workflows.AddRange(ParseFile(file));
        }

        return workflows;
    }

    public IReadOnlyList<string> Validate(IEnumerable<AiWorkflow> workflows)
    {
        var errors = new List<string>();
        foreach (var workflow in workflows)
        {
            var label = string.IsNullOrWhiteSpace(workflow.Id) ? "(missing id)" : workflow.Id;
            if (string.IsNullOrWhiteSpace(workflow.Id))
            {
                errors.Add("AI workflow is missing required field: id.");
            }

            if (string.IsNullOrWhiteSpace(workflow.Provider))
            {
                errors.Add($"AI workflow {label} is missing required field: provider.");
            }

            if (string.IsNullOrWhiteSpace(workflow.Model))
            {
                errors.Add($"AI workflow {label} is missing required field: model.");
            }

            if (workflow.EstimatedMonthlyRequests <= 0)
            {
                errors.Add($"AI workflow {label} must set estimatedMonthlyRequests greater than 0.");
            }

            if (workflow.AverageInputTokens <= 0)
            {
                errors.Add($"AI workflow {label} must set averageInputTokens greater than 0.");
            }

            if (workflow.AverageOutputTokens <= 0)
            {
                errors.Add($"AI workflow {label} must set averageOutputTokens greater than 0.");
            }
        }

        return errors;
    }

    private static IReadOnlyList<AiWorkflow> ParseFile(RepositoryFile file)
    {
        var workflows = new List<AiWorkflow>();
        var inWorkflows = false;
        AiWorkflow? current = null;

        foreach (var rawLine in file.Content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var withoutComment = ParserText.RemoveComment(rawLine);
            if (string.IsNullOrWhiteSpace(withoutComment))
            {
                continue;
            }

            var indent = withoutComment.TakeWhile(char.IsWhiteSpace).Count();
            var line = withoutComment.Trim();

            if (indent == 0)
            {
                inWorkflows = line.Equals("aiWorkflows:", StringComparison.OrdinalIgnoreCase);
                current = null;
                continue;
            }

            if (!inWorkflows)
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                current = new AiWorkflow { SourceFile = ParserText.NormalizePath(file.Path) };
                workflows.Add(current);
                Apply(current, line[2..].Trim());
            }
            else if (current is not null)
            {
                Apply(current, line);
            }
        }

        return workflows;
    }

    private static void Apply(AiWorkflow workflow, string line)
    {
        var index = line.IndexOf(':', StringComparison.Ordinal);
        if (index < 0)
        {
            return;
        }

        var key = line[..index].Trim();
        var value = SpendGovConfigParser.Unquote(line[(index + 1)..].Trim());
        switch (key)
        {
            case "id":
                workflow.Id = value;
                break;
            case "provider":
                workflow.Provider = value;
                break;
            case "model":
                workflow.Model = value;
                break;
            case "estimatedMonthlyRequests":
                workflow.EstimatedMonthlyRequests = ParseInt(value);
                break;
            case "averageInputTokens":
                workflow.AverageInputTokens = ParseInt(value);
                break;
            case "averageOutputTokens":
                workflow.AverageOutputTokens = ParseInt(value);
                break;
            case "maxOutputTokens":
                workflow.MaxOutputTokens = ParseInt(value);
                break;
            case "maxAgentSteps":
                workflow.MaxAgentSteps = ParseInt(value);
                break;
            case "environment":
                workflow.Environment = value;
                break;
            case "tenant":
                workflow.Tenant = value;
                break;
            case "feature":
                workflow.Feature = value;
                break;
        }
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}

public sealed class AiModelPriceCatalog
{
    private readonly List<AiModelPrice> prices =
    [
        new("openai", "gpt-4.1-mini", 0.40m, 1.60m, "EUR", new DateOnly(2026, 1, 1), "Seed model catalog"),
        new("openai", "gpt-4.1", 2.00m, 8.00m, "EUR", new DateOnly(2026, 1, 1), "Seed model catalog"),
        new("openai", "gpt-4o-mini", 0.14m, 0.56m, "EUR", new DateOnly(2026, 1, 1), "Seed model catalog"),
        new("azure-openai", "gpt-4.1-mini", 0.40m, 1.60m, "EUR", new DateOnly(2026, 1, 1), "Workspace default catalog"),
        new("azure-openai", "gpt-4.1", 2.00m, 8.00m, "EUR", new DateOnly(2026, 1, 1), "Workspace default catalog"),
        new("anthropic", "claude-3-5-sonnet", 3.00m, 15.00m, "EUR", new DateOnly(2026, 1, 1), "Seed model catalog")
    ];

    public AiModelPrice? Find(string provider, string model)
    {
        return prices.FirstOrDefault(price =>
            price.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)
            && price.Model.Equals(model, StringComparison.OrdinalIgnoreCase));
    }

    public void Upsert(AiModelPrice price)
    {
        prices.RemoveAll(existing =>
            existing.Provider.Equals(price.Provider, StringComparison.OrdinalIgnoreCase)
            && existing.Model.Equals(price.Model, StringComparison.OrdinalIgnoreCase));
        prices.Add(price);
    }

    public IReadOnlyList<AiModelPrice> List() => prices.ToArray();
}
