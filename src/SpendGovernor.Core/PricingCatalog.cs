namespace SpendGovernor.Core;

public enum PricingMatchType
{
    AzureRetailExactRegionSkuMatch,
    AzureRetailExactRegionApproximateSkuMatch,
    AzureRetailDefaultRegionSkuMatch,
    AzureRetailResourceTypeFallback,
    AzureRetailAmbiguousMatch,
    ExactRegionSkuMatch,
    DefaultRegionSkuMatch,
    SkuOnlyFallback,
    ResourceTypeFallback,
    ProviderFallback,
    ManualEstimate,
    Unknown
}

public enum PricingConfidenceImpact
{
    Increase,
    Neutral,
    Decrease
}

public sealed class PricingCatalog
{
    public string Provider { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string Currency { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? LastUpdatedAt { get; set; }
    public string? Notes { get; set; }
    public List<PricingCatalogItem> Items { get; set; } = [];
}

public sealed class PricingCatalogItem
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Service { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string? Sku { get; set; }
    public string? Tier { get; set; }
    public string? Size { get; set; }
    public string? MeterName { get; set; }
    public string? Region { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Unit { get; set; } = "month";
    public decimal? UnitPrice { get; set; }
    public decimal? MonthlyEstimate { get; set; }
    public int? MonthlyHours { get; set; }
    public decimal? UsageQuantity { get; set; }
    public string PriceType { get; set; } = "LocalStaticCatalog";
    public string? MatchKey { get; set; }
    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Notes { get; set; }
    public decimal? InputPricePerMillionTokens { get; set; }
    public decimal? OutputPricePerMillionTokens { get; set; }
}

public sealed class PricingLookupRequest
{
    public string Provider { get; init; } = "";
    public string ResourceType { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public string? Sku { get; init; }
    public string? Tier { get; init; }
    public string? Size { get; init; }
    public string? Region { get; init; }
    public decimal? UsageQuantity { get; init; }
    public int MonthlyHours { get; init; } = 730;
    public int Quantity { get; init; } = 1;
    public string Currency { get; init; } = "EUR";
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}

public sealed class AiPricingLookupRequest
{
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string WorkflowId { get; init; } = "";
    public int EstimatedRunsPerMonth { get; init; }
    public int AverageInputTokens { get; init; }
    public int AverageOutputTokens { get; init; }
    public string Currency { get; init; } = "EUR";
}

public sealed class PricingMatchResult
{
    public bool Matched { get; init; }
    public string Provider { get; init; } = "";
    public string ResourceType { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public string? Sku { get; init; }
    public string? Region { get; init; }
    public string Currency { get; init; } = "EUR";
    public decimal? EstimatedMonthlyCost { get; init; }
    public decimal? UnitPrice { get; init; }
    public string? Unit { get; init; }
    public string CatalogName { get; init; } = "";
    public string CatalogVersion { get; init; } = "";
    public string PricingSource { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string? DefaultRegion { get; init; }
    public DateOnly? EffectiveFrom { get; init; }
    public DateOnly? LastUpdatedAt { get; init; }
    public PricingMatchType MatchType { get; init; } = PricingMatchType.Unknown;
    public PricingConfidenceImpact ConfidenceImpact { get; init; } = PricingConfidenceImpact.Decrease;
    public string? FallbackReason { get; init; }
    public IReadOnlyDictionary<string, string> Assumptions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? MatchedCatalogItemIdOrKey { get; init; }
    public bool LiveApiUsed { get; init; }
    public bool FallbackUsed { get; init; }
    public bool RegionDefaulted { get; init; }
    public bool AmbiguousMatch { get; init; }
    public int? MonthlyHours { get; init; }
    public string? UnitOfMeasure { get; init; }
    public string? MeterId { get; init; }
    public string? MeterName { get; init; }
    public string? ProductName { get; init; }
    public string? SkuName { get; init; }
    public string? ArmSkuName { get; init; }
    public string? ServiceName { get; init; }
    public string? ServiceFamily { get; init; }
    public string? PriceType { get; init; }
    public DateTimeOffset? EffectiveStartDate { get; init; }
    public decimal? InputPricePerMillionTokens { get; init; }
    public decimal? OutputPricePerMillionTokens { get; init; }
    public decimal? MonthlyInputTokens { get; init; }
    public decimal? MonthlyOutputTokens { get; init; }
}

public interface IPricingCatalogService
{
    PricingMatchResult EstimateMonthlyCost(PricingLookupRequest request);

    PricingMatchResult EstimateAiMonthlyCost(AiPricingLookupRequest request);

    PricingCatalogValidationResult Validate();
}

public sealed record PricingCatalogValidationResult(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class PricingCatalogValidationException : Exception
{
    public PricingCatalogValidationException(IEnumerable<string> errors)
        : base("Pricing catalog validation failed: " + string.Join("; ", errors))
    {
    }
}
