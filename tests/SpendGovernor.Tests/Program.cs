using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpendGovernor.Cli;
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
    ("Resource coverage - MVP estimates core Azure resource shapes", ScenarioResourceCoverage),
    ("Pricing Catalog v2 loads Azure and AI catalogs", ScenarioPricingCatalogLoadsAndValidates),
    ("Pricing Catalog v2 matches exact and fallback prices", ScenarioPricingCatalogMatchTypes),
    ("Pricing Catalog v2 calculates AI monthly token costs", ScenarioPricingCatalogAiTokenCost),
    ("Pricing Catalog v2 match quality affects confidence", ScenarioPricingCatalogConfidenceImpact),
    ("Pricing Catalog v2 metadata appears in assumptions and PR markdown", ScenarioPricingCatalogReportMetadata),
    ("Pricing Catalog v2 invalid catalog validation fails", ScenarioPricingCatalogInvalidValidation),
    ("Azure Retail Prices API client builds query and paginates", () => ScenarioAzureRetailClientBuildsQueryAndPaginates().GetAwaiter().GetResult()),
    ("Azure Retail Prices API client handles failures", () => ScenarioAzureRetailClientHandlesFailures().GetAwaiter().GetResult()),
    ("Azure Retail provider matches exact hourly price", () => ScenarioAzureRetailProviderExactHourly().GetAwaiter().GetResult()),
    ("Azure Retail provider handles GB-month and ambiguous matches", () => ScenarioAzureRetailProviderGbMonthAndAmbiguous().GetAwaiter().GetResult()),
    ("Azure Retail hybrid falls back to local catalog", ScenarioAzureRetailHybridFallback),
    ("Azure Retail metadata appears in reports and dashboard", () => ScenarioAzureRetailMetadataReportAndDashboard().GetAwaiter().GetResult()),
    ("Azure Retail region normalization and candidate mapping", ScenarioAzureRetailRegionAndMapping),
    ("CLI emits Markdown and JSON reports", () => ScenarioCliEmitsMarkdownAndJson().GetAwaiter().GetResult()),
    ("CLI returns non-zero when policy fails", () => ScenarioCliPolicyExitCode().GetAwaiter().GetResult()),
    ("Terraform plan JSON files are detected", ScenarioTerraformPlanJsonDetection),
    ("Terraform plan JSON parser maps resource actions", ScenarioTerraformPlanJsonActionMapping),
    ("Terraform plan JSON parser extracts Azure pricing fields", ScenarioTerraformPlanJsonAzureExtraction),
    ("Terraform plan JSON creates cost breakdowns and PR source text", ScenarioTerraformPlanJsonEngineFlow),
    ("Terraform plan JSON modified SKU shows before and after values", ScenarioTerraformPlanJsonSkuUpgrade),
    ("Terraform plan JSON removed resources produce negative deltas", ScenarioTerraformPlanJsonRemovedResource),
    ("Terraform plan JSON unknown resources do not crash", ScenarioTerraformPlanJsonUnknownResource),
    ("Terraform plan JSON invalid input becomes a warning", ScenarioTerraformPlanJsonInvalidInput),
    ("ARM template JSON files are detected and unrelated JSON is rejected", ScenarioArmTemplateJsonDetection),
    ("ARM template JSON parser extracts and maps Azure resources", ScenarioArmTemplateJsonExtraction),
    ("ARM template JSON resolves simple parameters and variables", ScenarioArmTemplateJsonExpressionHandling),
    ("ARM template JSON creates cost breakdowns and PR source text", ScenarioArmTemplateJsonEngineFlow),
    ("ARM template JSON invalid and empty inputs do not crash", ScenarioArmTemplateJsonInvalidAndEmptyInput),
    ("ARM template JSON metadata persists for dashboard details", () => ScenarioArmTemplateJsonPersistence().GetAwaiter().GetResult()),
    (".spendgov.yml validation reports actionable errors", ScenarioPolicyValidation),
    ("Confidence scoring downgrades defaulted region", ScenarioConfidenceScoring),
    ("PR comment uses beta report format", ScenarioPrCommentFormatting),
    ("GitHub PR comments are idempotent", ScenarioIdempotentPrComments),
    ("GitHub webhook signatures verify valid HMACs", ScenarioGitHubSignatureVerification),
    ("GitHub webhook signatures reject invalid HMACs", ScenarioGitHubSignatureInvalid),
    ("GitHub reporter mode selection uses Real only when configured", ScenarioGitHubModeSelection),
    ("GitHub real reporter updates a marker-matched PR comment", () => ScenarioGitHubMarkerUpdate().GetAwaiter().GetResult()),
    ("GitHub real reporter creates a PR comment when no marker exists", () => ScenarioGitHubCommentCreate().GetAwaiter().GetResult()),
    ("GitHub real reporter falls back when stored comment was deleted", () => ScenarioGitHubDeletedCommentFallback().GetAwaiter().GetResult()),
    ("GitHub check run conclusion mapping follows policy status", ScenarioGitHubCheckRunConclusionMapping),
    ("Persistence creates repository records", () => ScenarioRepositoryPersistence().GetAwaiter().GetResult()),
    ("Persistence creates pull request scans", () => ScenarioCreateScanPersistence().GetAwaiter().GetResult()),
    ("Persistence updates scan from queued to running", () => ScenarioRunningScanPersistence().GetAwaiter().GetResult()),
    ("Background scan queue round-trips jobs", () => ScenarioBackgroundScanQueueRoundTrips().GetAwaiter().GetResult()),
    ("Persistence marks completed scans and saves child rows", () => ScenarioCompletedScanPersistence().GetAwaiter().GetResult()),
    ("Persistence marks failed scans with failure reason", () => ScenarioFailedScanPersistence().GetAwaiter().GetResult()),
    ("Persistence maps pricing metadata for dashboard details", () => ScenarioPricingCatalogDashboardPersistence().GetAwaiter().GetResult()),
    ("Persistence saves Terraform plan metadata for dashboard details", () => ScenarioTerraformPlanJsonPersistence().GetAwaiter().GetResult()),
    ("Persistence saves GitHub publishing metadata", () => ScenarioGitHubPublishingMetadataPersistence().GetAwaiter().GetResult()),
    ("Persistence keeps scan result when GitHub publishing fails", () => ScenarioGitHubPublishFailureDoesNotLoseScan().GetAwaiter().GetResult()),
    ("Persistence retrieves dashboard scans, details, and GitHub comment id", () => ScenarioDashboardPersistenceQueries().GetAwaiter().GetResult()),
    ("Private beta model persists users, workspaces, projects, and budgets", () => ScenarioPrivateBetaModelPersistence().GetAwaiter().GetResult()),
    ("Private beta repositories are scoped by project", () => ScenarioProjectScopedRepositoryPersistence().GetAwaiter().GetResult()),
    ("Private beta scoping blocks cross-workspace access", () => ScenarioPrivateBetaCrossWorkspaceScoping().GetAwaiter().GetResult()),
    ("Private beta members are view-only", () => ScenarioPrivateBetaMemberIsViewOnly().GetAwaiter().GetResult())
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

static AnalysisEngine CreateEngine(IPricingCatalogService? pricingCatalogService = null)
{
    return new AnalysisEngine(new MonthlyCostEstimator(pricingCatalogService ?? JsonPricingCatalogService.LoadDefault()));
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
    Assert(ai.Confidence == ConfidenceLevel.Medium, $"Expected AI workflow confidence to be medium, got {ai.Confidence}.");
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

        resource "azurerm_log_analytics_workspace" "logs" {
          name               = "app-logs"
          location           = "westeurope"
          sku                = "PerGB2018"
          estimated_gb       = 25
          retention_in_days  = 30
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
    Assert(estimatedCount >= 6, $"Expected at least six estimated Azure resources, got {estimatedCount}.");
    Assert(result.ProposedResources.Any(resource => resource.ResourceType == "azurerm_log_analytics_workspace" && resource.Status == EstimateStatus.Estimated), "Expected Log Analytics workspace to be estimated.");
}

static void ScenarioPricingCatalogLoadsAndValidates()
{
    var service = JsonPricingCatalogService.LoadDefault();
    var validation = service.Validate();

    Assert(validation.IsValid, "Expected default pricing catalogs to validate.");
    Assert(service.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    }).CatalogVersion == "2026.07.01", "Expected Azure catalog version 2026.07.01.");
    Assert(service.EstimateAiMonthlyCost(new AiPricingLookupRequest
    {
        Provider = "openai",
        Model = "gpt-4.1",
        WorkflowId = "wf",
        EstimatedRunsPerMonth = 1,
        AverageInputTokens = 1,
        AverageOutputTokens = 1,
        Currency = "EUR"
    }).CatalogName == "Local AI MVP Catalog", "Expected AI pricing catalog to load.");
}

static void ScenarioPricingCatalogMatchTypes()
{
    var service = JsonPricingCatalogService.LoadDefault();
    var exact = service.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    });
    var defaultRegion = service.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "northeurope",
        Currency = "EUR"
    });
    var resourceFallback = service.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_container_app",
        ResourceName = "jobs",
        Region = "northeurope",
        Currency = "EUR"
    });
    var unknown = service.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_totally_unknown",
        ResourceName = "mystery",
        Sku = "unknown",
        Region = "westeurope",
        Currency = "EUR"
    });

    Assert(exact.MatchType == PricingMatchType.ExactRegionSkuMatch, $"Expected exact region SKU match, got {exact.MatchType}.");
    Assert(exact.EstimatedMonthlyCost > 200, "Expected P1v3 monthly estimate.");
    Assert(defaultRegion.MatchType == PricingMatchType.DefaultRegionSkuMatch, $"Expected default region fallback, got {defaultRegion.MatchType}.");
    Assert(!string.IsNullOrWhiteSpace(defaultRegion.FallbackReason), "Expected fallback reason.");
    Assert(resourceFallback.MatchType == PricingMatchType.ResourceTypeFallback, $"Expected resource type fallback, got {resourceFallback.MatchType}.");
    Assert(unknown.MatchType == PricingMatchType.ProviderFallback, $"Expected provider fallback, got {unknown.MatchType}.");
}

