using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SpendGovernor.Core;

namespace SpendGovernor.Infrastructure.Services;

public sealed class AzureRetailPricesOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://prices.azure.com/api/retail/prices";
    public string? ApiVersion { get; set; } = "2023-01-01-preview";
    public string CurrencyCode { get; set; } = "EUR";
    public string DefaultRegion { get; set; } = "westeurope";
    public int TimeoutSeconds { get; set; } = 10;
    public int CacheTtlHours { get; set; } = 24;
    public int MaxPages { get; set; } = 5;
    public bool FallbackToLocalCatalog { get; set; } = true;
    public decimal DefaultStorageGb { get; set; } = 100;
    public decimal DefaultLogAnalyticsGb { get; set; } = 10;
}

public interface IAzureRetailPricesClient
{
    Task<AzureRetailPriceSearchResult> SearchAsync(
        AzureRetailPriceSearchRequest request,
        CancellationToken cancellationToken);
}

public interface IAzureLivePricingProvider
{
    Task<PricingMatchResult> TryEstimateMonthlyCostAsync(
        PricingLookupRequest request,
        CancellationToken cancellationToken);
}

public sealed record AzureRetailPriceSearchRequest(
    string Filter,
    string CurrencyCode,
    string? ApiVersion = null,
    int? MaxPages = null);

public sealed record AzureRetailPriceSearchResult(
    bool Succeeded,
    IReadOnlyList<AzureRetailPriceItem> Items,
    IReadOnlyList<string> RequestUrls,
    string? ErrorMessage = null,
    bool TooManyPages = false)
{
    public static AzureRetailPriceSearchResult Failed(string message, IReadOnlyList<string>? requestUrls = null) =>
        new(false, [], requestUrls ?? [], message);
}

