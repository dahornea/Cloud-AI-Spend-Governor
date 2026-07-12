using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpendGovernor.Core;

public static class SarifReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Render(AnalysisResult result)
    {
        var findings = result.Findings;
        var rules = findings
            .GroupBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new
                {
                    id = first.RuleId,
                    name = first.Title,
                    shortDescription = new { text = first.Title },
                    fullDescription = new { text = first.Message },
                    help = string.IsNullOrWhiteSpace(first.Recommendation)
                        ? null
                        : new { text = first.Recommendation },
                    properties = new
                    {
                        category = first.Category,
                        severity = FormatSeverity(first.Severity)
                    }
                };
            })
            .ToArray();

        var run = new
        {
            tool = new
            {
                driver = new
                {
                    name = "Cloud & AI Spend Governor",
                    informationUri = "https://github.com/",
                    rules
                }
            },
            results = findings.Select(finding => ToResult(result, finding)).ToArray()
        };
        var report = new Dictionary<string, object>
        {
            ["version"] = "2.1.0",
            ["$schema"] = "https://json.schemastore.org/sarif-2.1.0.json",
            ["runs"] = new[] { run }
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static object ToResult(AnalysisResult result, SpendFinding finding)
    {
        return new
        {
            ruleId = finding.RuleId,
            level = FormatSeverity(finding.Severity),
            message = new { text = finding.Message },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new
                        {
                            uri = string.IsNullOrWhiteSpace(finding.SourceFile)
                                ? $"spendgov://scan/{result.Analysis.Id}"
                                : finding.SourceFile
                        },
                        region = new
                        {
                            startLine = finding.StartLine ?? 1,
                            startColumn = finding.StartColumn ?? 1,
                            endLine = finding.EndLine,
                            endColumn = finding.EndColumn
                        }
                    }
                }
            },
            properties = BuildProperties(finding)
        };
    }

    private static Dictionary<string, object> BuildProperties(SpendFinding finding)
    {
        var properties = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = finding.Category,
            ["currency"] = finding.Currency
        };

        Add(properties, "recommendation", finding.Recommendation);
        Add(properties, "estimatedMonthlyCost", finding.EstimatedMonthlyCost);
        Add(properties, "estimatedMonthlyDelta", finding.EstimatedMonthlyDelta);
        Add(properties, "confidence", finding.ConfidenceLevel?.ToString());
        Add(properties, "environment", finding.Environment);
        Add(properties, "provider", finding.Provider);
        Add(properties, "resourceType", finding.ResourceType);
        Add(properties, "resourceName", finding.ResourceName);
        Add(properties, "policyId", finding.PolicyId);
        Add(properties, "pricingSource", finding.PricingSource);
        Add(properties, "pricingMatchType", finding.PricingMatchType);
        return properties;
    }

    private static void Add(Dictionary<string, object> properties, string key, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        properties[key] = value;
    }

    private static string FormatSeverity(SpendFindingSeverity severity) => severity switch
    {
        SpendFindingSeverity.Error => "error",
        SpendFindingSeverity.Warning => "warning",
        _ => "note"
    };
}