static void ScenarioPricingCatalogAiTokenCost()
{
    var service = JsonPricingCatalogService.LoadDefault();
    var match = service.EstimateAiMonthlyCost(new AiPricingLookupRequest
    {
        Provider = "openai",
        Model = "gpt-4.1",
        WorkflowId = "sales-agent",
        EstimatedRunsPerMonth = 10_000,
        AverageInputTokens = 8_000,
        AverageOutputTokens = 2_000,
        Currency = "EUR"
    });

    Assert(match.Matched, "Expected AI model price match.");
    Assert(match.EstimatedMonthlyCost == 320.00m, $"Expected 320 EUR monthly AI cost, got {match.EstimatedMonthlyCost}.");
    Assert(match.InputPricePerMillionTokens == 2.0m, "Expected input token price in match.");
    Assert(match.OutputPricePerMillionTokens == 8.0m, "Expected output token price in match.");
}

static void ScenarioPricingCatalogConfidenceImpact()
{
    const string proposed =
        """
        resource "azurerm_service_plan" "api" {
          name     = "api-plan"
          location = "northeurope"
          os_type  = "Linux"
          sku_name = "P1v3"
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 30));
    var resource = result.ProposedResources.Single();

    Assert(resource.PricingMatchType == PricingMatchType.DefaultRegionSkuMatch.ToString(), $"Expected default region pricing match, got {resource.PricingMatchType}.");
    Assert(resource.Confidence == ConfidenceLevel.Medium, $"Expected default region fallback to lower confidence to medium, got {resource.Confidence}.");
}

static void ScenarioPricingCatalogReportMetadata()
{
    const string proposed =
        """
        resource "azurerm_service_plan" "api" {
          name     = "api-plan"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "P1v3"
        }
        """;

    var result = CreateEngine().Analyze(Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 31));

    Assert(result.ProposedResources.Single().PricingCatalogVersion == "2026.07.01", "Expected resource pricing catalog version.");
    Assert(result.CommentMarkdown.Contains("### Pricing metadata", StringComparison.Ordinal), "Expected pricing metadata section.");
    Assert(result.CommentMarkdown.Contains("Catalog version: 2026.07.01", StringComparison.Ordinal), "Expected catalog version in PR markdown.");
    Assert(result.CommentMarkdown.Contains("ExactRegionSkuMatch", StringComparison.Ordinal), "Expected match quality in PR markdown.");
}

static void ScenarioPricingCatalogInvalidValidation()
{
    var invalidAzure = new PricingCatalog
    {
        Provider = "Azure",
        Name = "Invalid",
        Currency = "EUR",
        Items =
        [
            new PricingCatalogItem { Provider = "azure", ResourceType = "azurerm_service_plan", Sku = "B1", Region = "westeurope", Currency = "EUR", Unit = "hour", UnitPrice = -1 }
        ]
    };
    var invalidAi = new PricingCatalog
    {
        Provider = "AI",
        Name = "Invalid AI",
        Version = "2026.07.01",
        Currency = "EUR",
        Items =
        [
            new PricingCatalogItem { Provider = "openai", ResourceType = "ai.workflow", Sku = "gpt-4.1", Currency = "EUR", Unit = "1M tokens", InputPricePerMillionTokens = 1 }
        ]
    };

    var failed = false;
    try
    {
        _ = new JsonPricingCatalogService(invalidAzure, invalidAi);
    }
    catch (PricingCatalogValidationException)
    {
        failed = true;
    }

    Assert(failed, "Expected invalid pricing catalog validation to fail.");
}

static async Task ScenarioAzureRetailClientBuildsQueryAndPaginates()
{
    var handler = new QueueHttpMessageHandler();
    handler.EnqueueJson("""
    {
      "Items": [
        { "currencyCode": "EUR", "unitPrice": 0.287, "armRegionName": "westeurope", "armSkuName": "P1v3", "skuName": "P1v3", "serviceName": "App Service", "productName": "App Service", "meterName": "P1v3", "unitOfMeasure": "1 Hour", "priceType": "Consumption" }
      ],
      "NextPageLink": "https://prices.test/next-page"
    }
    """);
    handler.EnqueueJson("""
    {
      "Items": [
        { "currencyCode": "EUR", "unitPrice": 0.288, "armRegionName": "westeurope", "armSkuName": "P1v3", "skuName": "P1v3", "serviceName": "App Service", "productName": "App Service", "meterName": "P1v3", "unitOfMeasure": "1 Hour", "priceType": "Consumption" }
      ]
    }
    """);

    var client = new AzureRetailPricesClient(
        new HttpClient(handler),
        Options.Create(AzureOptions(baseUrl: "https://prices.test/api/retail/prices")));
    var result = await client.SearchAsync(
        new AzureRetailPriceSearchRequest("serviceName eq 'App Service'", "EUR"),
        CancellationToken.None);

    var firstUrl = Uri.UnescapeDataString(handler.Requests[0].ToString());
    Assert(result.Succeeded, "Expected Azure Retail client result to succeed.");
    Assert(result.Items.Count == 2, "Expected paged items to be aggregated.");
    Assert(firstUrl.Contains("api-version=2023-01-01-preview", StringComparison.Ordinal), "Expected API version in request.");
    Assert(firstUrl.Contains("$filter=", StringComparison.Ordinal), "Expected OData filter in request.");
    Assert(firstUrl.Contains("currencyCode eq 'EUR'", StringComparison.Ordinal), "Expected currencyCode filter.");
    Assert(handler.Requests[1].ToString() == "https://prices.test/next-page", "Expected NextPageLink to be requested.");
}

static async Task ScenarioAzureRetailClientHandlesFailures()
{
    var nonOk = new QueueHttpMessageHandler();
    nonOk.Enqueue(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
    var nonOkClient = new AzureRetailPricesClient(new HttpClient(nonOk), Options.Create(AzureOptions()));
    var nonOkResult = await nonOkClient.SearchAsync(new AzureRetailPriceSearchRequest("serviceName eq 'App Service'", "EUR"), CancellationToken.None);
    Assert(!nonOkResult.Succeeded && nonOkResult.ErrorMessage!.Contains("HTTP 429", StringComparison.Ordinal), "Expected non-200 response to fail gracefully.");

    var invalidJson = new QueueHttpMessageHandler();
    invalidJson.EnqueueText("{ not json");
    var invalidJsonClient = new AzureRetailPricesClient(new HttpClient(invalidJson), Options.Create(AzureOptions()));
    var invalidJsonResult = await invalidJsonClient.SearchAsync(new AzureRetailPriceSearchRequest("serviceName eq 'App Service'", "EUR"), CancellationToken.None);
    Assert(!invalidJsonResult.Succeeded && invalidJsonResult.ErrorMessage!.Contains("invalid JSON", StringComparison.OrdinalIgnoreCase), "Expected invalid JSON to fail gracefully.");

    var timeout = new QueueHttpMessageHandler { Exception = new TaskCanceledException("timeout") };
    var timeoutClient = new AzureRetailPricesClient(new HttpClient(timeout), Options.Create(AzureOptions()));
    var timeoutResult = await timeoutClient.SearchAsync(new AzureRetailPriceSearchRequest("serviceName eq 'App Service'", "EUR"), CancellationToken.None);
    Assert(!timeoutResult.Succeeded && timeoutResult.ErrorMessage!.Contains("timed out", StringComparison.OrdinalIgnoreCase), "Expected timeout to fail gracefully.");
}

static async Task ScenarioAzureRetailProviderExactHourly()
{
    var fake = new FakeAzureRetailPricesClient(RetailResult(RetailItem(
        serviceName: "App Service",
        productName: "App Service",
        skuName: "P1v3",
        armSkuName: "P1v3",
        meterName: "P1v3",
        unit: "1 Hour",
        unitPrice: 0.287m)));
    var provider = new AzureLivePricingProvider(fake, Options.Create(AzureOptions(enabled: true)));
    var result = await provider.TryEstimateMonthlyCostAsync(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "West Europe",
        Currency = "EUR",
        MonthlyHours = 730,
        Quantity = 1
    }, CancellationToken.None);

    Assert(result.Matched, "Expected live Azure Retail match.");
    Assert(result.MatchType == PricingMatchType.AzureRetailExactRegionSkuMatch, $"Expected exact live match, got {result.MatchType}.");
    Assert(result.EstimatedMonthlyCost == 209.51m, $"Expected hourly monthly conversion, got {result.EstimatedMonthlyCost}.");
    Assert(result.SourceType == "AzureRetailPricesApi", "Expected live pricing source type.");
    Assert(result.LiveApiUsed, "Expected live API flag.");
    Assert(result.ConfidenceImpact == PricingConfidenceImpact.Increase, "Expected exact live match to increase confidence.");
    Assert(!fake.Requests.Single().Filter.Contains("currencyCode", StringComparison.OrdinalIgnoreCase), "Currency should be added by client, not mapper.");
    Assert(fake.Requests.Single().Filter.Contains("armRegionName eq 'westeurope'", StringComparison.Ordinal), "Expected normalized region in filter.");

    var cached = await provider.TryEstimateMonthlyCostAsync(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "West Europe",
        Currency = "EUR",
        MonthlyHours = 730,
        Quantity = 1
    }, CancellationToken.None);
    Assert(cached.Matched && fake.Calls == 1, "Expected identical live pricing lookup to use in-memory cache.");
}

static async Task ScenarioAzureRetailProviderGbMonthAndAmbiguous()
{
    var storageProvider = new AzureLivePricingProvider(
        new FakeAzureRetailPricesClient(RetailResult(RetailItem(
            serviceName: "Storage",
            productName: "Storage",
            skuName: "Standard LRS",
            armSkuName: "Standard_LRS",
            meterName: "LRS Data Stored",
            unit: "1 GB/Month",
            unitPrice: 0.02m))),
        Options.Create(AzureOptions(enabled: true)));
    var storage = await storageProvider.TryEstimateMonthlyCostAsync(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_storage_account",
        ResourceName = "data",
        Sku = "Standard_LRS",
        Region = "westeurope",
        Currency = "EUR",
        UsageQuantity = 250
    }, CancellationToken.None);
    Assert(storage.Matched && storage.EstimatedMonthlyCost == 5.00m, $"Expected GB-month conversion, got {storage.EstimatedMonthlyCost}.");

    var ambiguousProvider = new AzureLivePricingProvider(
        new FakeAzureRetailPricesClient(RetailResult(
            RetailItem(serviceName: "App Service", productName: "App Service", skuName: "P1v3", armSkuName: "P1v3", meterName: "P1v3 A", unit: "1 Hour", unitPrice: 0.20m),
            RetailItem(serviceName: "App Service", productName: "App Service", skuName: "P1v3", armSkuName: "P1v3", meterName: "P1v3 B", unit: "1 Hour", unitPrice: 0.20m))),
        Options.Create(AzureOptions(enabled: true)));
    var ambiguous = await ambiguousProvider.TryEstimateMonthlyCostAsync(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    }, CancellationToken.None);
    Assert(ambiguous.MatchType == PricingMatchType.AzureRetailAmbiguousMatch, $"Expected ambiguous match, got {ambiguous.MatchType}.");
    Assert(ambiguous.AmbiguousMatch && ambiguous.ConfidenceImpact == PricingConfidenceImpact.Neutral, "Expected ambiguous match to lower confidence to medium.");

    var unknownUnitProvider = new AzureLivePricingProvider(
        new FakeAzureRetailPricesClient(RetailResult(RetailItem(
            serviceName: "App Service",
            productName: "App Service",
            skuName: "P1v3",
            armSkuName: "P1v3",
            meterName: "Operations",
            unit: "1M Operations",
            unitPrice: 1m))),
        Options.Create(AzureOptions(enabled: true)));
    var unknownUnit = await unknownUnitProvider.TryEstimateMonthlyCostAsync(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    }, CancellationToken.None);
    Assert(!unknownUnit.Matched, "Expected unknown unit to avoid live estimate.");
}

static void ScenarioAzureRetailHybridFallback()
{
    var local = JsonPricingCatalogService.LoadDefault();
    var failedLive = new FakeAzureLivePricingProvider(new PricingMatchResult
    {
        Matched = false,
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR",
        MatchType = PricingMatchType.Unknown,
        ConfidenceImpact = PricingConfidenceImpact.Decrease,
        FallbackReason = "Azure Retail Prices API request failed or timed out."
    });
    var hybrid = new HybridPricingCatalogService(failedLive, local, Options.Create(AzureOptions(enabled: true, fallback: true)));
    var fallback = hybrid.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    });
    Assert(fallback.Matched, "Expected local catalog fallback to provide an estimate.");
    Assert(fallback.FallbackUsed, "Expected fallback flag.");
    Assert(fallback.ConfidenceImpact == PricingConfidenceImpact.Decrease, "Expected live failure fallback to lower confidence.");
    Assert(fallback.FallbackReason!.Contains("Azure Retail Prices API", StringComparison.Ordinal), "Expected fallback reason to mention live API.");

    var noFallback = new HybridPricingCatalogService(failedLive, local, Options.Create(AzureOptions(enabled: true, fallback: false)));
    var unknown = noFallback.EstimateMonthlyCost(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_service_plan",
        ResourceName = "api",
        Sku = "P1v3",
        Region = "westeurope",
        Currency = "EUR"
    });
    Assert(!unknown.Matched && unknown.MatchType == PricingMatchType.Unknown, "Expected unknown pricing when fallback is disabled.");
}

static async Task ScenarioAzureRetailMetadataReportAndDashboard()
{
    var fake = new FakeAzureRetailPricesClient(RetailResult(RetailItem(
        serviceName: "App Service",
        productName: "App Service Premium v3",
        skuName: "P1v3",
        armSkuName: "P1v3",
        meterName: "P1v3",
        unit: "1 Hour",
        unitPrice: 0.287m,
        meterId: "meter-p1v3")));
    var service = new HybridPricingCatalogService(
        new AzureLivePricingProvider(fake, Options.Create(AzureOptions(enabled: true))),
        JsonPricingCatalogService.LoadDefault(),
        Options.Create(AzureOptions(enabled: true)));
    const string proposed =
        """
        resource "azurerm_service_plan" "api" {
          name     = "api-plan"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "P1v3"
        }
        """;
    var request = Request(["main.tf"], [], [new RepositoryFile("main.tf", proposed)], pr: 702);
    var result = CreateEngine(service).Analyze(request);
    var resource = result.ProposedResources.Single();

    Assert(resource.PricingLiveApiUsed, "Expected resource to record live API usage.");
    Assert(resource.PricingMeterName == "P1v3", "Expected meter name metadata.");
    Assert(resource.Confidence == ConfidenceLevel.High, $"Expected exact live match high confidence, got {resource.Confidence}.");
    Assert(result.CommentMarkdown.Contains("Azure Retail Prices API", StringComparison.Ordinal), "Expected PR report live pricing source.");
    Assert(result.CommentMarkdown.Contains("Fallback used: No", StringComparison.Ordinal), "Expected PR report no fallback flag.");
    Assert(result.CommentMarkdown.Contains("Meter: P1v3", StringComparison.Ordinal), "Expected PR report meter.");

    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "123");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);
    var detail = AnalysisDetailResponse.FromScan(loaded!);
    var persisted = detail.Resources.Single();

    Assert(persisted.PricingLiveApiUsed, "Expected dashboard resource live API flag.");
    Assert(persisted.PricingMeterName == "P1v3", "Expected dashboard meter name.");
    Assert(detail.CommentMarkdown.Contains("Azure Retail Prices API", StringComparison.Ordinal), "Expected persisted PR comment live pricing metadata.");
}

static void ScenarioAzureRetailRegionAndMapping()
{
    var normalized = AzureRegionNormalizer.Normalize("EU West", "northeurope", out var defaulted);
    Assert(normalized == "westeurope" && !defaulted, "Expected EU West to normalize to westeurope.");
    var defaultRegion = AzureRegionNormalizer.Normalize(null, "North Europe", out var wasDefaulted);
    Assert(defaultRegion == "northeurope" && wasDefaulted, "Expected missing region to default and normalize.");

    var candidates = AzureRetailPriceQueryMapper.BuildCandidates(new PricingLookupRequest
    {
        Provider = "azure",
        ResourceType = "azurerm_redis_cache",
        ResourceName = "cache",
        Sku = "Premium_P_1",
        Region = "West Europe",
        Currency = "EUR"
    }, AzureOptions(enabled: true), out var region, out _);

    Assert(region == "westeurope", "Expected candidate mapper to normalize region.");
    Assert(candidates.Any(candidate => candidate.Filter.Contains("Azure Cache for Redis", StringComparison.Ordinal)), "Expected Redis service candidate.");
    Assert(candidates.Any(candidate => candidate.Filter.Contains("P1", StringComparison.Ordinal)), "Expected Redis P1 SKU/meter candidate.");
}

static async Task ScenarioCliEmitsMarkdownAndJson()
{
    var root = CreateCliFixture();
    var markdown = Path.Combine(root, "artifacts", "spendgov-report.md");
    var json = Path.Combine(root, "artifacts", "spendgov-report.json");
    var exitCode = await RunCli([
        "scan",
        "--path", root,
        "--markdown", markdown,
        "--json", json,
        "--fail-on", "never",
        "--repository", "acme/shop-api",
        "--pr-number", "42",
        "--head-branch", "feature/dev-premium-redis",
        "--commit-sha", "test123"
    ]);

    Assert(exitCode == 0, $"Expected fail-on never to exit 0, got {exitCode}.");
    Assert(File.Exists(markdown), "Expected CLI markdown report file.");
    Assert(File.Exists(json), "Expected CLI JSON report file.");
    var markdownText = await File.ReadAllTextAsync(markdown);
    Assert(markdownText.Contains("Cloud & AI Spend Governor Report", StringComparison.Ordinal), "Expected PR markdown report heading.");
    Assert(markdownText.Contains("Status: FAIL", StringComparison.Ordinal), "Expected expensive fixture to fail policy in markdown.");

    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(json));
    var rootElement = document.RootElement;
    Assert(rootElement.GetProperty("decision").GetString() == "FAIL", "Expected JSON decision FAIL.");
    Assert(rootElement.GetProperty("repository").GetString() == "acme/shop-api", "Expected repository metadata in JSON.");
    Assert(rootElement.GetProperty("resources").GetArrayLength() > 0, "Expected JSON resources.");
}

static async Task ScenarioCliPolicyExitCode()
{
    var root = CreateCliFixture();
    var exitCode = await RunCli([
        "scan",
        "--path", root,
        "--markdown", Path.Combine(root, "report.md"),
        "--json", Path.Combine(root, "report.json"),
        "--fail-on", "fail",
        "--repository", "acme/shop-api",
        "--pr-number", "42",
        "--head-branch", "feature/dev-premium-redis"
    ]);

    Assert(exitCode == 2, $"Expected policy failure exit code 2, got {exitCode}.");
}

static async Task<int> RunCli(string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var exitCode = await SpendGovCliRunner.RunAsync(args, output, error);
    if (exitCode is not (0 or 2))
    {
        throw new InvalidOperationException($"Unexpected CLI exit code {exitCode}. Output: {output} Error: {error}");
    }

    return exitCode;
}

static string CreateCliFixture()
{
    var root = Path.Combine(Path.GetTempPath(), "spendgov-cli-tests", Guid.NewGuid().ToString("n"));
    var infra = Path.Combine(root, "infra");
    Directory.CreateDirectory(infra);
    File.WriteAllText(Path.Combine(root, ".spendgov.yml"), """
        version: 1
        currency: EUR
        defaultRegion: westeurope
        hoursPerMonth: 730

        rules:
          - id: dev-delta-limit
            description: Block dev PRs above 100 EUR/month
            type: monthly_delta
            threshold: 100
            action: block

        environments:
          dev:
            monthlyBudget: 100
            action: block
        """);
    File.WriteAllText(Path.Combine(infra, "main.tf"), """
        resource "azurerm_service_plan" "app_plan" {
          name     = "app-plan-dev"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "P1v3"

          tags = {
            environment = "dev"
          }
        }

        resource "azurerm_redis_cache" "session_cache" {
          name     = "session-cache-dev"
          location = "westeurope"
          capacity = 1
          family   = "P"
          sku_name = "Premium"

          tags = {
            environment = "dev"
          }
        }
        """);

    return root;
}

static void ScenarioTerraformPlanJsonDetection()
{
    Assert(FileDiscovery.Detect("tfplan.json").Kind == RelevantFileKind.TerraformPlanJson, "Expected root tfplan.json to be detected.");
    Assert(FileDiscovery.Detect("infra/terraform-plan.json").Kind == RelevantFileKind.TerraformPlanJson, "Expected infra Terraform plan JSON to be detected.");
    Assert(FileDiscovery.Detect("terraform/plan.json").Kind == RelevantFileKind.TerraformPlanJson, "Expected terraform/plan.json to be detected.");
    Assert(FileDiscovery.Detect("main.tf").Kind == RelevantFileKind.Terraform, "Expected .tf support to remain intact.");
}

static void ScenarioTerraformPlanJsonActionMapping()
{
    var parser = new TerraformPlanJsonParser();
    var parsed = parser.Parse([
        PlanFile(TerraformPlan(
            StorageCheapCreateChange(),
            ServicePlanUpgradeChange(),
            SqlDeleteChange(),
            ServicePlanReplaceChange(),
            NoOpChange(),
            ReadChange()))
    ], "westeurope", 730, "feature/dev-plan");

    Assert(parsed.Errors.Count == 0, "Expected valid plan JSON.");
    Assert(parsed.ChangeHints.Count == 4, $"Expected no-op/read changes to be ignored, got {parsed.ChangeHints.Count} changes.");
    Assert(parsed.ChangeHints.Any(change => change.ChangeKind == "added"), "Expected create to map to added.");
    Assert(parsed.ChangeHints.Any(change => change.ChangeKind == "changed" && change.TerraformActions == "update"), "Expected update to map to changed.");
    Assert(parsed.ChangeHints.Any(change => change.ChangeKind == "removed"), "Expected delete to map to removed.");
    Assert(parsed.ChangeHints.Any(change => change.ChangeKind == "changed" && change.Reason?.Contains("replaced", StringComparison.OrdinalIgnoreCase) == true), "Expected replace to map to changed with replacement reason.");
}

static void ScenarioTerraformPlanJsonAzureExtraction()
{
    var parser = new TerraformPlanJsonParser();
    var parsed = parser.Parse([PlanFile(TerraformPlan(RedisPremiumCreateChange()))], "westeurope", 730, "feature/dev-plan");
    var resource = parsed.AfterResources.Single();

    Assert(resource.AnalysisSource == TerraformPlanJsonParser.AnalysisSource, "Expected Terraform plan JSON analysis source.");
    Assert(resource.TerraformAddress == "azurerm_redis_cache.main", "Expected Terraform address.");
    Assert(resource.TerraformActions == "create", "Expected Terraform action.");
    Assert(resource.ResourceType == "azurerm_redis_cache", "Expected Redis resource type.");
    Assert(resource.Sku == "Premium_P_1", $"Expected Premium_P_1 SKU, got {resource.Sku}.");
    Assert(resource.Region == "westeurope", "Expected region from plan JSON.");
}

static void ScenarioTerraformPlanJsonEngineFlow()
{
    var result = CreateEngine().Analyze(Request(
        ["infra/tfplan.json"],
        [],
        [PlanFile(TerraformPlan(RedisPremiumCreateChange(), ServicePlanUpgradeChange()))],
        pr: 20));

    Assert(result.Analysis.Status == AnalysisStatus.Completed, "Expected completed plan JSON analysis.");
    Assert(result.ProposedResources.Any(resource => resource.AnalysisSource == TerraformPlanJsonParser.AnalysisSource), "Expected Terraform plan JSON resources.");
    Assert(result.CostChanges.Any(change => change.TerraformAddress == "azurerm_redis_cache.main"), "Expected plan JSON cost breakdown.");
    Assert(result.Analysis.MonthlyDelta > 300, $"Expected high monthly delta, got {result.Analysis.MonthlyDelta}.");
    Assert(result.CommentMarkdown.Contains("Terraform plan JSON was detected and used", StringComparison.Ordinal), "Expected PR comment to mention plan JSON source.");
    Assert(result.CommentMarkdown.Contains("| Resource | Change | Before | After | Estimated monthly delta |", StringComparison.Ordinal), "Expected before/after markdown table.");
}

static void ScenarioTerraformPlanJsonSkuUpgrade()
{
    var result = CreateEngine().Analyze(Request(
        ["infra/tfplan.json"],
        [],
        [PlanFile(TerraformPlan(ServicePlanUpgradeChange()))],
        pr: 21));
    var change = result.CostChanges.Single();

    Assert(change.ChangeKind == "changed", "Expected SKU upgrade to be modified.");
    Assert(change.BeforeSummary == "B1", $"Expected before summary B1, got {change.BeforeSummary}.");
    Assert(change.AfterSummary == "P1v3", $"Expected after summary P1v3, got {change.AfterSummary}.");
    Assert(change.MonthlyDelta > 200, $"Expected App Service SKU upgrade delta above 200, got {change.MonthlyDelta}.");
    Assert(result.Analysis.PolicyStatus is PolicyAction.Block or PolicyAction.Warn, "Expected SKU upgrade to warn or fail policy.");
}

static void ScenarioTerraformPlanJsonRemovedResource()
{
    var result = CreateEngine().Analyze(Request(
        ["infra/tfplan.json"],
        [],
        [PlanFile(TerraformPlan(SqlDeleteChange()))],
        pr: 22));
    var change = result.CostChanges.Single();

    Assert(change.ChangeKind == "removed", "Expected deleted plan resource to map to removed.");
    Assert(change.MonthlyDelta < 0, $"Expected removed resource to produce negative delta, got {change.MonthlyDelta}.");
    Assert(result.ProposedResources.Single().MonthlyCost == 0, "Expected removed resource to be persisted as zero proposed cost.");
}

static void ScenarioTerraformPlanJsonUnknownResource()
{
    var result = CreateEngine().Analyze(Request(
        ["terraform/plan.json"],
        [],
        [new RepositoryFile("terraform/plan.json", TerraformPlan(UnknownCreateChange()))],
        pr: 23));
    var resource = result.ProposedResources.Single();

    Assert(result.Analysis.Status == AnalysisStatus.Completed, "Expected unknown plan resource not to crash.");
    Assert(resource.Status == EstimateStatus.Unsupported, $"Expected unsupported status, got {resource.Status}.");
    Assert(resource.Confidence == ConfidenceLevel.Low, "Expected low confidence for unknown resource type.");
}

static void ScenarioTerraformPlanJsonInvalidInput()
{
    var result = CreateEngine().Analyze(Request(
        ["infra/tfplan.json"],
        [],
        [new RepositoryFile("infra/tfplan.json", "{ nope")],
        pr: 24));

    Assert(result.Analysis.Status == AnalysisStatus.Completed, "Expected invalid plan JSON to become a warning, not a crash.");
    Assert(result.ConfigErrors.Any(error => error.Contains("Terraform plan JSON could not be parsed", StringComparison.OrdinalIgnoreCase)), "Expected parse warning.");
    Assert(result.Analysis.PolicyStatus == PolicyAction.Warn, "Expected validation warning policy status.");
}

static void ScenarioArmTemplateJsonDetection()
{
    Assert(FileDiscovery.Detect("main.json").Kind == RelevantFileKind.ArmTemplateJson, "Expected root main.json to be detected.");
    Assert(FileDiscovery.Detect("infra/main.json").Kind == RelevantFileKind.ArmTemplateJson, "Expected infra main.json to be detected.");
    Assert(FileDiscovery.Detect("bicep/azuredeploy.json").Kind == RelevantFileKind.ArmTemplateJson, "Expected bicep/azuredeploy.json to be detected.");
    Assert(FileDiscovery.Detect("src/main.json").Kind == RelevantFileKind.Other, "Expected unrelated folder main.json to be ignored.");
    Assert(FileDiscovery.Detect("infra/package-lock.json").Kind == RelevantFileKind.Other, "Expected unrelated JSON file name to be ignored.");
    Assert(ArmTemplateJsonParser.IsArmTemplateJson(ArmTemplate(ServicePlanArmResource("api-plan", "B1"))), "Expected valid ARM template JSON.");
    Assert(!ArmTemplateJsonParser.IsArmTemplateJson("""{ "resources": [{ "name": "not-azure" }] }"""), "Expected unrelated JSON resources array to be rejected.");
}

static void ScenarioArmTemplateJsonExtraction()
{
    var parser = new ArmTemplateJsonParser();
    var parsed = parser.Parse([
        ArmFile(ArmTemplate(
            StorageArmResource("demo-storage", "Standard_LRS", "westeurope", 25),
            ServicePlanArmResource("api-plan", "B1"),
            RedisArmResource("redis-prod", "Premium", "P", 1),
            AksArmResource("aks-demo", "Standard_B2s", 2)))
    ], "westeurope", 730, "feature/dev-arm");

    Assert(parsed.Errors.Count == 0, "Expected valid ARM template JSON.");
    Assert(parsed.Resources.Count == 4, $"Expected four parsed ARM resources, got {parsed.Resources.Count}.");
    var servicePlan = parsed.Resources.Single(resource => resource.ResourceName == "api-plan");
    Assert(servicePlan.AnalysisSource == ArmTemplateJsonParser.AnalysisSource, "Expected ARM analysis source.");
    Assert(servicePlan.ArmResourceType == "Microsoft.Web/serverfarms", "Expected original ARM resource type.");
    Assert(servicePlan.ResourceType == "azurerm_service_plan", "Expected mapped pricing resource type.");
    Assert(servicePlan.MappedResourceType == "azurerm_service_plan", "Expected mapped resource metadata.");
    Assert(servicePlan.ArmApiVersion == "2022-03-01", "Expected API version.");
    Assert(servicePlan.Sku == "B1", $"Expected B1 SKU, got {servicePlan.Sku}.");
    Assert(servicePlan.Region == "westeurope", "Expected ARM location.");

    var redis = parsed.Resources.Single(resource => resource.ResourceName == "redis-prod");
    Assert(redis.ResourceType == "azurerm_redis_cache", "Expected Redis mapping.");
    Assert(redis.Sku == "Premium_P_1", $"Expected Premium_P_1 SKU, got {redis.Sku}.");

    var aks = parsed.Resources.Single(resource => resource.ResourceName == "aks-demo");
    Assert(aks.ResourceType == "azurerm_kubernetes_cluster", "Expected AKS mapping.");
    Assert(aks.Sku == "Standard_B2s", "Expected AKS VM size as SKU.");
    Assert(aks.Quantity == 2, "Expected AKS node count.");
}

static void ScenarioArmTemplateJsonExpressionHandling()
{
    var parser = new ArmTemplateJsonParser();
    var parsed = parser.Parse([ArmFile(ParameterizedArmTemplate())], "northeurope", 730, "feature/dev-arm");
    var servicePlan = parsed.Resources.Single(resource => resource.ResourceName == "parameterized-api-plan");
    var unknown = parser.Parse([ArmFile(ArmTemplate(UnknownArmResource()))], "westeurope", 730).Resources.Single();

    Assert(parsed.Errors.Count == 0, "Expected parameterized ARM template to parse.");
    Assert(servicePlan.Region == "westeurope", "Expected location parameter default to resolve.");
    Assert(servicePlan.Sku == "P1v3", $"Expected variable -> parameter SKU resolution, got {servicePlan.Sku}.");
    Assert(servicePlan.Environment == "dev", "Expected environment tag parameter to resolve.");
    Assert(parsed.Warnings.Any(warning => warning.Contains("Complex ARM expression", StringComparison.OrdinalIgnoreCase)), "Expected complex concat expression to be recorded.");
    Assert(servicePlan.Raw.TryGetValue("armParameterResolved", out var resolvedParameters) && resolvedParameters is List<string>, "Expected resolved parameters in raw metadata.");
    Assert(servicePlan.Raw.TryGetValue("armVariableResolved", out var resolvedVariables) && resolvedVariables is List<string>, "Expected resolved variables in raw metadata.");
    Assert(unknown.ResourceType == "Microsoft.Custom/widgets", "Expected unknown ARM resource type to be preserved.");
    Assert(!unknown.IsSupported, "Expected unknown ARM type to be unsupported.");
}

static void ScenarioArmTemplateJsonEngineFlow()
{
    var result = CreateEngine().Analyze(Request(
        ["infra/main.json", "infra/main.bicep"],
        [],
        [
            ArmFile(ArmTemplate(
                RedisArmResource("redis-prod-demo", "Premium", "P", 1),
                ServicePlanArmResource("expensive-api-plan", "P1v3"),
                LogAnalyticsArmResource("expensive-logs", 50))),
            new RepositoryFile("infra/main.bicep", """
            resource rawPlan 'Microsoft.Web/serverfarms@2022-03-01' = {
              name: 'raw-bicep-plan'
              location: 'westeurope'
              sku: {
                name: 'B1'
              }
            }
            """)
        ],
        pr: 25));

    Assert(result.Analysis.Status == AnalysisStatus.Completed, "Expected completed ARM JSON analysis.");
    Assert(result.ProposedResources.Any(resource => resource.AnalysisSource == ArmTemplateJsonParser.AnalysisSource), "Expected ARM resources.");
    Assert(result.ProposedResources.All(resource => resource.ResourceName != "raw-bicep-plan"), "Expected compiled ARM JSON to take priority over raw Bicep fallback.");
    Assert(result.CostChanges.Count >= 3, "Expected ARM cost breakdowns.");
    Assert(result.Analysis.MonthlyDelta > 300, $"Expected high monthly delta, got {result.Analysis.MonthlyDelta}.");
    Assert(result.Analysis.PolicyStatus is PolicyAction.Warn or PolicyAction.Block, "Expected expensive ARM change to warn or fail.");
    Assert(result.CommentMarkdown.Contains("Bicep compiled ARM JSON was detected and used", StringComparison.Ordinal), "Expected PR comment to mention ARM source.");
    Assert(result.CommentMarkdown.Contains("| Resource | ARM type | Mapped type | SKU | Region | Estimated monthly cost | Confidence |", StringComparison.Ordinal), "Expected ARM resource markdown table.");
}

static void ScenarioArmTemplateJsonInvalidAndEmptyInput()
{
    var invalid = CreateEngine().Analyze(Request(
        ["infra/main.json"],
        [],
        [ArmFile("{ nope")],
        pr: 26));
    Assert(invalid.Analysis.Status == AnalysisStatus.Completed, "Expected invalid ARM JSON to become a warning, not a crash.");
    Assert(invalid.ConfigErrors.Any(error => error.Contains("ARM template JSON could not be parsed", StringComparison.OrdinalIgnoreCase)), "Expected ARM parse warning.");
    Assert(invalid.Analysis.PolicyStatus == PolicyAction.Warn, "Expected validation warning policy status.");

    var parser = new ArmTemplateJsonParser();
    var empty = parser.Parse([ArmFile(ArmTemplate())], "westeurope", 730);
    Assert(empty.HasTemplateFiles, "Expected empty ARM template file to be recognized.");
    Assert(empty.Resources.Count == 0, "Expected empty ARM resources array to produce no resources.");
    Assert(empty.Warnings.Any(warning => warning.Contains("empty resources array", StringComparison.OrdinalIgnoreCase)), "Expected empty resources warning.");
}

static async Task ScenarioArmTemplateJsonPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = Request(
        ["infra/main.json"],
        [],
        [ArmFile(ArmTemplate(ServicePlanArmResource("api-plan", "P1v3")))],
        pr: 604);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    var result = CreateEngine().Analyze(request);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "123");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);
    var detected = loaded!.DetectedResources.Single();
    var detail = AnalysisDetailResponse.FromScan(loaded);
    var resource = detail.Resources.Single();

    Assert(detected.RawJson.Contains("Microsoft.Web/serverfarms", StringComparison.Ordinal), "Expected ARM type in detected resource raw JSON.");
    Assert(loaded.ScanAssumptions.Any(assumption => assumption.Name == "ArmTemplateDiffMode" && assumption.Value == ArmTemplateJsonParser.ArmTemplateDiffMode), "Expected ARM diff mode assumption.");
    Assert(detail.AnalysisSource == ArmTemplateJsonParser.AnalysisSource, "Expected dashboard detail source to be ARM JSON.");
    Assert(resource.ArmResourceType == "Microsoft.Web/serverfarms", "Expected dashboard resource ARM type.");
    Assert(resource.MappedResourceType == "azurerm_service_plan", "Expected dashboard resource mapped type.");
    Assert(detail.CommentMarkdown.Contains("Bicep compiled ARM JSON was detected and used", StringComparison.Ordinal), "Expected persisted dashboard PR comment to include ARM source.");
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

static void ScenarioGitHubSignatureVerification()
{
    const string payload = "{\"action\":\"opened\"}";
    const string secret = "dev-secret";
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    Assert(GitHubSignatureVerifier.VerifySha256(payload, signature, secret), "Expected valid GitHub signature.");
}

static void ScenarioGitHubSignatureInvalid()
{
    Assert(!GitHubSignatureVerifier.VerifySha256("{\"action\":\"opened\"}", "sha256=bad", "dev-secret"), "Expected invalid GitHub signature to fail.");
    Assert(!GitHubSignatureVerifier.VerifySha256("{\"action\":\"opened\"}", "", "dev-secret"), "Expected missing GitHub signature to fail.");
}

static void ScenarioGitHubModeSelection()
{
    Assert(!GitHubReporterFactory.ShouldUseReal(new GitHubIntegrationOptions { Mode = GitHubIntegrationMode.Simulated }), "Expected simulated mode to avoid real GitHub API.");
    Assert(GitHubReporterFactory.ShouldUseReal(new GitHubIntegrationOptions { Mode = GitHubIntegrationMode.Real }), "Expected real mode to use real GitHub API.");
}

static async Task ScenarioGitHubMarkerUpdate()
{
    var fake = new FakeGitHubApiClient();
    fake.Comments.Add(new GitHubIssueComment
    {
        Id = 100,
        Body = PrCommentRenderer.Marker + "\nold report",
        HtmlUrl = "https://github.test/comment/100"
    });
    var reporter = CreateRealReporter(fake);

    var result = await reporter.PublishAsync(CreateGitHubPublishRequest());

    Assert(result.PublishingStatus == "Published", "Expected GitHub publish to succeed.");
    Assert(result.CommentId == "100", "Expected marker-matched comment id to be reused.");
    Assert(fake.Events.Contains("list-comments:501"), "Expected marker lookup.");
    Assert(fake.Events.Contains("update-comment:100"), "Expected marker-matched comment update.");
    Assert(fake.Comments.Single().Body?.Contains("Cloud & AI Spend Governor Report", StringComparison.Ordinal) == true, "Expected updated report body.");
}

static async Task ScenarioGitHubCommentCreate()
{
    var fake = new FakeGitHubApiClient();
    var reporter = CreateRealReporter(fake);

    var result = await reporter.PublishAsync(CreateGitHubPublishRequest());

    Assert(result.PublishingStatus == "Published", "Expected GitHub publish to succeed.");
    Assert(result.CommentId == "1000", "Expected created comment id.");
    Assert(fake.Events.Contains("create-comment:501"), "Expected a new PR comment to be created.");
    Assert(fake.Comments.Single().Body?.Contains(PrCommentRenderer.Marker, StringComparison.Ordinal) == true, "Expected created comment to include the marker.");
}

static async Task ScenarioGitHubDeletedCommentFallback()
{
    var fake = new FakeGitHubApiClient();
    fake.DeletedCommentIds.Add("99");
    fake.Comments.Add(new GitHubIssueComment
    {
        Id = 100,
        Body = PrCommentRenderer.Marker + "\nold report",
        HtmlUrl = "https://github.test/comment/100"
    });
    var reporter = CreateRealReporter(fake);

    var result = await reporter.PublishAsync(CreateGitHubPublishRequest(existingCommentId: "99"));

    Assert(result.PublishingStatus == "Published", "Expected GitHub publish to recover from deleted stored comment.");
    Assert(result.CommentId == "100", "Expected fallback marker comment id.");
    Assert(fake.Events.Contains("update-comment:99"), "Expected first update attempt by stored id.");
    Assert(fake.Events.Contains("update-comment:100"), "Expected fallback update by marker.");
}

static void ScenarioGitHubCheckRunConclusionMapping()
{
    Assert(RealGitHubPullRequestReporter.ToCheckRunConclusion(ResultWith(PolicyAction.Pass)) == "success", "Expected pass to map to success.");
    Assert(RealGitHubPullRequestReporter.ToCheckRunConclusion(ResultWith(PolicyAction.Warn)) == "neutral", "Expected warn to map to neutral.");
    Assert(RealGitHubPullRequestReporter.ToCheckRunConclusion(ResultWith(PolicyAction.Block)) == "failure", "Expected block to map to failure.");
    Assert(RealGitHubPullRequestReporter.ToCheckRunConclusion(ResultWith(PolicyAction.ApprovalRequired)) == "failure", "Expected approval required to map to failure.");
    Assert(RealGitHubPullRequestReporter.ToCheckRunConclusion(ResultWith(PolicyAction.Pass, AnalysisStatus.Failed)) == "action_required", "Expected failed scan to map to action_required.");
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

static async Task ScenarioBackgroundScanQueueRoundTrips()
{
    var queue = new ChannelScanJobQueue();
    var job = new QueuedScanJob(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Request(["main.tf"], [], [], pr: 77),
        "corr-test");

    await queue.QueueAsync(job);
    var dequeued = await queue.DequeueAsync();

    Assert(dequeued.ScanId == job.ScanId, "Expected queued scan id to round-trip.");
    Assert(dequeued.ProjectId == job.ProjectId, "Expected queued project id to round-trip.");
    Assert(dequeued.RepositoryId == job.RepositoryId, "Expected queued repository id to round-trip.");
    Assert(dequeued.CorrelationId == "corr-test", "Expected queued correlation id to round-trip.");
    Assert(dequeued.Request.PullRequestNumber == 77, "Expected queued request to round-trip.");
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

static async Task ScenarioPricingCatalogDashboardPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = Request(
        ["main.tf"],
        [],
        [new RepositoryFile("main.tf", """
        resource "azurerm_service_plan" "api" {
          name     = "api-plan"
          location = "westeurope"
          os_type  = "Linux"
          sku_name = "P1v3"
        }
        """)],
        pr: 602);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    var result = CreateEngine().Analyze(request);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "123");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);
    var breakdown = loaded!.CostBreakdownItems.Single();
    var detail = AnalysisDetailResponse.FromScan(loaded);
    var resource = detail.Resources.Single();

    Assert(breakdown.PricingCatalogVersion == "2026.07.01", "Expected cost breakdown catalog version.");
    Assert(breakdown.PricingMatchType == PricingMatchType.ExactRegionSkuMatch.ToString(), "Expected exact pricing match on breakdown.");
    Assert(resource.PricingCatalogVersion == "2026.07.01", "Expected dashboard resource catalog version.");
    Assert(resource.PricingMatchType == PricingMatchType.ExactRegionSkuMatch.ToString(), "Expected dashboard resource match quality.");
    Assert(detail.CommentMarkdown.Contains("### Pricing metadata", StringComparison.Ordinal), "Expected persisted dashboard PR comment to include pricing metadata.");
}

static async Task ScenarioTerraformPlanJsonPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = Request(
        ["infra/tfplan.json"],
        [],
        [PlanFile(TerraformPlan(ServicePlanUpgradeChange()))],
        pr: 601);
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    var result = CreateEngine().Analyze(request);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "123");
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);
    var detected = loaded!.DetectedResources.Single();
    var breakdown = loaded.CostBreakdownItems.Single();
    var detail = AnalysisDetailResponse.FromScan(loaded);

    Assert(detected.TerraformAddress == "azurerm_service_plan.api", "Expected Terraform address on detected resource.");
    Assert(detected.TerraformActions == "update", "Expected Terraform actions on detected resource.");
    Assert(breakdown.BeforeSummary == "B1", "Expected before summary on cost breakdown.");
    Assert(breakdown.AfterSummary == "P1v3", "Expected after summary on cost breakdown.");
    Assert(detail.AnalysisSource == TerraformPlanJsonParser.AnalysisSource, "Expected dashboard detail source to be Terraform plan JSON.");
    Assert(detail.Resources.Single().TerraformAddress == "azurerm_service_plan.api", "Expected dashboard resource Terraform address.");
    Assert(detail.CostChanges.Single().BeforeSummary == "B1", "Expected dashboard before summary.");
}

static async Task ScenarioGitHubPublishingMetadataPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, null);
    var request = PersistenceRequest();
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    var result = CreateEngine().Analyze(request);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, "old");
    await fixture.ScanStore.SaveGitHubPublishingResultAsync(scan.Id, "123", "456", "https://github.test/comment/123", "Published", null);
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);

    Assert(loaded?.GitHubCommentId == "123", "Expected GitHub comment id to be updated.");
    Assert(loaded!.GitHubCheckRunId == "456", "Expected GitHub check run id to be saved.");
    Assert(loaded.GitHubReportUrl == "https://github.test/comment/123", "Expected GitHub report URL to be saved.");
    Assert(loaded.ReportPublishingStatus == "Published", "Expected publishing status.");
    Assert(loaded.ReportPublishingError is null, "Expected no publishing error.");
}

static async Task ScenarioGitHubPublishFailureDoesNotLoseScan()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var repository = await fixture.RepositoryStore.FindOrCreateAsync("github", "acme", "payments-api", "main", null, "123");
    var request = PersistenceRequest();
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);
    var result = CreateEngine().Analyze(request);
    var fake = new FakeGitHubApiClient { ThrowOnCreateComment = true };
    var reporter = CreateRealReporter(fake);

    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, null);
    var publishing = await reporter.PublishAsync(CreateGitHubPublishRequestFor(repository, request, result));
    await fixture.ScanStore.SaveGitHubPublishingResultAsync(
        scan.Id,
        publishing.CommentId,
        publishing.CheckRunId,
        publishing.ReportUrl,
        publishing.PublishingStatus,
        publishing.ErrorMessage);
    var loaded = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);

    Assert(publishing.PublishingStatus == "Failed", "Expected publishing failure result.");
    Assert(loaded?.Status == ScanStatus.Completed, "Expected scan to remain completed after publish failure.");
    Assert(loaded!.DetectedResources.Count > 0, "Expected persisted scan resources to remain available.");
    Assert(loaded.ReportPublishingStatus == "Failed", "Expected publishing failure status to be saved.");
    Assert(!string.IsNullOrWhiteSpace(loaded.ReportPublishingError), "Expected publishing error to be saved.");
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

static async Task ScenarioPrivateBetaModelPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var registered = fixture.Store.RegisterUser("owner@example.com", "secret-passphrase", "Owner One");
    var user = registered.User;
    Assert(user is not null, "Expected registered user.");

    var workspace = fixture.Store.GetWorkspaces(user!.Id).Single();
    var project = fixture.Store.CreateProject(new CreateProjectRequest(
        workspace.Id,
        "Payments API",
        "acme",
        "payments-api",
        "northeurope",
        "EUR",
        730));
    var repository = await fixture.RepositoryStore.FindOrCreateAsync(project.Id, "github", "acme", "payments-api", "main", null, null);

    var budgets = fixture.Store.GetBudgets(project.Id);
    Assert(budgets.Count == 3, "Expected default dev/staging/production budgets.");

    var saved = fixture.Store.UpsertBudget(project.Id, new EnvironmentBudgetUpdateRequest(
        "dev",
        50,
        25,
        "EUR",
        null,
        true));
    var loaded = fixture.Store.GetProjectForUser(project.Id, user.Id);
    var parsed = SpendGovConfigParser.Parse(loaded!.PolicyYaml, ProjectSettings.FromProject(loaded));

    Assert(saved.MaxMonthlyCost == 50, "Expected budget max monthly cost to persist.");
    Assert(loaded.PolicyYaml.Contains("BudgetSource: DatabaseProjectEnvironmentBudget", StringComparison.Ordinal), "Expected DB budget source marker.");
    Assert(parsed.Config.Environments["dev"].MonthlyBudget == 50, "Expected environment budget to feed effective policy YAML.");
    Assert(parsed.Config.Rules.Any(rule => rule.Id == "dev-monthly-delta" && rule.Threshold == 25), "Expected delta budget rule in effective policy YAML.");

    var analysisRequest = PersistenceRequest() with
    {
        ProjectId = project.Id,
        Settings = ProjectSettings.FromProject(loaded)
    };
    var scan = await fixture.ScanStore.CreateScanAsync(repository, analysisRequest);
    await fixture.ScanStore.MarkRunningAsync(scan.Id, DateTimeOffset.UtcNow);
    var result = CreateEngine().Analyze(analysisRequest);
    await fixture.ScanStore.MarkCompletedAsync(scan.Id, result, null);
    var details = await fixture.ScanStore.GetScanDetailsAsync(scan.Id);

    Assert(details!.ScanAssumptions.Any(assumption => assumption.Name == "BudgetSource" && assumption.Value == "DatabaseProjectEnvironmentBudget"), "Expected persisted DB budget source assumption.");
}

static async Task ScenarioProjectScopedRepositoryPersistence()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var user = fixture.Store.GetOrCreateUser("project-owner@example.com");
    var workspace = fixture.Store.GetWorkspaces(user.Id).Single();
    var firstProject = fixture.Store.CreateProject(new CreateProjectRequest(
        workspace.Id,
        "Payments API Dev",
        "acme",
        "payments-api",
        "westeurope",
        "EUR",
        730));
    var secondProject = fixture.Store.CreateProject(new CreateProjectRequest(
        workspace.Id,
        "Payments API Prod",
        "acme",
        "payments-api",
        "westeurope",
        "EUR",
        730));

    var firstRepository = await fixture.RepositoryStore.FindOrCreateAsync(firstProject.Id, "github", "acme", "payments-api", "main", null, null);
    var secondRepository = await fixture.RepositoryStore.FindOrCreateAsync(secondProject.Id, "github", "acme", "payments-api", "main", null, null);
    var loadedFirst = await fixture.RepositoryStore.FindByProjectIdAsync(firstProject.Id);
    var loadedSecond = await fixture.RepositoryStore.FindByProjectIdAsync(secondProject.Id);

    Assert(firstRepository.Id != secondRepository.Id, "Expected same GitHub repo to be tracked separately per project.");
    Assert(loadedFirst?.Id == firstRepository.Id, "Expected first project repository lookup.");
    Assert(loadedSecond?.Id == secondRepository.Id, "Expected second project repository lookup.");
}

static async Task ScenarioPrivateBetaCrossWorkspaceScoping()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var firstUser = fixture.Store.RegisterUser("first@example.com", "secret-passphrase", "First Owner").User!;
    var secondUser = fixture.Store.RegisterUser("second@example.com", "secret-passphrase", "Second Owner").User!;
    var firstWorkspace = fixture.Store.GetWorkspaces(firstUser.Id).Single();
    var project = fixture.Store.CreateProject(new CreateProjectRequest(
        firstWorkspace.Id,
        "Private API",
        "acme",
        "private-api",
        "westeurope",
        "EUR",
        730));
    var repository = await fixture.RepositoryStore.FindOrCreateAsync(project.Id, "github", "acme", "private-api", "main", null, null);
    var request = PersistenceRequest() with { ProjectId = project.Id, RepositoryName = "private-api" };
    var scan = await fixture.ScanStore.CreateScanAsync(repository, request);

    Assert(fixture.Store.GetProjectForUser(project.Id, firstUser.Id) is not null, "Expected owner to access project.");
    Assert(fixture.Store.GetProjectForUser(project.Id, secondUser.Id) is null, "Expected another workspace user to be blocked.");
    Assert(fixture.Store.GetProjectForUser(repository.ProjectId, secondUser.Id) is null, "Expected scan repository project to remain inaccessible.");
    Assert(scan.RepositoryId == repository.Id, "Expected scan to remain linked through repository.");
}

static async Task ScenarioPrivateBetaMemberIsViewOnly()
{
    await using var fixture = await SqliteFixture.CreateAsync();
    var owner = fixture.Store.RegisterUser("owner2@example.com", "secret-passphrase", "Owner Two").User!;
    var member = fixture.Store.RegisterUser("member@example.com", "secret-passphrase", "Member User").User!;
    var workspace = fixture.Store.GetWorkspaces(owner.Id).Single();
    fixture.Context.WorkspaceMembers.Add(new WorkspaceMemberEntity
    {
        WorkspaceId = workspace.Id,
        UserId = member.Id,
        Role = WorkspaceRole.Member
    });
    await fixture.Context.SaveChangesAsync();

    Assert(fixture.Store.CanAccessWorkspace(member.Id, workspace.Id), "Expected member to view workspace.");
    Assert(!fixture.Store.CanEditWorkspace(member.Id, workspace.Id), "Expected member to be blocked from management actions.");
    Assert(fixture.Store.CanEditWorkspace(owner.Id, workspace.Id), "Expected owner to manage workspace.");
}

static RealGitHubPullRequestReporter CreateRealReporter(FakeGitHubApiClient fake, bool enableCheckRuns = false)
{
    var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    var context = new SpendGovernorDbContext(new DbContextOptionsBuilder<SpendGovernorDbContext>().UseSqlite(connection).Options);
    context.Database.EnsureCreated();
    return new RealGitHubPullRequestReporter(
        fake,
        Options.Create(new GitHubIntegrationOptions
        {
            Mode = GitHubIntegrationMode.Real,
            EnableCheckRuns = enableCheckRuns,
            BotCommentMarker = PrCommentRenderer.Marker
        }),
        new SpendGovernorStore(context));
}

static GitHubReportPublishRequest CreateGitHubPublishRequest(string? existingCommentId = null, string? existingCheckRunId = null)
{
    var request = PersistenceRequest() with
    {
        CommitSha = "abc123",
        DashboardBaseUrl = "http://localhost:5102"
    };
    var result = CreateEngine().Analyze(request);
    var repository = new Repository
    {
        Provider = "github",
        Owner = request.RepositoryOwner,
        Name = request.RepositoryName,
        FullName = $"{request.RepositoryOwner}/{request.RepositoryName}",
        DefaultBranch = request.BaseBranch,
        InstallationId = "123"
    };
    return CreateGitHubPublishRequestFor(repository, request, result, existingCommentId, existingCheckRunId);
}

static GitHubReportPublishRequest CreateGitHubPublishRequestFor(
    Repository repository,
    AnalysisRequest request,
    AnalysisResult result,
    string? existingCommentId = null,
    string? existingCheckRunId = null)
{
    var project = new Project
    {
        WorkspaceId = Guid.NewGuid(),
        Name = "Payments API",
        RepositoryOwner = repository.Owner,
        RepositoryName = repository.Name,
        GitHubInstallationId = repository.InstallationId
    };
    return new GitHubReportPublishRequest(project, repository, result.Analysis.Id, result, request, existingCommentId, existingCheckRunId);
}

static AnalysisResult ResultWith(PolicyAction action, AnalysisStatus status = AnalysisStatus.Completed)
{
    return new AnalysisResult
    {
        Analysis = new PullRequestAnalysis
        {
            RepositoryOwner = "acme",
            RepositoryName = "payments-api",
            PullRequestNumber = 1,
            CommitSha = "abc123",
            Status = status,
            PolicyStatus = action,
            OverallConfidence = ConfidenceLevel.High,
            Currency = "EUR"
        },
        CommentMarkdown = PrCommentRenderer.Marker
    };
}

static RepositoryFile PlanFile(string content)
{
    return new RepositoryFile("infra/tfplan.json", content);
}

static RepositoryFile ArmFile(string content, string path = "infra/main.json")
{
    return new RepositoryFile(path, content);
}

static string ArmTemplate(params string[] resources)
{
    return """
           {
             "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
             "contentVersion": "1.0.0.0",
             "parameters": {},
             "variables": {},
             "resources": [
           """ + string.Join(",", resources) + """
             ],
             "outputs": {}
           }
           """;
}

static string StorageArmResource(string name, string sku, string location, int estimatedGb)
{
    return $$"""
             {
               "type": "Microsoft.Storage/storageAccounts",
               "apiVersion": "2023-01-01",
               "name": "{{name}}",
               "location": "{{location}}",
               "sku": {
                 "name": "{{sku}}",
                 "tier": "Standard"
               },
               "kind": "StorageV2",
               "tags": {
                 "environment": "dev"
               },
               "properties": {
                 "accessTier": "Hot",
                 "estimatedGb": {{estimatedGb}}
               }
             }
             """;
}

static string ServicePlanArmResource(string name, string sku)
{
    return $$"""
             {
               "type": "Microsoft.Web/serverfarms",
               "apiVersion": "2022-03-01",
               "name": "{{name}}",
               "location": "westeurope",
               "sku": {
                 "name": "{{sku}}",
                 "tier": "{{(sku.StartsWith("P", StringComparison.OrdinalIgnoreCase) ? "PremiumV3" : "Basic")}}",
                 "capacity": 1
               },
               "kind": "linux",
               "tags": {
                 "environment": "dev"
               },
               "properties": {
                 "reserved": true
               }
             }
             """;
}

static string RedisArmResource(string name, string skuName, string family, int capacity)
{
    return $$"""
             {
               "type": "Microsoft.Cache/Redis",
               "apiVersion": "2023-08-01",
               "name": "{{name}}",
               "location": "westeurope",
               "sku": {
                 "name": "{{skuName}}",
                 "family": "{{family}}",
                 "capacity": {{capacity}}
               },
               "tags": {
                 "environment": "dev"
               },
               "properties": {
                 "enableNonSslPort": false
               }
             }
             """;
}

static string AksArmResource(string name, string vmSize, int nodeCount)
{
    return $$"""
             {
               "type": "Microsoft.ContainerService/managedClusters",
               "apiVersion": "2023-10-01",
               "name": "{{name}}",
               "location": "westeurope",
               "tags": {
                 "environment": "dev"
               },
               "properties": {
                 "agentPoolProfiles": [
                   {
                     "name": "system",
                     "count": {{nodeCount}},
                     "vmSize": "{{vmSize}}",
                     "mode": "System"
                   }
                 ]
               }
             }
             """;
}

static string LogAnalyticsArmResource(string name, int estimatedGb)
{
    return $$"""
             {
               "type": "Microsoft.OperationalInsights/workspaces",
               "apiVersion": "2022-10-01",
               "name": "{{name}}",
               "location": "westeurope",
               "sku": {
                 "name": "PerGB2018"
               },
               "tags": {
                 "environment": "dev"
               },
               "properties": {
                 "retentionInDays": 30,
                 "estimatedIngestionGbPerMonth": {{estimatedGb}}
               }
             }
             """;
}

static string UnknownArmResource()
{
    return """
           {
             "type": "Microsoft.Custom/widgets",
             "apiVersion": "2024-01-01",
             "name": "custom-widget",
             "location": "westeurope",
             "sku": {
               "name": "Large"
             },
             "properties": {}
           }
           """;
}

static string ParameterizedArmTemplate()
{
    return """
           {
             "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
             "contentVersion": "1.0.0.0",
             "parameters": {
               "location": {
                 "type": "string",
                 "defaultValue": "westeurope"
               },
               "skuName": {
                 "type": "string",
                 "defaultValue": "P1v3"
               },
               "environment": {
                 "type": "string",
                 "defaultValue": "dev"
               }
             },
             "variables": {
               "servicePlanName": "parameterized-api-plan",
               "servicePlanSku": "[parameters('skuName')]"
             },
             "resources": [
               {
                 "type": "Microsoft.Web/serverfarms",
                 "apiVersion": "2022-03-01",
                 "name": "[variables('servicePlanName')]",
                 "location": "[parameters('location')]",
                 "sku": {
                   "name": "[variables('servicePlanSku')]",
                   "tier": "PremiumV3",
                   "capacity": 1
                 },
                 "kind": "linux",
                 "tags": {
                   "environment": "[parameters('environment')]",
                   "owner": "[concat('team-', parameters('environment'))]"
                 },
                 "properties": {
                   "reserved": true
                 }
               }
             ],
             "outputs": {}
           }
           """;
}

static string TerraformPlan(params string[] resourceChanges)
{
    return """
           {
             "format_version": "1.2",
             "resource_changes": [
           """ + string.Join(",", resourceChanges) + """
             ]
           }
           """;
}

static string StorageCheapCreateChange()
{
    return """
           {
             "address": "azurerm_storage_account.assets",
             "mode": "managed",
             "type": "azurerm_storage_account",
             "name": "assets",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["create"],
               "before": null,
               "after": {
                 "name": "assetsdev",
                 "location": "westeurope",
                 "account_tier": "Standard",
                 "account_replication_type": "LRS",
                 "estimated_gb": 100,
                 "tags": { "environment": "dev" }
               }
             }
           }
           """;
}

static string RedisPremiumCreateChange()
{
    return """
           {
             "address": "azurerm_redis_cache.main",
             "mode": "managed",
             "type": "azurerm_redis_cache",
             "name": "main",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["create"],
               "before": null,
               "after": {
                 "name": "redis-dev",
                 "location": "westeurope",
                 "sku_name": "Premium",
                 "family": "P",
                 "capacity": 1,
                 "tags": { "environment": "dev" }
               }
             }
           }
           """;
}

static string ServicePlanUpgradeChange()
{
    return """
           {
             "address": "azurerm_service_plan.api",
             "mode": "managed",
             "type": "azurerm_service_plan",
             "name": "api",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["update"],
               "before": {
                 "name": "api-dev-plan",
                 "location": "westeurope",
                 "os_type": "Linux",
                 "sku_name": "B1",
                 "tags": { "environment": "dev" }
               },
               "after": {
                 "name": "api-dev-plan",
                 "location": "westeurope",
                 "os_type": "Linux",
                 "sku_name": "P1v3",
                 "tags": { "environment": "dev" }
               }
             }
           }
           """;
}

static string ServicePlanReplaceChange()
{
    return """
           {
             "address": "azurerm_service_plan.replaced",
             "mode": "managed",
             "type": "azurerm_service_plan",
             "name": "replaced",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["delete", "create"],
               "before": {
                 "name": "old-plan",
                 "location": "westeurope",
                 "os_type": "Linux",
                 "sku_name": "B1"
               },
               "after": {
                 "name": "new-plan",
                 "location": "westeurope",
                 "os_type": "Linux",
                 "sku_name": "S1"
               }
             }
           }
           """;
}

static string SqlDeleteChange()
{
    return """
           {
             "address": "azurerm_mssql_database.old",
             "mode": "managed",
             "type": "azurerm_mssql_database",
             "name": "old",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["delete"],
               "before": {
                 "name": "old-db",
                 "location": "westeurope",
                 "sku_name": "Basic",
                 "max_size_gb": 2
               },
               "after": null
             }
           }
           """;
}

static string NoOpChange()
{
    return """
           {
             "address": "azurerm_service_plan.noop",
             "mode": "managed",
             "type": "azurerm_service_plan",
             "name": "noop",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["no-op"],
               "before": { "sku_name": "B1", "location": "westeurope" },
               "after": { "sku_name": "B1", "location": "westeurope" }
             }
           }
           """;
}

static string ReadChange()
{
    return """
           {
             "address": "azurerm_service_plan.readonly",
             "mode": "managed",
             "type": "azurerm_service_plan",
             "name": "readonly",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["read"],
               "before": null,
               "after": { "sku_name": "B1", "location": "westeurope" }
             }
           }
           """;
}

static string UnknownCreateChange()
{
    return """
           {
             "address": "azurerm_unknown_widget.experimental",
             "mode": "managed",
             "type": "azurerm_unknown_widget",
             "name": "experimental",
             "provider_name": "registry.terraform.io/hashicorp/azurerm",
             "change": {
               "actions": ["create"],
               "before": null,
               "after": {
                 "name": "experimental",
                 "location": "westeurope",
                 "sku_name": "mystery"
               }
             }
           }
           """;
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

static AzureRetailPricesOptions AzureOptions(
    bool enabled = false,
    bool fallback = true,
    string baseUrl = "https://prices.test/api/retail/prices")
{
    return new AzureRetailPricesOptions
    {
        Enabled = enabled,
        BaseUrl = baseUrl,
        ApiVersion = "2023-01-01-preview",
        CurrencyCode = "EUR",
        DefaultRegion = "westeurope",
        TimeoutSeconds = 1,
        CacheTtlHours = 24,
        MaxPages = 5,
        FallbackToLocalCatalog = fallback,
        DefaultStorageGb = 100,
        DefaultLogAnalyticsGb = 10
    };
}

static AzureRetailPriceSearchResult RetailResult(params AzureRetailPriceItem[] items)
{
    return new AzureRetailPriceSearchResult(true, items, []);
}

static AzureRetailPriceItem RetailItem(
    string serviceName,
    string productName,
    string skuName,
    string armSkuName,
    string meterName,
    string unit,
    decimal unitPrice,
    string meterId = "meter-test")
{
    return new AzureRetailPriceItem
    {
        CurrencyCode = "EUR",
        UnitPrice = unitPrice,
        RetailPrice = unitPrice,
        ArmRegionName = "westeurope",
        Location = "EU West",
        EffectiveStartDate = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        MeterId = meterId,
        MeterName = meterName,
        ProductName = productName,
        SkuName = skuName,
        ArmSkuName = armSkuName,
        ServiceName = serviceName,
        ServiceFamily = "Compute",
        UnitOfMeasure = unit,
        Type = "Consumption",
        PriceType = "Consumption",
        IsPrimaryMeterRegion = true
    };
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> responses = new();

    public List<Uri> Requests { get; } = [];

    public Exception? Exception { get; set; }

    public void EnqueueJson(string json)
    {
        EnqueueText(json, "application/json");
    }

    public void EnqueueText(string text, string mediaType = "text/plain")
    {
        Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        });
    }

    public void Enqueue(HttpResponseMessage response)
    {
        responses.Enqueue(response);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        if (Exception is not null)
        {
            throw Exception;
        }

        return Task.FromResult(responses.Count > 0
            ? responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

sealed class FakeAzureRetailPricesClient : IAzureRetailPricesClient
{
    private readonly Queue<AzureRetailPriceSearchResult> results = new();
    private AzureRetailPriceSearchResult? lastResult;

    public FakeAzureRetailPricesClient(params AzureRetailPriceSearchResult[] results)
    {
        foreach (var result in results)
        {
            this.results.Enqueue(result);
        }
    }

    public List<AzureRetailPriceSearchRequest> Requests { get; } = [];

    public int Calls { get; private set; }

    public Task<AzureRetailPriceSearchResult> SearchAsync(AzureRetailPriceSearchRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        lastResult = results.Count > 0 ? results.Dequeue() : lastResult ?? AzureRetailPriceSearchResult.Failed("No fake result configured.");
        return Task.FromResult(lastResult);
    }
}

sealed class FakeAzureLivePricingProvider : IAzureLivePricingProvider
{
    private readonly PricingMatchResult result;

    public FakeAzureLivePricingProvider(PricingMatchResult result)
    {
        this.result = result;
    }

    public Task<PricingMatchResult> TryEstimateMonthlyCostAsync(PricingLookupRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(result);
    }
}

sealed class FakeGitHubApiClient : IGitHubApiClient
{
    private long nextCommentId = 1000;
    private long nextCheckRunId = 2000;

    public List<GitHubIssueComment> Comments { get; } = [];

    public List<GitHubCheckRun> CheckRuns { get; } = [];

    public HashSet<string> DeletedCommentIds { get; } = new(StringComparer.Ordinal);

    public HashSet<string> DeletedCheckRunIds { get; } = new(StringComparer.Ordinal);

    public List<string> Events { get; } = [];

    public bool ThrowOnCreateComment { get; set; }

    public Task<IReadOnlyList<GitHubIssueComment>> ListIssueCommentsAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"list-comments:{pullRequestNumber}");
        return Task.FromResult<IReadOnlyList<GitHubIssueComment>>(Comments.ToArray());
    }

    public Task<GitHubIssueComment> CreateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        int pullRequestNumber,
        string body,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"create-comment:{pullRequestNumber}");
        if (ThrowOnCreateComment)
        {
            throw new GitHubApiException("comment create failed");
        }

        var id = nextCommentId++;
        var comment = new GitHubIssueComment
        {
            Id = id,
            Body = body,
            HtmlUrl = $"https://github.test/comment/{id}"
        };
        Comments.Add(comment);
        return Task.FromResult(comment);
    }

    public Task<GitHubIssueComment> UpdateIssueCommentAsync(
        string owner,
        string repository,
        string installationId,
        string commentId,
        string body,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"update-comment:{commentId}");
        if (DeletedCommentIds.Contains(commentId))
        {
            throw new GitHubNotFoundException("comment was deleted");
        }

        var comment = Comments.SingleOrDefault(item => item.Id.ToString() == commentId);
        if (comment is null)
        {
            throw new GitHubNotFoundException("comment not found");
        }

        comment.Body = body;
        return Task.FromResult(comment);
    }

    public Task<GitHubCheckRun> CreateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string headSha,
        string name,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"create-check:{conclusion}");
        var id = nextCheckRunId++;
        var checkRun = new GitHubCheckRun
        {
            Id = id,
            HtmlUrl = $"https://github.test/check/{id}"
        };
        CheckRuns.Add(checkRun);
        return Task.FromResult(checkRun);
    }

    public Task<GitHubCheckRun> UpdateCheckRunAsync(
        string owner,
        string repository,
        string installationId,
        string checkRunId,
        string conclusion,
        string? detailsUrl,
        GitHubCheckRunOutput output,
        CancellationToken cancellationToken = default)
    {
        Events.Add($"update-check:{checkRunId}:{conclusion}");
        if (DeletedCheckRunIds.Contains(checkRunId))
        {
            throw new GitHubNotFoundException("check run was deleted");
        }

        var checkRun = CheckRuns.SingleOrDefault(item => item.Id.ToString() == checkRunId);
        if (checkRun is null)
        {
            throw new GitHubNotFoundException("check run not found");
        }

        return Task.FromResult(checkRun);
    }
}

sealed class SqliteFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private SqliteFixture(SqliteConnection connection, SpendGovernorDbContext context)
    {
        this.connection = connection;
        Context = context;
        Store = new SpendGovernorStore(context);
        RepositoryStore = new RepositoryStore(context);
        ResultWriter = new ScanResultWriter(context);
        ScanStore = new ScanStore(context, ResultWriter);
    }

    public SpendGovernorDbContext Context { get; }

    public SpendGovernorStore Store { get; }

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
