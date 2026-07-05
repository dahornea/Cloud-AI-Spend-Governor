using SpendGovernor.Core;

public static class DemoScenarios
{
    public static readonly string[] SeedScenarioIds =
    [
        "scenario-cheap-change",
        "scenario-expensive-cloud-change",
        "scenario-expensive-ai-workflow"
    ];

    public static AnalysisRequest Create(Project project, string? scenario, string dashboardBaseUrl)
    {
        scenario = string.IsNullOrWhiteSpace(scenario) ? "small-vm" : scenario.Trim().ToLowerInvariant();
        return scenario switch
        {
            "scenario-cheap-change" => SmallVm(project, dashboardBaseUrl),
            "scenario-expensive-cloud-change" => ExpensiveCloudChange(project, dashboardBaseUrl),
            "scenario-expensive-ai-workflow" => AiExpensive(project, dashboardBaseUrl),
            "bicep-arm-cheap-change" => BicepArmCheapChange(project, dashboardBaseUrl),
            "bicep-arm-expensive-cloud-change" => BicepArmExpensiveCloudChange(project, dashboardBaseUrl),
            "bicep-arm-parameterized-template" => BicepArmParameterizedTemplate(project, dashboardBaseUrl),
            "no-cloud-impact" => NoCloudImpact(project, dashboardBaseUrl),
            "sku-threshold" => SkuThreshold(project, dashboardBaseUrl),
            "unknown-resource" => UnknownResource(project, dashboardBaseUrl),
            "ai-expensive" => AiExpensive(project, dashboardBaseUrl),
            "approval-required" => ApprovalRequired(project, dashboardBaseUrl),
            _ => SmallVm(project, dashboardBaseUrl)
        };
    }