public sealed class AzureRetailPricesClient : IAzureRetailPricesClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient httpClient;
    private readonly AzureRetailPricesOptions options;

    public AzureRetailPricesClient(HttpClient httpClient, IOptions<AzureRetailPricesOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<AzureRetailPriceSearchResult> SearchAsync(
        AzureRetailPriceSearchRequest request,
        CancellationToken cancellationToken)
    {
        var maxPages = Math.Clamp(request.MaxPages ?? options.MaxPages, 1, 20);
        var requestUrls = new List<string>();
        var items = new List<AzureRetailPriceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var url = BuildUrl(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.TimeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        }

        for (var page = 0; page < maxPages && !string.IsNullOrWhiteSpace(url); page++)
        {
            if (!seen.Add(url))
            {
                return new AzureRetailPriceSearchResult(false, items, requestUrls, "Azure Retail Prices API pagination loop detected.");
            }

            requestUrls.Add(url);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(url, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return AzureRetailPriceSearchResult.Failed("Azure Retail Prices API request timed out.", requestUrls);
            }
            catch (HttpRequestException ex)
            {
                return AzureRetailPriceSearchResult.Failed($"Azure Retail Prices API request failed: {ex.Message}", requestUrls);
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return AzureRetailPriceSearchResult.Failed($"Azure Retail Prices API returned HTTP {(int)response.StatusCode}.", requestUrls);
            }

            AzureRetailPricesResponse? payload;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                payload = await JsonSerializer.DeserializeAsync<AzureRetailPricesResponse>(stream, JsonOptions, timeout.Token);
            }
            catch (JsonException)
            {
                return AzureRetailPriceSearchResult.Failed("Azure Retail Prices API returned invalid JSON.", requestUrls);
            }

            if (payload?.Items is not null)
            {
                items.AddRange(payload.Items);
            }

            url = payload?.NextPageLink;
        }

        var tooManyPages = !string.IsNullOrWhiteSpace(url);
        return new AzureRetailPriceSearchResult(
            true,
            items,
            requestUrls,
            tooManyPages ? "Azure Retail Prices API pagination exceeded configured MaxPages." : null,
            tooManyPages);
    }

    private string BuildUrl(AzureRetailPriceSearchRequest request)
    {
        var query = new List<string>();
        var apiVersion = request.ApiVersion ?? options.ApiVersion;
        if (!string.IsNullOrWhiteSpace(apiVersion))
        {
            query.Add("api-version=" + Uri.EscapeDataString(apiVersion));
        }

        var filter = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? request.Filter
            : $"({request.Filter}) and currencyCode eq '{EscapeOData(request.CurrencyCode)}'";
        query.Add("$filter=" + Uri.EscapeDataString(filter));

        var separator = options.BaseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return options.BaseUrl + separator + string.Join("&", query);
    }

    private static string EscapeOData(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class AzureRetailPricesResponse
{
    public string? BillingCurrency { get; set; }
    public string? CurrencyCode { get; set; }
    public int? Count { get; set; }
    public string? NextPageLink { get; set; }
    public List<AzureRetailPriceItem> Items { get; set; } = [];
}

public sealed class AzureRetailPriceItem
{
    public string? CurrencyCode { get; set; }
    public decimal? RetailPrice { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? ArmRegionName { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset? EffectiveStartDate { get; set; }
    public string? MeterId { get; set; }
    public string? MeterName { get; set; }
    public string? ProductId { get; set; }
    public string? SkuId { get; set; }
    public string? ProductName { get; set; }
    public string? SkuName { get; set; }
    public string? ServiceName { get; set; }
    public string? ServiceFamily { get; set; }
    public string? UnitOfMeasure { get; set; }
    public string? Type { get; set; }
    public string? PriceType { get; set; }
    public bool? IsPrimaryMeterRegion { get; set; }
    public string? ArmSkuName { get; set; }
    public string? ReservationTerm { get; set; }
}

public sealed record AzureRetailPriceQueryCandidate(
    string Name,
    string Filter,
    string? ExpectedSku,
    string? ExpectedServiceName,
    string? ExpectedProductName,
    string? ExpectedMeterName,
    decimal? DefaultUsageQuantity = null);

public static class AzureRegionNormalizer
{
    private static readonly Dictionary<string, string> Regions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["westeurope"] = "westeurope",
        ["west europe"] = "westeurope",
        ["eu west"] = "westeurope",
        ["euwest"] = "westeurope",
        ["northeurope"] = "northeurope",
        ["north europe"] = "northeurope",
        ["eu north"] = "northeurope",
        ["eastus"] = "eastus",
        ["east us"] = "eastus",
        ["westus"] = "westus",
        ["west us"] = "westus",
        ["uksouth"] = "uksouth",
        ["uk south"] = "uksouth",
        ["francecentral"] = "francecentral",
        ["france central"] = "francecentral",
        ["germanywestcentral"] = "germanywestcentral",
        ["germany west central"] = "germanywestcentral"
    };

    public static string Normalize(string? region, string defaultRegion, out bool defaulted)
    {
        defaulted = string.IsNullOrWhiteSpace(region);
        var candidate = defaulted ? defaultRegion : region!;
        var key = candidate.Replace("-", " ", StringComparison.Ordinal).Trim();
        return Regions.TryGetValue(key, out var normalized)
            ? normalized
            : candidate.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    }
}

public static class AzureRetailPriceQueryMapper
{
    public static IReadOnlyList<AzureRetailPriceQueryCandidate> BuildCandidates(
        PricingLookupRequest request,
        AzureRetailPricesOptions options,
        out string normalizedRegion,
        out bool regionDefaulted)
    {
        normalizedRegion = AzureRegionNormalizer.Normalize(request.Region, options.DefaultRegion, out regionDefaulted);
        var sku = request.Sku ?? request.Size ?? request.Tier;
        var candidates = new List<AzureRetailPriceQueryCandidate>();

        switch (request.ResourceType.ToLowerInvariant())
        {
            case "azurerm_service_plan":
            case "azurerm_app_service_plan":
                Add(candidates, "App Service SKU", normalizedRegion, sku, "App Service", "App Service", sku, null);
                Add(candidates, "App Service fallback", normalizedRegion, sku, "App Service", null, sku, null);
                break;
            case "azurerm_linux_web_app":
            case "azurerm_windows_web_app":
                Add(candidates, "Web App linked App Service", normalizedRegion, sku, "App Service", "App Service", sku, null);
                break;
            case "azurerm_redis_cache":
                var redisSku = NormalizeRedisSku(sku);
                Add(candidates, "Azure Cache for Redis", normalizedRegion, redisSku ?? sku, "Azure Cache for Redis", "Azure Cache for Redis", redisSku ?? sku, null);
                Add(candidates, "Redis service family", normalizedRegion, redisSku ?? sku, null, "Redis", redisSku ?? sku, null);
                break;
            case "azurerm_mssql_database":
            case "azurerm_sql_database":
                Add(candidates, "SQL Database SKU", normalizedRegion, sku, "SQL Database", "SQL Database", sku, null);
                Add(candidates, "SQL Database product", normalizedRegion, sku, null, "SQL Database", sku, null);
                break;
            case "azurerm_storage_account":
                Add(candidates, "Storage GB-month", normalizedRegion, sku, "Storage", "Storage", StorageMeterHint(sku, request.Metadata), options.DefaultStorageGb);
                break;
            case "azurerm_log_analytics_workspace":
                Add(candidates, "Log Analytics ingestion", normalizedRegion, sku, "Azure Monitor", "Log Analytics", "Data Ingestion", options.DefaultLogAnalyticsGb);
                Add(candidates, "Log Analytics workspace", normalizedRegion, sku, "Log Analytics", "Log Analytics", "Data", options.DefaultLogAnalyticsGb);
                break;
            case "azurerm_container_app":
                Add(candidates, "Azure Container Apps", normalizedRegion, sku, "Azure Container Apps", "Container Apps", sku, null);
                break;
            case "azurerm_kubernetes_cluster":
            case "azurerm_kubernetes_cluster_node_pool":
                Add(candidates, "AKS node VM", normalizedRegion, sku, "Virtual Machines", "Virtual Machines", sku, null);
                break;
            case "azurerm_linux_virtual_machine":
            case "azurerm_windows_virtual_machine":
            case "azurerm_virtual_machine_scale_set":
            case "microsoft.compute/virtualmachines":
                Add(candidates, "Virtual Machine SKU", normalizedRegion, sku, "Virtual Machines", "Virtual Machines", sku, null);
                break;
        }

        return candidates;
    }

    private static void Add(
        List<AzureRetailPriceQueryCandidate> candidates,
        string name,
        string region,
        string? sku,
        string? serviceName,
        string? productName,
        string? meterName,
        decimal? defaultUsageQuantity)
    {
        var filter = new List<string>
        {
            $"armRegionName eq '{Escape(region)}'",
            "priceType eq 'Consumption'"
        };

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            filter.Add($"serviceName eq '{Escape(serviceName)}'");
        }

        var skuTerms = new List<string>();
        if (!string.IsNullOrWhiteSpace(sku))
        {
            skuTerms.Add($"armSkuName eq '{Escape(sku)}'");
            skuTerms.Add($"skuName eq '{Escape(sku)}'");
            skuTerms.Add($"contains(skuName, '{Escape(sku)}')");
        }

        if (!string.IsNullOrWhiteSpace(productName))
        {
            filter.Add($"contains(productName, '{Escape(productName)}')");
        }

        if (!string.IsNullOrWhiteSpace(meterName))
        {
            skuTerms.Add($"contains(meterName, '{Escape(meterName)}')");
        }

        if (skuTerms.Count > 0)
        {
            filter.Add("(" + string.Join(" or ", skuTerms) + ")");
        }

        candidates.Add(new AzureRetailPriceQueryCandidate(
            name,
            string.Join(" and ", filter),
            sku,
            serviceName,
            productName,
            meterName,
            defaultUsageQuantity));
    }

    private static string? NormalizeRedisSku(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        var parts = sku.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3)
        {
            return parts[1] + parts[2];
        }

        return sku.Replace("_", "", StringComparison.Ordinal);
    }

    private static string? StorageMeterHint(string? sku, IReadOnlyDictionary<string, object?> metadata)
    {
        if (metadata.TryGetValue("accessTier", out var accessTier) && accessTier is not null)
        {
            return accessTier.ToString();
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            return "Data Stored";
        }

        if (sku.Contains("GRS", StringComparison.OrdinalIgnoreCase))
        {
            return "GRS";
        }

        if (sku.Contains("LRS", StringComparison.OrdinalIgnoreCase))
        {
            return "LRS";
        }

        return "Data Stored";
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public sealed class AzureLivePricingProvider : IAzureLivePricingProvider
{
    private readonly IAzureRetailPricesClient client;
    private readonly AzureRetailPricesOptions options;
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

    public AzureLivePricingProvider(IAzureRetailPricesClient client, IOptions<AzureRetailPricesOptions> options)
    {
        this.client = client;
        this.options = options.Value;
    }

    public async Task<PricingMatchResult> TryEstimateMonthlyCostAsync(
        PricingLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return Unknown(request, "Azure Retail Prices API is disabled.");
        }

        if (!request.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
            || request.ResourceType.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase))
        {
            return Unknown(request, "Azure Retail Prices API supports Azure cloud resources only.");
        }

        var candidates = AzureRetailPriceQueryMapper.BuildCandidates(request, options, out var normalizedRegion, out var regionDefaulted);
        if (candidates.Count == 0)
        {
            return Unknown(request, $"No Azure Retail Prices API mapping exists for {request.ResourceType}.");
        }

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            var cacheKey = CacheKey(request, candidate, normalizedRegion);
            var search = await GetSearchResultAsync(cacheKey, candidate, cancellationToken);
            if (!search.Succeeded)
            {
                errors.Add(search.ErrorMessage ?? "Azure Retail Prices API lookup failed.");
                continue;
            }

            var selected = SelectBest(request, candidate, search.Items, normalizedRegion, regionDefaulted);
            if (selected is null)
            {
                continue;
            }

            return BuildMatch(request, candidate, selected, normalizedRegion, regionDefaulted);
        }

        return Unknown(
            request,
            errors.Count == 0
                ? $"No reliable Azure Retail Prices API match found for {request.ResourceType}/{Display(request.Sku)} in {Display(normalizedRegion)}."
                : string.Join(" ", errors.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    private async Task<AzureRetailPriceSearchResult> GetSearchResultAsync(
        string cacheKey,
        AzureRetailPriceQueryCandidate candidate,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Result;
        }

        var result = await client.SearchAsync(
            new AzureRetailPriceSearchRequest(candidate.Filter, options.CurrencyCode, options.ApiVersion, options.MaxPages),
            cancellationToken);

        if (options.CacheTtlHours > 0)
        {
            cache[cacheKey] = new CacheEntry(result, DateTimeOffset.UtcNow.AddHours(options.CacheTtlHours));
        }

        return result;
    }

    private SelectedRetailPrice? SelectBest(
        PricingLookupRequest request,
        AzureRetailPriceQueryCandidate candidate,
        IReadOnlyList<AzureRetailPriceItem> items,
        string normalizedRegion,
        bool regionDefaulted)
    {
        var scored = items
            .Select(item => Score(item, candidate, request, normalizedRegion))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ToArray();
        if (scored.Length == 0)
        {
            return null;
        }

        var best = scored[0];
        var ambiguous = scored.Length > 1 && scored[1].Score == best.Score;
        var monthly = EstimateMonthly(best.Item, request, candidate, out var fallbackReason);
        if (monthly is null)
        {
            return null;
        }

        return new SelectedRetailPrice(best.Item, best.Score, best.ExactSku, ambiguous, monthly.Value, fallbackReason, regionDefaulted);
    }

    private static ScoredRetailPrice Score(
        AzureRetailPriceItem item,
        AzureRetailPriceQueryCandidate candidate,
        PricingLookupRequest request,
        string normalizedRegion)
    {
        if ((item.UnitPrice ?? item.RetailPrice) is null or <= 0)
        {
            return new ScoredRetailPrice(item, 0, false);
        }

        if (!string.IsNullOrWhiteSpace(item.ReservationTerm)
            || Contains(item.PriceType, "reservation")
            || Contains(item.Type, "reservation")
            || Contains(item.PriceType, "savings")
            || Contains(item.MeterName, "spot")
            || Contains(item.PriceType, "devtest")
            || Contains(item.Type, "devtest"))
        {
            return new ScoredRetailPrice(item, 0, false);
        }

        var score = 1;
        if (EqualsText(item.ArmRegionName, normalizedRegion))
        {
            score += 50;
        }

        var expectedSku = candidate.ExpectedSku ?? request.Sku ?? request.Size ?? request.Tier;
        var exactSku = !string.IsNullOrWhiteSpace(expectedSku)
            && (EqualsText(item.ArmSkuName, expectedSku) || EqualsText(item.SkuName, expectedSku));
        if (exactSku)
        {
            score += 40;
        }
        else if (!string.IsNullOrWhiteSpace(expectedSku)
            && (Contains(item.SkuName, expectedSku) || Contains(item.MeterName, expectedSku) || Contains(item.ProductName, expectedSku)))
        {
            score += 20;
        }

        if (EqualsText(item.PriceType, "Consumption") || EqualsText(item.Type, "Consumption"))
        {
            score += 15;
        }

        if (item.IsPrimaryMeterRegion == true)
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ExpectedServiceName) && Contains(item.ServiceName, candidate.ExpectedServiceName))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ExpectedProductName) && Contains(item.ProductName, candidate.ExpectedProductName))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ExpectedMeterName)
            && (Contains(item.MeterName, candidate.ExpectedMeterName) || Contains(item.ProductName, candidate.ExpectedMeterName)))
        {
            score += 10;
        }

        return new ScoredRetailPrice(item, score, exactSku);
    }

    private static decimal? EstimateMonthly(
        AzureRetailPriceItem item,
        PricingLookupRequest request,
        AzureRetailPriceQueryCandidate candidate,
        out string? assumption)
    {
        assumption = null;
        var unitPrice = item.UnitPrice ?? item.RetailPrice;
        if (unitPrice is null)
        {
            return null;
        }

        var unit = item.UnitOfMeasure ?? "";
        var quantity = Math.Max(1, request.Quantity);
        if (unit.Contains("hour", StringComparison.OrdinalIgnoreCase))
        {
            return decimal.Round(unitPrice.Value * request.MonthlyHours * quantity, 2);
        }

        if (unit.Contains("gb/month", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("gb-month", StringComparison.OrdinalIgnoreCase)
            || (unit.Contains("gb", StringComparison.OrdinalIgnoreCase) && unit.Contains("month", StringComparison.OrdinalIgnoreCase)))
        {
            var usage = request.UsageQuantity ?? candidate.DefaultUsageQuantity;
            if (usage is null)
            {
                return null;
            }

            if (request.UsageQuantity is null)
            {
                assumption = $"Usage quantity was missing; assumed {usage.Value.ToString("0.##", CultureInfo.InvariantCulture)} GB/month for Azure Retail Prices API conversion.";
            }

            return decimal.Round(unitPrice.Value * usage.Value * quantity, 2);
        }

        return null;
    }

    private PricingMatchResult BuildMatch(
        PricingLookupRequest request,
        AzureRetailPriceQueryCandidate candidate,
        SelectedRetailPrice selected,
        string normalizedRegion,
        bool regionDefaulted)
    {
        var item = selected.Item;
        var unitPrice = item.UnitPrice ?? item.RetailPrice;
        var ambiguous = selected.Ambiguous;
        var approximate = !selected.ExactSku || ambiguous || regionDefaulted;
        var matchType = ambiguous
            ? PricingMatchType.AzureRetailAmbiguousMatch
            : regionDefaulted
                ? PricingMatchType.AzureRetailDefaultRegionSkuMatch
                : selected.ExactSku
                    ? PricingMatchType.AzureRetailExactRegionSkuMatch
                    : PricingMatchType.AzureRetailExactRegionApproximateSkuMatch;
        var fallbackReason = ambiguous
            ? "Multiple Azure Retail Prices API meters had the same score; selected the safest consumption meter and lowered confidence."
            : selected.Assumption;

        var assumptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PricingSource"] = "AzureRetailPricesApi",
            ["PricingCurrency"] = options.CurrencyCode,
            ["PricingRegion"] = normalizedRegion,
            ["PricingMatchType"] = matchType.ToString(),
            ["FallbackUsed"] = "false",
            ["UnitPrice"] = unitPrice?.ToString("0.########", CultureInfo.InvariantCulture) ?? "",
            ["UnitOfMeasure"] = item.UnitOfMeasure ?? "",
            ["MonthlyHours"] = request.MonthlyHours.ToString(CultureInfo.InvariantCulture),
            ["MeterName"] = item.MeterName ?? "",
            ["ProductName"] = item.ProductName ?? "",
            ["ServiceName"] = item.ServiceName ?? ""
        };
        if (regionDefaulted)
        {
            assumptions["RegionDefaulted"] = "true";
        }

        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            assumptions["PricingAmbiguity"] = fallbackReason;
        }

        return new PricingMatchResult
        {
            Matched = true,
            Provider = request.Provider,
            ResourceType = request.ResourceType,
            ResourceName = request.ResourceName,
            Sku = request.Sku ?? request.Size ?? request.Tier,
            Region = item.ArmRegionName ?? normalizedRegion,
            Currency = options.CurrencyCode,
            EstimatedMonthlyCost = selected.MonthlyCost,
            UnitPrice = unitPrice,
            Unit = item.UnitOfMeasure,
            CatalogName = "Azure Retail Prices API",
            CatalogVersion = options.ApiVersion ?? "",
            PricingSource = "Azure Retail Prices API",
            SourceType = "AzureRetailPricesApi",
            DefaultRegion = options.DefaultRegion,
            MatchType = matchType,
            ConfidenceImpact = approximate ? PricingConfidenceImpact.Neutral : PricingConfidenceImpact.Increase,
            FallbackReason = fallbackReason,
            Assumptions = assumptions,
            MatchedCatalogItemIdOrKey = item.MeterId ?? item.SkuId ?? candidate.Name,
            LiveApiUsed = true,
            FallbackUsed = false,
            RegionDefaulted = regionDefaulted,
            AmbiguousMatch = ambiguous,
            MonthlyHours = request.MonthlyHours,
            UnitOfMeasure = item.UnitOfMeasure,
            MeterId = item.MeterId,
            MeterName = item.MeterName,
            ProductName = item.ProductName,
            SkuName = item.SkuName,
            ArmSkuName = item.ArmSkuName,
            ServiceName = item.ServiceName,
            ServiceFamily = item.ServiceFamily,
            PriceType = item.PriceType ?? item.Type,
            EffectiveStartDate = item.EffectiveStartDate
        };
    }

    private PricingMatchResult Unknown(PricingLookupRequest request, string reason)
    {
        return new PricingMatchResult
        {
            Matched = false,
            Provider = request.Provider,
            ResourceType = request.ResourceType,
            ResourceName = request.ResourceName,
            Sku = request.Sku ?? request.Size ?? request.Tier,
            Region = request.Region,
            Currency = request.Currency,
            CatalogName = "Azure Retail Prices API",
            CatalogVersion = options.ApiVersion ?? "",
            PricingSource = "Azure Retail Prices API",
            SourceType = "AzureRetailPricesApi",
            DefaultRegion = options.DefaultRegion,
            MatchType = PricingMatchType.Unknown,
            ConfidenceImpact = PricingConfidenceImpact.Decrease,
            FallbackReason = reason,
            LiveApiUsed = true,
            FallbackUsed = false,
            Assumptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PricingSource"] = "AzureRetailPricesApi",
                ["PricingFallbackReason"] = reason
            }
        };
    }

    private static string CacheKey(PricingLookupRequest request, AzureRetailPriceQueryCandidate candidate, string normalizedRegion)
    {
        return string.Join('|', request.ResourceType, request.Sku, normalizedRegion, request.Currency, candidate.Filter);
    }

    private static bool EqualsText(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? text, string? value) =>
        !string.IsNullOrWhiteSpace(text)
        && !string.IsNullOrWhiteSpace(value)
        && text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "unspecified" : value;

    private sealed record CacheEntry(AzureRetailPriceSearchResult Result, DateTimeOffset ExpiresAt);

    private sealed record ScoredRetailPrice(AzureRetailPriceItem Item, int Score, bool ExactSku);

    private sealed record SelectedRetailPrice(
        AzureRetailPriceItem Item,
        int Score,
        bool ExactSku,
        bool Ambiguous,
        decimal MonthlyCost,
        string? Assumption,
        bool RegionDefaulted);
}

