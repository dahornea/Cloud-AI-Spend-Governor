using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpendGovernor.Core;
using SpendGovernor.Infrastructure.Persistence;
using SpendGovernor.Infrastructure.Services;

var tests = new List<(string Name, Action Test)>
{
    ("Scenario 1 - PR with no cloud impact is skipped", ScenarioNoCloudImpact),
    ("Scenario 2 - PR adds a small Azure VM", ScenarioSmallVm),
    ("Scenario 3 - SKU change exceeds warning threshold", ScenarioSkuThreshold),
    ("Scenario 4 - Unsupported Terraform resource is unknown", ScenarioUnknownResource),
    ("Scenario 5 - Expensive AI workflow triggers policy", ScenarioExpensiveAiWorkflow),
    ("Scenario 6 - Staging budget requires approval", ScenarioApprovalRequired),
    ("Resource coverage - MVP estimates five Azure resource shapes", ScenarioResourceCoverage),
    (".spendgov.yml validation reports actionable errors", ScenarioPolicyValidation),
    ("Confidence scoring downgrades defaulted region", ScenarioConfidenceScoring),
    ("PR comment uses beta report format", ScenarioPrCommentFormatting),
    ("GitHub PR comments are idempotent", ScenarioIdempotentPrComments),
    ("Persistence creates repository records", () => ScenarioRepositoryPersistence().GetAwaiter().GetResult()),
    ("Persistence creates pull request scans", () => ScenarioCreateScanPersistence().GetAwaiter().GetResult()),
    ("Persistence updates scan from queued to running", () => ScenarioRunningScanPersistence().GetAwaiter().GetResult()),
    ("Persistence marks completed scans and saves child rows", () => ScenarioCompletedScanPersistence().GetAwaiter().GetResult()),
    ("Persistence marks failed scans with failure reason", () => ScenarioFailedScanPersistence().GetAwaiter().GetResult()),
    ("Persistence retrieves dashboard scans, details, and GitHub comment id", () => ScenarioDashboardPersistenceQueries().GetAwaiter().GetResult())
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {name}");
        Console.WriteLine(ex.Message);
    }
}

if (failures > 0)
{
    Console.WriteLine($"{failures} test(s) failed.");
    Environment.Exit(1);
}

Console.WriteLine("All MVP scenario tests passed.");

static AnalysisEngine CreateEngine()
{
    return new AnalysisEngine(new MonthlyCostEstimator(new SeededAzurePricingAdapter(), new AiModelPriceCatalog()));
}

static ProjectSettings Settings(string? yaml = null)
{
    return new ProjectSettings
    {
        Provider = "azure",
        Currency = "EUR",
        DefaultRegion = "westeurope",
        HoursPerMonth = 730,
        PolicyYaml = yaml ?? PolicyConfig.DefaultYaml
    };
}

static AnalysisRequest Request(
    IReadOnlyList<string> changedFiles,
    IReadOnlyList<RepositoryFile> baseline,
    IReadOnlyList<RepositoryFile> proposed,
    ProjectSettings? settings = null,
    int pr = 1)
{
    return new AnalysisRequest
    {
        ProjectId = Guid.NewGuid(),
        RepositoryOwner = "acme",
        RepositoryName = "payments-api",
        PullRequestNumber = pr,
        BaseBranch = "main",
        HeadBranch = "feature/test",
        CommitSha = "abc123",
        ChangedFiles = changedFiles,
        BaselineFiles = baseline,
        ProposedFiles = proposed,
        Settings = settings ?? Settings(),
        DashboardBaseUrl = "http://localhost:5102"
    };
}

static void ScenarioNoCloudImpact()
{
    var result = CreateEngine().Analyze(Request(["README.md"], [], [new RepositoryFile("README.md", "docs")]));
    Assert(result.Analysis.Status == AnalysisStatus.Skipped, "Expected skipped analysis.");
    Assert(result.Analysis.PolicyStatus == PolicyAction.Pass, "Expected pass policy.");
    Assert(result.ProposedResources.Count == 0, "Expected no resource estimates.");
}