    private static AnalysisRequest NoCloudImpact(Project project, string dashboardBaseUrl)
    {
        return Base(project, 101, "docs/readme-update", "demo-readme", ["README.md"], [], [
            new RepositoryFile("README.md", "# Payments API\n\nUpdated docs only.")
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest SmallVm(Project project, string dashboardBaseUrl)
    {
        const string proposed =
            """
            resource "azurerm_storage_account" "assets" {
              name                     = "assetsdevdemo"
              location                 = "westeurope"
              account_tier             = "Standard"
              account_replication_type = "LRS"
              estimated_gb             = 25

              tags = {
                environment = "dev"
                owner       = "platform"
              }
            }

            resource "azurerm_service_plan" "app_plan" {
              name     = "app-plan-dev"
              location = "westeurope"
              os_type  = "Linux"
              sku_name = "B1"

              tags = {
                environment = "dev"
                owner       = "platform"
              }
            }
            """;

        return Base(project, 102, "infra/small-storage-appservice", "demo-cheap-change", ["infra/main.tf"], [], [
            new RepositoryFile("infra/main.tf", proposed)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest SkuThreshold(Project project, string dashboardBaseUrl)
    {
        const string baseline =
            """
            resource "azurerm_service_plan" "app_plan" {
              name                = "app-plan-prod"
              location            = "westeurope"
              os_type             = "Linux"
              sku_name            = "S1"

              tags = {
                environment = "production"
              }
            }
            """;

        const string proposed =
            """
            resource "azurerm_service_plan" "app_plan" {
              name                = "app-plan-prod"
              location            = "westeurope"
              os_type             = "Linux"
              sku_name            = "P1v3"

              tags = {
                environment = "production"
              }
            }
            """;

        return Base(project, 103, "infra/scale-plan", "demo-sku-threshold", ["infra/main.tf"], [
            new RepositoryFile("infra/main.tf", baseline)
        ], [
            new RepositoryFile("infra/main.tf", proposed)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest ExpensiveCloudChange(Project project, string dashboardBaseUrl)
    {
        const string policy =
            """
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

            ai:
              enabled: true
              monthlyBudget: 300
              maxCostPerWorkflowMonthly: 100
              action: warn
            """;

        const string proposed =
            """
            resource "azurerm_service_plan" "app_plan" {
              name     = "app-plan-dev"
              location = "westeurope"
              os_type  = "Linux"
              sku_name = "S1"

              tags = {
                environment = "dev"
              }
            }

            resource "azurerm_redis_cache" "session_cache" {
              name                = "session-cache-dev"
              location            = "westeurope"
              capacity            = 1
              family              = "P"
              sku_name            = "Premium"

              tags = {
                environment = "dev"
              }
            }

            resource "azurerm_kubernetes_cluster_node_pool" "workers" {
              name       = "workers"
              location   = "westeurope"
              vm_size    = "Standard_B2s"
              node_count = 1

              tags = {
                environment = "dev"
              }
            }
            """;

        return Base(project, 107, "infra/dev-premium-redis", "demo-expensive-cloud", [".spendgov.yml", "infra/cache.tf"], [], [
            new RepositoryFile(".spendgov.yml", policy),
            new RepositoryFile("infra/cache.tf", proposed)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest UnknownResource(Project project, string dashboardBaseUrl)
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
              target_resource_id = azurerm_linux_virtual_machine.worker.id
            }
            """;

        return Base(project, 104, "infra/diagnostics", "demo-unknown", ["infra/monitoring.tf", ".spendgov.yml"], [], [
            new RepositoryFile(".spendgov.yml", policy),
            new RepositoryFile("infra/monitoring.tf", proposed)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest AiExpensive(Project project, string dashboardBaseUrl)
    {
        const string workflow =
            """
            aiWorkflows:
              - id: sales-agent
                provider: azure-openai
                model: gpt-4.1
                estimatedMonthlyRequests: 10000
                averageInputTokens: 8000
                averageOutputTokens: 2000
                maxAgentSteps: 8
                environment: production
            """;

        return Base(project, 105, "ai/sales-agent-cost", "demo-ai", ["ai-spend.yml"], [], [
            new RepositoryFile("ai-spend.yml", workflow)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest ApprovalRequired(Project project, string dashboardBaseUrl)
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

        return Base(project, 106, "infra/staging-etl", "demo-approval", [".spendgov.yml", "infra/staging/main.tf"], [], [
            new RepositoryFile(".spendgov.yml", policy),
            new RepositoryFile("infra/staging/main.tf", proposed)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest BicepArmCheapChange(Project project, string dashboardBaseUrl)
    {
        const string compiledArm =
            """
            {
              "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
              "contentVersion": "1.0.0.0",
              "parameters": {},
              "variables": {},
              "resources": [
                {
                  "type": "Microsoft.Storage/storageAccounts",
                  "apiVersion": "2023-01-01",
                  "name": "cheapdemostorage",
                  "location": "westeurope",
                  "sku": { "name": "Standard_LRS", "tier": "Standard" },
                  "kind": "StorageV2",
                  "tags": { "environment": "dev" },
                  "properties": { "accessTier": "Hot", "estimatedGb": 100 }
                },
                {
                  "type": "Microsoft.Web/serverfarms",
                  "apiVersion": "2022-03-01",
                  "name": "cheap-demo-plan",
                  "location": "westeurope",
                  "sku": { "name": "B1", "tier": "Basic", "capacity": 1 },
                  "kind": "linux",
                  "tags": { "environment": "dev" },
                  "properties": { "reserved": true }
                }
              ],
              "outputs": {}
            }
            """;

        const string rawBicep =
            """
            // Raw Bicep remains present, but compiled ARM JSON is preferred.
            resource plan 'Microsoft.Web/serverfarms@2022-03-01' = {
              name: 'raw-bicep-fallback-plan'
              location: 'westeurope'
              sku: { name: 'B1' }
            }
            """;

        return Base(project, 108, "infra/bicep-arm-cheap", "demo-bicep-arm-cheap", ["infra/main.bicep", "infra/main.json"], [], [
            new RepositoryFile("infra/main.bicep", rawBicep),
            new RepositoryFile("infra/main.json", compiledArm)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest BicepArmExpensiveCloudChange(Project project, string dashboardBaseUrl)
    {
        const string policy =
            """
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
              staging:
                monthlyBudget: 150
                action: block
            """;
        const string compiledArm =
            """
            {
              "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
              "contentVersion": "1.0.0.0",
              "parameters": {},
              "variables": {},
              "resources": [
                {
                  "type": "Microsoft.Cache/Redis",
                  "apiVersion": "2023-08-01",
                  "name": "redis-prod-demo",
                  "location": "westeurope",
                  "sku": { "name": "Premium", "family": "P", "capacity": 1 },
                  "tags": { "environment": "staging" },
                  "properties": { "enableNonSslPort": false, "minimumTlsVersion": "1.2" }
                },
                {
                  "type": "Microsoft.Web/serverfarms",
                  "apiVersion": "2022-03-01",
                  "name": "expensive-api-plan",
                  "location": "westeurope",
                  "sku": { "name": "P1v3", "tier": "PremiumV3", "capacity": 1 },
                  "kind": "linux",
                  "tags": { "environment": "staging" },
                  "properties": { "reserved": true }
                },
                {
                  "type": "Microsoft.OperationalInsights/workspaces",
                  "apiVersion": "2022-10-01",
                  "name": "expensive-demo-logs",
                  "location": "westeurope",
                  "sku": { "name": "PerGB2018" },
                  "tags": { "environment": "staging" },
                  "properties": { "retentionInDays": 30, "estimatedIngestionGbPerMonth": 50 }
                }
              ],
              "outputs": {}
            }
            """;

        return Base(project, 109, "infra/bicep-arm-expensive", "demo-bicep-arm-expensive", [".spendgov.yml", "infra/main.json"], [], [
            new RepositoryFile(".spendgov.yml", policy),
            new RepositoryFile("infra/main.json", compiledArm)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest BicepArmParameterizedTemplate(Project project, string dashboardBaseUrl)
    {
        const string compiledArm =
            """
            {
              "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#",
              "contentVersion": "1.0.0.0",
              "parameters": {
                "location": { "type": "string", "defaultValue": "westeurope" },
                "skuName": { "type": "string", "defaultValue": "P1v3" },
                "environment": { "type": "string", "defaultValue": "dev" }
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
                  "sku": { "name": "[variables('servicePlanSku')]", "tier": "PremiumV3", "capacity": 1 },
                  "kind": "linux",
                  "tags": {
                    "environment": "[parameters('environment')]",
                    "owner": "[concat('team-', parameters('environment'))]"
                  },
                  "properties": { "reserved": true }
                },
                {
                  "type": "Microsoft.Storage/storageAccounts",
                  "apiVersion": "2023-01-01",
                  "name": "parameterizedstorage",
                  "location": "[parameters('location')]",
                  "sku": { "name": "Standard_LRS", "tier": "Standard" },
                  "kind": "StorageV2",
                  "tags": { "environment": "[parameters('environment')]" },
                  "properties": { "accessTier": "Hot", "estimatedGb": 100 }
                }
              ],
              "outputs": {}
            }
            """;

        return Base(project, 110, "infra/bicep-arm-parameterized", "demo-bicep-arm-parameterized", ["infra/main.json"], [], [
            new RepositoryFile("infra/main.json", compiledArm)
        ], dashboardBaseUrl);
    }

    private static AnalysisRequest Base(
        Project project,
        int pr,
        string branch,
        string sha,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<RepositoryFile> baselineFiles,
        IReadOnlyList<RepositoryFile> proposedFiles,
        string dashboardBaseUrl)
    {
        return new AnalysisRequest
        {
            ProjectId = project.Id,
            RepositoryOwner = project.RepositoryOwner,
            RepositoryName = project.RepositoryName,
            PullRequestNumber = pr,
            BaseBranch = "main",
            HeadBranch = branch,
            CommitSha = sha,
            ChangedFiles = changedFiles,
            BaselineFiles = baselineFiles,
            ProposedFiles = proposedFiles,
            Settings = ProjectSettings.FromProject(project),
            DashboardBaseUrl = dashboardBaseUrl
        };
    }
}
