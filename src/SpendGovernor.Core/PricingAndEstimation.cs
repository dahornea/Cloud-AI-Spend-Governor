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
        new("azurerm_app_service_plan", "S1", null, CostCategory.Compute, 0.095m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "S1", null, CostCategory.Compute, 0.095m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_service_plan", "P1v3", null, CostCategory.Compute, 0.315m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_app_service_plan", "P1v3", null, CostCategory.Compute, 0.315m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "P1v3", null, CostCategory.Compute, 0.315m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_service_plan", "B1", null, CostCategory.Compute, 0.018m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_app_service_plan", "B1", null, CostCategory.Compute, 0.018m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Web/serverfarms", "B1", null, CostCategory.Compute, 0.018m, PriceUnit.Hour, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_storage_account", "Standard_LRS", null, CostCategory.Storage, 0.018m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Storage/storageAccounts", "Standard_LRS", null, CostCategory.Storage, 0.018m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_storage_account", "Standard_GRS", null, CostCategory.Storage, 0.036m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Storage/storageAccounts", "Standard_GRS", null, CostCategory.Storage, 0.036m, PriceUnit.GbMonth, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),

        new("azurerm_mssql_database", "Basic", null, CostCategory.Database, 4.60m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_sql_database", "Basic", null, CostCategory.Database, 4.60m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("Microsoft.Sql/servers/databases", "Basic", null, CostCategory.Database, 4.60m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_mssql_database", "S0", null, CostCategory.Database, 13.50m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
        new("azurerm_sql_database", "S0", null, CostCategory.Database, 13.50m, PriceUnit.Month, "EUR", "Seeded Azure retail-style catalog", new DateOnly(2026, 1, 1)),
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
    private readonly IPricingCatalogService pricingCatalogService;

    public MonthlyCostEstimator(IPricingCatalogService pricingCatalogService)
    {
        this.pricingCatalogService = pricingCatalogService;
    }

    public MonthlyCostEstimator(IPricingAdapter pricingAdapter, AiModelPriceCatalog aiPriceCatalog)
        : this(new LegacyPricingCatalogService(pricingAdapter, aiPriceCatalog))
    {
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
            var inputTokens = workflow.EstimatedMonthlyRequests * (decimal)workflow.AverageInputTokens;
            var outputTokens = workflow.EstimatedMonthlyRequests * (decimal)workflow.AverageOutputTokens;
            var match = pricingCatalogService.EstimateAiMonthlyCost(new AiPricingLookupRequest
            {
                Provider = workflow.Provider,
                Model = workflow.Model,
                WorkflowId = workflow.Id,
                EstimatedRunsPerMonth = workflow.EstimatedMonthlyRequests,
                AverageInputTokens = workflow.AverageInputTokens,
                AverageOutputTokens = workflow.AverageOutputTokens,
                Currency = currency
            });
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
                PricingCatalogName = match.CatalogName,
                PricingCatalogVersion = match.CatalogVersion,
                PricingSource = match.PricingSource,
                PricingSourceType = match.SourceType,
                PricingMatchType = match.MatchType.ToString(),
                PricingFallbackReason = match.FallbackReason,
                PricingUnit = match.Unit,
                PricingUnitPrice = match.UnitPrice,
                PricingMatchedKey = match.MatchedCatalogItemIdOrKey,
                PricingConfidenceImpact = match.ConfidenceImpact.ToString(),
                AssumptionsJson = JsonSerializer.Serialize(new
                {
                    workflow.EstimatedMonthlyRequests,
                    workflow.AverageInputTokens,
                    workflow.AverageOutputTokens,
                    MonthlyInputTokens = inputTokens,
                    MonthlyOutputTokens = outputTokens,
                    workflow.MaxOutputTokens,
                    workflow.MaxAgentSteps,
                    workflow.Tenant,
                    workflow.Feature,
                    Pricing = match
                })
            };

            if (!match.Matched || match.EstimatedMonthlyCost is null)
            {
                estimate.Status = EstimateStatus.PriceNotFound;
                estimate.Confidence = ConfidenceLevel.Low;
                estimate.Warnings.Add(match.FallbackReason ?? $"No AI model price found for {workflow.Provider}/{workflow.Model}.");
            }
            else
            {
                estimate.MonthlyCost = match.EstimatedMonthlyCost;
                estimate.Status = EstimateStatus.Estimated;
                estimate.Confidence = ConfidenceLevel.Medium;
                estimate.PriceSource = match.PricingSource;
                estimate.PriceLastUpdated = match.LastUpdatedAt ?? match.EffectiveFrom;
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
            AnalysisSource = resource.AnalysisSource,
            ArmResourceType = resource.ArmResourceType,
            ArmApiVersion = resource.ArmApiVersion,
            ArmKind = resource.ArmKind,
            MappedResourceType = resource.MappedResourceType,
            TerraformAddress = resource.TerraformAddress,
            TerraformActions = resource.TerraformActions,
            TerraformChangeType = resource.TerraformChangeType,
            BeforeSummary = resource.BeforeSummary,
            AfterSummary = resource.AfterSummary,
            Environment = resource.Environment,
            Currency = currency,
            Quantity = Math.Max(1, resource.Quantity),
            HoursPerMonth = resource.HoursPerMonth
        };
        estimate.Warnings.AddRange(resource.Warnings);

        var match = pricingCatalogService.EstimateMonthlyCost(new PricingLookupRequest
        {
            Provider = resource.Provider,
            ResourceType = resource.ResourceType,
            ResourceName = resource.ResourceName,
            Sku = resource.Sku,
            Tier = resource.Tier,
            Size = resource.Sku,
            Region = resource.Region,
            UsageQuantity = resource.Capacity,
            MonthlyHours = resource.HoursPerMonth,
            Quantity = Math.Max(1, resource.Quantity),
            Currency = currency,
            Metadata = resource.Raw
        });
        ApplyPricingMetadata(estimate, match);
        estimate.AssumptionsJson = BuildCloudAssumptionsJson(resource, match);

        if (!resource.IsSupported)
        {
            estimate.Status = EstimateStatus.Unsupported;
            estimate.Category = CostCategory.Unknown;
            estimate.Confidence = ConfidenceLevel.Low;
            if (!string.IsNullOrWhiteSpace(match.FallbackReason))
            {
                estimate.Warnings.Add(match.FallbackReason);
            }
            return estimate;
        }

        if (!match.Matched || match.EstimatedMonthlyCost is null)
        {
            estimate.Status = string.IsNullOrWhiteSpace(resource.Sku) ? EstimateStatus.Unknown : EstimateStatus.PriceNotFound;
            estimate.Category = InferCategory(resource.ResourceType);
            estimate.Confidence = ConfidenceLevel.Low;
            if (!string.IsNullOrWhiteSpace(match.FallbackReason))
            {
                estimate.Warnings.Add(match.FallbackReason);
            }
            return estimate;
        }

        estimate.Category = InferCategory(resource.ResourceType);
        estimate.MonthlyCost = match.EstimatedMonthlyCost;
        estimate.Status = EstimateStatus.Estimated;
        estimate.Confidence = EstimateConfidence(resource, match);

        return estimate;
    }

    private static void ApplyPricingMetadata(ResourceEstimate estimate, PricingMatchResult match)
    {
        estimate.PricingCatalogName = match.CatalogName;
        estimate.PricingCatalogVersion = match.CatalogVersion;
        estimate.PricingSource = match.PricingSource;
        estimate.PricingSourceType = match.SourceType;
        estimate.PricingMatchType = match.MatchType.ToString();
        estimate.PricingFallbackReason = match.FallbackReason;
        estimate.PricingUnit = match.Unit;
        estimate.PricingUnitPrice = match.UnitPrice;
        estimate.PricingMatchedKey = match.MatchedCatalogItemIdOrKey;
        estimate.PricingConfidenceImpact = match.ConfidenceImpact.ToString();
        estimate.PricingLiveApiUsed = match.LiveApiUsed;
        estimate.PricingFallbackUsed = match.FallbackUsed;
        estimate.PricingRegionDefaulted = match.RegionDefaulted;
        estimate.PricingAmbiguousMatch = match.AmbiguousMatch;
        estimate.PricingMonthlyHours = match.MonthlyHours;
        estimate.PricingUnitOfMeasure = match.UnitOfMeasure;
        estimate.PricingMeterId = match.MeterId;
        estimate.PricingMeterName = match.MeterName;
        estimate.PricingProductName = match.ProductName;
        estimate.PricingSkuName = match.SkuName;
        estimate.PricingArmSkuName = match.ArmSkuName;
        estimate.PricingServiceName = match.ServiceName;
        estimate.PricingServiceFamily = match.ServiceFamily;
        estimate.PricingPriceType = match.PriceType;
        estimate.PricingEffectiveStartDate = match.EffectiveStartDate;
        estimate.PriceSource = match.PricingSource;
        estimate.PriceLastUpdated = match.LastUpdatedAt ?? match.EffectiveFrom;
    }

    private static string BuildCloudAssumptionsJson(CloudResourceEstimateInput resource, PricingMatchResult match)
    {
        return JsonSerializer.Serialize(new
        {
            resource.Capacity,
            resource.Quantity,
            resource.HoursPerMonth,
            resource.AnalysisSource,
            resource.ArmResourceType,
            resource.ArmApiVersion,
            resource.ArmKind,
            resource.MappedResourceType,
            resource.TerraformAddress,
            resource.TerraformActions,
            resource.TerraformChangeType,
            resource.BeforeSummary,
            resource.AfterSummary,
            resource.Tags,
            resource.Raw,
            Pricing = match
        });
    }

    private static ConfidenceLevel EstimateConfidence(CloudResourceEstimateInput resource, PricingMatchResult match)
    {
        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            return ConfidenceLevel.Low;
        }

        if (!match.Matched
            || match.FallbackUsed
            || match.MatchType is PricingMatchType.AzureRetailResourceTypeFallback or PricingMatchType.ResourceTypeFallback or PricingMatchType.ProviderFallback or PricingMatchType.ManualEstimate or PricingMatchType.Unknown
            || match.ConfidenceImpact == PricingConfidenceImpact.Decrease)
        {
            return ConfidenceLevel.Low;
        }

        if (string.IsNullOrWhiteSpace(resource.Region)
            || match.RegionDefaulted
            || match.AmbiguousMatch
            || match.MatchType is PricingMatchType.AzureRetailExactRegionApproximateSkuMatch or PricingMatchType.AzureRetailDefaultRegionSkuMatch or PricingMatchType.AzureRetailAmbiguousMatch or PricingMatchType.DefaultRegionSkuMatch or PricingMatchType.SkuOnlyFallback
            || resource.Warnings.Any(warning => warning.Contains("defaulted", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("depends on", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("runtime assumptions", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("ARM expression", StringComparison.OrdinalIgnoreCase))
            || resource.Warnings.Any(warning => warning.Contains("Nested ARM resource", StringComparison.OrdinalIgnoreCase)))
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

internal sealed class LegacyPricingCatalogService : IPricingCatalogService
{
    private readonly IPricingAdapter pricingAdapter;
    private readonly AiModelPriceCatalog aiPriceCatalog;

    public LegacyPricingCatalogService(IPricingAdapter pricingAdapter, AiModelPriceCatalog aiPriceCatalog)
    {
        this.pricingAdapter = pricingAdapter;
        this.aiPriceCatalog = aiPriceCatalog;
    }

    public PricingMatchResult EstimateMonthlyCost(PricingLookupRequest request)
    {
        var input = new CloudResourceEstimateInput
        {
            Provider = request.Provider,
            ResourceType = request.ResourceType,
            ResourceName = request.ResourceName,
            Sku = request.Sku,
            Tier = request.Tier,
            Region = request.Region,
            Capacity = request.UsageQuantity,
            HoursPerMonth = request.MonthlyHours,
            Quantity = request.Quantity
        };
        var price = pricingAdapter.FindPrice(input, request.Currency);
        if (price is null)
        {
            return new PricingMatchResult
            {
                Matched = false,
                Provider = request.Provider,
                ResourceType = request.ResourceType,
                ResourceName = request.ResourceName,
                Sku = request.Sku,
                Region = request.Region,
                Currency = request.Currency,
                CatalogName = "Legacy Seed Catalog",
                CatalogVersion = "legacy",
                PricingSource = "Legacy in-memory seed catalog",
                SourceType = "LocalStaticCatalog",
                MatchType = PricingMatchType.Unknown,
                ConfidenceImpact = PricingConfidenceImpact.Decrease,
                FallbackReason = $"No legacy price found for {request.ResourceType}/{request.Sku}."
            };
        }

        decimal? estimated = price.Unit switch
        {
            PriceUnit.Hour => decimal.Round(price.UnitPrice * request.MonthlyHours * Math.Max(1, request.Quantity), 2),
            PriceUnit.Month => decimal.Round(price.UnitPrice * Math.Max(1, request.Quantity), 2),
            PriceUnit.GbMonth => request.UsageQuantity is null ? null : decimal.Round(price.UnitPrice * request.UsageQuantity.Value * Math.Max(1, request.Quantity), 2),
            _ => null
        };

        return new PricingMatchResult
        {
            Matched = estimated is not null,
            Provider = request.Provider,
            ResourceType = request.ResourceType,
            ResourceName = request.ResourceName,
            Sku = request.Sku,
            Region = request.Region,
            Currency = request.Currency,
            EstimatedMonthlyCost = estimated,
            UnitPrice = price.UnitPrice,
            Unit = price.Unit.ToString(),
            CatalogName = "Legacy Seed Catalog",
            CatalogVersion = "legacy",
            PricingSource = price.Source,
            SourceType = "LocalStaticCatalog",
            EffectiveFrom = price.LastUpdated,
            LastUpdatedAt = price.LastUpdated,
            MatchType = PricingMatchType.SkuOnlyFallback,
            ConfidenceImpact = PricingConfidenceImpact.Neutral,
            MatchedCatalogItemIdOrKey = $"{price.ResourceType}:{price.Sku}:{price.Region ?? "*"}"
        };
    }

    public PricingMatchResult EstimateAiMonthlyCost(AiPricingLookupRequest request)
    {
        var price = aiPriceCatalog.Find(request.Provider, request.Model);
        if (price is null)
        {
            return new PricingMatchResult
            {
                Matched = false,
                Provider = request.Provider,
                ResourceType = "ai.workflow",
                ResourceName = request.WorkflowId,
                Sku = request.Model,
                Currency = request.Currency,
                CatalogName = "Legacy AI Seed Catalog",
                CatalogVersion = "legacy",
                PricingSource = "Legacy in-memory seed catalog",
                SourceType = "LocalStaticCatalog",
                MatchType = PricingMatchType.Unknown,
                ConfidenceImpact = PricingConfidenceImpact.Decrease,
                FallbackReason = $"No legacy AI model price found for {request.Provider}/{request.Model}."
            };
        }

        var inputTokens = request.EstimatedRunsPerMonth * (decimal)request.AverageInputTokens;
        var outputTokens = request.EstimatedRunsPerMonth * (decimal)request.AverageOutputTokens;
        var inputCost = inputTokens / 1_000_000m * price.InputPricePerMillionTokens;
        var outputCost = outputTokens / 1_000_000m * price.OutputPricePerMillionTokens;
        return new PricingMatchResult
        {
            Matched = true,
            Provider = request.Provider,
            ResourceType = "ai.workflow",
            ResourceName = request.WorkflowId,
            Sku = request.Model,
            Currency = request.Currency,
            EstimatedMonthlyCost = decimal.Round(inputCost + outputCost, 2),
            Unit = "1M tokens",
            CatalogName = "Legacy AI Seed Catalog",
            CatalogVersion = "legacy",
            PricingSource = price.Source,
            SourceType = "LocalStaticCatalog",
            EffectiveFrom = price.ValidFrom,
            LastUpdatedAt = price.ValidFrom,
            MatchType = PricingMatchType.ExactRegionSkuMatch,
            ConfidenceImpact = PricingConfidenceImpact.Neutral,
            MatchedCatalogItemIdOrKey = $"{price.Provider}:{price.Model}",
            InputPricePerMillionTokens = price.InputPricePerMillionTokens,
            OutputPricePerMillionTokens = price.OutputPricePerMillionTokens,
            MonthlyInputTokens = inputTokens,
            MonthlyOutputTokens = outputTokens
        };
    }

    public PricingCatalogValidationResult Validate()
    {
        return new PricingCatalogValidationResult([], ["Using legacy in-memory pricing catalog."]);
    }
}
