using System.Globalization;
using System.Text.Json;
using SpendGovernor.Core;

namespace SpendGovernor.Infrastructure.Services;

public sealed class JsonPricingCatalogService : IPricingCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PricingCatalog azureCatalog;
    private readonly PricingCatalog aiCatalog;

    public JsonPricingCatalogService(PricingCatalog azureCatalog, PricingCatalog aiCatalog, bool validate = true)
    {
        this.azureCatalog = azureCatalog;
        this.aiCatalog = aiCatalog;
        if (validate)
        {
            var validation = Validate();
            if (!validation.IsValid)
            {
                throw new PricingCatalogValidationException(validation.Errors);
            }
        }
    }

    public static JsonPricingCatalogService LoadDefault(bool validate = true)
    {
        var catalogDirectory = FindCatalogDirectory();
        return new JsonPricingCatalogService(
            LoadCatalog(Path.Combine(catalogDirectory, "azure-pricing-catalog.v2026.07.01.json")),
            LoadCatalog(Path.Combine(catalogDirectory, "ai-pricing-catalog.v2026.07.01.json")),
            validate);
    }

    public static PricingCatalog LoadCatalog(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<PricingCatalog>(stream, JsonOptions)
            ?? throw new PricingCatalogValidationException([$"Pricing catalog {path} could not be deserialized."]);
    }

    public PricingMatchResult EstimateMonthlyCost(PricingLookupRequest request)
    {
        var catalog = azureCatalog;
        var candidates = catalog.Items
            .Where(item => Matches(item.Provider, request.Provider)
                && !IsWildcard(item.ResourceType)
                && Matches(item.ResourceType, request.ResourceType)
                && Matches(item.Currency, request.Currency))
            .ToArray();
        var sku = FirstNonEmpty(request.Sku, request.Size, request.Tier);

        var exact = candidates.FirstOrDefault(item => MatchesConcrete(item.Sku, sku) && MatchesConcrete(item.Region, request.Region));
        if (exact is not null)
        {
            return BuildCloudResult(catalog, exact, request, PricingMatchType.ExactRegionSkuMatch, PricingConfidenceImpact.Increase);
        }

        var defaultRegion = catalog.DefaultRegion;
        var defaultRegionMatch = candidates.FirstOrDefault(item => MatchesConcrete(item.Sku, sku) && MatchesConcrete(item.Region, defaultRegion));
        if (defaultRegionMatch is not null)
        {
            return BuildCloudResult(
                catalog,
                defaultRegionMatch,
                request,
                PricingMatchType.DefaultRegionSkuMatch,
                PricingConfidenceImpact.Neutral,
                $"Region {Display(request.Region)} was not found; {Display(defaultRegion)} default pricing was used.");
        }

        var skuOnly = candidates.FirstOrDefault(item => MatchesConcrete(item.Sku, sku) && string.IsNullOrWhiteSpace(item.Region));
        if (skuOnly is not null)
        {
            return BuildCloudResult(
                catalog,
                skuOnly,
                request,
                PricingMatchType.SkuOnlyFallback,
                PricingConfidenceImpact.Neutral,
                $"No regional price found for {request.ResourceType}/{Display(sku)}. Used SKU-only catalog fallback.");
        }

        var resourceTypeFallback = candidates.FirstOrDefault(item => IsWildcard(item.Sku));
        if (resourceTypeFallback is not null)
        {
            return BuildCloudResult(
                catalog,
                resourceTypeFallback,
                request,
                PricingMatchType.ResourceTypeFallback,
                PricingConfidenceImpact.Decrease,
                $"No exact price found for {request.ResourceType} in {Display(request.Region)}. Used resource type fallback from {Display(resourceTypeFallback.Region ?? catalog.DefaultRegion)}.");
        }

        var providerFallback = catalog.Items.FirstOrDefault(item =>
            Matches(item.Provider, request.Provider)
            && IsWildcard(item.ResourceType)
            && Matches(item.Currency, request.Currency));
        if (providerFallback is not null)
        {
            return BuildCloudResult(
                catalog,
                providerFallback,
                request,
                PricingMatchType.ProviderFallback,
                PricingConfidenceImpact.Decrease,
                $"No resource type price found for {request.ResourceType}. Used provider fallback estimate.");
        }

        return UnknownCloudResult(catalog, request, $"No price found for {request.Provider}/{request.ResourceType}/{Display(sku)} in {Display(request.Region)}.");
    }

    public PricingMatchResult EstimateAiMonthlyCost(AiPricingLookupRequest request)
    {
        var item = aiCatalog.Items.FirstOrDefault(item =>
            Matches(item.Provider, request.Provider)
            && Matches(item.Sku, request.Model)
            && Matches(item.Currency, request.Currency));
        if (item is null || item.InputPricePerMillionTokens is null || item.OutputPricePerMillionTokens is null)
        {
            return new PricingMatchResult
            {
                Matched = false,
                Provider = request.Provider,
                ResourceType = "ai.workflow",
                ResourceName = request.WorkflowId,
                Sku = request.Model,
                Currency = request.Currency,
                CatalogName = aiCatalog.Name,
                CatalogVersion = aiCatalog.Version,
                PricingSource = aiCatalog.Source,
                SourceType = aiCatalog.SourceType,
                DefaultRegion = aiCatalog.DefaultRegion,
                EffectiveFrom = aiCatalog.EffectiveFrom,
                LastUpdatedAt = aiCatalog.LastUpdatedAt,
                MatchType = PricingMatchType.Unknown,
                ConfidenceImpact = PricingConfidenceImpact.Decrease,
                FallbackReason = $"No AI model price found for {request.Provider}/{request.Model} in {aiCatalog.Name} {aiCatalog.Version}."
            };
        }

        var monthlyInputTokens = request.EstimatedRunsPerMonth * (decimal)request.AverageInputTokens;
        var monthlyOutputTokens = request.EstimatedRunsPerMonth * (decimal)request.AverageOutputTokens;
        var inputCost = monthlyInputTokens / 1_000_000m * item.InputPricePerMillionTokens.Value;
        var outputCost = monthlyOutputTokens / 1_000_000m * item.OutputPricePerMillionTokens.Value;
        var assumptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AIPricingCatalogName"] = aiCatalog.Name,
            ["AIPricingCatalogVersion"] = aiCatalog.Version,
            ["AIModel"] = request.Model,
            ["InputPricePer1MTokens"] = item.InputPricePerMillionTokens.Value.ToString("0.####", CultureInfo.InvariantCulture),
            ["OutputPricePer1MTokens"] = item.OutputPricePerMillionTokens.Value.ToString("0.####", CultureInfo.InvariantCulture),
            ["MonthlyInputTokens"] = monthlyInputTokens.ToString("0", CultureInfo.InvariantCulture),
            ["MonthlyOutputTokens"] = monthlyOutputTokens.ToString("0", CultureInfo.InvariantCulture)
        };

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
            UnitPrice = item.InputPricePerMillionTokens,
            CatalogName = aiCatalog.Name,
            CatalogVersion = aiCatalog.Version,
            PricingSource = aiCatalog.Source,
            SourceType = aiCatalog.SourceType,
            DefaultRegion = aiCatalog.DefaultRegion,
            EffectiveFrom = aiCatalog.EffectiveFrom,
            LastUpdatedAt = aiCatalog.LastUpdatedAt,
            MatchType = PricingMatchType.ExactRegionSkuMatch,
            ConfidenceImpact = PricingConfidenceImpact.Neutral,
            Assumptions = assumptions,
            MatchedCatalogItemIdOrKey = ItemKey(item),
            LiveApiUsed = false,
            FallbackUsed = false,
            UnitOfMeasure = "1M tokens",
            MeterName = item.MeterName ?? item.Sku,
            ProductName = item.Service,
            SkuName = item.Sku,
            ServiceName = item.Service,
            PriceType = item.PriceType,
            InputPricePerMillionTokens = item.InputPricePerMillionTokens,
            OutputPricePerMillionTokens = item.OutputPricePerMillionTokens,
            MonthlyInputTokens = monthlyInputTokens,
            MonthlyOutputTokens = monthlyOutputTokens
        };
    }

    public PricingCatalogValidationResult Validate()
    {
        var errors = new List<string>();
        ValidateCatalog(azureCatalog, errors, requireAiPrices: false);
        ValidateCatalog(aiCatalog, errors, requireAiPrices: true);
        return new PricingCatalogValidationResult(errors, []);
    }

    private static PricingMatchResult BuildCloudResult(
        PricingCatalog catalog,
        PricingCatalogItem item,
        PricingLookupRequest request,
        PricingMatchType matchType,
        PricingConfidenceImpact confidenceImpact,
        string? fallbackReason = null)
    {
        var estimated = EstimateMonthly(item, request);
        var assumptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PricingCatalogName"] = catalog.Name,
            ["PricingCatalogVersion"] = catalog.Version,
            ["PricingSource"] = catalog.SourceType,
            ["PricingCurrency"] = catalog.Currency,
            ["PricingDefaultRegion"] = catalog.DefaultRegion,
            ["PricingMatchType"] = matchType.ToString(),
            ["MonthlyHours"] = (item.MonthlyHours ?? request.MonthlyHours).ToString(CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            assumptions["PricingFallbackUsed"] = "true";
            assumptions["PricingFallbackReason"] = fallbackReason;
        }

        if (item.UsageQuantity is not null || request.UsageQuantity is not null)
        {
            assumptions["UsageQuantity"] = (request.UsageQuantity ?? item.UsageQuantity ?? 0).ToString("0.##", CultureInfo.InvariantCulture);
        }

        return new PricingMatchResult
        {
            Matched = estimated is not null,
            Provider = request.Provider,
            ResourceType = request.ResourceType,
            ResourceName = request.ResourceName,
            Sku = FirstNonEmpty(request.Sku, request.Size, request.Tier),
            Region = request.Region ?? item.Region ?? catalog.DefaultRegion,
            Currency = request.Currency,
            EstimatedMonthlyCost = estimated,
            UnitPrice = item.UnitPrice,
            Unit = item.Unit,
            CatalogName = catalog.Name,
            CatalogVersion = catalog.Version,
            PricingSource = catalog.Source,
            SourceType = catalog.SourceType,
            DefaultRegion = catalog.DefaultRegion,
            EffectiveFrom = catalog.EffectiveFrom,
            LastUpdatedAt = catalog.LastUpdatedAt,
            MatchType = estimated is null ? PricingMatchType.Unknown : matchType,
            ConfidenceImpact = estimated is null ? PricingConfidenceImpact.Decrease : confidenceImpact,
            FallbackReason = estimated is null ? $"Catalog item {ItemKey(item)} could not produce a monthly estimate." : fallbackReason,
            Assumptions = assumptions,
            MatchedCatalogItemIdOrKey = ItemKey(item),
            LiveApiUsed = false,
            FallbackUsed = false,
            MonthlyHours = item.MonthlyHours ?? request.MonthlyHours,
            UnitOfMeasure = item.Unit,
            MeterName = item.MeterName,
            ProductName = item.Service,
            SkuName = item.Sku,
            ArmSkuName = item.Sku,
            ServiceName = item.Service,
            PriceType = item.PriceType
        };
    }

    private static PricingMatchResult UnknownCloudResult(PricingCatalog catalog, PricingLookupRequest request, string reason)
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
            CatalogName = catalog.Name,
            CatalogVersion = catalog.Version,
            PricingSource = catalog.Source,
            SourceType = catalog.SourceType,
            DefaultRegion = catalog.DefaultRegion,
            EffectiveFrom = catalog.EffectiveFrom,
            LastUpdatedAt = catalog.LastUpdatedAt,
            MatchType = PricingMatchType.Unknown,
            ConfidenceImpact = PricingConfidenceImpact.Decrease,
            FallbackReason = reason,
            FallbackUsed = true,
            Assumptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PricingCatalogName"] = catalog.Name,
                ["PricingCatalogVersion"] = catalog.Version,
                ["PricingSource"] = catalog.SourceType,
                ["PricingFallbackUsed"] = "true",
                ["PricingFallbackReason"] = reason
            }
        };
    }

    private static decimal? EstimateMonthly(PricingCatalogItem item, PricingLookupRequest request)
    {
        if (item.MonthlyEstimate is not null)
        {
            return decimal.Round(item.MonthlyEstimate.Value * Math.Max(1, request.Quantity), 2);
        }

        if (item.UnitPrice is null)
        {
            return null;
        }

        var quantity = Math.Max(1, request.Quantity);
        return item.Unit.ToLowerInvariant() switch
        {
            "hour" => decimal.Round(item.UnitPrice.Value * (item.MonthlyHours ?? request.MonthlyHours) * quantity, 2),
            "month" => decimal.Round(item.UnitPrice.Value * quantity, 2),
            "gb-month" => decimal.Round(item.UnitPrice.Value * (request.UsageQuantity ?? item.UsageQuantity ?? 0) * quantity, 2),
            "gb" => decimal.Round(item.UnitPrice.Value * (request.UsageQuantity ?? item.UsageQuantity ?? 0) * quantity, 2),
            "request" => decimal.Round(item.UnitPrice.Value * (request.UsageQuantity ?? item.UsageQuantity ?? 0) * quantity, 2),
            _ => null
        };
    }

    private static void ValidateCatalog(PricingCatalog catalog, List<string> errors, bool requireAiPrices)
    {
        if (string.IsNullOrWhiteSpace(catalog.Provider))
        {
            errors.Add("Pricing catalog provider is required.");
        }

        if (string.IsNullOrWhiteSpace(catalog.Version))
        {
            errors.Add($"Pricing catalog {catalog.Name} version is required.");
        }

        if (string.IsNullOrWhiteSpace(catalog.Currency))
        {
            errors.Add($"Pricing catalog {catalog.Name} currency is required.");
        }

        if (catalog.Items.Count == 0)
        {
            errors.Add($"Pricing catalog {catalog.Name} must include at least one item.");
        }

        var exactKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ResourceType))
            {
                errors.Add($"Pricing catalog {catalog.Name} has an item missing resourceType.");
            }

            if (string.IsNullOrWhiteSpace(item.Unit))
            {
                errors.Add($"Pricing catalog {catalog.Name} item {ItemKey(item)} is missing unit.");
            }

            if (item.UnitPrice is < 0 || item.MonthlyEstimate is < 0 || item.InputPricePerMillionTokens is < 0 || item.OutputPricePerMillionTokens is < 0)
            {
                errors.Add($"Pricing catalog {catalog.Name} item {ItemKey(item)} has a negative price.");
            }

            if (requireAiPrices && (item.InputPricePerMillionTokens is null || item.OutputPricePerMillionTokens is null))
            {
                errors.Add($"AI pricing catalog item {ItemKey(item)} must include input and output token prices.");
            }

            var exactKey = $"{item.Provider}|{item.ResourceType}|{item.Sku}|{item.Region}|{item.Currency}";
            if (!IsWildcard(item.Sku) && !string.IsNullOrWhiteSpace(item.Region) && !exactKeys.Add(exactKey))
            {
                errors.Add($"Duplicate exact pricing catalog key: {exactKey}.");
            }
        }
    }

    private static string FindCatalogDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "SpendGovernor.Infrastructure", "Pricing", "Catalogs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "Pricing", "Catalogs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Pricing/Catalogs directory.");
    }

    private static bool Matches(string? candidate, string? requested)
    {
        if (IsWildcard(candidate))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(candidate)
            && !string.IsNullOrWhiteSpace(requested)
            && candidate.Equals(requested, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesConcrete(string? candidate, string? requested)
    {
        return !IsWildcard(candidate)
            && !string.IsNullOrWhiteSpace(requested)
            && candidate!.Equals(requested, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWildcard(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value == "*";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unspecified" : value;
    }

    private static string ItemKey(PricingCatalogItem item)
    {
        return string.IsNullOrWhiteSpace(item.Id)
            ? $"{item.Provider}:{item.ResourceType}:{item.Sku ?? "*"}:{item.Region ?? "*"}"
            : item.Id;
    }
}
