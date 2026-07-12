using System.Text;

namespace SpendGovernor.Core;

public static class GitHubActionsAnnotationWriter
{
    public static string Render(IEnumerable<SpendFinding> findings, SpendFindingSeverity minimumSeverity)
    {
        var builder = new StringBuilder();
        foreach (var finding in findings
            .Where(finding => Rank(finding.Severity) >= Rank(minimumSeverity))
            .Take(50))
        {
            builder.AppendLine(Render(finding));
        }

        return builder.ToString();
    }

    public static string Render(SpendFinding finding)
    {
        var command = finding.Severity switch
        {
            SpendFindingSeverity.Error => "error",
            SpendFindingSeverity.Warning => "warning",
            _ => "notice"
        };

        var properties = new List<string> { "title=Cloud & AI Spend Governor" };
        if (!string.IsNullOrWhiteSpace(finding.SourceFile))
        {
            properties.Add($"file={EscapeProperty(finding.SourceFile)}");
            properties.Add($"line={finding.StartLine ?? 1}");
            if (finding.StartColumn is { } column)
            {
                properties.Add($"col={column}");
            }

            if (finding.EndLine is { } endLine)
            {
                properties.Add($"endLine={endLine}");
            }

            if (finding.EndColumn is { } endColumn)
            {
                properties.Add($"endColumn={endColumn}");
            }
        }

        var message = $"{finding.RuleId}: {finding.Message}";
        if (!string.IsNullOrWhiteSpace(finding.Recommendation))
        {
            message = $"{message} Recommendation: {finding.Recommendation}";
        }

        return $"::{command} {string.Join(',', properties)}::{EscapeData(message)}";
    }

    private static string EscapeData(string value)
    {
        return value
            .Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
    }

    private static string EscapeProperty(string value)
    {
        return EscapeData(value)
            .Replace(":", "%3A", StringComparison.Ordinal)
            .Replace(",", "%2C", StringComparison.Ordinal);
    }

    private static int Rank(SpendFindingSeverity severity) => severity switch
    {
        SpendFindingSeverity.Error => 3,
        SpendFindingSeverity.Warning => 2,
        _ => 1
    };
}
