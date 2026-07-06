using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Services;

namespace SpendGovernor.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        return SpendGovCliRunner.RunAsync(args, Console.Out, Console.Error);
    }
}

public static class SpendGovCliRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static SpendGovCliRunner()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            await output.WriteLineAsync(Usage);
            return 0;
        }

        var command = args[0].Equals("scan", StringComparison.OrdinalIgnoreCase)
            ? "scan"
            : "";
        if (command.Length == 0)
        {
            await error.WriteLineAsync($"Unknown command: {args[0]}");
            await error.WriteLineAsync(Usage);
            return 1;
        }

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args.Skip(1).ToArray());
        }
        catch (CliUsageException ex)
        {
            await error.WriteLineAsync(ex.Message);
            await error.WriteLineAsync(Usage);
            return 1;
        }

        if (options.ShowHelp)
        {
            await output.WriteLineAsync(Usage);
            return 0;
        }

        try
        {
            var report = RunScan(options);
            await WriteReportAsync(options.MarkdownReportPath, report.Markdown, output);
            await WriteReportAsync(options.JsonReportPath, JsonSerializer.Serialize(report.Json, JsonOptions), output);

            var summary = $"SpendGov decision: {report.Json.Decision}; monthly delta: {FormatMoney(report.Json.MonthlyDelta, report.Json.Currency)}; confidence: {report.Json.Confidence}; check: {report.Json.CheckConclusion}";
            if (options.MarkdownReportPath == "-")
            {
                await error.WriteLineAsync(summary);
            }
            else
            {
                await output.WriteLineAsync(summary);
                await output.WriteLineAsync($"Markdown report: {NormalizeDisplayPath(options.MarkdownReportPath)}");
                await output.WriteLineAsync($"JSON report: {NormalizeDisplayPath(options.JsonReportPath)}");
            }

            return DetermineExitCode(report.Json, options.FailOn);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            await error.WriteLineAsync($"spendgov scan failed: {ex.Message}");
            return 1;
        }
    }

    private static SpendGovCliReport RunScan(CliOptions options)
    {
        var proposedRoot = ResolveRoot(options.ScanPath);
        var baselineRoot = string.IsNullOrWhiteSpace(options.BaselinePath) ? null : ResolveRoot(options.BaselinePath);
        var proposedFiles = LoadRepositoryFiles(proposedRoot);
        var baselineFiles = baselineRoot is null ? Array.Empty<RepositoryFile>() : LoadRepositoryFiles(baselineRoot);
        var changedFiles = options.ChangedFiles.Count > 0
            ? options.ChangedFiles.Select(NormalizeRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : proposedFiles.Select(file => file.Path).ToArray();

        var repository = ParseRepository(options.Repository, options.RepositoryOwner, options.RepositoryName);
        var settings = new ProjectSettings
        {
            Provider = options.Provider,
            Currency = options.Currency,
            DefaultRegion = options.DefaultRegion,
            HoursPerMonth = options.HoursPerMonth,
            PolicyYaml = proposedFiles.FirstOrDefault(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.SpendGovConfig)?.Content
                ?? PolicyConfig.DefaultYaml
        };

        var request = new AnalysisRequest
        {
            ProjectId = options.ProjectId,
            RepositoryOwner = repository.Owner,
            RepositoryName = repository.Name,
            PullRequestNumber = options.PullRequestNumber,
            BaseBranch = options.BaseBranch,
            HeadBranch = options.HeadBranch,
            CommitSha = options.CommitSha,
            ChangedFiles = changedFiles,
            BaselineFiles = baselineFiles,
            ProposedFiles = proposedFiles,
            Settings = settings,
            DashboardBaseUrl = options.DashboardUrl
        };

        var engine = new AnalysisEngine(new MonthlyCostEstimator(JsonPricingCatalogService.LoadDefault()));
        var result = engine.Analyze(request);
        var json = SpendGovJsonReport.FromResult(result, proposedRoot, baselineRoot, options);
        return new SpendGovCliReport(result.CommentMarkdown, json);
    }

    private static RepositoryIdentity ParseRepository(string? repository, string owner, string name)
    {
        if (!string.IsNullOrWhiteSpace(repository))
        {
            var parts = repository.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                return new RepositoryIdentity(parts[0], parts[1]);
            }
        }

        return new RepositoryIdentity(owner, name);
    }

    private static string ResolveRoot(string path)
    {
        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
        {
            throw new DirectoryNotFoundException($"Scan path does not exist: {resolved}");
        }

        return resolved;
    }

    private static RepositoryFile[] LoadRepositoryFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(root, path))
            .Select(path => new
            {
                Absolute = path,
                Relative = NormalizeRelativePath(Path.GetRelativePath(root, path))
            })
            .Where(file => FileDiscovery.Detect(file.Relative).IsRelevant)
            .Select(file => new RepositoryFile(file.Relative, File.ReadAllText(file.Absolute)))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsExcludedPath(string root, string path)
    {
        var relative = NormalizeRelativePath(Path.GetRelativePath(root, path));
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is ".git" or ".github" or ".vs" or ".idea" or ".codex" or ".agents" or "bin" or "obj" or "node_modules");
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static async Task WriteReportAsync(string path, string content, TextWriter stdout)
    {
        if (path == "-")
        {
            await stdout.WriteLineAsync(content);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(fullPath, content);
    }

    private static int DetermineExitCode(SpendGovJsonReport report, FailOnLevel failOn)
    {
        if (report.Status == AnalysisStatus.Failed)
        {
            return 3;
        }

        if (failOn == FailOnLevel.Never)
        {
            return 0;
        }

        var severity = PolicyEngine.Severity(report.PolicyStatus);
        var threshold = failOn == FailOnLevel.Warn
            ? PolicyEngine.Severity(PolicyAction.Warn)
            : PolicyEngine.Severity(PolicyAction.ApprovalRequired);
        return severity >= threshold ? 2 : 0;
    }

    private static bool IsHelp(string value)
    {
        return value is "-h" or "--help" or "help";
    }

    private static string NormalizeDisplayPath(string path)
    {
        return path == "-" ? "stdout" : Path.GetFullPath(path);
    }

    private static string FormatMoney(decimal? amount, string currency)
    {
        return amount is null ? "not available" : $"{currency.ToUpperInvariant()} {amount.Value.ToString("0.00", CultureInfo.InvariantCulture)}";
    }

    private const string Usage =
        """
        spendgov scan [options]

        Runs Cloud & AI Spend Governor checks locally or in CI without deploying the web app.

        Options:
          --path, -p <directory>            Proposed repository/worktree path. Defaults to current directory.
          --baseline-path <directory>       Optional baseline directory for before/after comparisons.
          --changed-file <path>             Repeatable changed file path. Defaults to all detected relevant files.
          --changed-files <paths>           Comma/semicolon/newline separated changed file paths.
          --markdown <path|->               Markdown report path, or '-' for stdout. Defaults to spendgov-report.md.
          --json <path|->                   JSON report path, or '-' for stdout. Defaults to spendgov-report.json.
          --fail-on <fail|warn|never>       Exit non-zero on FAIL, WARN-or-higher, or never. Defaults to fail.
          --repository <owner/name>         Repository identity for report links.
          --repo-owner <owner>              Repository owner. Defaults to local.
          --repo-name <name>                Repository name. Defaults to repository.
          --pr-number <number>              Pull Request number for report metadata. Defaults to 0.
          --base-branch <branch>            Base branch name. Defaults to main.
          --head-branch <branch>            Head branch name. Defaults to local.
          --commit-sha <sha>                Commit SHA for report metadata. Defaults to local.
          --currency <code>                 Currency code. Defaults to EUR.
          --default-region <region>         Azure default region. Defaults to westeurope.
          --hours-per-month <hours>         Monthly hours assumption. Defaults to 730.
          --dashboard-url <url>             Optional dashboard base URL for report links.
          --help                            Show help.

        Exit codes:
          0 success
          1 CLI usage or file I/O error
          2 policy threshold failed for the configured --fail-on level
          3 scan engine failed unexpectedly
        """;
}