public sealed class HybridPricingCatalogService : IPricingCatalogService
{
    private readonly IAzureLivePricingProvider azureLivePricingProvider;
    private readonly JsonPricingCatalogService localCatalog;
    private readonly AzureRetailPricesOptions options;

    public HybridPricingCatalogService(
        IAzureLivePricingProvider azureLivePricingProvider,
        JsonPricingCatalogService localCatalog,
        IOptions<AzureRetailPricesOptions> options)
    {
        this.azureLivePricingProvider = azureLivePricingProvider;
        this.localCatalog = localCatalog;
        this.options = options.Value;
    }

    public PricingMatchResult EstimateMonthlyCost(PricingLookupRequest request)
    {
        if (!options.Enabled
            || !request.Provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
            || request.ResourceType.Equals("ai.workflow", StringComparison.OrdinalIgnoreCase))
        {
            return localCatalog.EstimateMonthlyCost(request);
        }

        PricingMatchResult live;
        try
        {
            live = azureLivePricingProvider.TryEstimateMonthlyCostAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            live = new PricingMatchResult
            {
                Matched = false,
                Provider = request.Provider,
                ResourceType = request.ResourceType,
                ResourceName = request.ResourceName,
                Sku = request.Sku,
                Region = request.Region,
                Currency = request.Currency,
                CatalogName = "Azure Retail Prices API",
                PricingSource = "Azure Retail Prices API",
                SourceType = "AzureRetailPricesApi",
                MatchType = PricingMatchType.Unknown,
                ConfidenceImpact = PricingConfidenceImpact.Decrease,
                FallbackReason = "Azure Retail Prices API request failed or timed out.",
                LiveApiUsed = true
            };
        }

        if (live.Matched && live.EstimatedMonthlyCost is not null)
        {
            return live;
        }

        if (!options.FallbackToLocalCatalog)
        {
            return live;
        }

        var local = localCatalog.EstimateMonthlyCost(request);
        return WithFallbackMetadata(local, live.FallbackReason ?? "No reliable Azure Retail Prices API match found for this resource/SKU/region.");
    }

