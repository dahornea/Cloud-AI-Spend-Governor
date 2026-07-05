using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpendGovernor.Core;

public sealed class ArmTemplateJsonParser
{
    public const string AnalysisSource = "Bicep compiled ARM JSON";
    public const string ArmTemplateFormat = "ARM deployment template JSON";
    public const string ArmTemplateDiffMode = "DesiredStateOnly";

    private static readonly Regex ParameterReference = new(@"^\[parameters\((?:'|"")(?<name>[^'""]+)(?:'|"")\)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VariableReference = new(@"^\[variables\((?:'|"")(?<name>[^'""]+)(?:'|"")\)\]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedArmTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.Web/serverfarms",
        "Microsoft.Web/sites",
        "Microsoft.Cache/Redis",
        "Microsoft.Sql/servers/databases",
        "Microsoft.Storage/storageAccounts",
        "Microsoft.ContainerService/managedClusters",
        "Microsoft.App/containerApps",
        "Microsoft.OperationalInsights/workspaces",
        "Microsoft.DBforPostgreSQL/flexibleServers",
        "Microsoft.DBforMySQL/flexibleServers",
        "Microsoft.ContainerRegistry/registries"
    };

    public ArmTemplateParseResult Parse(
        IEnumerable<RepositoryFile> files,
        string defaultRegion,
        int hoursPerMonth,
        string? branch = null)
    {
        var armFiles = files
            .Select(file => new RepositoryFile(ParserText.NormalizePath(file.Path), file.Content))
            .Where(file => FileDiscovery.Detect(file.Path).Kind == RelevantFileKind.ArmTemplateJson)
            .ToArray();
        if (armFiles.Length == 0)
        {
            return ArmTemplateParseResult.Empty;
        }

        var resources = new List<CloudResourceEstimateInput>();
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (var file in armFiles)
        {
            ParseFile(file, defaultRegion, hoursPerMonth, branch, resources, errors, warnings);
        }

        return new ArmTemplateParseResult(
            armFiles.Select(file => file.Path).ToArray(),
            resources,
            errors,
            warnings);
    }

    public static bool IsArmTemplateJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return LooksLikeArmTemplateRoot(root)
                && root.TryGetProperty("resources", out var resources)
                && resources.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ParseFile(
        RepositoryFile file,
        string defaultRegion,
        int hoursPerMonth,
        string? branch,
        List<CloudResourceEstimateInput> resources,
        List<string> errors,
        List<string> warnings)
    {
        using var document = ParseDocument(file, errors);
        if (document is null)
        {
            return;
        }

        var root = document.RootElement;
        if (!LooksLikeArmTemplateRoot(root))
        {
            errors.Add($"ARM template JSON could not be parsed: {file.Path} is not an ARM deployment template.");
            return;
        }

        if (!root.TryGetProperty("resources", out var resourceArray))
        {
            errors.Add($"ARM template JSON could not be parsed: {file.Path} is missing resources array.");
            return;
        }

        if (resourceArray.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"ARM template JSON could not be parsed: {file.Path} resources is not an array.");
            return;
        }

        if (resourceArray.GetArrayLength() == 0)
        {
            warnings.Add($"ARM template JSON {file.Path} contains an empty resources array.");
            return;
        }

        var state = new ArmParseState(file.Path, root, defaultRegion, hoursPerMonth, branch, warnings);
        ParseResourceArray(resourceArray, state, resources, null, null);
    }

    private static JsonDocument? ParseDocument(RepositoryFile file, List<string> errors)
    {
        try
        {
            return JsonDocument.Parse(file.Content);
        }
        catch (JsonException ex)
        {
            errors.Add($"ARM template JSON could not be parsed: {file.Path} contains invalid JSON ({ex.Message}).");
            return null;
        }
    }

    private static bool LooksLikeArmTemplateRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var schema = GetStringLiteral(root, "$schema");
        var hasDeploymentSchema = schema?.Contains("deploymentTemplate.json", StringComparison.OrdinalIgnoreCase) == true
            || schema?.Contains("schemas.management.azure.com", StringComparison.OrdinalIgnoreCase) == true;
        var hasContentVersion = root.TryGetProperty("contentVersion", out _);
        if (hasDeploymentSchema && hasContentVersion)
        {
            return true;
        }

        if (!root.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return resources.EnumerateArray().Any(IsAzureResourceObject);
    }

    private static bool IsAzureResourceObject(JsonElement resource)
    {
        if (resource.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var type = GetStringLiteral(resource, "type");
        if (type?.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(type)
            && resource.TryGetProperty("apiVersion", out _)
            && resource.TryGetProperty("name", out _);
    }

    private static void ParseResourceArray(
        JsonElement resourceArray,
        ArmParseState state,
        List<CloudResourceEstimateInput> resources,
        string? parentType,
        string? parentName)
    {
        var index = 0;
        foreach (var resourceElement in resourceArray.EnumerateArray())
        {
            index++;
            if (resourceElement.ValueKind != JsonValueKind.Object)
            {
                state.Warnings.Add($"ARM template JSON {state.SourceFile} resources[{index - 1}] is not an object and was skipped.");
                continue;
            }

            var resource = CreateResource(resourceElement, state, parentType, parentName, index);
            if (resource is not null)
            {
                resources.Add(resource);
            }

            if (TryGetProperty(resourceElement, "resources", out var nestedResources) && nestedResources.ValueKind == JsonValueKind.Array)
            {
                var nestedParentType = resource?.ArmResourceType ?? parentType;
                var nestedParentName = resource?.ResourceName ?? parentName;
                ParseResourceArray(nestedResources, state, resources, nestedParentType, nestedParentName);
            }
        }
    }

    private static CloudResourceEstimateInput? CreateResource(
        JsonElement element,
        ArmParseState state,
        string? parentType,
        string? parentName,
        int index)
    {
        var raw = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["analysisSource"] = AnalysisSource,
            ["armTemplateFormat"] = ArmTemplateFormat,
            ["armTemplateDiffMode"] = ArmTemplateDiffMode,
            ["sourceFile"] = state.SourceFile,
            ["rawValueJson"] = element.GetRawText()
        };

        if (TryGetProperty(element, "condition", out var condition))
        {
            raw["armConditionJson"] = condition.GetRawText();
            if (condition.ValueKind == JsonValueKind.False)
            {
                state.Warnings.Add($"ARM resource in {state.SourceFile} has condition=false and was skipped.");
                return null;
            }
        }

        var typeValue = ResolveString(GetPropertyOrNull(element, "type"), "type", state, raw, required: true);
        if (string.IsNullOrWhiteSpace(typeValue.Text))
        {
            state.Warnings.Add($"ARM template JSON {state.SourceFile} contains a resource without type and it was skipped.");
            return null;
        }

        var armType = ResolveNestedResourceType(typeValue.Text!, parentType);
        var nameValue = ResolveString(GetPropertyOrNull(element, "name"), "name", state, raw, required: true);
        var resourceName = ResolveNestedResourceName(nameValue.Text, parentName, index);
        var apiVersion = ResolveString(GetPropertyOrNull(element, "apiVersion"), "apiVersion", state, raw).Text;
        var kind = ResolveString(GetPropertyOrNull(element, "kind"), "kind", state, raw).Text;
        var explicitLocation = ResolveString(GetPropertyOrNull(element, "location"), "location", state, raw).Text;
        var location = explicitLocation ?? state.DefaultRegion;
        var sku = GetPropertyOrNull(element, "sku");
        var skuName = ResolveString(GetNestedPropertyOrNull(sku, "name"), "sku.name", state, raw).Text;
        var skuTier = ResolveString(GetNestedPropertyOrNull(sku, "tier"), "sku.tier", state, raw).Text;
        var skuSize = ResolveString(GetNestedPropertyOrNull(sku, "size"), "sku.size", state, raw).Text;
        var skuFamily = ResolveString(GetNestedPropertyOrNull(sku, "family"), "sku.family", state, raw).Text;
        var skuCapacity = ResolveString(GetNestedPropertyOrNull(sku, "capacity"), "sku.capacity", state, raw).Text;
        var mappedType = MapArmResourceType(armType, kind, element);
        var tags = ReadTags(element, state, raw);

        raw["armResourceType"] = armType;
        raw["mappedResourceType"] = mappedType;
        raw["armApiVersion"] = apiVersion;
        raw["armKind"] = kind;
        raw["armSkuName"] = skuName;
        raw["armSkuTier"] = skuTier;
        raw["armSkuSize"] = skuSize;
        raw["armSkuFamily"] = skuFamily;
        raw["armSkuCapacity"] = skuCapacity;
        if (parentType is not null)
        {
            raw["armNestedResource"] = true;
            raw["armParentResourceType"] = parentType;
            raw["armParentResourceName"] = parentName;
        }

        if (TryGetProperty(element, "properties", out var properties))
        {
            raw["armPropertiesJson"] = properties.GetRawText();
        }

        if (TryGetProperty(element, "dependsOn", out var dependsOn))
        {
            raw["armDependsOnJson"] = dependsOn.GetRawText();
        }

        if (TryGetProperty(element, "copy", out var copy))
        {
            raw["armCopyJson"] = copy.GetRawText();
            state.Warnings.Add($"ARM resource {resourceName} uses copy loops; pricing assumes the declared capacity or one resource instance.");
        }

        var resource = new CloudResourceEstimateInput
        {
            SourceType = ResourceSourceType.Bicep,
            SourceFile = state.SourceFile,
            Provider = "azure",
            ResourceType = mappedType,
            ResourceName = resourceName,
            Region = location,
            Sku = FirstNonEmpty(skuName, skuSize),
            Tier = skuTier,
            Quantity = 1,
            HoursPerMonth = state.HoursPerMonth,
            Tags = new Dictionary<string, string>(tags, StringComparer.OrdinalIgnoreCase),
            Environment = ParserText.InferEnvironment(state.SourceFile, tags, state.Branch),
            IsSupported = SupportedArmTypes.Contains(armType),
            AnalysisSource = AnalysisSource,
            ArmResourceType = armType,
            ArmApiVersion = apiVersion,
            ArmKind = kind,
            MappedResourceType = mappedType,
            BeforeSummary = null,
            AfterSummary = FirstNonEmpty(skuName, skuSize, skuTier, armType),
            Raw = raw
        };

        if (!resource.IsSupported)
        {
            resource.Warnings.Add("ARM resource type is outside the Bicep compiled ARM JSON MVP support list.");
        }

        if (parentType is not null)
        {
            resource.Warnings.Add("Nested ARM resource support is limited; pricing confidence may be lower.");
        }

        if (string.IsNullOrWhiteSpace(explicitLocation))
        {
            resource.Warnings.Add($"Region was not set in ARM template JSON; defaulted to {state.DefaultRegion}.");
            resource.Raw["regionDefaulted"] = true;
            resource.Raw["defaultRegion"] = state.DefaultRegion;
        }

        ApplyPricingFields(resource, element, skuName, skuTier, skuSize, skuFamily, skuCapacity, state, raw);

        if (string.IsNullOrWhiteSpace(resource.Sku))
        {
            resource.Warnings.Add("SKU, tier, or size was not found in ARM template JSON; pricing confidence will be lower.");
        }

        return resource;
    }

    private static void ApplyPricingFields(
        CloudResourceEstimateInput resource,
        JsonElement element,
        string? skuName,
        string? skuTier,
        string? skuSize,
        string? skuFamily,
        string? skuCapacity,
        ArmParseState state,
        Dictionary<string, object?> raw)
    {
        var properties = GetPropertyOrNull(element, "properties");
        switch (resource.ArmResourceType)
        {
            case "Microsoft.Web/serverfarms":
                resource.Sku = FirstNonEmpty(skuName, skuSize);
                resource.Tier = skuTier;
                resource.Quantity = Math.Max(1, ParseInt(skuCapacity) ?? 1);
                break;
            case "Microsoft.Web/sites":
                resource.Sku = FirstNonEmpty(skuName, skuSize);
                resource.Warnings.Add("Web app cost depends on the referenced App Service Plan; include the plan in the ARM JSON for higher confidence.");
                break;
            case "Microsoft.Cache/Redis":
                resource.Sku = string.Join('_', new[] { skuName, skuFamily, skuCapacity }.Where(part => !string.IsNullOrWhiteSpace(part)));
                if (string.IsNullOrWhiteSpace(resource.Sku))
                {
                    resource.Sku = FirstNonEmpty(skuName, skuSize);
                }
                resource.Capacity = ParseDecimal(skuCapacity);
                break;
            case "Microsoft.Sql/servers/databases":
                resource.Sku = FirstNonEmpty(skuName, skuSize, skuTier);
                resource.Capacity = FirstDecimal(
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "maxSizeGb"), "properties.maxSizeGb", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "maxSizeBytes"), "properties.maxSizeBytes", state, raw));
                break;
            case "Microsoft.Storage/storageAccounts":
                resource.Sku = FirstNonEmpty(skuName, skuSize, skuTier);
                resource.Tier = FirstNonEmpty(skuTier, ResolveString(GetNestedPropertyOrNull(properties, "accessTier"), "properties.accessTier", state, raw).Text);
                resource.Capacity = FirstDecimal(
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "estimatedGb"), "properties.estimatedGb", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "estimatedGB"), "properties.estimatedGB", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "capacityGb"), "properties.capacityGb", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "capacityGB"), "properties.capacityGB", state, raw));
                break;
            case "Microsoft.ContainerService/managedClusters":
                var defaultPool = FirstArrayObject(GetNestedPropertyOrNull(properties, "agentPoolProfiles"));
                resource.Sku = ResolveString(GetNestedPropertyOrNull(defaultPool, "vmSize"), "properties.agentPoolProfiles[0].vmSize", state, raw).Text;
                resource.Quantity = Math.Max(1, ResolveInt(GetNestedPropertyOrNull(defaultPool, "count"), "properties.agentPoolProfiles[0].count", state, raw) ?? 1);
                break;
            case "Microsoft.App/containerApps":
                resource.Sku = FirstNonEmpty(
                    skuName,
                    ResolveString(GetNestedPropertyOrNull(properties, "workloadProfileName"), "properties.workloadProfileName", state, raw).Text,
                    "Consumption");
                resource.Quantity = Math.Max(1, ResolveInt(GetNestedPropertyOrNull(GetNestedPropertyOrNull(properties, "template"), "scale", "minReplicas"), "properties.template.scale.minReplicas", state, raw) ?? 1);
                resource.Warnings.Add("Container App usage-based compute needs runtime assumptions; the MVP uses the workload profile or consumption fallback.");
                break;
            case "Microsoft.OperationalInsights/workspaces":
                resource.Sku = FirstNonEmpty(skuName, skuSize, "PerGB2018");
                resource.Capacity = FirstDecimal(
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "estimatedIngestionGbPerMonth"), "properties.estimatedIngestionGbPerMonth", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "estimatedGb"), "properties.estimatedGb", state, raw),
                    ResolveDecimal(GetNestedPropertyOrNull(properties, "dailyQuotaGb"), "properties.dailyQuotaGb", state, raw));
                resource.Warnings.Add("Log Analytics pricing depends on ingestion volume; include estimatedIngestionGbPerMonth for a stronger estimate.");
                break;
            case "Microsoft.DBforPostgreSQL/flexibleServers":
            case "Microsoft.DBforMySQL/flexibleServers":
            case "Microsoft.ContainerRegistry/registries":
                resource.Sku = FirstNonEmpty(skuName, skuSize, skuTier);
                resource.Tier = skuTier;
                break;
        }
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonElement element, ArmParseState state, Dictionary<string, object?> raw)
    {
        if (!TryGetProperty(element, "tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags.EnumerateObject())
        {
            var value = ResolveString(tag.Value, $"tags.{tag.Name}", state, raw);
            if (!string.IsNullOrWhiteSpace(value.Text))
            {
                result[tag.Name] = value.Text!;
            }
        }

        return result;
    }

    private static string ResolveNestedResourceType(string type, string? parentType)
    {
        if (parentType is null || type.Contains('/', StringComparison.Ordinal))
        {
            return type;
        }

        return $"{parentType}/{type}";
    }

    private static string ResolveNestedResourceName(string? name, string? parentName, int index)
    {
        var resolved = string.IsNullOrWhiteSpace(name) ? $"unnamed-arm-resource-{index}" : name!;
        if (parentName is null || resolved.Contains('/', StringComparison.Ordinal))
        {
            return resolved;
        }

        return $"{parentName}/{resolved}";
    }

    private static string MapArmResourceType(string armType, string? kind, JsonElement element)
    {
        return armType switch
        {
            "Microsoft.Web/serverfarms" => "azurerm_service_plan",
            "Microsoft.Web/sites" => IsLinuxWebApp(kind, element) ? "azurerm_linux_web_app" : "azurerm_windows_web_app",
            "Microsoft.Cache/Redis" => "azurerm_redis_cache",
            "Microsoft.Sql/servers/databases" => "azurerm_mssql_database",
            "Microsoft.Storage/storageAccounts" => "azurerm_storage_account",
            "Microsoft.ContainerService/managedClusters" => "azurerm_kubernetes_cluster",
            "Microsoft.App/containerApps" => "azurerm_container_app",
            "Microsoft.OperationalInsights/workspaces" => "azurerm_log_analytics_workspace",
            "Microsoft.DBforPostgreSQL/flexibleServers" => "azurerm_postgresql_flexible_server",
            "Microsoft.DBforMySQL/flexibleServers" => "azurerm_mysql_flexible_server",
            "Microsoft.ContainerRegistry/registries" => "azurerm_container_registry",
            _ => armType
        };
    }

    private static bool IsLinuxWebApp(string? kind, JsonElement element)
    {
        if (kind?.Contains("linux", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var properties = GetPropertyOrNull(element, "properties");
        var siteConfig = GetNestedPropertyOrNull(properties, "siteConfig");
        return !string.IsNullOrWhiteSpace(GetStringLiteral(siteConfig, "linuxFxVersion"));
    }

    private static ResolvedValue ResolveString(JsonElement? value, string fieldPath, ArmParseState state, Dictionary<string, object?> raw, bool required = false)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new ResolvedValue(null, false, false);
        }

        if (value.Value.ValueKind != JsonValueKind.String)
        {
            return new ResolvedValue(ValueToString(value.Value), true, false);
        }

        var text = value.Value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ResolvedValue(text, false, false);
        }

        if (!IsArmExpression(text))
        {
            return new ResolvedValue(text, true, false);
        }

        var parameterMatch = ParameterReference.Match(text);
        if (parameterMatch.Success)
        {
            var parameterName = parameterMatch.Groups["name"].Value;
            if (TryResolveParameter(parameterName, fieldPath, state, raw, out var parameterValue))
            {
                return new ResolvedValue(parameterValue, true, false);
            }

            AddUnresolved(raw, fieldPath, text);
            state.Warnings.Add($"ARM expression for {fieldPath} could not be resolved: {text}.");
            return new ResolvedValue(required ? text : null, false, true);
        }

        var variableMatch = VariableReference.Match(text);
        if (variableMatch.Success)
        {
            var variableName = variableMatch.Groups["name"].Value;
            if (TryResolveVariable(variableName, fieldPath, state, raw, out var variableValue))
            {
                return new ResolvedValue(variableValue, true, false);
            }

            AddUnresolved(raw, fieldPath, text);
            state.Warnings.Add($"ARM expression for {fieldPath} could not be resolved: {text}.");
            return new ResolvedValue(required ? text : null, false, true);
        }

        AddUnresolved(raw, fieldPath, text);
        state.Warnings.Add($"Complex ARM expression for {fieldPath} was not evaluated: {text}.");
        return new ResolvedValue(required ? text : null, false, true);
    }

    private static bool TryResolveParameter(string name, string fieldPath, ArmParseState state, Dictionary<string, object?> raw, out string? value)
    {
        value = null;
        if (!TryGetProperty(state.Root, "parameters", out var parameters)
            || !TryGetProperty(parameters, name, out var parameter)
            || !TryGetProperty(parameter, "defaultValue", out var defaultValue))
        {
            return false;
        }

        var resolved = ResolveString(defaultValue, $"parameters.{name}.defaultValue", state, raw);
        if (string.IsNullOrWhiteSpace(resolved.Text) || resolved.Unresolved)
        {
            return false;
        }

        value = resolved.Text;
        AddResolved(raw, "armParameterResolved", $"{name} -> {value}");
        return true;
    }

    private static bool TryResolveVariable(string name, string fieldPath, ArmParseState state, Dictionary<string, object?> raw, out string? value)
    {
        value = null;
        if (!TryGetProperty(state.Root, "variables", out var variables)
            || !TryGetProperty(variables, name, out var variable))
        {
            return false;
        }

        var resolved = ResolveString(variable, $"variables.{name}", state, raw);
        if (string.IsNullOrWhiteSpace(resolved.Text) || resolved.Unresolved)
        {
            return false;
        }

        value = resolved.Text;
        AddResolved(raw, "armVariableResolved", $"{name} -> {value}");
        return true;
    }

    private static decimal? ResolveDecimal(JsonElement? value, string fieldPath, ArmParseState state, Dictionary<string, object?> raw)
    {
        var resolved = ResolveString(value, fieldPath, state, raw);
        return ParseDecimal(resolved.Text);
    }

    private static int? ResolveInt(JsonElement? value, string fieldPath, ArmParseState state, Dictionary<string, object?> raw)
    {
        var resolved = ResolveString(value, fieldPath, state, raw);
        return ParseInt(resolved.Text);
    }

    private static void AddResolved(Dictionary<string, object?> raw, string key, string value)
    {
        if (!raw.TryGetValue(key, out var existing) || existing is not List<string> values)
        {
            values = [];
            raw[key] = values;
        }

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static void AddUnresolved(Dictionary<string, object?> raw, string fieldPath, string expression)
    {
        raw["armExpressionUnresolved"] = true;
        if (!raw.TryGetValue("armUnresolvedExpressions", out var existing) || existing is not List<Dictionary<string, string>> unresolved)
        {
            unresolved = [];
            raw["armUnresolvedExpressions"] = unresolved;
        }

        unresolved.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["field"] = fieldPath,
            ["expression"] = expression
        });
    }

    private static bool IsArmExpression(string value)
    {
        return value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal);
    }

    private static JsonElement? GetPropertyOrNull(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var value) ? value : null;
    }

    private static JsonElement? GetNestedPropertyOrNull(JsonElement? element, params string[] path)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var current = element.Value;
        foreach (var name in path)
        {
            if (!TryGetProperty(current, name, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static JsonElement? FirstArrayObject(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return element.Value.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Object);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringLiteral(JsonElement? element, string name)
    {
        if (element is null || !TryGetProperty(element.Value, name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
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

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
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

    private sealed record ArmParseState(
        string SourceFile,
        JsonElement Root,
        string DefaultRegion,
        int HoursPerMonth,
        string? Branch,
        List<string> Warnings);

    private sealed record ResolvedValue(string? Text, bool Resolved, bool Unresolved);
}

public sealed record ArmTemplateParseResult(
    IReadOnlyList<string> SourceFiles,
    IReadOnlyList<CloudResourceEstimateInput> Resources,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static ArmTemplateParseResult Empty { get; } = new([], [], [], []);

    public bool HasTemplateFiles => SourceFiles.Count > 0;

    public bool HasCostRelevantResources => Resources.Count > 0;
}