public sealed record SpendGovCliReport(string Markdown, SpendGovJsonReport Json);

public sealed record RepositoryIdentity(string Owner, string Name);

public enum FailOnLevel
{
    Fail,
    Warn,
    Never
}

public sealed class CliOptions
{
    public string ScanPath { get; private init; } = ".";
    public string? BaselinePath { get; private init; }
    public string MarkdownReportPath { get; private init; } = "spendgov-report.md";
    public string JsonReportPath { get; private init; } = "spendgov-report.json";
    public FailOnLevel FailOn { get; private init; } = FailOnLevel.Fail;
    public string? Repository { get; private init; }
    public string RepositoryOwner { get; private init; } = "local";
    public string RepositoryName { get; private init; } = "repository";
    public int PullRequestNumber { get; private init; }
    public string BaseBranch { get; private init; } = "main";
    public string HeadBranch { get; private init; } = "local";
    public string CommitSha { get; private init; } = "local";
    public string Provider { get; private init; } = "azure";
    public string Currency { get; private init; } = "EUR";
    public string DefaultRegion { get; private init; } = "westeurope";
    public int HoursPerMonth { get; private init; } = 730;
    public Guid ProjectId { get; private init; } = Guid.NewGuid();
    public string? DashboardUrl { get; private init; }
    public bool ShowHelp { get; private init; }
    public IReadOnlyList<string> ChangedFiles { get; private init; } = [];