static void ScenarioSmallVm()
{
    const string proposed =
        """
        resource "azurerm_linux_virtual_machine" "worker" {
          name     = "worker-dev"
          location = "westeurope"
          size     = "Standard_B2s"
          tags = {
            environment = "dev"
          }
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 2));
    var resource = result.ProposedResources.Single();
    Assert(result.Analysis.Status == AnalysisStatus.Completed, "Expected completed analysis.");
    Assert(resource.Status == EstimateStatus.Estimated, "Expected VM to be estimated.");
    Assert(resource.MonthlyCost > 0, "Expected positive VM monthly cost.");
    Assert(result.Analysis.PolicyStatus == PolicyAction.Pass, "Expected small VM to pass default policy.");
}

static void ScenarioSkuThreshold()
{
    const string baseline =
        """
        resource "azurerm_service_plan" "app_plan" {
          name     = "app-plan-prod"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "S1"
          tags = {
            environment = "production"
          }
        }
        """;
    const string proposed =
        """
        resource "azurerm_service_plan" "app_plan" {
          name     = "app-plan-prod"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "P1v3"
          tags = {
            environment = "production"
          }
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [new RepositoryFile("main.tf", baseline)], [new RepositoryFile("main.tf", proposed)], pr: 3));
    Assert(result.Analysis.MonthlyDelta > 100, "Expected monthly delta above warning threshold.");
    Assert(result.Analysis.PolicyStatus == PolicyAction.Warn, $"Expected warn, got {result.Analysis.PolicyStatus}.");
    Assert(result.PolicyFindings.Any(finding => finding.RuleId == "warn-pr-delta"), "Expected warn-pr-delta finding.");
}

static void ScenarioUnknownResource()
{
    const string policy =
        """
        version: 1
        currency: EUR
        defaultRegion: westeurope
        hoursPerMonth: 730
        rules:
          - id: unknown-resource-demo
            description: Warn on any unknown resource
            type: unknown_resource_count
            threshold: 0
            action: warn
        """;
    const string proposed =
        """
        resource "azurerm_monitor_diagnostic_setting" "logs" {
          name = "send-logs"
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf", ".spendgov.yml"], [], [
        new RepositoryFile(".spendgov.yml", policy),
        new RepositoryFile("main.tf", proposed)
    ], Settings(policy), 4));

    Assert(result.Analysis.UnknownResourceCount == 1, "Expected one unknown resource.");
    Assert(result.ProposedResources.Single(resource => resource.ResourceName == "logs").Status == EstimateStatus.Unsupported, "Expected unsupported status.");
    Assert(result.Analysis.PolicyStatus == PolicyAction.Warn, "Expected unknown resource policy warning.");
}

static void ScenarioExpensiveAiWorkflow()
{
    const string workflow =
        """
        aiWorkflows:
          - id: sales-agent
            provider: azure-openai
            model: gpt-4.1
            estimatedMonthlyRequests: 100000
            averageInputTokens: 4000
            averageOutputTokens: 1200
            maxAgentSteps: 8
            environment: production
        """;

    var result = CreateEngine().Analyze(Request(["ai-spend.yml"], [], [new RepositoryFile("ai-spend.yml", workflow)], pr: 5));
    var ai = result.ProposedResources.Single(resource => resource.Category == CostCategory.Ai);
    Assert(ai.MonthlyCost > 300, "Expected AI workflow cost above budget.");
    Assert(PolicyEngine.Severity(result.Analysis.PolicyStatus) >= PolicyEngine.Severity(PolicyAction.Warn), "Expected AI policy to warn or block.");
    Assert(result.PolicyFindings.Any(finding => finding.RuleId.StartsWith("ai-", StringComparison.OrdinalIgnoreCase)), "Expected AI policy finding.");
    Assert(result.CommentMarkdown.Contains("AI spend", StringComparison.OrdinalIgnoreCase), "Expected AI section in PR comment.");
}

static void ScenarioApprovalRequired()
{
    const string policy =
        """
        version: 1
        currency: EUR
        defaultRegion: westeurope
        hoursPerMonth: 730
        environments:
          staging:
            monthlyBudget: 10
            action: approval_required
        """;
    const string proposed =
        """
        resource "azurerm_linux_virtual_machine" "etl_runner" {
          name     = "etl-runner-staging"
          location = "westeurope"
          size     = "Standard_D4s_v5"
          tags = {
            environment = "staging"
          }
        }
        """;

    var result = CreateEngine().Analyze(Request([".spendgov.yml", "main.tf"], [], [
        new RepositoryFile(".spendgov.yml", policy),
        new RepositoryFile("main.tf", proposed)
    ], Settings(policy), 6));

    Assert(result.Analysis.PolicyStatus == PolicyAction.ApprovalRequired, $"Expected approval_required, got {result.Analysis.PolicyStatus}.");
    Assert(result.CheckConclusion == "failure", "Expected failure check conclusion before approval.");
}

static void ScenarioResourceCoverage()
{
    const string terraform =
        """
        resource "azurerm_service_plan" "app_plan" {
          name     = "app-plan"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "S1"
        }

        resource "azurerm_mssql_database" "db" {
          name     = "app-db"
          sku_name = "S0"
        }

        resource "azurerm_postgresql_flexible_server" "pg" {
          name       = "app-pg"
          sku_name   = "B_Standard_B1ms"
          storage_mb = 32768
        }

        resource "azurerm_redis_cache" "cache" {
          name     = "app-cache"
          sku_name = "Basic"
          family   = "C"
          capacity = 0
        }
        """;
    const string bicep =
        """
        resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
          name: 'appstorage'
          location: 'westeurope'
          sku: {
            name: 'Standard_LRS'
          }
          properties: {
            estimatedGb: 100
          }
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf", "storage.bicep"], [], [
        new RepositoryFile("main.tf", terraform),
        new RepositoryFile("storage.bicep", bicep)
    ], pr: 7));

    var estimatedCount = result.ProposedResources.Count(resource => resource.Status == EstimateStatus.Estimated);
    Assert(estimatedCount >= 5, $"Expected at least five estimated Azure resources, got {estimatedCount}.");
}

