using System.Globalization;
using System.Text.Json;

namespace SpendGovernor.Core;

public sealed class TerraformPlanJsonParser
{
    public const string AnalysisSource = "Terraform plan JSON";
    public const string TerraformPlanFormat = "terraform show -json";

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "azurerm_service_plan",
        "azurerm_app_service_plan",
        "azurerm_linux_web_app",
        "azurerm_windows_web_app",
        "azurerm_redis_cache",
        "azurerm_mssql_database",
        "azurerm_sql_database",
        "azurerm_storage_account",
        "azurerm_kubernetes_cluster",
        "azurerm_container_app",
        "azurerm_log_analytics_workspace"
    };

    public TerraformPlanParseResult Parse(
        IEnumerable<RepositoryFile> files,
        string defaultRegion,
        int hoursPerMonth,
        string? branch = null)
    {
        var planFiles = files
            .Select(file => new RepositoryFile(ParserText.NormalizePath(file.Path), file.Content))
            .Where(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.TerraformPlanJson)
            .ToArray();
        if (planFiles.Length == 0)
        {
            return TerraformPlanParseResult.Empty;
        }

        var beforeResources = new List<CloudResourceEstimateInput>();
        var afterResources = new List<CloudResourceEstimateInput>();
        var changeHints = new List<TerraformPlanChangeHint>();
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var file in planFiles)
        {
            ParseFile(file, defaultRegion, hoursPerMonth, branch, beforeResources, afterResources, changeHints, errors, warnings);
        }

        return new TerraformPlanParseResult(
            planFiles.Select(file => file.Path).ToArray(),
            beforeResources,
            afterResources,
            changeHints,
            errors,
            warnings);
    }

    private static void ParseFile(
        RepositoryFile file,
        string defaultRegion,
        int hoursPerMonth,
        string? branch,
        List<CloudResourceEstimateInput> beforeResources,
        List<CloudResourceEstimateInput> afterResources,
        List<TerraformPlanChangeHint> changeHints,
        List<string> errors,
        List<string> warnings)
    {
        using var document = ParseDocument(file, errors);
        if (document is null)
        {
            return;
        }

        if (!document.RootElement.TryGetProperty("resource_changes", out var resourceChanges)
            || resourceChanges.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Terraform plan JSON could not be parsed: {file.Path} is missing resource_changes.");
            return;
        }

        foreach (var resourceChange in resourceChanges.EnumerateArray())
        {
            ParseResourceChange(file.Path, resourceChange, defaultRegion, hoursPerMonth, branch, beforeResources, afterResources, changeHints, warnings);
        }
    }

    private static JsonDocument? ParseDocument(RepositoryFile file, List<string> errors)
    {
        try
        {
            return JsonDocument.Parse(file.Content);
        }
        catch (JsonException ex)
        {
            errors.Add($"Terraform plan JSON could not be parsed: {file.Path} contains invalid JSON ({ex.Message}).");
            return null;
        }
    }

    private static void ParseResourceChange(
        string sourceFile,
        JsonElement resourceChange,
        string defaultRegion,
        int hoursPerMonth,
        string? branch,
        List<CloudResourceEstimateInput> beforeResources,
        List<CloudResourceEstimateInput> afterResources,
        List<TerraformPlanChangeHint> changeHints,
        List<string> warnings)
    {
        var mode = GetString(resourceChange, "mode");
        if (!string.IsNullOrWhiteSpace(mode) && !mode.Equals("managed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var address = GetString(resourceChange, "address")
            ?? $"{GetString(resourceChange, "type")}.{GetString(resourceChange, "name")}";
        var resourceType = GetString(resourceChange, "type") ?? "unknown";
        var resourceName = GetString(resourceChange, "name") ?? address;
        var providerName = GetString(resourceChange, "provider_name");
        var provider = IsAzureProvider(providerName, resourceType) ? "azure" : providerName ?? "unknown";

        if (!resourceChange.TryGetProperty("change", out var change) || change.ValueKind != JsonValueKind.Object)
        {
            warnings.Add($"Terraform plan JSON resource {address} is missing a change block.");
            return;
        }

        var actions = ReadActions(change);
        var actionSummary = string.Join(",", actions);
        var changeKind = MapActions(actions, out var terraformChangeType);
        if (changeKind is null)
        {
            return;
        }

        var before = GetObjectOrNull(change, "before");
        var after = GetObjectOrNull(change, "after");
        var beforeInput = before is null
            ? null
            : CreateResourceInput(sourceFile, resourceType, resourceName, address, provider, providerName, actionSummary, terraformChangeType, before.Value, defaultRegion, hoursPerMonth, branch);
        var afterInput = after is null
            ? null
            : CreateResourceInput(sourceFile, resourceType, resourceName, address, provider, providerName, actionSummary, terraformChangeType, after.Value, defaultRegion, hoursPerMonth, branch);

        var beforeSummary = beforeInput is null ? null : Summarize(beforeInput);
        var afterSummary = afterInput is null ? null : Summarize(afterInput);
        if (beforeInput is not null)
        {
            beforeInput.BeforeSummary = beforeSummary;
            beforeInput.AfterSummary = afterSummary;
            beforeInput.Raw["beforeSummary"] = beforeSummary;
            beforeInput.Raw["afterSummary"] = afterSummary;
            beforeResources.Add(beforeInput);
        }

        if (afterInput is not null)
        {
            afterInput.BeforeSummary = beforeSummary;
            afterInput.AfterSummary = afterSummary;
            afterInput.Raw["beforeSummary"] = beforeSummary;
            afterInput.Raw["afterSummary"] = afterSummary;
            afterResources.Add(afterInput);
        }

        changeHints.Add(new TerraformPlanChangeHint
        {
            SourceFile = sourceFile,
            ResourceKey = ResourceKey(provider, resourceType, resourceName),
            ResourceName = resourceName,
            ResourceType = resourceType,
            Provider = provider,
            Region = afterInput?.Region ?? beforeInput?.Region,
            BeforeSku = beforeInput?.Sku,
            AfterSku = afterInput?.Sku,
            BeforeSummary = beforeSummary,
            AfterSummary = afterSummary,
            ChangeKind = changeKind,
            TerraformAddress = address,
            TerraformActions = actionSummary,
            Reason = BuildReason(resourceType, address, changeKind, terraformChangeType, beforeSummary, afterSummary)
        });
    }

    private static CloudResourceEstimateInput CreateResourceInput(
        string sourceFile,
        string resourceType,
        string resourceName,
        string address,
        string provider,
        string? providerName,
        string actions,
        string terraformChangeType,
        JsonElement values,
        string defaultRegion,
        int hoursPerMonth,
        string? branch)
    {
        var tags = ReadTags(values);
        var explicitLocation = FirstNonEmpty(GetString(values, "location"), GetString(values, "region"));
        var region = explicitLocation ?? defaultRegion;
        var resource = new CloudResourceEstimateInput
        {
            SourceType = ResourceSourceType.Terraform,
            SourceFile = sourceFile,
            Provider = provider,
            ResourceType = resourceType,
            ResourceName = resourceName,
            Region = region,
            Quantity = Math.Max(1, GetInt(values, "count") ?? 1),
            HoursPerMonth = hoursPerMonth,
            Tags = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase),
            Environment = ParserText.InferEnvironment(sourceFile, tags, branch),
            IsSupported = provider.Equals("azure", StringComparison.OrdinalIgnoreCase) && SupportedTypes.Contains(resourceType),
            AnalysisSource = AnalysisSource,
            TerraformAddress = address,
            TerraformActions = actions,
            TerraformChangeType = terraformChangeType,
            Raw =
            {
                ["analysisSource"] = AnalysisSource,
                ["terraformPlanFormat"] = TerraformPlanFormat,
                ["terraformAddress"] = address,
                ["terraformActions"] = actions,
                ["terraformChangeType"] = terraformChangeType,
                ["terraformType"] = resourceType,
                ["terraformName"] = resourceName,
                ["terraformProviderName"] = providerName,
                ["rawValueJson"] = values.GetRawText()
            }
        };

        if (!resource.IsSupported)
        {
            resource.Warnings.Add(provider.Equals("azure", StringComparison.OrdinalIgnoreCase)
                ? "Resource type is outside the Terraform plan JSON MVP support list."
                : "Terraform plan JSON resource uses an unsupported provider.");
        }

        if (explicitLocation is null)
        {
            resource.Warnings.Add($"Region was not set in Terraform plan JSON; defaulted to {defaultRegion}.");
            resource.Raw["regionDefaulted"] = true;
            resource.Raw["defaultRegion"] = defaultRegion;
        }

        ApplyPricingFields(resource, values);

        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            resource.Warnings.Add("SKU, tier, or size was not found in Terraform plan JSON; pricing confidence will be lower.");
        }

        return resource;
    }

    private static void ApplyPricingFields(CloudResourceEstimateInput resource, JsonElement values)
    {
        switch (resource.ResourceType)
        {
            case "azurerm_service_plan":
                resource.Sku = FirstNonEmpty(GetString(values, "sku_name"), GetNestedString(values, "sku", "name"), GetNestedString(values, "sku", "size"));
                resource.Tier = FirstNonEmpty(GetString(values, "os_type"), GetNestedString(values, "sku", "tier"));
                resource.Quantity = GetInt(values, "worker_count") ?? resource.Quantity;
                break;
            case "azurerm_app_service_plan":
                resource.Sku = FirstNonEmpty(GetString(values, "sku_name"), GetNestedString(values, "sku", "name"), GetNestedString(values, "sku", "size"));
                resource.Tier = FirstNonEmpty(GetNestedString(values, "sku", "tier"), GetString(values, "kind"));
                resource.Quantity = GetInt(values, "worker_count") ?? resource.Quantity;
                break;
            case "azurerm_linux_web_app":
            case "azurerm_windows_web_app":
                resource.Sku = GetString(values, "sku_name");
                resource.Warnings.Add("Web app cost depends on the referenced App Service Plan; include the plan in the Terraform plan for higher confidence.");
                break;
            case "azurerm_redis_cache":
                var redisSku = GetString(values, "sku_name");
                var family = GetString(values, "family");
                var redisCapacity = GetString(values, "capacity");
                resource.Sku = string.Join('_', new[] { redisSku, family, redisCapacity }.Where(part => !string.IsNullOrWhiteSpace(part)));
                resource.Capacity = GetDecimal(values, "capacity");
                break;
            case "azurerm_mssql_database":
            case "azurerm_sql_database":
                resource.Sku = FirstNonEmpty(GetString(values, "sku_name"), GetString(values, "requested_service_objective_name"), GetString(values, "edition"));
                resource.Capacity = GetDecimal(values, "max_size_gb");
                break;
            case "azurerm_storage_account":
                var tier = GetString(values, "account_tier");
                var replication = GetString(values, "account_replication_type");
                resource.Tier = tier;
                resource.Sku = string.Join('_', new[] { tier, replication }.Where(part => !string.IsNullOrWhiteSpace(part)));
                resource.Capacity = FirstDecimal(GetDecimal(values, "estimated_gb"), GetDecimal(values, "capacity_gb"), GetDecimal(values, "storage_gb"));
                break;
            case "azurerm_kubernetes_cluster":
                var defaultNodePool = GetObjectOrNull(values, "default_node_pool");
                resource.Sku = defaultNodePool is null ? null : GetString(defaultNodePool.Value, "vm_size");
                resource.Quantity = defaultNodePool is null ? resource.Quantity : GetInt(defaultNodePool.Value, "node_count") ?? resource.Quantity;
                break;
            case "azurerm_container_app":
                resource.Sku = GetString(values, "workload_profile_name");
                var template = GetObjectOrNull(values, "template");
                resource.Quantity = template is null ? resource.Quantity : GetInt(template.Value, "min_replicas") ?? resource.Quantity;
                resource.Warnings.Add("Container App usage-based compute needs runtime assumptions; the MVP marks it unknown unless a workload profile has a catalog price.");
                break;
            case "azurerm_log_analytics_workspace":
                resource.Sku = FirstNonEmpty(GetString(values, "sku"), GetNestedString(values, "sku", "name"));
                resource.Capacity = FirstDecimal(GetDecimal(values, "estimated_gb"), GetDecimal(values, "daily_quota_gb"));
                resource.Warnings.Add("Log Analytics pricing depends on ingestion volume; include estimated_gb for a stronger estimate.");
                break;
        }
    }

    private static IReadOnlyList<string> ReadActions(JsonElement change)
    {
        if (!change.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return actions.EnumerateArray()
            .Select(action => action.ValueKind == JsonValueKind.String ? action.GetString() ?? "" : action.GetRawText())
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .ToArray();
    }

    private static string? MapActions(IReadOnlyList<string> actions, out string terraformChangeType)
    {
        terraformChangeType = string.Join(",", actions);
        if (actions.Count == 0
            || actions.All(action => action.Equals("no-op", StringComparison.OrdinalIgnoreCase) || action.Equals("read", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (actions.Count == 1 && actions[0].Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            terraformChangeType = "create";
            return "added";
        }

        if (actions.Count == 1 && actions[0].Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            terraformChangeType = "delete";
            return "removed";
        }

        if (actions.Any(action => action.Equals("delete", StringComparison.OrdinalIgnoreCase))
            && actions.Any(action => action.Equals("create", StringComparison.OrdinalIgnoreCase)))
        {
            terraformChangeType = "replace";
            return "changed";
        }

        if (actions.Any(action => action.Equals("update", StringComparison.OrdinalIgnoreCase)))
        {
            terraformChangeType = "update";
            return "changed";
        }

        return "changed";
    }

    private static string BuildReason(string resourceType, string address, string changeKind, string terraformChangeType, string? beforeSummary, string? afterSummary)
    {
        return changeKind switch
        {
            "added" => $"New {resourceType} detected from Terraform plan JSON at {address}.",
            "removed" => $"{resourceType} removed by Terraform plan JSON at {address}.",
            _ when terraformChangeType.Equals("replace", StringComparison.OrdinalIgnoreCase) =>
                $"{resourceType} replaced by Terraform plan JSON at {address}: {beforeSummary ?? "unknown"} -> {afterSummary ?? "unknown"}.",
            _ => $"{resourceType} changed by Terraform plan JSON at {address}: {beforeSummary ?? "unknown"} -> {afterSummary ?? "unknown"}."
        };
    }

    private static string Summarize(CloudResourceEstimateInput resource)
    {
        var value = FirstNonEmpty(resource.Sku, resource.Tier, resource.Capacity?.ToString("0.##", CultureInfo.InvariantCulture));
        return string.IsNullOrWhiteSpace(value) ? resource.ResourceType : value;
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement values)
    {
        if (!values.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags.EnumerateObject())
        {
            result[tag.Name] = ValueToString(tag.Value) ?? "";
        }

        return result;
    }

    private static bool IsAzureProvider(string? providerName, string resourceType)
    {
        return resourceType.StartsWith("azurerm_", StringComparison.OrdinalIgnoreCase)
            || providerName?.Contains("/azurerm", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static string ResourceKey(string provider, string resourceType, string resourceName)
    {
        return $"{provider}:{resourceType}:{resourceName}";
    }

    private static JsonElement? GetObjectOrNull(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static string? GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        var nested = GetObjectOrNull(element, objectName);
        return nested is null ? null : GetString(nested.Value, propertyName);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return ValueToString(value);
    }

    private static string? ValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
        {
            return parsedNumber;
        }

        return int.TryParse(ValueToString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? GetDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var parsedNumber))
        {
            return parsedNumber;
        }

        return decimal.TryParse(ValueToString(value), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static decimal? FirstDecimal(params decimal?[] values)
    {
        return values.FirstOrDefault(value => value is not null);
    }
}

public sealed record TerraformPlanParseResult(
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<CloudResourceEstimateInput> BeforeResources,
    IReadOnlyList<CloudResourceEstimateInput> AfterResources,
    IReadOnlyList<TerraformPlanChangeHint> ChangeHints,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static TerraformPlanParseResult Empty { get; } = new([], [], [], [], [], []);

    public bool HasPlanFiles => SourceFiles.Count > 0;

    public bool HasCostRelevantChanges => ChangeHints.Count > 0;
}

public sealed class TerraformPlanChangeHint
{
    public string SourceFile { get; set; } = "";
    public string ResourceKey { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string Provider { get; set; } = "azure";
    public string? Region { get; set; }
    public string? BeforeSku { get; set; }
    public string? AfterSku { get; set; }
    public string? BeforeSummary { get; set; }
    public string? AfterSummary { get; set; }
    public string ChangeKind { get; set; } = "changed";
    public string? TerraformAddress { get; set; }
    public string? TerraformActions { get; set; }
    public string? Reason { get; set; }
}