    public static CliOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changedFiles = new List<string>();
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is "-h" or "--help" or "help")
            {
                showHelp = true;
                continue;
            }

            if (arg is "--changed-file")
            {
                changedFiles.Add(ReadValue(args, ref index, arg));
                continue;
            }

            if (arg is "--changed-files")
            {
                changedFiles.AddRange(SplitChangedFiles(ReadValue(args, ref index, arg)));
                continue;
            }

            var key = arg switch
            {
                "-p" => "--path",
                "--path" or "--baseline-path" or "--markdown" or "--markdown-report" or "--json" or "--json-report"
                    or "--fail-on" or "--repository" or "--repo-owner" or "--repo-name" or "--pr-number"
                    or "--base-branch" or "--head-branch" or "--commit-sha" or "--provider" or "--currency"
                    or "--default-region" or "--hours-per-month" or "--dashboard-url" => arg,
                _ => throw new CliUsageException($"Unknown option: {arg}")
            };

            values[key] = ReadValue(args, ref index, arg);
        }

        return new CliOptions
        {
            ShowHelp = showHelp,
            ScanPath = Get(values, "--path", "."),
            BaselinePath = GetNullable(values, "--baseline-path"),
            MarkdownReportPath = Get(values, "--markdown", Get(values, "--markdown-report", "spendgov-report.md")),
            JsonReportPath = Get(values, "--json", Get(values, "--json-report", "spendgov-report.json")),
            FailOn = ParseFailOn(Get(values, "--fail-on", "fail")),
            Repository = GetNullable(values, "--repository"),
            RepositoryOwner = Get(values, "--repo-owner", "local"),
            RepositoryName = Get(values, "--repo-name", "repository"),
            PullRequestNumber = ParseInt(Get(values, "--pr-number", "0"), "--pr-number"),
            BaseBranch = Get(values, "--base-branch", "main"),
            HeadBranch = Get(values, "--head-branch", "local"),
            CommitSha = Get(values, "--commit-sha", "local"),
            Provider = Get(values, "--provider", "azure"),
            Currency = Get(values, "--currency", "EUR"),
            DefaultRegion = Get(values, "--default-region", "westeurope"),
            HoursPerMonth = ParseInt(Get(values, "--hours-per-month", "730"), "--hours-per-month"),
            DashboardUrl = GetNullable(values, "--dashboard-url"),
            ChangedFiles = changedFiles
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            throw new CliUsageException($"Missing value for {option}.");
        }

        index++;
        return args[index];
    }

    private static IEnumerable<string> SplitChangedFiles(string value)
    {
        return value.Split([',', ';', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static FailOnLevel ParseFailOn(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "fail" or "failure" or "block" => FailOnLevel.Fail,
            "warn" or "warning" => FailOnLevel.Warn,
            "never" or "none" => FailOnLevel.Never,
            _ => throw new CliUsageException("--fail-on must be one of: fail, warn, never.")
        };
    }

    private static int ParseInt(string value, string option)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            return parsed;
        }

        throw new CliUsageException($"{option} must be a non-negative integer.");
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    private static string? GetNullable(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}

public sealed class CliUsageException : Exception
{
    public CliUsageException(string message)
        : base(message)
    {
    }
}

public sealed record SpendGovJsonReport(
    string Tool,
    string Version,
    DateTimeOffset GeneratedAt,
    string Repository,
    int PullRequestNumber,
    string BaseBranch,
    string HeadBranch,
    string CommitSha,
    AnalysisStatus Status,
    PolicyAction PolicyStatus,
    string Decision,
    string CheckConclusion,
    string? Environment,
    ConfidenceLevel Confidence,
    string Currency,
    decimal? BaselineMonthlyCost,
    decimal? ProposedMonthlyCost,
    decimal? MonthlyDelta,
    decimal? BudgetLimitMonthly,
    int UnknownResourceCount,
    string? BudgetSource,
    string ScanPath,
    string? BaselinePath,
    IReadOnlyList<string> ConfigErrors,
    IReadOnlyList<ResourceJsonItem> Resources,
    IReadOnlyList<CostChangeJsonItem> CostChanges,
    IReadOnlyList<PolicyFindingJsonItem> PolicyFindings,
    IReadOnlyList<RecommendationJsonItem> Recommendations)
{
    public static SpendGovJsonReport FromResult(AnalysisResult result, string scanPath, string? baselinePath, CliOptions options)
    {
        var analysis = result.Analysis;
        return new SpendGovJsonReport(
            "spendgov",
            typeof(SpendGovCliRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            DateTimeOffset.UtcNow,
            $"{analysis.RepositoryOwner}/{analysis.RepositoryName}",
            analysis.PullRequestNumber,
            analysis.BaseBranch,
            analysis.HeadBranch,
            analysis.CommitSha,
            analysis.Status,
            analysis.PolicyStatus,
            ToDecision(analysis.PolicyStatus),
            result.CheckConclusion,
            analysis.Environment,
            analysis.OverallConfidence,
            analysis.Currency,
            analysis.BaselineMonthlyCost,
            analysis.ProposedMonthlyCost,
            analysis.MonthlyDelta,
            analysis.BudgetLimitMonthly,
            analysis.UnknownResourceCount,
            analysis.BudgetSource,
            scanPath,
            baselinePath,
            result.ConfigErrors,
            result.ProposedResources.Select(ResourceJsonItem.FromResource).ToArray(),
            result.CostChanges.Select(CostChangeJsonItem.FromChange).ToArray(),
            result.PolicyFindings.Select(PolicyFindingJsonItem.FromFinding).ToArray(),
            result.Recommendations.Select(RecommendationJsonItem.FromRecommendation).ToArray());
    }

    private static string ToDecision(PolicyAction action)
    {
        return action is PolicyAction.Block or PolicyAction.ApprovalRequired ? "FAIL" : action == PolicyAction.Warn ? "WARN" : "PASS";
    }
}

public sealed record ResourceJsonItem(
    string Name,
    string Type,
    string? AnalysisSource,
    string? SourceFile,
    string? Provider,
    string? Region,
    string? Sku,
    string? Environment,
    CostCategory Category,
    decimal? MonthlyCost,
    decimal? MonthlyDelta,
    ConfidenceLevel Confidence,
    EstimateStatus Status,
    string? PricingSource,
    string? PricingCatalogVersion,
    string? PricingMatchType,
    string? PricingFallbackReason,
    string? ArmResourceType,
    string? MappedResourceType,
    string? TerraformAddress,
    string? TerraformActions)
{
    public static ResourceJsonItem FromResource(ResourceEstimate resource)
    {
        return new ResourceJsonItem(
            resource.ResourceName,
            resource.ResourceType,
            resource.AnalysisSource,
            resource.SourceFile,
            resource.Provider,
            resource.Region,
            resource.Sku,
            resource.Environment,
            resource.Category,
            resource.MonthlyCost,
            resource.MonthlyDelta,
            resource.Confidence,
            resource.Status,
            resource.PricingSource ?? resource.PriceSource,
            resource.PricingCatalogVersion,
            resource.PricingMatchType,
            resource.PricingFallbackReason,
            resource.ArmResourceType,
            resource.MappedResourceType,
            resource.TerraformAddress,
            resource.TerraformActions);
    }
}

public sealed record CostChangeJsonItem(
    string Resource,
    string Type,
    string ChangeKind,
    string? Before,
    string? After,
    decimal MonthlyDelta,
    string? Region,
    string? Reason,
    string? TerraformAddress)
{
    public static CostChangeJsonItem FromChange(ResourceCostChange change)
    {
        return new CostChangeJsonItem(
            change.ResourceName,
            change.ResourceType,
            change.ChangeKind,
            change.BeforeSummary ?? change.BeforeSku,
            change.AfterSummary ?? change.AfterSku,
            change.MonthlyDelta,
            change.Region,
            change.Reason,
            change.TerraformAddress);
    }
}

public sealed record PolicyFindingJsonItem(
    string RuleId,
    PolicyAction Action,
    string Message,
    decimal? ActualValue,
    decimal? ThresholdValue)
{
    public static PolicyFindingJsonItem FromFinding(PolicyFinding finding)
    {
        return new PolicyFindingJsonItem(
            finding.RuleId,
            finding.Action,
            finding.Message,
            finding.ActualValue,
            finding.ThresholdValue);
    }
}

public sealed record RecommendationJsonItem(
    string Severity,
    string Title,
    string Description,
    decimal? EstimatedMonthlySavings)
{
    public static RecommendationJsonItem FromRecommendation(Recommendation recommendation)
    {
        return new RecommendationJsonItem(
            recommendation.Severity,
            recommendation.Title,
            recommendation.Description,
            recommendation.EstimatedMonthlySavings);
    }
}