static void ScenarioPolicyValidation()
{
    const string invalid =
        """
        version: 1
        currency: GBP
        defaultRegion:
        hoursPerMonth: -1
        budgets:
          dev:
            maxMonthlyDelta: nope
        rules:
          - id: typo-rule
            description: Unknown rule
            type: unknown_rule
            threshold: -5
            action: warn
        environments:
          dev:
            action: block
        ai:
          enabled: true
          monthlyBudget: 0
          maxCostPerWorkflowMonthly: 0
          action: warn
        """;

    var parsed = SpendGovConfigParser.Parse(invalid, Settings(invalid));
    Assert(parsed.Errors.Any(error => error.Contains("Unsupported currency", StringComparison.OrdinalIgnoreCase)), "Expected unsupported currency error.");
    Assert(parsed.Errors.Any(error => error.Contains("Unsupported top-level section 'budgets'", StringComparison.OrdinalIgnoreCase)), "Expected unknown section error.");
    Assert(parsed.Errors.Any(error => error.Contains("unsupported type", StringComparison.OrdinalIgnoreCase)), "Expected unknown rule type error.");
    Assert(parsed.Errors.Any(error => error.Contains("dev", StringComparison.OrdinalIgnoreCase) && error.Contains("monthlyBudget", StringComparison.OrdinalIgnoreCase)), "Expected missing budget error.");
}

static void ScenarioConfidenceScoring()
{
    const string proposed =
        """
        resource "azurerm_linux_virtual_machine" "worker" {
          name = "worker-dev"
          size = "Standard_B2s"
          tags = {
            environment = "dev"
          }
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 8));
    Assert(result.ProposedResources.Single().Confidence == ConfidenceLevel.Medium, "Expected defaulted region to produce medium confidence.");
    Assert(result.Analysis.OverallConfidence == ConfidenceLevel.Medium, "Expected overall confidence to be medium.");
}

static void ScenarioPrCommentFormatting()
{
    const string proposed =
        """
        resource "azurerm_linux_virtual_machine" "worker" {
          name     = "worker-dev"
          location = "westeurope"
          size     = "Standard_B2s"
          tags = {
            environment = "dev"
          }
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 9));
    Assert(result.CommentMarkdown.Contains("Cloud & AI Spend Governor Report", StringComparison.Ordinal), "Expected report heading.");
    Assert(result.CommentMarkdown.Contains("Status: PASS", StringComparison.Ordinal), "Expected uppercase PASS status.");
    Assert(result.CommentMarkdown.Contains("Estimated monthly impact:", StringComparison.Ordinal), "Expected monthly impact line.");
    Assert(result.CommentMarkdown.Contains("Detected resources and workflows", StringComparison.Ordinal), "Expected resources section.");
    Assert(result.CommentMarkdown.Contains("Confidence:", StringComparison.Ordinal), "Expected confidence line.");
    Assert(result.CommentMarkdown.Contains(PrCommentRenderer.Marker, StringComparison.Ordinal), "Expected idempotency marker.");
}

static void ScenarioIdempotentPrComments()
{
    var tracker = new GitHubPrCommentTracker();
    var analysis = new PullRequestAnalysis
    {
        RepositoryOwner = "acme",
        RepositoryName = "payments-api",
        PullRequestNumber = 42,
        CommitSha = "a"
    };

    var first = tracker.Upsert(analysis, "first", "success");
    var second = tracker.Upsert(analysis, "second", "neutral");
    Assert(first.CommentId == second.CommentId, "Expected repeated PR report to update the same comment.");
    Assert(!second.WasCreatedOnLastWrite, "Expected second write to be an update.");
    Assert(second.UpdateCount == 1, "Expected one update count.");
    Assert(second.Body == "second", "Expected latest body to be stored.");
}

