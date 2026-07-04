using System.Text.Json;

namespace SpendGovernor.Core;

public enum PriceUnit
{
    Hour,
    Month,
    GbMonth
}

public sealed record PriceEntry(
    string ResourceType,
    string Sku,
    string? Region,
    CostCategory Category,
    decimal UnitPrice,
    PriceUnit Unit,
    string Currency,
    string Source,
    DateOnly LastUpdated);

public interface IPricingAdapter
{
    PriceEntry? FindPrice(CloudResourceEstimateInput resource, string currency);
}

public sealed class SeededAzurePricingAdapter : IPricingAdapter
{
    private readonly List<PriceEntry> prices =
    [
        new("azurerm_linux_virtual_machine", "Standard_B2s", null, CostCategory.Compute, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_windows_virtual_machine", "Standard_B2s", null, CostCategory.Compute, 0.055m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Compute/virtualMachines", "Standard_B2s", null, CostCategory.Compute, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_linux_virtual_machine", "Standard_D4s_v5", null, CostCategory.Compute, 0.192m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_windows_virtual_machine", "Standard_D4s_v5", null, CostCategory.Compute, 0.260m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Compute/virtualMachines", "Standard_D4s_v5", null, CostCategory.Compute, 0.192m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_virtual_machine_scale_set", "Standard_B2s", null, CostCategory.Compute, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_kubernetes_cluster", "Standard_B2s", null, CostCategory.Container, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_kubernetes_cluster_node_pool", "Standard_B2s", null, CostCategory.Container, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.ContainerService/managedClusters", "Standard_B2s", null, CostCategory.Container, 0.038m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_service_plan", "S1", null, CostCategory.Compute, 0.095m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "S1", null, CostCategory.Compute, 0.095m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_service_plan", "P1v3", null, CostCategory.Compute, 0.315m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "P1v3", null, CostCategory.Compute, 0.315m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_service_plan", "B1", null, CostCategory.Compute, 0.018m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "B1", null, CostCategory.Compute, 0.018m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_storage_account", "Standard_LRS", null, CostCategory.Storage, 0.018m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Storage/storageAccounts", "Standard_LRS", null, CostCategory.Storage, 0.018m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_storage_account", "Standard_GRS", null, CostCategory.Storage, 0.036m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Storage/storageAccounts", "Standard_GRS", null, CostCategory.Storage, 0.036m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_mssql_database", "Basic", null, CostCategory.Database, 4.60m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Sql/servers/databases", "Basic", null, CostCategory.Database, 4.60m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_mssql_database", "S0", null, CostCategory.Database, 13.50m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Sql/servers/databases", "S0", null, CostCategory.Database, 13.50m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_postgresql_flexible_server", "B_Standard_B1ms", null, CostCategory.Database, 0.034m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.DBforPostgreSQL/flexibleServers", "B_Standard_B1ms", null, CostCategory.Database, 0.034m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_redis_cache", "Basic_C_0", null, CostCategory.Database, 0.015m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Cache/Redis", "Basic_C_0", null, CostCategory.Database, 0.015m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_redis_cache", "Standard_C_1", null, CostCategory.Database, 0.090m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Cache/Redis", "Standard_C_1", null, CostCategory.Database, 0.090m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_redis_cache", "Premium_P_1", null, CostCategory.Database, 0.425m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Cache/Redis", "Premium_P_1", null, CostCategory.Database, 0.425m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1))
    ];

    public PriceEntry? FindPrice(CloudResourceEstimateInput resource, string currency)
    {
        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            return null;
        }

        var candidates = prices.Where(price =>
                price.ResourceType.Equals(resource.ResourceType, StringComparison.OrdinalIgnoreCase)
                && price.Sku.Equals(resource.Sku, StringComparison.OrdinalIgnoreCase)
                && price.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (resource.Region is not null)
        {
            return candidates.FirstOrDefault(price => price.Region is not null && price.Region.Equals(resource.Region, StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(price => price.Region is null);
        }

        return candidates.FirstOrDefault();
    }
}

public sealed class MonthlyCostEstimator
{
    private readonly IPricingAdapter pricingAdapter;
    private readonly AiModelPriceCatalog aiPriceCatalog;

    public MonthlyCostEstimator(IPricingAdapter pricingAdapter, AiModelPriceCatalog aiPriceCatalog)
    {
        this.pricingAdapter = pricingAdapter;
        this.aiPriceCatalog = aiPriceCatalog;
    }

    public IReadOnlyList<ResourceEstimate> EstimateCloudResources(
        IEnumerable<CloudResourceEstimateInput> resources,
        Guid analysisId,
        string currency)
    {
        return resources.Select(resource => EstimateCloudResource(resource, analysisId, currency)).ToArray();
    }

    public IReadOnlyList<ResourceEstimate> EstimateAiWorkflows(
        IEnumerable<AiWorkflow> workflows,
        Guid analysisId,
        string currency)
    {
        var estimates = new List<ResourceEstimate>();
        foreach (var workflow in workflows)
        {
            var price = aiPriceCatalog.Find(workflow.Provider, workflow.Model);
            var estimate = new ResourceEstimate
            {
                AnalysisId = analysisId,
                SourceType = ResourceSourceType.AiConfig,
                SourceFile = workflow.SourceFile,
                Provider = workflow.Provider,
                ResourceType = "ai.workflow",
                ResourceName = workflow.Id,
                Region = null,
                Sku = workflow.Model,
                Environment = workflow.Environment,
                Category = CostCategory.Ai,
                Currency = currency,
                Quantity = workflow.EstimatedMonthlyRequests,
                HoursPerMonth = 0,
                AssumptionsJson = JsonSerializer.Serialize(new
                {
                    workflow.EstimatedMonthlyRequests,
                    workflow.AverageInputTokens,
                    workflow.AverageOutputTokens,
                    workflow.MaxOutputTokens,
                    workflow.MaxAgentSteps,
                    workflow.Tenant,
                    workflow.Feature
                })
            };

            if (price is null)
            {
                estimate.Status = EstimateStatus.PriceNotFound;
                estimate.Confidence = ConfidenceLevel.Low;
                estimate.Warnings.Add($"No AI model price found for {workflow.Provider}/{workflow.Model}.");
            }
            else
            {
                var inputCost = workflow.EstimatedMonthlyRequests * workflow.AverageInputTokens / 1_000_000m * price.InputPricePerMillionTokens;
                var outputCost = workflow.EstimatedMonthlyRequests * workflow.AverageOutputTokens / 1_000_000m * price.OutputPricePerMillionTokens;
                estimate.MonthlyCost = decimal.Round(inputCost + outputCost, 2);
                estimate.Status = EstimateStatus.Estimated;
                estimate.Confidence = ConfidenceLevel.Medium;
                estimate.PriceSource = price.Source;
                estimate.PriceLastUpdated = price.ValidFrom;
                if (workflow.MaxAgentSteps is > 6)
                {
                    estimate.Warnings.Add("Workflow allows more than 6 agent steps; cap steps to reduce runaway AI cost.");
                }
            }

            estimates.Add(estimate);
        }

        return estimates;
    }

    private ResourceEstimate EstimateCloudResource(CloudResourceEstimateInput resource, Guid analysisId, string currency)
    {
        var estimate = new ResourceEstimate
        {
            AnalysisId = analysisId,
            SourceType = resource.SourceType,
            SourceFile = resource.SourceFile,
            Provider = resource.Provider,
            ResourceType = resource.ResourceType,
            ResourceName = resource.ResourceName,
            Region = resource.Region,
            Sku = resource.Sku,
            Tier = resource.Tier,
            Environment = resource.Environment,
            Currency = currency,
            Quantity = Math.Max(1, resource.Quantity),
            HoursPerMonth = resource.HoursPerMonth,
            AssumptionsJson = JsonSerializer.Serialize(new
            {
                resource.Capacity,
                resource.Quantity,
                resource.HoursPerMonth,
                resource.Tags,
                resource.Raw
            })
        };
        estimate.Warnings.AddRange(resource.Warnings);

        if (!resource.IsSupported)
        {
            estimate.Status = EstimateStatus.Unsupported;
            estimate.Category = CostCategory.Unknown;
            estimate.Confidence = ConfidenceLevel.Low;
            return estimate;
        }

        var price = pricingAdapter.FindPrice(resource, currency);
        if (price is null)
        {
            estimate.Status = string.IsNullOrWhiteSpace(resource.Sku) ? EstimateStatus.Unknown : EstimateStatus.PriceNotFound;
            estimate.Category = InferCategory(resource.ResourceType);
            estimate.Confidence = ConfidenceLevel.Low;
            return estimate;
        }

        estimate.Category = price.Category;
        estimate.PriceSource = price.Source;
        estimate.PriceLastUpdated = price.LastUpdated;

        if (price.Unit == PriceUnit.GbMonth && resource.Capacity is null)
        {
            estimate.Status = EstimateStatus.Unknown;
            estimate.Confidence = ConfidenceLevel.Low;
            estimate.Warnings.Add("Storage capacity is missing, so the MVP cannot calculate a monthly storage cost.");
            return estimate;
        }

        estimate.MonthlyCost = price.Unit switch
        {
            PriceUnit.Hour => decimal.Round(price.UnitPrice * resource.HoursPerMonth * Math.Max(1, resource.Quantity), 2),
            PriceUnit.Month => decimal.Round(price.UnitPrice * Math.Max(1, resource.Quantity), 2),
            PriceUnit.GbMonth => decimal.Round(price.UnitPrice * (resource.Capacity ?? 0) * Math.Max(1, resource.Quantity), 2),
            _ => null
        };
        estimate.Status = EstimateStatus.Estimated;
        estimate.Confidence = EstimateConfidence(resource, price);

        return estimate;
    }

    private static ConfidenceLevel EstimateConfidence(CloudResourceEstimateInput resource, PriceEntry price)
    {
        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            return ConfidenceLevel.Low;
        }

        if (string.IsNullOrWhiteSpace(resource.Region)
            || resource.Warnings.Any(warning => warning.Contains("defaulted", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("depends on", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("runtime assumptions", StringComparison.OrdinalIgnoreCase)))
        {
            return ConfidenceLevel.Medium;
        }

        return ConfidenceLevel.High;
    }

    private static CostCategory InferCategory(string resourceType)
    {
        if (resourceType.Contains("storage", StringComparison.OrdinalIgnoreCase))
        {
            return CostCategory.Storage;
        }

        if (resourceType.Contains("sql", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("postgres", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("redis", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("cache", StringComparison.OrdinalIgnoreCase))
        {
            return CostCategory.Database;
        }

        if (resourceType.Contains("kubernetes", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("container", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("ContainerService", StringComparison.OrdinalIgnoreCase))
        {
            return CostCategory.Container;
        }

        if (resourceType.Contains("virtual", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("Compute", StringComparison.OrdinalIgnoreCase)
            || resourceType.Contains("Web", StringComparison.OrdinalIgnoreCase))
        {
            return CostCategory.Compute;
        }

        return CostCategory.Unknown;
    }
}