    public PricingMatchResult EstimateAiMonthlyCost(AiPricingLookupRequest request)
    {
        return localCatalog.EstimateAiMonthlyCost(request);
    }

    public PricingCatalogValidationResult Validate()
    {
        return localCatalog.Validate();
    }

    private static PricingMatchResult WithFallbackMetadata(PricingMatchResult local, string reason)
    {
        var assumptions = new Dictionary<string, string>(local.Assumptions, StringComparer.OrdinalIgnoreCase)
        {
            ["FallbackUsed"] = "true",
            ["PricingFallbackReason"] = reason,
            ["LivePricingAttempted"] = "true"
        };

        return new PricingMatchResult
        {
            Matched = local.Matched,
            Provider = local.Provider,
            ResourceType = local.ResourceType,
            ResourceName = local.ResourceName,
            Sku = local.Sku,
            Region = local.Region,
            Currency = local.Currency,
            EstimatedMonthlyCost = local.EstimatedMonthlyCost,
            UnitPrice = local.UnitPrice,
            Unit = local.Unit,
            CatalogName = local.CatalogName,
            CatalogVersion = local.CatalogVersion,
            PricingSource = local.PricingSource,
            SourceType = local.SourceType,
            DefaultRegion = local.DefaultRegion,
            EffectiveFrom = local.EffectiveFrom,
            LastUpdatedAt = local.LastUpdatedAt,
            MatchType = local.MatchType,
            ConfidenceImpact = PricingConfidenceImpact.Decrease,
            FallbackReason = reason,
            Assumptions = assumptions,
            MatchedCatalogItemIdOrKey = local.MatchedCatalogItemIdOrKey,
            LiveApiUsed = false,
            FallbackUsed = true,
            RegionDefaulted = local.RegionDefaulted,
            AmbiguousMatch = local.AmbiguousMatch,
            MonthlyHours = local.MonthlyHours,
            UnitOfMeasure = local.UnitOfMeasure,
            MeterId = local.MeterId,
            MeterName = local.MeterName,
            ProductName = local.ProductName,
            SkuName = local.SkuName,
            ArmSkuName = local.ArmSkuName,
            ServiceName = local.ServiceName,
            ServiceFamily = local.ServiceFamily,
            PriceType = local.PriceType,
            EffectiveStartDate = local.EffectiveStartDate,
            InputPricePerMillionTokens = local.InputPricePerMillionTokens,
            OutputPricePerMillionTokens = local.OutputPricePerMillionTokens,
            MonthlyInputTokens = local.MonthlyInputTokens,
            MonthlyOutputTokens = local.MonthlyOutputTokens
        };
    }
}