static async Task ScenarioRepositoryPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", "123", "456");
    var loaded = await fixture.RepositoryStore.FindByProviderAndFullNameAsync("github", "acme/payments-api");

    Assert(repository.Id != Guid.Empty, "Expected repository id.");
    Assert(loaded is not null, "Expected repository to be loaded.");
    Assert(loaded!.InstallationId == "456", "Expected installation id to be saved.");
}

static async Task ScenarioCreateScanPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = PersistenceRequest();
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);

    Assert(scan.Id != Guid.Empty, "Expected scan id.");
    Assert(scan.Status == ScanStatus.Queued, "Expected queued status.");
    Assert(scan.PullRequestNumber == request.PullRequestNumber, "Expected PR number.");
}

static async Task ScenarioRunningScanPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, PersistenceRequest());
    var started = DateTimeOffset.UtcNow;

    await fixture.ScanStore.MarkRunningAsync(scan.Id, started);
    var scanId = scan.Id;
    var loaded = await fixture.Context.PullRequestScans.SingleAsync(item => item.Id == scanId);

    Assert(loaded.Status == ScanStatus.Running, "Expected running status.");
    Assert(loaded.StartedAt is not null, "Expected started timestamp.");
}

static async Task ScenarioCompletedScanPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = PersistenceRequest();
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    await fixture.ScanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow);
    var result = CreateEngine().Analyze(request);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "777");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);

    Assert(loaded?.Status == ScanStatus.Completed, "Expected completed status.");
    Assert(loaded!.Decision == PolicyDecision.Pass, "Expected pass decision.");
    Assert(loaded.GitHubCommentId == "777", "Expected GitHub comment id.");
    Assert(loaded.CostBreakdownItems.Count > 0, "Expected cost breakdown rows.");
    Assert(loaded.DetectedResources.Count > 0, "Expected detected resource rows.");
    Assert(loaded.ScanAssumptions.Count > 0, "Expected assumption rows.");
    Assert(loaded.PolicyEvaluations.Count > 0, "Expected policy evaluation rows.");
}

static async Task ScenarioFailedScanPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, PersistenceRequest());

    await fixture.ScanStore.MarkFailedAsync(scan.Id, "boom", "888");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);

    Assert(loaded?.Status == ScanStatus.Failed, "Expected failed status.");
    Assert(loaded!.FailureReason == "boom", "Expected failure reason.");
    Assert(loaded.GitHubCommentId == "888", "Expected GitHub comment id to be saved on failure.");
}

static async Task ScenarioDashboardPersistenceQueries()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = PersistenceRequest();
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    await fixture.ScanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow);
    var result = CreateEngine().Analyze(request);
    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "999");

    var latest = await fixture.ScanStore.GetLatestScansForRepositoryAsync(repository.Id);
    var details = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);
    var commentId = await fixture.ScanStore.FindExistingGitHubCommentIdAsync(repository.Id, request.PullRequestNumber);

    Assert(latest.Count == 1, "Expected latest dashboard scan.");
    Assert(details?.DetectedResources.Count > 0, "Expected scan details.");
    Assert(commentId == "999", "Expected existing GitHub comment id.");
}

static AnalysisRequest PersistenceRequest()
{
    const string proposed =
        """
        resource "azurerm_linux_virtual_machine" "worker" {
          name     = "worker-dev"
          location = "westeurope"
          size     = "Standard_B2s"
          tags = {
            environment = "dev"
          }
        }
        """;

    return Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 501);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private SqliteFixture(SqliteConnection connection, SpendGovernorDbContext context)
    {
        this.connection = connection;
        Context = context;
        RepositoryStore = new RepositoryStore(context);
        ResultWriter = new ScanResultWriter(context);
        ScanStore = new ScanStore(context, ResultWriter);
    }

    public SpendGovernorDbContext Context { get; }

    public RepositoryStore RepositoryStore { get; }

    public ScanResultWriter ResultWriter { get; }

    public ScanStore ScanStore { get; }

    public static async Task<SqliteFixture> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SpendGovernorDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new SpendGovernorDbContext(options);
        await context.Database.EnsureCreatedAsync();
        return new SqliteFixture(connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
